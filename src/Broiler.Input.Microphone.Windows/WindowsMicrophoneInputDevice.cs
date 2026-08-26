using System;
using System.Threading;
using System.Threading.Tasks;
using Broiler.Input;
using Broiler.Input.Microphone;
using Broiler.Input.Windows;

namespace Broiler.Input.Microphone.Windows;

public sealed class WindowsMicrophoneInputDevice : MicrophoneInputDevice
{
    private readonly MicrophoneOpenOptions _options;
    private readonly WindowsMicrophoneCaptureSession _captureSession;

    public WindowsMicrophoneInputDevice(
        InputDeviceDescriptor descriptor,
        MicrophoneOpenOptions options,
        IInputClock? clock = null,
        IInputDiagnosticSink? diagnostics = null)
        : base(descriptor, clock ?? WindowsInputClock.Shared, diagnostics)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _captureSession = new WindowsMicrophoneCaptureSession(
            descriptor,
            _options,
            RaiseCapturedBuffer,
            HandleCaptureInvalidated,
            SetCaptureStatistics,
            clock ?? WindowsInputClock.Shared,
            diagnostics);
    }

    public override async ValueTask StartAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        if (State == InputDeviceState.Running)
            return;
        if (State is InputDeviceState.Discovered or InputDeviceState.Closed)
            throw new InvalidOperationException("The input device must be open before it can be started.");

        TransitionCaptureTo(MicrophoneCaptureState.Starting);
        try
        {
            await _captureSession.StartAsync(cancellationToken).ConfigureAwait(false);
            await base.StartAsync(cancellationToken).ConfigureAwait(false);
            TransitionCaptureTo(MicrophoneCaptureState.Running);
        }
        catch (InputMicrophoneException exception)
        {
            InputFault fault = exception.Fault;
            if (fault.Category == InputErrorCategory.DeviceRemoved)
                MarkCaptureInvalidated(fault);
            else
                MarkCaptureFaulted(fault);
            throw;
        }
    }

    public override async ValueTask StopAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        if (State != InputDeviceState.Running && CaptureState != MicrophoneCaptureState.Running)
            return;

        TransitionCaptureTo(MicrophoneCaptureState.Stopping);
        await _captureSession.StopAsync(cancellationToken).ConfigureAwait(false);
        await base.StopAsync(cancellationToken).ConfigureAwait(false);
        TransitionCaptureTo(MicrophoneCaptureState.Stopped);
    }

    public override async ValueTask CloseAsync(CancellationToken cancellationToken = default)
    {
        await _captureSession.StopAsync(cancellationToken).ConfigureAwait(false);
        await base.CloseAsync(cancellationToken).ConfigureAwait(false);
        TransitionCaptureTo(MicrophoneCaptureState.Stopped);
    }

    public override async ValueTask DisposeAsync()
    {
        await _captureSession.DisposeAsync().ConfigureAwait(false);
        await base.DisposeAsync().ConfigureAwait(false);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            _captureSession.Dispose();

        base.Dispose(disposing);
    }

    private void RaiseCapturedBuffer(MicrophoneBufferLease buffer)
    {
        if (State == InputDeviceState.Running)
            RaiseBufferReady(buffer);
        else
            buffer.Dispose();
    }

    private void HandleCaptureInvalidated(InputFault fault)
    {
        MarkCaptureInvalidated(fault);
    }
}
