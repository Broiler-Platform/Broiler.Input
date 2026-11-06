using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Broiler.Input.Scanner;

namespace Broiler.Input.Testing;

public sealed class FakeScannerInputDevice : ScannerInputDevice
{
    private readonly Queue<ScannerPageLease> _queue = new();
    private readonly ScannerOpenOptions _options;
    private long _capturedCount;
    private long _deliveredCount;
    private long _droppedNewestCount;
    private long _droppedOldestCount;
    private long _formatChangedCount;
    private long _bytesTransferred;
    private long _pageNumber;

    public FakeScannerInputDevice(InputDeviceDescriptor descriptor, ScannerOpenOptions options, ManualInputClock clock,
        IInputDiagnosticSink? diagnostics = null) : base(descriptor, clock, diagnostics)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        if (_options.PreferredFormat is not null)
            SetNegotiatedFormat(_options.PreferredFormat);
    }

    public bool TryCapture(byte[] bytes, int width, int height, ScannerFormat format,
        IReadOnlyList<ScannerImagePlane>? planes = null, ScannerPageFlags flags = ScannerPageFlags.None,
        ScannerRotation rotation = ScannerRotation.None, ScannerColorSpace colorSpace = ScannerColorSpace.Unknown)
    {
        ThrowIfDisposed();

        ArgumentNullException.ThrowIfNull(bytes);
        ArgumentNullException.ThrowIfNull(format);

        if (NegotiatedFormat is null || NegotiatedFormat != format)
        {
            SetNegotiatedFormat(format);
            if (NegotiatedFormat is not null && _capturedCount > 0 && _options.SessionOptions.ReportFormatChanges)
                flags |= ScannerPageFlags.FormatChanged;
        }

        if (_options.SessionOptions.DeliveryMode == ScannerDeliveryMode.LatestPagePreview)
            flags |= ScannerPageFlags.Preview;

        if ((flags & ScannerPageFlags.FormatChanged) != 0)
            _formatChangedCount++;

        ScannerPageLease lease = new(
            bytes,
            width,
            height,
            format,
            planes ?? [new ScannerImagePlane(0, bytes.Length, 0, width, height)],
            Clock.GetTimestamp(),
            _pageNumber++,
            flags,
            rotation,
            colorSpace);

        _capturedCount++;
        _bytesTransferred += bytes.Length;
        bool accepted = TryEnqueue(lease);
        PublishStatistics();

        if ((flags & ScannerPageFlags.EndOfBatch) != 0)
            MarkCaptureCompleted();

        return accepted;
    }

    public bool TryRead(out ScannerPageLease? page)
    {
        ThrowIfDisposed();
        if (_queue.Count == 0)
        {
            page = null;
            return false;
        }

        page = _queue.Dequeue();
        _deliveredCount++;
        PublishStatistics();
        return true;
    }

    public bool DrainNext()
    {
        if (!TryRead(out ScannerPageLease? page) || page is null)
            return false;

        RaisePageReady(page);
        return true;
    }

    public void ReportProgress(long pageNumber, double progressFraction, long bytesTransferred)
    {
        ManualAdvance();
        RaiseProgressChanged(pageNumber, progressFraction, bytesTransferred);
    }

    public void CompleteBatch()
    {
        ManualAdvance();
        MarkCaptureCompleted();
    }

    public void SimulatePaperJam() =>
        MarkCaptureFaulted(
            new InputFault(InputErrorCategory.CaptureDiscontinuity, "Paper jam detected in scanner feeder."),
            ScannerFaultCode.PaperJam);

    public void SimulateInvalidation() =>
        MarkCaptureInvalidated(
            new InputFault(InputErrorCategory.DeviceRemoved, "Fake scanner source was invalidated."));

    public override async ValueTask OpenAsync(CancellationToken cancellationToken = default)
    {
        ManualAdvance();
        await base.OpenAsync(cancellationToken).ConfigureAwait(false);
    }

    public override async ValueTask StartAsync(CancellationToken cancellationToken = default)
    {
        ManualAdvance();
        TransitionCaptureTo(ScannerCaptureState.Starting);
        await base.StartAsync(cancellationToken).ConfigureAwait(false);
        TransitionCaptureTo(ScannerCaptureState.Scanning);
    }

    public override async ValueTask StopAsync(CancellationToken cancellationToken = default)
    {
        ManualAdvance();
        TransitionCaptureTo(ScannerCaptureState.Stopping);
        await base.StopAsync(cancellationToken).ConfigureAwait(false);
        TransitionCaptureTo(ScannerCaptureState.Stopped);
    }

    public override async ValueTask CloseAsync(CancellationToken cancellationToken = default)
    {
        ManualAdvance();
        await base.CloseAsync(cancellationToken).ConfigureAwait(false);
        TransitionCaptureTo(ScannerCaptureState.Stopped);
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

    private bool TryEnqueue(ScannerPageLease page)
    {
        InputDeliveryOptions deliveryOptions = _options.SessionOptions.DeliveryOptions;
        if (_queue.Count < deliveryOptions.Capacity)
        {
            _queue.Enqueue(page);
            return true;
        }

        switch (deliveryOptions.OverflowPolicy)
        {
            case InputDeliveryOverflowPolicy.DropNewest:
                _droppedNewestCount++;
                page.Dispose();
                return false;

            case InputDeliveryOverflowPolicy.DropOldest:
                _queue.Dequeue().Dispose();
                _droppedOldestCount++;
                _queue.Enqueue(page);
                return true;

            case InputDeliveryOverflowPolicy.KeepLatest:
                _droppedOldestCount += _queue.Count;
                while (_queue.Count > 0)
                    _queue.Dequeue().Dispose();
                _queue.Enqueue(page);
                return true;

            case InputDeliveryOverflowPolicy.Fail:
                page.Dispose();
                throw new InvalidOperationException("The fake scanner delivery queue is full.");

            default:
                page.Dispose();
                throw new ArgumentOutOfRangeException(nameof(deliveryOptions.OverflowPolicy));
        }
    }

    private void PublishStatistics()
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

    private void ManualAdvance()
    {
        if (Clock is ManualInputClock manual)
            manual.Advance(1);
    }
}
