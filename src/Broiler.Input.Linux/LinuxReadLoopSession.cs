using System;
using System.Threading;
using System.Threading.Tasks;

namespace Broiler.Input.Linux;

internal sealed class LinuxReadLoopSession
{
    [ThreadStatic] private static LinuxReadLoopSession? _executing;
    private readonly Lock _gate = new();
    private CancellationTokenSource? _cancellation;
    private Task _task = Task.CompletedTask;

    public void Start(Action<CancellationToken> run, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            if (!_task.IsCompleted)
                throw new InvalidOperationException("The previous evdev read loop is still running.");

            CancellationTokenSource cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _cancellation = cancellation;
            // Capture a local source: stopping must not null the token before this starts.
            _task = Task.Run(() =>
            {
                _executing = this;
                try { run(cancellation.Token); }
                finally
                {
                    _executing = null;
                    lock (_gate)
                    {
                        _cancellation = null;
                        cancellation.Dispose();
                    }
                }
            }, CancellationToken.None);
        }
    }

    public ValueTask StopAsync()
    {
        lock (_gate)
        {
            _cancellation?.Cancel();
            return _executing == this ? ValueTask.CompletedTask : new ValueTask(_task);
        }
    }
}
