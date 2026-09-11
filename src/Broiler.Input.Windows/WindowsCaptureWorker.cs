using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Broiler.Input.Windows;

// Native capture never waits for consumers. The delivery thread owns callbacks;
// stop waits for both threads, except for the callback currently requesting stop.
internal sealed class WindowsCaptureWorker<T>(string name, InputDeliveryOptions options,
    Action capture, Action interrupt, Action<T> deliver, Func<Exception, Exception> translateFailure,
    Action<Exception> captureFailed, Action<Exception> callbackFailed, Action statisticsChanged)
    : IDisposable, IAsyncDisposable where T : class, IDisposable
{
    private readonly object _gate = new();
    private readonly Queue<T> _queue = new();
    private Thread? _deliveryThread;
    private TaskCompletionSource _started = NewCompletion();
    private TaskCompletionSource _captureEnded = Completed();
    private TaskCompletionSource _deliveryEnded = Completed();
    private Task _interrupt = Task.CompletedTask;
    private Exception? _failure;
    private bool _stopRequested = true;
    private bool _deliveryEnabled;
    private bool _disposed;
    private long _enqueued;
    private long _delivered;
    private long _droppedNewest;
    private long _droppedOldest;

    public bool IsStopRequested
    {
        get { lock (_gate) return _stopRequested; }
    }

    public InputDeliveryMetrics Metrics
    {
        get
        {
            lock (_gate)
                return new(_enqueued, _delivered, _droppedNewest, _droppedOldest, _queue.Count);
        }
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Task started;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_captureEnded.Task.IsCompleted && _deliveryEnded.Task.IsCompleted && _interrupt.IsCompleted)
            {
                _started = NewCompletion();
                _captureEnded = NewCompletion();
                _deliveryEnded = NewCompletion();
                _interrupt = Task.CompletedTask;
                _failure = null;
                _stopRequested = false;
                _deliveryEnabled = false;
                _enqueued = _delivered = _droppedNewest = _droppedOldest = 0;
                _deliveryThread = new Thread(RunDelivery) { IsBackground = true, Name = name + ".Delivery" };
                _deliveryThread.Start();
                new Thread(RunCapture) { IsBackground = true, Name = name }.Start();
            }
            else if (_stopRequested || _captureEnded.Task.IsCompleted)
            {
                throw new InvalidOperationException("The previous capture session is still shutting down.");
            }

            started = _started.Task;
        }

        try
        {
            await started.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            await StopAsync(CancellationToken.None).ConfigureAwait(false);
            throw;
        }
    }

    public void SignalStarted() => _started.TrySetResult();

    // The device enables callbacks only after publishing its Running state.
    public void EnableDelivery()
    {
        lock (_gate)
        {
            _deliveryEnabled = true;
            Monitor.PulseAll(_gate);
        }
    }

    public bool TryEnqueue(T item)
    {
        ArgumentNullException.ThrowIfNull(item);
        lock (_gate)
        {
            if (_stopRequested)
            {
                item.Dispose();
                return false;
            }

            if (_queue.Count >= options.Capacity)
            {
                switch (options.OverflowPolicy)
                {
                    case InputDeliveryOverflowPolicy.DropNewest:
                        _droppedNewest++;
                        item.Dispose();
                        return false;
                    case InputDeliveryOverflowPolicy.DropOldest:
                        _queue.Dequeue().Dispose();
                        _droppedOldest++;
                        break;
                    case InputDeliveryOverflowPolicy.KeepLatest:
                        _droppedOldest += _queue.Count;
                        ClearQueue();
                        break;
                    case InputDeliveryOverflowPolicy.Fail:
                        item.Dispose();
                        throw new InvalidOperationException("The capture delivery queue is full.");
                    default:
                        item.Dispose();
                        throw new ArgumentOutOfRangeException(nameof(options.OverflowPolicy));
                }
            }

            _queue.Enqueue(item);
            _enqueued++;
            Monitor.PulseAll(_gate);
            return true;
        }
    }

    public ValueTask StopAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            if (!_stopRequested)
            {
                _stopRequested = true;
                ClearQueue();
                // Native interruption can block (e.g. Source Reader Flush). Keep
                // it off the caller and include it in the shutdown completion.
                if (!_captureEnded.Task.IsCompleted)
                    _interrupt = Task.Run(interrupt);
                Monitor.PulseAll(_gate);
            }

            Task delivery = Thread.CurrentThread == _deliveryThread
                ? Task.CompletedTask : _deliveryEnded.Task;
            return new ValueTask(Task.WhenAll(_captureEnded.Task, _interrupt, delivery).WaitAsync(cancellationToken));
        }
    }

    public void Dispose()
    {
        lock (_gate) _disposed = true;
        StopAsync(CancellationToken.None).GetAwaiter().GetResult();
    }

    public ValueTask DisposeAsync()
    {
        lock (_gate) _disposed = true;
        return StopAsync(CancellationToken.None);
    }

    private void RunCapture()
    {
        Exception? failure = null;
        try
        {
            capture();
        }
        catch (Exception exception)
        {
            failure = translateFailure(exception);
        }
        finally
        {
            lock (_gate)
            {
                if (failure is not null)
                {
                    if (!_started.TrySetException(failure) && !_stopRequested)
                        _failure = failure;
                    ClearQueue();
                }
                else
                {
                    _started.TrySetException(new InvalidOperationException("Capture ended before startup completed."));
                }

                // Fault callbacks can safely stop/dispose: native cleanup has finished.
                _captureEnded.TrySetResult();
                Monitor.PulseAll(_gate);
            }
        }
    }

    private void RunDelivery()
    {
        try
        {
            while (true)
            {
                T? item = null;
                Exception? failure;
                lock (_gate)
                {
                    while (!_stopRequested &&
                        !(_deliveryEnabled && (_failure is not null || _queue.Count > 0)) &&
                        !(_captureEnded.Task.IsCompleted && _failure is null && _queue.Count == 0))
                        Monitor.Wait(_gate);

                    if (_stopRequested)
                        return;

                    failure = _failure;
                    if (failure is null && !_queue.TryDequeue(out item))
                        return;
                }

                if (failure is not null)
                {
                    InvokeSafely(() => captureFailed(failure));
                    return;
                }

                try
                {
                    deliver(item!);
                    lock (_gate) _delivered++;
                }
                catch (Exception exception)
                {
                    item!.Dispose();
                    ReportCallbackFailure(exception);
                }

                InvokeSafely(statisticsChanged);
            }
        }
        finally
        {
            lock (_gate) ClearQueue();
            InvokeSafely(statisticsChanged);
            _deliveryEnded.TrySetResult();
        }
    }

    private void InvokeSafely(Action callback)
    {
        try { callback(); }
        catch (Exception exception) { ReportCallbackFailure(exception); }
    }

    private void ReportCallbackFailure(Exception exception)
    {
        try { callbackFailed(exception); }
        catch (Exception) { /* A diagnostic sink must not terminate a background thread. */ }
    }

    private void ClearQueue()
    {
        while (_queue.TryDequeue(out T? item))
            item.Dispose();
    }

    private static TaskCompletionSource NewCompletion() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static TaskCompletionSource Completed()
    {
        TaskCompletionSource completion = NewCompletion();
        completion.SetResult();
        return completion;
    }
}
