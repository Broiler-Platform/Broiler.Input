using System;
using System.Threading;

namespace Broiler.Input.Scanner;

public abstract class ScannerInputDevice : InputDevice
{
    private ScannerCaptureState _captureState = ScannerCaptureState.Stopped;
    private ScannerCaptureStatistics _captureStatistics;
    private readonly Lock _statisticsGate = new();

    protected ScannerInputDevice(InputDeviceDescriptor descriptor, IInputClock? clock = null,
        IInputDiagnosticSink? diagnostics = null) : base(descriptor, clock, diagnostics)
    {
        if (descriptor.Kind != InputKind.Scanner)
            throw new ArgumentException("Scanner devices require a scanner descriptor.", nameof(descriptor));
    }

    public event Action<ScannerPageReadyEvent>? PageReady;

    public event Action<ScannerCaptureStateChangedEvent>? CaptureStateChanged;

    public event Action<ScannerProgressEvent>? ProgressChanged;

    public ScannerCaptureState CaptureState => _captureState;

    public ScannerFaultCode LastFaultCode { get; private set; }

    public ScannerCaptureStatistics CaptureStatistics
    {
        get { lock (_statisticsGate) return _captureStatistics; }
    }

    public ScannerFormat? NegotiatedFormat { get; private set; }

    protected void RaisePageReady(ScannerPageLease page, InputEventHeader? header = null)
    {
        ArgumentNullException.ThrowIfNull(page);
        ThrowIfDisposed();

        PageReady?.Invoke(new ScannerPageReadyEvent(header ?? NextEventHeader(page.Timestamp), page));
    }

    protected void RaiseProgressChanged(long pageNumber, double progressFraction, long bytesTransferred,
        InputEventHeader? header = null)
    {
        ThrowIfDisposed();

        ProgressChanged?.Invoke(new ScannerProgressEvent(header ?? NextEventHeader(), pageNumber,
            progressFraction, bytesTransferred));
    }

    protected void SetNegotiatedFormat(ScannerFormat format) =>
        NegotiatedFormat = format ?? throw new ArgumentNullException(nameof(format));

    protected void SetCaptureStatistics(ScannerCaptureStatistics statistics)
    {
        lock (_statisticsGate) _captureStatistics = statistics;
    }

    protected void TransitionCaptureTo(ScannerCaptureState state, InputFault? fault = null,
        ScannerFaultCode faultCode = ScannerFaultCode.None)
    {
        if (_captureState == state)
            return;

        ScannerCaptureState previous = _captureState;
        _captureState = state;
        LastFaultCode = faultCode;

        CaptureStateChanged?.Invoke(new ScannerCaptureStateChangedEvent(previous, state, Clock.GetTimestamp(),
            fault, faultCode));
    }

    protected void MarkCaptureCompleted() => TransitionCaptureTo(ScannerCaptureState.Completed);

    protected void MarkCaptureInvalidated(InputFault fault, ScannerFaultCode faultCode = ScannerFaultCode.None)
    {
        ArgumentNullException.ThrowIfNull(fault);
        TransitionCaptureTo(ScannerCaptureState.Invalidated, fault, faultCode);
        if (State != InputDeviceState.Disposed)
            MarkUnavailable(fault);
    }

    protected void MarkCaptureFaulted(InputFault fault, ScannerFaultCode faultCode = ScannerFaultCode.None)
    {
        ArgumentNullException.ThrowIfNull(fault);
        TransitionCaptureTo(ScannerCaptureState.Faulted, fault, faultCode);
        if (State != InputDeviceState.Disposed)
            SetFault(fault);
    }
}
