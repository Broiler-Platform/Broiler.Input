using System;
using System.Threading;
using Broiler.Input;

namespace Broiler.Input.Camera;

public abstract class CameraInputDevice : InputDevice
{
    private CameraCaptureState _captureState = CameraCaptureState.Stopped;
    private CameraCaptureStatistics _captureStatistics;
    private readonly Lock _statisticsGate = new();

    protected CameraInputDevice(InputDeviceDescriptor descriptor, IInputClock? clock = null,
        IInputDiagnosticSink? diagnostics = null) : base(descriptor, clock, diagnostics)
    {
        if (descriptor.Kind != InputKind.Camera)
            throw new ArgumentException("Camera devices require a camera descriptor.", nameof(descriptor));
    }

    public event Action<CameraFrameReadyEvent>? FrameReady;

    public event Action<CameraCaptureStateChangedEvent>? CaptureStateChanged;

    public CameraCaptureState CaptureState => _captureState;

    public CameraCaptureStatistics CaptureStatistics
    {
        get { lock (_statisticsGate) return _captureStatistics; }
    }

    public CameraFormat? NegotiatedFormat { get; private set; }

    protected void RaiseFrameReady(CameraFrameLease frame, InputEventHeader? header = null)
    {
        ArgumentNullException.ThrowIfNull(frame);
        ThrowIfDisposed();

        FrameReady?.Invoke(new CameraFrameReadyEvent(header ?? NextEventHeader(frame.Timestamp), frame));
    }

    protected void SetNegotiatedFormat(CameraFormat format) => NegotiatedFormat = format ?? throw new ArgumentNullException(nameof(format));

    protected void SetCaptureStatistics(CameraCaptureStatistics statistics)
    {
        lock (_statisticsGate) _captureStatistics = statistics;
    }

    protected void TransitionCaptureTo(CameraCaptureState state, InputFault? fault = null)
    {
        if (_captureState == state)
            return;

        CameraCaptureState previous = _captureState;
        _captureState = state;
        
        CaptureStateChanged?.Invoke(new CameraCaptureStateChangedEvent(previous, state, Clock.GetTimestamp(), fault));
    }

    protected void MarkCaptureInvalidated(InputFault fault)
    {
        ArgumentNullException.ThrowIfNull(fault);
        TransitionCaptureTo(CameraCaptureState.Invalidated, fault);
        if (State != InputDeviceState.Disposed)
            MarkUnavailable(fault);
    }

    protected void MarkCaptureFaulted(InputFault fault)
    {
        ArgumentNullException.ThrowIfNull(fault);
        TransitionCaptureTo(CameraCaptureState.Faulted, fault);
        if (State != InputDeviceState.Disposed)
            SetFault(fault);
    }
}
