using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Broiler.Input.Scanner.Linux;

public sealed class LinuxScannerInputDevice : ScannerInputDevice
{
    [ThreadStatic]
    private static LinuxScannerInputDevice? s_CallbackOwner;

    private readonly string _deviceName;
    private readonly ScannerOpenOptions _options;
    private readonly ILinuxSaneInterop _interop;
    private readonly Lock _gate = new();
    private readonly Queue<ScannerPageLease> _queue = new();

    private IntPtr _handle;
    private bool _saneInitialized;
    private CancellationTokenSource? _sessionCts;
    private Task _captureTask = Task.CompletedTask;
    private long _capturedCount;
    private long _deliveredCount;
    private long _droppedNewestCount;
    private long _droppedOldestCount;
    private long _formatChangedCount;
    private long _bytesTransferred;

    public LinuxScannerInputDevice(
        InputDeviceDescriptor descriptor,
        string deviceName,
        ScannerOpenOptions options,
        IInputClock? clock = null,
        IInputDiagnosticSink? diagnostics = null)
        : this(descriptor, deviceName, options, NativeLinuxSaneInterop.Shared, clock, diagnostics)
    {
    }

    internal LinuxScannerInputDevice(
        InputDeviceDescriptor descriptor,
        string deviceName,
        ScannerOpenOptions options,
        ILinuxSaneInterop interop,
        IInputClock? clock = null,
        IInputDiagnosticSink? diagnostics = null)
        : base(descriptor, clock, diagnostics)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(deviceName);
        _deviceName = deviceName;
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _interop = interop ?? throw new ArgumentNullException(nameof(interop));
    }

    public override async ValueTask OpenAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfDisposed();

        if (State is InputDeviceState.Open or InputDeviceState.Running)
            return;

        SaneStatus initStatus = _interop.Init(out _);
        LinuxScannerFaults.ThrowIfFailed(initStatus, "SANE scanner subsystem initialization failed.");
        _saneInitialized = true;

        try
        {
            SaneStatus openStatus = _interop.Open(_deviceName, out IntPtr handle);
            LinuxScannerFaults.ThrowIfFailed(openStatus, "SANE scanner device open failed.");
            _handle = handle;

            await base.OpenAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (InputScannerException exception)
        {
            CloseNativeHandle();
            if (exception.Fault.Category == InputErrorCategory.DeviceRemoved)
                MarkCaptureInvalidated(exception.Fault, exception.FaultCode);
            else
                MarkCaptureFaulted(exception.Fault, exception.FaultCode);

            throw;
        }
        catch
        {
            CloseNativeHandle();
            throw;
        }
    }

    public override async ValueTask StartAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfDisposed();

        if (State == InputDeviceState.Running)
            return;

        if (State is InputDeviceState.Discovered or InputDeviceState.Closed || _handle == IntPtr.Zero)
            throw new InvalidOperationException("The Linux SANE scanner device must be open before it can be started.");

        TransitionCaptureTo(ScannerCaptureState.Starting);

        try
        {
            ScannerFormat negotiatedFormat = NegotiateFormat(_handle);
            SetNegotiatedFormat(negotiatedFormat);

            await base.StartAsync(cancellationToken).ConfigureAwait(false);
            TransitionCaptureTo(ScannerCaptureState.Scanning);

            Interlocked.Exchange(ref _capturedCount, 0);
            Interlocked.Exchange(ref _deliveredCount, 0);
            Interlocked.Exchange(ref _droppedNewestCount, 0);
            Interlocked.Exchange(ref _droppedOldestCount, 0);
            Interlocked.Exchange(ref _formatChangedCount, 0);
            Interlocked.Exchange(ref _bytesTransferred, 0);

            CancellationTokenSource cts = new();
            lock (_gate)
            {
                _sessionCts = cts;
                _captureTask = Task.Run(() => RunCaptureSession(_handle, negotiatedFormat, cts.Token), CancellationToken.None);
            }
        }
        catch (InputScannerException exception)
        {
            if (exception.Fault.Category == InputErrorCategory.DeviceRemoved)
                MarkCaptureInvalidated(exception.Fault, exception.FaultCode);
            else
                MarkCaptureFaulted(exception.Fault, exception.FaultCode);

            throw;
        }
    }

    public override async ValueTask StopAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfDisposed();

        if (State != InputDeviceState.Running &&
            CaptureState is not (ScannerCaptureState.Scanning or ScannerCaptureState.Starting or ScannerCaptureState.WarmingUp))
        {
            return;
        }

        TransitionCaptureTo(ScannerCaptureState.Stopping);
        await StopCaptureTaskAsync(cancellationToken).ConfigureAwait(false);
        await base.StopAsync(cancellationToken).ConfigureAwait(false);
        TransitionCaptureTo(ScannerCaptureState.Stopped);
    }

    public override async ValueTask CloseAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await StopCaptureTaskAsync(cancellationToken).ConfigureAwait(false);
        CloseNativeHandle();
        await base.CloseAsync(cancellationToken).ConfigureAwait(false);
        TransitionCaptureTo(ScannerCaptureState.Stopped);
    }

    public override async ValueTask DisposeAsync()
    {
        await StopCaptureTaskAsync(CancellationToken.None).ConfigureAwait(false);
        CloseNativeHandle();
        await base.DisposeAsync().ConfigureAwait(false);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            StopCaptureTaskAsync(CancellationToken.None).AsTask().GetAwaiter().GetResult();
            CloseNativeHandle();
        }

        base.Dispose(disposing);
    }

    private ScannerFormat NegotiateFormat(IntPtr handle)
    {
        ScannerFormat preferred = _options.PreferredFormat
            ?? new ScannerFormat(ScannerSourceKind.Flatbed, 300, 300, ScannerPixelFormat.Rgb24);

        ScannerSourceKind actualSource = preferred.Source == ScannerSourceKind.Auto
            ? ScannerSourceKind.Flatbed
            : preferred.Source;

        string sourceOption = actualSource switch
        {
            ScannerSourceKind.FeederSimplex => "ADF",
            ScannerSourceKind.FeederDuplex => "ADF Duplex",
            _ => "Flatbed",
        };
        _ = _interop.TrySetStringOption(handle, "source", sourceOption);

        (string modeOption, int depthOption) = preferred.PixelFormat switch
        {
            ScannerPixelFormat.BlackAndWhite1Bit => ("Lineart", 1),
            ScannerPixelFormat.Grayscale8 => ("Gray", 8),
            ScannerPixelFormat.Grayscale16 => ("Gray", 16),
            _ => ("Color", 8),
        };

        _ = _interop.TrySetStringOption(handle, "mode", modeOption);
        _ = _interop.TrySetIntOrFixedOption(handle, "depth", depthOption);

        if (!_interop.TrySetIntOrFixedOption(handle, "resolution", preferred.DpiX))
        {
            _ = _interop.TrySetIntOrFixedOption(handle, "x-resolution", preferred.DpiX);
            _ = _interop.TrySetIntOrFixedOption(handle, "y-resolution", preferred.DpiY);
        }
        else if (preferred.DpiY != preferred.DpiX)
        {
            _ = _interop.TrySetIntOrFixedOption(handle, "y-resolution", preferred.DpiY);
        }

        if (_options.SessionOptions.Brightness != 0.0)
            _ = _interop.TrySetIntOrFixedOption(handle, "brightness", _options.SessionOptions.Brightness * 100.0);

        if (_options.SessionOptions.Contrast != 0.0)
            _ = _interop.TrySetIntOrFixedOption(handle, "contrast", _options.SessionOptions.Contrast * 100.0);

        if (preferred.Region is { } region)
        {
            double tlX = region.OffsetXMicrons / 1_000.0;
            double tlY = region.OffsetYMicrons / 1_000.0;
            double brX = (region.OffsetXMicrons + region.WidthMicrons) / 1_000.0;
            double brY = (region.OffsetYMicrons + region.HeightMicrons) / 1_000.0;

            _ = _interop.TrySetIntOrFixedOption(handle, "tl-x", tlX);
            _ = _interop.TrySetIntOrFixedOption(handle, "tl-y", tlY);
            _ = _interop.TrySetIntOrFixedOption(handle, "br-x", brX);
            _ = _interop.TrySetIntOrFixedOption(handle, "br-y", brY);
        }

        return new ScannerFormat(actualSource, preferred.DpiX, preferred.DpiY, preferred.PixelFormat, preferred.Region);
    }

    private void RunCaptureSession(IntPtr handle, ScannerFormat negotiatedFormat, CancellationToken cancellationToken)
    {
        long pageNumber = 0;
        int maxPages = _options.SessionOptions.MaxPages;
        if (maxPages == 0 && negotiatedFormat.Source == ScannerSourceKind.Flatbed)
            maxPages = 1;

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                if (maxPages > 0 && pageNumber >= maxPages)
                    break;

                SaneStatus startStatus = _interop.Start(handle);
                if (startStatus == SaneStatus.NoDocs)
                {
                    if (pageNumber > 0)
                        break;

                    LinuxScannerFaults.ThrowIfFailed(startStatus, "SANE scanner document feeder is empty.");
                }

                if (startStatus == SaneStatus.Cancelled && cancellationToken.IsCancellationRequested)
                    break;

                LinuxScannerFaults.ThrowIfFailed(startStatus, "SANE scanner page start failed.");

                SaneStatus paramStatus = _interop.GetParameters(handle, out SaneParameters parameters);
                LinuxScannerFaults.ThrowIfFailed(paramStatus, "SANE scanner parameter query failed.");

                byte[] pageBytes = ReadPageBytes(handle, parameters, pageNumber, cancellationToken);
                if (cancellationToken.IsCancellationRequested)
                    break;

                int width = Math.Max(1, parameters.PixelsPerLine);
                int stride = Math.Max(1, parameters.BytesPerLine);
                int height = parameters.Lines > 0
                    ? parameters.Lines
                    : Math.Max(1, pageBytes.Length / stride);

                ScannerPixelFormat actualPixelFormat = ResolvePixelFormat(parameters, negotiatedFormat.PixelFormat);
                ScannerFormat pageFormat = actualPixelFormat == negotiatedFormat.PixelFormat
                    ? negotiatedFormat
                    : new ScannerFormat(negotiatedFormat.Source, negotiatedFormat.DpiX, negotiatedFormat.DpiY, actualPixelFormat, negotiatedFormat.Region);

                ScannerPageFlags flags = ScannerPageFlags.None;
                if (_options.SessionOptions.DeliveryMode == ScannerDeliveryMode.LatestPagePreview)
                    flags |= ScannerPageFlags.Preview;

                if (negotiatedFormat.Source == ScannerSourceKind.FeederDuplex && (pageNumber % 2 == 1))
                    flags |= ScannerPageFlags.DuplexBackSide;

                if (actualPixelFormat != negotiatedFormat.PixelFormat)
                {
                    Interlocked.Increment(ref _formatChangedCount);
                    if (_options.SessionOptions.ReportFormatChanges)
                        flags |= ScannerPageFlags.FormatChanged;
                }

                bool isLastPage = maxPages > 0 && (pageNumber + 1 >= maxPages);
                if (isLastPage)
                    flags |= ScannerPageFlags.EndOfBatch;

                ScannerColorSpace colorSpace = actualPixelFormat is ScannerPixelFormat.Grayscale8 or ScannerPixelFormat.Grayscale16 or ScannerPixelFormat.BlackAndWhite1Bit
                    ? ScannerColorSpace.LinearGray
                    : ScannerColorSpace.SRgb;

                ScannerImagePlane plane = new(0, pageBytes.Length, stride, width, height);
                ScannerPageLease lease = new(
                    pageBytes,
                    width,
                    height,
                    pageFormat,
                    [plane],
                    Clock.GetTimestamp(),
                    pageNumber,
                    flags,
                    ScannerRotation.None,
                    colorSpace);

                Interlocked.Increment(ref _capturedCount);
                Interlocked.Add(ref _bytesTransferred, pageBytes.Length);
                EnqueueAndDeliver(lease, cancellationToken);
                pageNumber++;
            }

            if (!cancellationToken.IsCancellationRequested && CaptureState == ScannerCaptureState.Scanning)
            {
                DrainQueue(cancellationToken);
                if (!cancellationToken.IsCancellationRequested && CaptureState == ScannerCaptureState.Scanning)
                    MarkCaptureCompleted();
            }
        }
        catch (InputScannerException exception)
        {
            ClearQueue();
            if (!cancellationToken.IsCancellationRequested)
            {
                if (exception.Fault.Category == InputErrorCategory.DeviceRemoved)
                    MarkCaptureInvalidated(exception.Fault, exception.FaultCode);
                else
                    MarkCaptureFaulted(exception.Fault, exception.FaultCode);
            }
        }
        catch (Exception exception)
        {
            ClearQueue();
            if (!cancellationToken.IsCancellationRequested)
            {
                InputFault fault = new(InputErrorCategory.NativeFailure, "SANE scanner capture failed.", exception, nativeFacility: "SANE");
                MarkCaptureFaulted(fault, ScannerFaultCode.None);
            }
        }
        finally
        {
            if (handle != IntPtr.Zero)
                _interop.Cancel(handle);
        }
    }

    private byte[] ReadPageBytes(IntPtr handle, SaneParameters parameters, long pageNumber, CancellationToken cancellationToken)
    {
        int expectedBytes = parameters.Lines > 0 && parameters.BytesPerLine > 0
            ? parameters.Lines * parameters.BytesPerLine
            : 0;

        using MemoryStream stream = expectedBytes > 0 ? new MemoryStream(expectedBytes) : new MemoryStream();
        byte[] buffer = new byte[32 * 1024];

        while (!cancellationToken.IsCancellationRequested)
        {
            SaneStatus status = _interop.Read(handle, buffer, out int bytesRead);
            if (bytesRead > 0)
            {
                stream.Write(buffer, 0, bytesRead);
                double fraction = expectedBytes > 0
                    ? Math.Clamp((double)stream.Length / expectedBytes, 0.0, 1.0)
                    : 0.0;
                RaiseProgressChanged(pageNumber, fraction, stream.Length);
            }

            if (status == SaneStatus.Eof)
                break;

            if (status == SaneStatus.Cancelled && cancellationToken.IsCancellationRequested)
                break;

            LinuxScannerFaults.ThrowIfFailed(status, "SANE scanner page read failed.");
            if (bytesRead == 0)
                break;
        }

        return stream.ToArray();
    }

    private void EnqueueAndDeliver(ScannerPageLease lease, CancellationToken cancellationToken)
    {
        InputDeliveryOptions delivery = _options.SessionOptions.DeliveryOptions;

        lock (_gate)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                lease.Dispose();
                return;
            }

            if (_queue.Count >= delivery.Capacity)
            {
                switch (delivery.OverflowPolicy)
                {
                    case InputDeliveryOverflowPolicy.DropNewest:
                        Interlocked.Increment(ref _droppedNewestCount);
                        lease.Dispose();
                        PublishStatisticsLocked();
                        return;

                    case InputDeliveryOverflowPolicy.DropOldest:
                        _queue.Dequeue().Dispose();
                        Interlocked.Increment(ref _droppedOldestCount);
                        break;

                    case InputDeliveryOverflowPolicy.KeepLatest:
                        Interlocked.Add(ref _droppedOldestCount, _queue.Count);
                        while (_queue.TryDequeue(out ScannerPageLease? dropped))
                            dropped.Dispose();
                        break;

                    case InputDeliveryOverflowPolicy.Fail:
                        lease.Dispose();
                        throw new InvalidOperationException("The SANE scanner delivery queue is full.");
                }
            }

            _queue.Enqueue(lease);
            PublishStatisticsLocked();
        }

        DrainQueue(cancellationToken);
    }

    private void DrainQueue(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            ScannerPageLease? next;
            lock (_gate)
            {
                if (!_queue.TryDequeue(out next))
                    return;
            }

            s_CallbackOwner = this;
            try
            {
                if (State == InputDeviceState.Running)
                {
                    RaisePageReady(next);
                    Interlocked.Increment(ref _deliveredCount);
                }
                else
                {
                    next.Dispose();
                }
            }
            catch
            {
                next.Dispose();
                throw;
            }
            finally
            {
                s_CallbackOwner = null;
                lock (_gate)
                    PublishStatisticsLocked();
            }
        }
    }

    private void PublishStatisticsLocked()
    {
        SetCaptureStatistics(new ScannerCaptureStatistics(
            Interlocked.Read(ref _capturedCount),
            Interlocked.Read(ref _deliveredCount),
            Interlocked.Read(ref _droppedNewestCount),
            Interlocked.Read(ref _droppedOldestCount),
            Interlocked.Read(ref _formatChangedCount),
            Interlocked.Read(ref _bytesTransferred),
            _queue.Count));
    }

    private void ClearQueue()
    {
        lock (_gate)
        {
            while (_queue.TryDequeue(out ScannerPageLease? page))
                page.Dispose();
            PublishStatisticsLocked();
        }
    }

    private async ValueTask StopCaptureTaskAsync(CancellationToken cancellationToken)
    {
        Task taskToWait;
        CancellationTokenSource? cts;

        lock (_gate)
        {
            cts = _sessionCts;
            _sessionCts = null;
            taskToWait = _captureTask;
        }

        if (cts is not null)
        {
            cts.Cancel();
            if (_handle != IntPtr.Zero)
                _interop.Cancel(_handle);
        }

        try
        {
            if (s_CallbackOwner != this && !taskToWait.IsCompleted)
                await taskToWait.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            cts?.Dispose();
            ClearQueue();
        }
    }

    private void CloseNativeHandle()
    {
        IntPtr handle = Interlocked.Exchange(ref _handle, IntPtr.Zero);
        if (handle != IntPtr.Zero)
        {
            _interop.Cancel(handle);
            _interop.Close(handle);
        }

        if (_saneInitialized)
        {
            _saneInitialized = false;
            _interop.Exit();
        }
    }

    private static ScannerPixelFormat ResolvePixelFormat(SaneParameters parameters, ScannerPixelFormat fallback) =>
        parameters.Format switch
        {
            SaneFrame.Gray when parameters.Depth == 1 => ScannerPixelFormat.BlackAndWhite1Bit,
            SaneFrame.Gray when parameters.Depth <= 8 => ScannerPixelFormat.Grayscale8,
            SaneFrame.Gray when parameters.Depth > 8 => ScannerPixelFormat.Grayscale16,
            SaneFrame.Rgb => ScannerPixelFormat.Rgb24,
            _ => fallback,
        };
}
