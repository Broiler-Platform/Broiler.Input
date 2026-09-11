using System;
using System.Threading;

namespace Broiler.Input.Microphone;

public abstract class MicrophoneInputDevice : InputDevice
{
    private MicrophoneCaptureState _captureState = MicrophoneCaptureState.Stopped;
    private MicrophoneCaptureStatistics _captureStatistics;
    private readonly Lock _statisticsGate = new();

    protected MicrophoneInputDevice(InputDeviceDescriptor descriptor, IInputClock? clock = null, 
        IInputDiagnosticSink? diagnostics = null) : base(descriptor, clock, diagnostics)
    {
        if (descriptor.Kind != InputKind.Microphone)
            throw new ArgumentException("Microphone devices require a microphone descriptor.", nameof(descriptor));
    }

    public event Action<MicrophoneBufferReadyEvent>? BufferReady;

    public event Action<MicrophoneCaptureStateChangedEvent>? CaptureStateChanged;

    public MicrophoneCaptureState CaptureState => _captureState;

    public MicrophoneCaptureStatistics CaptureStatistics
    {
        get { lock (_statisticsGate) return _captureStatistics; }
    }

    protected void RaiseBufferReady(MicrophoneBufferLease buffer, InputEventHeader? header = null)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        ThrowIfDisposed();

        BufferReady?.Invoke(new MicrophoneBufferReadyEvent(header ?? NextEventHeader(buffer.Timestamp), buffer));
    }

    protected void SetCaptureStatistics(MicrophoneCaptureStatistics statistics)
    {
        lock (_statisticsGate) _captureStatistics = statistics;
    }

    protected void TransitionCaptureTo(MicrophoneCaptureState state, InputFault? fault = null)
    {
        if (_captureState == state)
            return;

        MicrophoneCaptureState previous = _captureState;
        _captureState = state;
        CaptureStateChanged?.Invoke(new MicrophoneCaptureStateChangedEvent(previous, state, Clock.GetTimestamp(), fault));
    }

    protected void MarkCaptureInvalidated(InputFault fault)
    {
        ArgumentNullException.ThrowIfNull(fault);
        TransitionCaptureTo(MicrophoneCaptureState.Invalidated, fault);
        if (State != InputDeviceState.Disposed)
            MarkUnavailable(fault);
    }

    protected void MarkCaptureFaulted(InputFault fault)
    {
        ArgumentNullException.ThrowIfNull(fault);
        TransitionCaptureTo(MicrophoneCaptureState.Faulted, fault);
        if (State != InputDeviceState.Disposed)
            SetFault(fault);
    }
}
