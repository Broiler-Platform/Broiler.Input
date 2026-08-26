using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Broiler.Input.Microphone;

namespace Broiler.Input.Testing;

public sealed class FakeMicrophoneInputDevice : MicrophoneInputDevice
{
    private readonly Queue<MicrophoneBufferLease> _queue = new();
    private readonly MicrophoneOpenOptions _options;
    private long _capturedCount;
    private long _deliveredCount;
    private long _droppedNewestCount;
    private long _droppedOldestCount;
    private long _silentCount;
    private long _discontinuousCount;

    public FakeMicrophoneInputDevice(
        InputDeviceDescriptor descriptor,
        MicrophoneOpenOptions options,
        ManualInputClock clock,
        IInputDiagnosticSink? diagnostics = null)
        : base(descriptor, clock, diagnostics)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public bool TryCapture(byte[] bytes, MicrophoneFormat format, MicrophoneBufferFlags flags = MicrophoneBufferFlags.None)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(bytes);
        ArgumentNullException.ThrowIfNull(format);

        MicrophoneBufferLease lease = new(bytes, format, Clock.GetTimestamp(), _capturedCount, flags);
        _capturedCount++;
        if ((flags & MicrophoneBufferFlags.Silent) != 0)
            _silentCount++;
        if ((flags & MicrophoneBufferFlags.Discontinuous) != 0)
            _discontinuousCount++;

        bool accepted = TryEnqueue(lease);
        PublishStatistics();
        return accepted;
    }

    public bool TryRead(out MicrophoneBufferLease? lease)
    {
        ThrowIfDisposed();
        if (_queue.Count == 0)
        {
            lease = null;
            return false;
        }

        lease = _queue.Dequeue();
        _deliveredCount++;
        PublishStatistics();
        return true;
    }

    public bool DrainNext()
    {
        if (!TryRead(out MicrophoneBufferLease? lease) || lease is null)
            return false;

        RaiseBufferReady(lease);
        return true;
    }

    public void SimulateInvalidation()
    {
        MarkCaptureInvalidated(new InputFault(InputErrorCategory.DeviceRemoved, "Fake microphone endpoint was invalidated."));
    }

    public override async ValueTask OpenAsync(CancellationToken cancellationToken = default)
    {
        ManualAdvance();
        await base.OpenAsync(cancellationToken).ConfigureAwait(false);
    }

    public override async ValueTask StartAsync(CancellationToken cancellationToken = default)
    {
        ManualAdvance();
        TransitionCaptureTo(MicrophoneCaptureState.Starting);
        await base.StartAsync(cancellationToken).ConfigureAwait(false);
        TransitionCaptureTo(MicrophoneCaptureState.Running);
    }

    public override async ValueTask StopAsync(CancellationToken cancellationToken = default)
    {
        ManualAdvance();
        TransitionCaptureTo(MicrophoneCaptureState.Stopping);
        await base.StopAsync(cancellationToken).ConfigureAwait(false);
        TransitionCaptureTo(MicrophoneCaptureState.Stopped);
    }

    public override async ValueTask CloseAsync(CancellationToken cancellationToken = default)
    {
        ManualAdvance();
        await base.CloseAsync(cancellationToken).ConfigureAwait(false);
        TransitionCaptureTo(MicrophoneCaptureState.Stopped);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            while (_queue.Count > 0)
                _queue.Dequeue().Dispose();
        }

        base.Dispose(disposing);
    }

    private bool TryEnqueue(MicrophoneBufferLease lease)
    {
        InputDeliveryOptions deliveryOptions = _options.SessionOptions.DeliveryOptions;
        if (_queue.Count < deliveryOptions.Capacity)
        {
            _queue.Enqueue(lease);
            return true;
        }

        switch (deliveryOptions.OverflowPolicy)
        {
            case InputDeliveryOverflowPolicy.DropNewest:
                _droppedNewestCount++;
                lease.Dispose();
                return false;

            case InputDeliveryOverflowPolicy.DropOldest:
                _queue.Dequeue().Dispose();
                _droppedOldestCount++;
                _queue.Enqueue(lease);
                return true;

            case InputDeliveryOverflowPolicy.KeepLatest:
                _droppedOldestCount += _queue.Count;
                while (_queue.Count > 0)
                    _queue.Dequeue().Dispose();
                _queue.Enqueue(lease);
                return true;

            case InputDeliveryOverflowPolicy.Fail:
                lease.Dispose();
                throw new InvalidOperationException("The fake microphone delivery queue is full.");

            default:
                lease.Dispose();
                throw new ArgumentOutOfRangeException(nameof(deliveryOptions.OverflowPolicy));
        }
    }

    private void PublishStatistics()
    {
        SetCaptureStatistics(new MicrophoneCaptureStatistics(
            _capturedCount,
            _deliveredCount,
            _droppedNewestCount,
            _droppedOldestCount,
            _silentCount,
            _discontinuousCount,
            _queue.Count));
    }

    private void ManualAdvance()
    {
        if (Clock is ManualInputClock manual)
            manual.Advance(1);
    }
}
