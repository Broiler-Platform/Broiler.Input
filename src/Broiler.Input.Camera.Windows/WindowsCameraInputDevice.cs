using System;
using System.Threading;
using System.Threading.Tasks;
using Broiler.Input.Windows;

namespace Broiler.Input.Camera.Windows;

public sealed class WindowsCameraInputDevice : CameraInputDevice
{
    private readonly WindowsCameraCaptureSession _captureSession;

    public WindowsCameraInputDevice(InputDeviceDescriptor descriptor, CameraOpenOptions options, IInputClock? clock = null,
        IInputDiagnosticSink? diagnostics = null) : base(descriptor, clock ?? WindowsInputClock.Shared, diagnostics)
    {
        _captureSession = new WindowsCameraCaptureSession(descriptor, options ?? throw new ArgumentNullException(nameof(options)),
            RaiseCapturedFrame, HandleCaptureFault, SetNegotiatedFormat, SetCaptureStatistics,
            clock ?? WindowsInputClock.Shared, diagnostics);
    }

    public override async ValueTask StartAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfDisposed();

        if (State == InputDeviceState.Running)
            return;

        if (State is InputDeviceState.Discovered or InputDeviceState.Closed)
            throw new InvalidOperationException("The input device must be open before it can be started.");

        TransitionCaptureTo(CameraCaptureState.Starting);

        try
        {
            await _captureSession.StartAsync(cancellationToken).ConfigureAwait(false);
            await base.StartAsync(cancellationToken).ConfigureAwait(false);
            TransitionCaptureTo(CameraCaptureState.Running);
            _captureSession.EnableDelivery();
        }
        catch (InputCameraException exception)
        {
            InputFault fault = exception.Fault;
            
            if (fault.Category == InputErrorCategory.DeviceRemoved)
                MarkCaptureInvalidated(fault);
            else
                MarkCaptureFaulted(fault);
            
            throw;
        }
        catch (OperationCanceledException)
        {
            await _captureSession.StopAsync(CancellationToken.None).ConfigureAwait(false);
            TransitionCaptureTo(CameraCaptureState.Stopped);
            throw;
        }
        catch
        {
            await _captureSession.StopAsync(CancellationToken.None).ConfigureAwait(false);
            throw;
        }
    }

    public override async ValueTask StopAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfDisposed();
        
        if (State != InputDeviceState.Running && CaptureState != CameraCaptureState.Running)
            return;

        TransitionCaptureTo(CameraCaptureState.Stopping);
        await _captureSession.StopAsync(cancellationToken).ConfigureAwait(false);
        await base.StopAsync(cancellationToken).ConfigureAwait(false);
        TransitionCaptureTo(CameraCaptureState.Stopped);
    }

    public override async ValueTask CloseAsync(CancellationToken cancellationToken = default)
    {
        await _captureSession.StopAsync(cancellationToken).ConfigureAwait(false);
        await base.CloseAsync(cancellationToken).ConfigureAwait(false);
        TransitionCaptureTo(CameraCaptureState.Stopped);
    }

    public override async ValueTask DisposeAsync()
    {
        await _captureSession.DisposeAsync().ConfigureAwait(false);
        base.Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            _captureSession.Dispose();

        base.Dispose(disposing);
    }

    private void RaiseCapturedFrame(CameraFrameLease frame)
    {
        if (State == InputDeviceState.Running)
            RaiseFrameReady(frame);
        else
            frame.Dispose();
    }

    private void HandleCaptureFault(InputFault fault)
    {
        if (fault.Category == InputErrorCategory.DeviceRemoved)
            MarkCaptureInvalidated(fault);
        else
            MarkCaptureFaulted(fault);
    }
}
