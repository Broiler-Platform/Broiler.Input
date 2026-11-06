using Broiler.Input.Windows;
using System;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;

namespace Broiler.Input.Scanner.Windows;

[SupportedOSPlatform("windows")]
public sealed class WindowsScannerInputDevice : ScannerInputDevice
{
    private readonly WindowsScannerCaptureSession _captureSession;

    public WindowsScannerInputDevice(
        InputDeviceDescriptor descriptor,
        ScannerOpenOptions options,
        IInputClock? clock = null,
        IInputDiagnosticSink? diagnostics = null)
        : base(descriptor, clock ?? WindowsInputClock.Shared, diagnostics)
    {
        _captureSession = new WindowsScannerCaptureSession(
            descriptor,
            options ?? throw new ArgumentNullException(nameof(options)),
            RaiseCapturedPage,
            HandleCaptureFault,
            SetNegotiatedFormat,
            SetCaptureStatistics,
            (pageNumber, fraction, bytes) => RaiseProgressChanged(pageNumber, fraction, bytes),
            MarkCaptureCompleted,
            clock ?? WindowsInputClock.Shared,
            diagnostics);
    }

    public override async ValueTask StartAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfDisposed();

        if (State == InputDeviceState.Running)
            return;

        if (State is InputDeviceState.Discovered or InputDeviceState.Closed)
            throw new InvalidOperationException("The input device must be open before it can be started.");

        TransitionCaptureTo(ScannerCaptureState.Starting);

        try
        {
            await _captureSession.StartAsync(cancellationToken).ConfigureAwait(false);
            await base.StartAsync(cancellationToken).ConfigureAwait(false);
            TransitionCaptureTo(ScannerCaptureState.Scanning);
            _captureSession.EnableDelivery();
        }
        catch (InputScannerException exception)
        {
            InputFault fault = exception.Fault;

            if (fault.Category == InputErrorCategory.DeviceRemoved)
                MarkCaptureInvalidated(fault, exception.FaultCode);
            else
                MarkCaptureFaulted(fault, exception.FaultCode);

            throw;
        }
        catch (OperationCanceledException)
        {
            await _captureSession.StopAsync(CancellationToken.None).ConfigureAwait(false);
            TransitionCaptureTo(ScannerCaptureState.Stopped);
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

        if (State != InputDeviceState.Running && CaptureState != ScannerCaptureState.Scanning)
            return;

        TransitionCaptureTo(ScannerCaptureState.Stopping);
        await _captureSession.StopAsync(cancellationToken).ConfigureAwait(false);
        await base.StopAsync(cancellationToken).ConfigureAwait(false);
        TransitionCaptureTo(ScannerCaptureState.Stopped);
    }

    public override async ValueTask CloseAsync(CancellationToken cancellationToken = default)
    {
        await _captureSession.StopAsync(cancellationToken).ConfigureAwait(false);
        await base.CloseAsync(cancellationToken).ConfigureAwait(false);
        TransitionCaptureTo(ScannerCaptureState.Stopped);
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

    private void RaiseCapturedPage(ScannerPageLease page)
    {
        if (State == InputDeviceState.Running)
            RaisePageReady(page);
        else
            page.Dispose();
    }

    private void HandleCaptureFault(InputFault fault, ScannerFaultCode faultCode)
    {
        if (fault.Category == InputErrorCategory.DeviceRemoved)
            MarkCaptureInvalidated(fault, faultCode);
        else
            MarkCaptureFaulted(fault, faultCode);
    }
}
