using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Broiler.Input.Android;

namespace Broiler.Input.Scanner.Android;

/// <summary>
/// A <see cref="ScannerInputDevice"/> driven by the Android host Activity (such as system document scanner intents or network eSCL bridges).
/// </summary>
public sealed class AndroidScannerInputDevice(
    InputDeviceDescriptor descriptor,
    AndroidUptimeInputClock clock,
    ScannerOpenOptions? options = null,
    IInputDiagnosticSink? diagnostics = null)
    : ScannerInputDevice(descriptor, clock, diagnostics), IAndroidInputDevice
{
    private readonly AndroidUptimeInputClock _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    private readonly ScannerOpenOptions _options = options ?? ScannerOpenOptions.Default;
    private readonly Lock _gate = new();
    private readonly Queue<ScannerPageLease> _queue = new();

    private long _pageNumber;
    private long _capturedCount;
    private long _deliveredCount;
    private long _droppedNewestCount;
    private long _droppedOldestCount;
    private long _formatChangedCount;
    private long _bytesTransferred;

    public override async ValueTask StartAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfDisposed();

        if (State == InputDeviceState.Running)
            return;

        if (State is InputDeviceState.Discovered or InputDeviceState.Closed)
            throw new InvalidOperationException("The Android scanner device must be open before it can be started.");

        TransitionCaptureTo(ScannerCaptureState.Starting);

        ScannerFormat format = _options.PreferredFormat
            ?? new ScannerFormat(ScannerSourceKind.Flatbed, 300, 300, ScannerPixelFormat.Rgba32);
        SetNegotiatedFormat(format);

        lock (_gate)
        {
            _pageNumber = 0;
            _capturedCount = 0;
            _deliveredCount = 0;
            _droppedNewestCount = 0;
            _droppedOldestCount = 0;
            _formatChangedCount = 0;
            _bytesTransferred = 0;
            ClearQueueLocked();
            PublishStatisticsLocked();
        }

        await base.StartAsync(cancellationToken).ConfigureAwait(false);
        TransitionCaptureTo(ScannerCaptureState.Scanning);
    }

    public override async ValueTask StopAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfDisposed();

        if (State != InputDeviceState.Running && CaptureState != ScannerCaptureState.Scanning)
            return;

        TransitionCaptureTo(ScannerCaptureState.Stopping);
        lock (_gate)
        {
            ClearQueueLocked();
            PublishStatisticsLocked();
        }

        await base.StopAsync(cancellationToken).ConfigureAwait(false);
        TransitionCaptureTo(ScannerCaptureState.Stopped);
    }

    public override async ValueTask CloseAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            ClearQueueLocked();
            PublishStatisticsLocked();
        }

        await base.CloseAsync(cancellationToken).ConfigureAwait(false);
        TransitionCaptureTo(ScannerCaptureState.Stopped);
    }

    /// <summary>
    /// Reports intermediate scan progress from the Android host.
    /// </summary>
    public void ReportProgress(
        long pageNumber,
        double progressFraction,
        long bytesTransferred,
        long eventTimeMilliseconds = 0)
    {
        ThrowIfDisposed();
        if (eventTimeMilliseconds > 0)
            _clock.Observe(eventTimeMilliseconds);

        if (!CanDeliverInput)
            return;

        RaiseProgressChanged(pageNumber, progressFraction, bytesTransferred);
    }

    /// <summary>
    /// Delivers a scanned page buffer acquired by the Android host into the bounded scanner pipeline.
    /// </summary>
    public bool DeliverScannedPage(
        byte[] pixelBytes,
        int width,
        int height,
        ScannerFormat? format = null,
        IEnumerable<ScannerImagePlane>? planes = null,
        ScannerPageFlags flags = ScannerPageFlags.None,
        ScannerRotation rotation = ScannerRotation.None,
        ScannerColorSpace colorSpace = ScannerColorSpace.SRgb,
        long eventTimeMilliseconds = 0)
    {
        ArgumentNullException.ThrowIfNull(pixelBytes);
        ThrowIfDisposed();

        if (eventTimeMilliseconds > 0)
            _clock.Observe(eventTimeMilliseconds);

        if (!CanDeliverInput || CaptureState != ScannerCaptureState.Scanning)
            return false;

        ScannerFormat negotiated = NegotiatedFormat
            ?? _options.PreferredFormat
            ?? new ScannerFormat(ScannerSourceKind.Flatbed, 300, 300, ScannerPixelFormat.Rgba32);
        ScannerFormat pageFormat = format ?? negotiated;

        int defaultStride = Math.Max(1, pixelBytes.Length / Math.Max(1, height));
        IEnumerable<ScannerImagePlane> resolvedPlanes = planes
            ?? [new ScannerImagePlane(0, pixelBytes.Length, defaultStride, width, height)];

        ScannerPageLease? toDeliver = null;
        bool completedBatch = false;

        lock (_gate)
        {
            long currentPageNumber = _pageNumber++;
            _capturedCount++;
            _bytesTransferred += pixelBytes.Length;

            if (_options.SessionOptions.DeliveryMode == ScannerDeliveryMode.LatestPagePreview)
                flags |= ScannerPageFlags.Preview;

            if (pageFormat.Source == ScannerSourceKind.FeederDuplex && (currentPageNumber % 2 == 1))
                flags |= ScannerPageFlags.DuplexBackSide;

            if (pageFormat != negotiated)
            {
                _formatChangedCount++;
                if (_options.SessionOptions.ReportFormatChanges)
                    flags |= ScannerPageFlags.FormatChanged;
                SetNegotiatedFormat(pageFormat);
            }

            int maxPages = _options.SessionOptions.MaxPages;
            if (maxPages > 0 && currentPageNumber + 1 >= maxPages)
                flags |= ScannerPageFlags.EndOfBatch;

            InputTimestamp timestamp = eventTimeMilliseconds > 0
                ? new InputTimestamp(eventTimeMilliseconds, 1_000, AndroidUptimeInputClock.ClockName)
                : _clock.GetTimestamp();

            ScannerPageLease lease = new(
                pixelBytes,
                width,
                height,
                pageFormat,
                resolvedPlanes,
                timestamp,
                currentPageNumber,
                flags,
                rotation,
                colorSpace);

            InputDeliveryOptions delivery = _options.SessionOptions.DeliveryOptions;
            if (_queue.Count >= delivery.Capacity)
            {
                switch (delivery.OverflowPolicy)
                {
                    case InputDeliveryOverflowPolicy.DropNewest:
                        _droppedNewestCount++;
                        lease.Dispose();
                        PublishStatisticsLocked();
                        return false;

                    case InputDeliveryOverflowPolicy.DropOldest:
                        _queue.Dequeue().Dispose();
                        _droppedOldestCount++;
                        break;

                    case InputDeliveryOverflowPolicy.KeepLatest:
                        _droppedOldestCount += _queue.Count;
                        ClearQueueLocked();
                        break;

                    case InputDeliveryOverflowPolicy.Fail:
                        lease.Dispose();
                        PublishStatisticsLocked();
                        throw new InvalidOperationException("The Android scanner page delivery queue is full.");
                }
            }

            _queue.Enqueue(lease);
            if (_queue.Count == 1)
            {
                toDeliver = _queue.Dequeue();
                _deliveredCount++;
            }

            completedBatch = (flags & ScannerPageFlags.EndOfBatch) != 0;
            PublishStatisticsLocked();
        }

        if (toDeliver is not null)
            RaisePageReady(toDeliver);

        if (completedBatch)
            MarkCaptureCompleted();

        return true;
    }

    /// <summary>
    /// Marks the current multi-page scan batch completed when the Android host finishes returning pages.
    /// </summary>
    public void CompleteBatch(long eventTimeMilliseconds = 0)
    {
        ThrowIfDisposed();
        if (eventTimeMilliseconds > 0)
            _clock.Observe(eventTimeMilliseconds);

        if (CaptureState == ScannerCaptureState.Scanning)
            MarkCaptureCompleted();
    }

    /// <summary>
    /// Reports a hardware or host scanner fault (such as paper jam, feeder empty, or user cancellation).
    /// </summary>
    public void ReportFault(
        ScannerFaultCode faultCode,
        string message,
        InputErrorCategory category = InputErrorCategory.CaptureDiscontinuity)
    {
        ThrowIfDisposed();
        InputFault fault = new(category, message, nativeFacility: "AndroidScanner");

        lock (_gate)
        {
            ClearQueueLocked();
            PublishStatisticsLocked();
        }

        if (category == InputErrorCategory.DeviceRemoved)
            MarkCaptureInvalidated(fault, faultCode);
        else
            MarkCaptureFaulted(fault, faultCode);
    }

    void IAndroidInputDevice.NotifyRemoved(InputFault fault)
    {
        lock (_gate)
        {
            ClearQueueLocked();
            PublishStatisticsLocked();
        }

        MarkCaptureInvalidated(fault, ScannerFaultCode.None);
    }

    void IAndroidInputDevice.NotifyCaptureLost(string reason)
    {
        if (CaptureState != ScannerCaptureState.Scanning)
            return;

        lock (_gate)
        {
            ClearQueueLocked();
            PublishStatisticsLocked();
        }

        EmitDiagnostic(
            InputDiagnosticLevel.Information,
            "input.scanner.capture-lost",
            new Dictionary<string, string> { ["reason"] = reason });

        MarkCaptureFaulted(AndroidInputFaults.CaptureLost(reason), ScannerFaultCode.TransferAborted);
    }

    private void ClearQueueLocked()
    {
        while (_queue.TryDequeue(out ScannerPageLease? lease))
            lease.Dispose();
    }

    private void PublishStatisticsLocked()
    {
        SetCaptureStatistics(new ScannerCaptureStatistics(
            _capturedCount,
            _deliveredCount,
            _droppedNewestCount,
            _droppedOldestCount,
            _formatChangedCount,
            _bytesTransferred,
            _queue.Count));
    }
}
