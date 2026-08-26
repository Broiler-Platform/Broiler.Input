using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Broiler.Input.Camera;

namespace Broiler.Input.Testing;

public sealed class FakeCameraInputDevice : CameraInputDevice
{
    private readonly Queue<CameraFrameLease> _queue = new();
    private readonly CameraOpenOptions _options;
    private long _capturedCount;
    private long _deliveredCount;
    private long _droppedNewestCount;
    private long _droppedOldestCount;
    private long _formatChangedCount;
    private long _discontinuousCount;
    private long _frameNumber;

    public FakeCameraInputDevice(
        InputDeviceDescriptor descriptor,
        CameraOpenOptions options,
        ManualInputClock clock,
        IInputDiagnosticSink? diagnostics = null)
        : base(descriptor, clock, diagnostics)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        if (_options.PreferredFormat is not null)
            SetNegotiatedFormat(_options.PreferredFormat);
    }

    public bool TryCapture(
        byte[] bytes,
        CameraFormat format,
        IReadOnlyList<CameraFramePlane>? planes = null,
        CameraFrameFlags flags = CameraFrameFlags.None,
        CameraRotation rotation = CameraRotation.None,
        CameraColorSpace colorSpace = CameraColorSpace.Unknown)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(bytes);
        ArgumentNullException.ThrowIfNull(format);

        if (NegotiatedFormat is null || NegotiatedFormat != format)
        {
            SetNegotiatedFormat(format);
            if (NegotiatedFormat is not null && _capturedCount > 0)
                flags |= CameraFrameFlags.FormatChanged;
        }

        if ((flags & CameraFrameFlags.Discontinuous) != 0)
            _discontinuousCount++;
        if ((flags & CameraFrameFlags.FormatChanged) != 0)
            _formatChangedCount++;

        CameraFrameLease lease = new(
            bytes,
            format,
            planes ?? [new CameraFramePlane(0, bytes.Length, 0, format.Width, format.Height)],
            Clock.GetTimestamp(),
            _frameNumber++,
            flags,
            rotation,
            colorSpace);

        _capturedCount++;
        bool accepted = TryEnqueue(lease);
        PublishStatistics();
        return accepted;
    }

    public bool TryRead(out CameraFrameLease? frame)
    {
        ThrowIfDisposed();
        if (_queue.Count == 0)
        {
            frame = null;
            return false;
        }

        frame = _queue.Dequeue();
        _deliveredCount++;
        PublishStatistics();
        return true;
    }

    public bool DrainNext()
    {
        if (!TryRead(out CameraFrameLease? frame) || frame is null)
            return false;

        RaiseFrameReady(frame);
        return true;
    }

    public void SimulateInvalidation()
    {
        MarkCaptureInvalidated(new InputFault(InputErrorCategory.DeviceRemoved, "Fake camera source was invalidated."));
    }

    public override async ValueTask OpenAsync(CancellationToken cancellationToken = default)
    {
        ManualAdvance();
        await base.OpenAsync(cancellationToken).ConfigureAwait(false);
    }

    public override async ValueTask StartAsync(CancellationToken cancellationToken = default)
    {
        ManualAdvance();
        TransitionCaptureTo(CameraCaptureState.Starting);
        await base.StartAsync(cancellationToken).ConfigureAwait(false);
        TransitionCaptureTo(CameraCaptureState.Running);
    }

    public override async ValueTask StopAsync(CancellationToken cancellationToken = default)
    {
        ManualAdvance();
        TransitionCaptureTo(CameraCaptureState.Stopping);
        await base.StopAsync(cancellationToken).ConfigureAwait(false);
        TransitionCaptureTo(CameraCaptureState.Stopped);
    }

    public override async ValueTask CloseAsync(CancellationToken cancellationToken = default)
    {
        ManualAdvance();
        await base.CloseAsync(cancellationToken).ConfigureAwait(false);
        TransitionCaptureTo(CameraCaptureState.Stopped);
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

    private bool TryEnqueue(CameraFrameLease frame)
    {
        InputDeliveryOptions deliveryOptions = _options.SessionOptions.DeliveryOptions;
        if (_queue.Count < deliveryOptions.Capacity)
        {
            _queue.Enqueue(frame);
            return true;
        }

        switch (deliveryOptions.OverflowPolicy)
        {
            case InputDeliveryOverflowPolicy.DropNewest:
                _droppedNewestCount++;
                frame.Dispose();
                return false;

            case InputDeliveryOverflowPolicy.DropOldest:
                _queue.Dequeue().Dispose();
                _droppedOldestCount++;
                _queue.Enqueue(frame);
                return true;

            case InputDeliveryOverflowPolicy.KeepLatest:
                _droppedOldestCount += _queue.Count;
                while (_queue.Count > 0)
                    _queue.Dequeue().Dispose();
                _queue.Enqueue(frame);
                return true;

            case InputDeliveryOverflowPolicy.Fail:
                frame.Dispose();
                throw new InvalidOperationException("The fake camera delivery queue is full.");

            default:
                frame.Dispose();
                throw new ArgumentOutOfRangeException(nameof(deliveryOptions.OverflowPolicy));
        }
    }

    private void PublishStatistics()
    {
        SetCaptureStatistics(new CameraCaptureStatistics(
            _capturedCount,
            _deliveredCount,
            _droppedNewestCount,
            _droppedOldestCount,
            _formatChangedCount,
            _discontinuousCount,
            _queue.Count));
    }

    private void ManualAdvance()
    {
        if (Clock is ManualInputClock manual)
            manual.Advance(1);
    }
}
