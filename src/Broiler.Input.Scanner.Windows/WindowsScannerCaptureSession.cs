using Broiler.Input.Windows;
using Broiler.Native.Windows;
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;

namespace Broiler.Input.Scanner.Windows;

[SupportedOSPlatform("windows")]
internal sealed partial class WindowsScannerCaptureSession : IDisposable, IAsyncDisposable
{
    private const uint StreamSeekSet = 0;
    private const uint StreamSeekEnd = 2;

    private readonly Lock _gate = new();
    private readonly InputDeviceDescriptor _descriptor;
    private readonly ScannerOpenOptions _options;
    private readonly Action<InputFault, ScannerFaultCode> _faulted;
    private readonly Action<ScannerFormat> _formatNegotiated;
    private readonly Action<ScannerCaptureStatistics> _statisticsChanged;
    private readonly Action<long, double, long> _progressChanged;
    private readonly Action _completed;
    private readonly IInputClock _clock;
    private readonly IInputDiagnosticSink _diagnostics;
    private readonly WindowsCaptureWorker<ScannerPageLease> _worker;
    private readonly Lock _statisticsGate = new();

    private IWiaTransfer? _activeTransfer;
    private WiaTransferCallback? _activeCallback;
    private long _capturedCount;
    private long _formatChangedCount;
    private long _bytesTransferred;
    private long _pageNumber;

    public WindowsScannerCaptureSession(
        InputDeviceDescriptor descriptor,
        ScannerOpenOptions options,
        Action<ScannerPageLease> deliver,
        Action<InputFault, ScannerFaultCode> faulted,
        Action<ScannerFormat> formatNegotiated,
        Action<ScannerCaptureStatistics> statisticsChanged,
        Action<long, double, long> progressChanged,
        Action completed,
        IInputClock clock,
        IInputDiagnosticSink? diagnostics)
    {
        _descriptor = descriptor ?? throw new ArgumentNullException(nameof(descriptor));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        ArgumentNullException.ThrowIfNull(deliver);
        _faulted = faulted ?? throw new ArgumentNullException(nameof(faulted));
        _formatNegotiated = formatNegotiated ?? throw new ArgumentNullException(nameof(formatNegotiated));
        _statisticsChanged = statisticsChanged ?? throw new ArgumentNullException(nameof(statisticsChanged));
        _progressChanged = progressChanged ?? throw new ArgumentNullException(nameof(progressChanged));
        _completed = completed ?? throw new ArgumentNullException(nameof(completed));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _diagnostics = diagnostics ?? NullInputDiagnosticSink.Shared;
        _worker = new WindowsCaptureWorker<ScannerPageLease>(
            "Broiler.Input.Scanner.Windows.Wia",
            _options.SessionOptions.DeliveryOptions,
            RunCapture,
            InterruptCapture,
            deliver,
            TranslateFailure,
            HandleWorkerFailure,
            ReportCallbackFailure,
            PublishStatistics);
    }

    public Task StartAsync(CancellationToken cancellationToken) => _worker.StartAsync(cancellationToken);

    public void EnableDelivery() => _worker.EnableDelivery();

    public ValueTask StopAsync(CancellationToken cancellationToken) => _worker.StopAsync(cancellationToken);

    public void Dispose() => _worker.Dispose();

    public ValueTask DisposeAsync() => _worker.DisposeAsync();

    private void InterruptCapture()
    {
        lock (_gate)
        {
            _activeCallback?.RequestCancel();
            if (_activeTransfer is not { } transfer)
                return;

            using WindowsComApartmentScope apartment = WindowsComApartmentScope.EnterBestEffort();
            try
            {
                _ = transfer.Cancel();
            }
            catch (Exception exception)
            {
                ReportCallbackFailure(exception);
            }
        }
    }

    private void RunCapture()
    {
        Interlocked.Exchange(ref _capturedCount, 0);
        Interlocked.Exchange(ref _formatChangedCount, 0);
        Interlocked.Exchange(ref _bytesTransferred, 0);
        Interlocked.Exchange(ref _pageNumber, 0);

        using WindowsComApartmentScope apartment = WindowsComApartmentScope.Enter(
            static hr => WindowsScannerFaults.CreateException(hr, "COM initialization for WIA scanner capture failed.", "COM"));

        object? devMgrObject = null;
        object? rootItemObject = null;
        IWiaItem2? scanItem = null;
        IWiaTransfer? transfer = null;

        try
        {
            IWiaItem2 rootItem = WindowsScannerDeviceEnumerator.ActivateDevice(_descriptor, out devMgrObject, out rootItemObject);
            ScannerSourceKind requestedSource = _options.PreferredFormat?.Source ?? ScannerSourceKind.Auto;
            scanItem = SelectScanItem(rootItem, requestedSource, out ScannerSourceKind actualSource);

            ScannerFormat negotiatedFormat = NegotiateFormat(rootItem, scanItem, actualSource);
            _formatNegotiated(negotiatedFormat);

            transfer = scanItem as IWiaTransfer
                ?? throw WindowsScannerFaults.CreateException(ComNative.E_NOINTERFACE, "WIA scan item does not implement IWiaTransfer.");

            WiaTransferCallback callback = new(this, negotiatedFormat);
            lock (_gate)
            {
                _activeTransfer = transfer;
                _activeCallback = callback;
            }

            _worker.SignalStarted();

            if (!_worker.IsStopRequested)
            {
                int downloadResult = transfer.Download(0, callback);
                callback.FlushPendingPage();

                if (!_worker.IsStopRequested && downloadResult < 0 && downloadResult != WindowsWiaConstants.WIA_ERROR_PAPER_EMPTY)
                    WindowsScannerFaults.ThrowIfFailed(downloadResult, "WIA scanner page transfer failed.");

                if (!_worker.IsStopRequested)
                    _completed();
            }
        }
        finally
        {
            lock (_gate)
            {
                _activeTransfer = null;
                _activeCallback = null;
            }

            if (!ReferenceEquals(scanItem, rootItemObject))
                WindowsComInterop.Release(scanItem);

            WindowsComInterop.Release(rootItemObject);
            WindowsComInterop.Release(devMgrObject);
        }
    }

    private static IWiaItem2 SelectScanItem(IWiaItem2 rootItem, ScannerSourceKind requestedSource, out ScannerSourceKind selectedSource)
    {
        selectedSource = requestedSource == ScannerSourceKind.Auto ? ScannerSourceKind.Flatbed : requestedSource;

        if (rootItem.EnumChildItems(IntPtr.Zero, out IEnumWiaItem2 enumItems) < 0 || enumItems is null)
            return rootItem;

        IWiaItem2? fallbackItem = null;
        ScannerSourceKind fallbackSource = selectedSource;

        try
        {
            while (enumItems.Next(1, out IntPtr itemPointer, out uint fetched) == ComNative.S_OK &&
                   fetched > 0 &&
                   itemPointer != IntPtr.Zero)
            {
                IWiaItem2? child = WindowsComInterop.Wrap<IWiaItem2>(itemPointer);
                if (child is null)
                    continue;

                Guid category = Guid.Empty;
                _ = child.GetItemCategory(out category);

                bool isFeeder = category == WindowsWiaConstants.WiaItemCategoryFeeder;
                bool isFlatbed = category == WindowsWiaConstants.WiaItemCategoryFlatbed;

                ScannerSourceKind childKind = isFeeder
                    ? (requestedSource == ScannerSourceKind.FeederDuplex ? ScannerSourceKind.FeederDuplex : ScannerSourceKind.FeederSimplex)
                    : ScannerSourceKind.Flatbed;

                bool matches = requestedSource switch
                {
                    ScannerSourceKind.Flatbed => isFlatbed,
                    ScannerSourceKind.FeederSimplex or ScannerSourceKind.FeederDuplex => isFeeder,
                    _ => isFlatbed || isFeeder,
                };

                if (matches)
                {
                    WindowsComInterop.Release(fallbackItem);
                    selectedSource = childKind;
                    return child;
                }

                if (fallbackItem is null)
                {
                    fallbackItem = child;
                    fallbackSource = childKind;
                }
                else
                {
                    WindowsComInterop.Release(child);
                }
            }

            if (fallbackItem is not null)
            {
                selectedSource = fallbackSource;
                return fallbackItem;
            }

            return rootItem;
        }
        finally
        {
            WindowsComInterop.Release(enumItems);
        }
    }

    private ScannerFormat NegotiateFormat(IWiaItem2 rootItem, IWiaItem2 scanItem, ScannerSourceKind actualSource)
    {
        ScannerFormat preferred = _options.PreferredFormat
            ?? new ScannerFormat(actualSource, 300, 300, ScannerPixelFormat.Rgb24);

        if (rootItem is IWiaPropertyStorage rootProps)
        {
            int handlingSelect = actualSource switch
            {
                ScannerSourceKind.FeederSimplex => WindowsWiaConstants.FEEDER,
                ScannerSourceKind.FeederDuplex => WindowsWiaConstants.FEEDER | WindowsWiaConstants.DUPLEX,
                ScannerSourceKind.Flatbed => WindowsWiaConstants.FLATBED,
                _ => WindowsWiaConstants.AUTO_SOURCE,
            };

            _ = WindowsWiaPropertyHelper.TryWriteInt32(rootProps, WindowsWiaConstants.WIA_DPS_DOCUMENT_HANDLING_SELECT, handlingSelect);
            _ = WindowsWiaPropertyHelper.TryWriteInt32(rootProps, WindowsWiaConstants.WIA_DPS_PAGES, _options.SessionOptions.MaxPages);
        }

        int dpiX = preferred.DpiX;
        int dpiY = preferred.DpiY;
        ScannerRegion? region = preferred.Region;

        if (scanItem is IWiaPropertyStorage itemProps)
        {
            _ = WindowsWiaPropertyHelper.TryWriteInt32(itemProps, WindowsWiaConstants.WIA_IPS_XRES, dpiX);
            _ = WindowsWiaPropertyHelper.TryWriteInt32(itemProps, WindowsWiaConstants.WIA_IPS_YRES, dpiY);

            (int dataType, int depth) = preferred.PixelFormat switch
            {
                ScannerPixelFormat.BlackAndWhite1Bit => (WindowsWiaConstants.WIA_DATA_THRESHOLD, 1),
                ScannerPixelFormat.Grayscale8 => (WindowsWiaConstants.WIA_DATA_GRAYSCALE, 8),
                ScannerPixelFormat.Grayscale16 => (WindowsWiaConstants.WIA_DATA_GRAYSCALE, 16),
                _ => (WindowsWiaConstants.WIA_DATA_COLOR, 24),
            };

            _ = WindowsWiaPropertyHelper.TryWriteInt32(itemProps, WindowsWiaConstants.WIA_IPA_DATATYPE, dataType);
            _ = WindowsWiaPropertyHelper.TryWriteInt32(itemProps, WindowsWiaConstants.WIA_IPA_DEPTH, depth);
            _ = WindowsWiaPropertyHelper.TryWriteGuid(itemProps, WindowsWiaConstants.WIA_IPA_FORMAT, WindowsWiaConstants.WiaImgFmt_BMP);

            if (_options.SessionOptions.Brightness != 0.0)
                _ = WindowsWiaPropertyHelper.TryWriteInt32(itemProps, WindowsWiaConstants.WIA_IPS_BRIGHTNESS, (int)Math.Round(_options.SessionOptions.Brightness * 1000.0));

            if (_options.SessionOptions.Contrast != 0.0)
                _ = WindowsWiaPropertyHelper.TryWriteInt32(itemProps, WindowsWiaConstants.WIA_IPS_CONTRAST, (int)Math.Round(_options.SessionOptions.Contrast * 1000.0));

            if (region is { } crop)
            {
                int xPos = (int)Math.Round(crop.OffsetXMicrons / 25_400.0 * dpiX);
                int yPos = (int)Math.Round(crop.OffsetYMicrons / 25_400.0 * dpiY);
                int xExtent = Math.Max(1, (int)Math.Round(crop.WidthMicrons / 25_400.0 * dpiX));
                int yExtent = Math.Max(1, (int)Math.Round(crop.HeightMicrons / 25_400.0 * dpiY));

                _ = WindowsWiaPropertyHelper.TryWriteInt32(itemProps, WindowsWiaConstants.WIA_IPS_XPOS, xPos);
                _ = WindowsWiaPropertyHelper.TryWriteInt32(itemProps, WindowsWiaConstants.WIA_IPS_YPOS, yPos);
                _ = WindowsWiaPropertyHelper.TryWriteInt32(itemProps, WindowsWiaConstants.WIA_IPS_XEXTENT, xExtent);
                _ = WindowsWiaPropertyHelper.TryWriteInt32(itemProps, WindowsWiaConstants.WIA_IPS_YEXTENT, yExtent);
            }

            if (WindowsWiaPropertyHelper.TryReadInt32(itemProps, WindowsWiaConstants.WIA_IPS_XRES, out int actualDpiX) && actualDpiX > 0)
                dpiX = actualDpiX;

            if (WindowsWiaPropertyHelper.TryReadInt32(itemProps, WindowsWiaConstants.WIA_IPS_YRES, out int actualDpiY) && actualDpiY > 0)
                dpiY = actualDpiY;
        }

        return new ScannerFormat(actualSource, dpiX, dpiY, preferred.PixelFormat, region);
    }

    private void OnStreamCompleted(IStream stream, ScannerFormat negotiatedFormat)
    {
        byte[] rawBytes = ReadAllBytes(stream);
        if (rawBytes.Length == 0)
            return;

        ParsedScannerPage parsed = WindowsScannerImageParser.ParseDibOrBmp(rawBytes);
        ScannerFormat pageFormat = parsed.PixelFormat == negotiatedFormat.PixelFormat
            ? negotiatedFormat
            : new ScannerFormat(negotiatedFormat.Source, negotiatedFormat.DpiX, negotiatedFormat.DpiY, parsed.PixelFormat, negotiatedFormat.Region);

        long currentPageNumber = Interlocked.Increment(ref _pageNumber) - 1;
        ScannerPageFlags flags = ScannerPageFlags.None;

        if (_options.SessionOptions.DeliveryMode == ScannerDeliveryMode.LatestPagePreview)
            flags |= ScannerPageFlags.Preview;

        if (negotiatedFormat.Source == ScannerSourceKind.FeederDuplex && (currentPageNumber % 2 == 1))
            flags |= ScannerPageFlags.DuplexBackSide;

        if (parsed.PixelFormat != negotiatedFormat.PixelFormat)
        {
            Interlocked.Increment(ref _formatChangedCount);
            if (_options.SessionOptions.ReportFormatChanges)
                flags |= ScannerPageFlags.FormatChanged;
        }

        int maxPages = _options.SessionOptions.MaxPages;
        if (maxPages > 0 && currentPageNumber + 1 >= maxPages)
        {
            flags |= ScannerPageFlags.EndOfBatch;
            _activeCallback?.RequestCancel();
        }

        ScannerColorSpace colorSpace = parsed.PixelFormat is ScannerPixelFormat.Grayscale8 or ScannerPixelFormat.Grayscale16 or ScannerPixelFormat.BlackAndWhite1Bit
            ? ScannerColorSpace.LinearGray
            : ScannerColorSpace.SRgb;

        ScannerPageLease lease = new(
            parsed.PixelBytes,
            parsed.Width,
            parsed.Height,
            pageFormat,
            parsed.Planes,
            _clock.GetTimestamp(),
            currentPageNumber,
            flags,
            ScannerRotation.None,
            colorSpace);

        Interlocked.Increment(ref _capturedCount);
        Interlocked.Add(ref _bytesTransferred, parsed.PixelBytes.Length);
        _worker.TryEnqueue(lease);
        PublishStatistics();
    }

    private static unsafe byte[] ReadAllBytes(IStream stream)
    {
        if (stream.Seek(0, StreamSeekEnd, out ulong length) < 0 || length == 0)
            return [];

        if (stream.Seek(0, StreamSeekSet, out _) < 0)
            return [];

        byte[] buffer = new byte[checked((int)length)];
        fixed (byte* destination = buffer)
        {
            int offset = 0;
            while (offset < buffer.Length)
            {
                int hr = stream.Read((IntPtr)(destination + offset), (uint)(buffer.Length - offset), out uint bytesRead);
                if (hr < 0 || bytesRead == 0)
                    break;

                offset += (int)bytesRead;
            }
        }

        return buffer;
    }

    private void PublishStatistics()
    {
        lock (_statisticsGate)
        {
            InputDeliveryMetrics metrics = _worker.Metrics;
            _statisticsChanged(new ScannerCaptureStatistics(
                Interlocked.Read(ref _capturedCount),
                metrics.DequeuedCount,
                metrics.DroppedNewestCount,
                metrics.DroppedOldestCount,
                Interlocked.Read(ref _formatChangedCount),
                Interlocked.Read(ref _bytesTransferred),
                metrics.QueueDepth));
        }
    }

    private void HandleWorkerFailure(Exception exception)
    {
        if (exception is InputScannerException scannerException)
        {
            _faulted(scannerException.Fault, scannerException.FaultCode);
            return;
        }

        _faulted(
            new InputFault(InputErrorCategory.NativeFailure, "WIA scanner capture failed.", exception, nativeFacility: "WIA"),
            ScannerFaultCode.None);
    }

    private static Exception TranslateFailure(Exception exception) => exception is InputScannerException
        ? exception
        : new InputScannerException(new InputFault(InputErrorCategory.NativeFailure,
            "WIA scanner capture failed.", exception, nativeFacility: "WIA"));

    private void ReportCallbackFailure(Exception exception) =>
        _diagnostics.Write(new InputDiagnosticEvent(
            InputDiagnosticLevel.Error,
            "scanner.callback.failed",
            _clock.GetTimestamp(),
            _descriptor.Id,
            InputErrorCategory.NativeFailure,
            new Dictionary<string, string>
            {
                ["exception"] = exception.GetType().FullName ?? exception.GetType().Name,
                ["message"] = exception.Message,
            }));

    [GeneratedComClass]
    internal sealed partial class WiaTransferCallback(WindowsScannerCaptureSession session, ScannerFormat format) : IWiaTransferCallback
    {
        private IStream? _currentStream;
        private volatile bool _cancelRequested;

        public void RequestCancel() => _cancelRequested = true;

        public int GetNextStream(int flags, string itemName, string fullItemName, out IStream destination)
        {
            FlushPendingPage();

            int hr = ComNative.CreateStreamOnHGlobal(IntPtr.Zero, true, out destination);
            if (hr >= 0)
                _currentStream = destination;

            return hr;
        }

        public int TransferCallback(int flags, ref WiaTransferParams transferParams)
        {
            if (_cancelRequested || session._worker.IsStopRequested)
                return ComNative.S_FALSE;

            if (transferParams.ErrorStatus < 0)
                return transferParams.ErrorStatus;

            switch (transferParams.Message)
            {
                case WindowsWiaConstants.WIA_TRANSFER_MSG_STATUS:
                    double fraction = Math.Clamp(transferParams.PercentComplete / 100.0, 0.0, 1.0);
                    session._progressChanged(
                        Interlocked.Read(ref session._pageNumber),
                        fraction,
                        (long)transferParams.TransferredBytes);
                    break;

                case WindowsWiaConstants.WIA_TRANSFER_MSG_END_OF_STREAM:
                case WindowsWiaConstants.WIA_TRANSFER_MSG_NEW_PAGE:
                    FlushPendingPage();
                    break;
            }

            return _cancelRequested || session._worker.IsStopRequested ? ComNative.S_FALSE : ComNative.S_OK;
        }

        public void FlushPendingPage()
        {
            IStream? stream = Interlocked.Exchange(ref _currentStream, null);
            if (stream is null)
                return;

            try
            {
                session.OnStreamCompleted(stream, format);
            }
            finally
            {
                WindowsComInterop.Release(stream);
            }
        }
    }
}
