using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Xml.Linq;
using Broiler.Input.Camera;
using Broiler.Input.Camera.Windows;
using Broiler.Input.Keyboard;
using Broiler.Input.Keyboard.Windows;
using Broiler.Input.Legacy;
using Broiler.Input.Microphone;
using Broiler.Input.Microphone.Windows;
using Broiler.Input.Mouse;
using Broiler.Input.Mouse.Windows;
using Broiler.Input.Scanner;
using Broiler.Input.Scanner.Windows;
using Broiler.Input.Testing;
using Broiler.Input.Windows;

namespace Broiler.Input.Contract.Tests;

internal static partial class Program
{
    private static async Task MicrophoneSyntheticCaptureHasBoundedDelivery()
    {
        ManualInputClock clock = new();
        FakeMicrophoneProvider provider = new(clock);
        MicrophoneFormat format = new(48_000, 1, 16, MicrophoneSampleFormat.Pcm16);
        MicrophoneOpenOptions options = new(sessionOptions: new MicrophoneSessionOptions(new InputDeliveryOptions(2, InputDeliveryOverflowPolicy.DropOldest)));

        FakeMicrophoneInputDevice device = (FakeMicrophoneInputDevice)await provider.OpenAsync(provider.DefaultDescriptor, options).ConfigureAwait(false);

        await device.OpenAsync().ConfigureAwait(false);
        await device.StartAsync().ConfigureAwait(false);

        AssertTrue(device.TryCapture([1, 0], format), "First microphone packet should be accepted.");
        AssertTrue(device.TryCapture([2, 0], format, MicrophoneBufferFlags.Silent), "Second microphone packet should be accepted.");
        AssertTrue(device.TryCapture([3, 0], format, MicrophoneBufferFlags.Discontinuous), "Drop-oldest accepts the newest microphone packet.");

        MicrophoneCaptureStatistics statistics = device.CaptureStatistics;

        AssertEqual(3L, statistics.CapturedCount, "All attempted microphone packets are counted.");
        AssertEqual(1L, statistics.DroppedOldestCount, "Bounded microphone delivery drops the oldest packet.");
        AssertEqual(1L, statistics.SilentCount, "Silent microphone packets are counted.");
        AssertEqual(1L, statistics.DiscontinuousCount, "Discontinuous microphone packets are counted.");
        AssertEqual(2, statistics.QueueDepth, "Microphone queue depth is bounded by options.");

        AssertTrue(device.TryRead(out MicrophoneBufferLease? first), "First remaining microphone packet should be readable.");

        using (first)
        {
            AssertEqual((byte)2, first?.Memory.Span[0], "Oldest microphone packet was dropped.");
            AssertTrue((first?.Flags & MicrophoneBufferFlags.Silent) != 0, "Silence flag is preserved.");
        }

        MicrophoneBufferReadyEvent? ready = null;
        device.BufferReady += inputEvent => ready = inputEvent;

        AssertTrue(device.DrainNext(), "Second remaining microphone packet should be delivered.");
        AssertEqual((byte)3, ready?.Buffer.Memory.Span[0], "Microphone buffer event preserves packet memory.");
        AssertTrue((ready?.Buffer.Flags & MicrophoneBufferFlags.Discontinuous) != 0, "Discontinuity flag is preserved.");

        ready?.Buffer.Dispose();

        AssertEqual(2L, device.CaptureStatistics.DeliveredCount, "Read and drained microphone packets are counted as delivered.");
        await device.StopAsync().ConfigureAwait(false);
    }

    private static void MicrophoneLeaseDisposalInvalidatesMemory()
    {
        MicrophoneBufferLease lease = new([1, 2, 3, 4], new MicrophoneFormat(48_000, 1, 16, MicrophoneSampleFormat.Pcm16),
            new InputTimestamp(10, 1_000, "test"), 20, MicrophoneBufferFlags.None);

        AssertEqual(2, lease.FrameCount, "Microphone lease derives frame count from format.");
        lease.Dispose();
        AssertTrue(lease.IsDisposed, "Microphone lease reports disposal.");

        try
        {
            _ = lease.Memory;
            throw new InvalidOperationException("Disposed microphone leases should not expose memory.");
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private static void MicrophoneDefaultDeviceChangeIsObservable()
    {
        FakeMicrophoneProvider provider = new();
        List<InputDeviceChange> changes = [];
        provider.DeviceChanged += changes.Add;

        InputDeviceDescriptor second = provider.AddDevice("fake:microphone:communications", "Fake communications microphone");
        provider.SwitchDefaultDevice(second);

        AssertEqual(InputDeviceChangeKind.DefaultChanged, changes[^1].Kind, "Microphone default-device changes are observable.");
        AssertEqual(second.Id, changes[^1].Descriptor.Id, "Default-device change names the new endpoint.");
    }

    private static void WindowsMicrophoneContractsAreIsolated()
    {
        Assembly windowsMicrophone = typeof(WindowsMicrophoneProvider).Assembly;
        string[] references = [.. windowsMicrophone.GetReferencedAssemblies().Select(static reference => reference.Name ?? string.Empty)];

        AssertFalse(references.Any(static reference =>
                reference.Contains("NAudio", StringComparison.OrdinalIgnoreCase) ||
                reference.Contains("MediaFoundation", StringComparison.OrdinalIgnoreCase) ||
                reference.Contains("PresentationCore", StringComparison.OrdinalIgnoreCase) ||
                reference.Contains("WindowsForms", StringComparison.OrdinalIgnoreCase) ||
                reference.Contains("Broiler.Graphics", StringComparison.Ordinal)),
            "Windows microphone provider must not introduce encoder, playback, UI, or graphics dependencies.");
    }

    private static async Task CameraSyntheticPreviewKeepsLatestFrame()
    {
        FakeCameraProvider provider = new();
        CameraFormat format = new(2, 2, 30, 1, CameraPixelFormat.Bgra32);
        FakeCameraInputDevice device =
            (FakeCameraInputDevice)await provider.OpenAsync(provider.DefaultDescriptor, CameraOpenOptions.Default).ConfigureAwait(false);

        await device.OpenAsync().ConfigureAwait(false);
        await device.StartAsync().ConfigureAwait(false);

        AssertTrue(device.TryCapture([1, 0, 0, 0], format), "First preview frame should be accepted.");
        AssertTrue(device.TryCapture([2, 0, 0, 0], format), "Latest-frame preview accepts the replacement frame.");
        AssertTrue(device.TryCapture([3, 0, 0, 0], format, flags: CameraFrameFlags.Discontinuous), "Latest-frame preview accepts the newest frame.");

        CameraCaptureStatistics statistics = device.CaptureStatistics;
        AssertEqual(3L, statistics.CapturedCount, "All camera frames are counted.");
        AssertEqual(2L, statistics.DroppedOldestCount, "Preview mode drops older frames.");
        AssertEqual(1L, statistics.DiscontinuousCount, "Discontinuity is counted.");
        AssertEqual(1, statistics.QueueDepth, "Preview mode keeps bounded latest-frame memory.");

        AssertTrue(device.TryRead(out CameraFrameLease? frame), "Latest camera frame should be readable.");
        using (frame)
        {
            AssertEqual((byte)3, frame?.Memory.Span[0], "Only the newest preview frame remains.");
            AssertEqual(2, frame?.Format.Width, "Negotiated camera format is preserved on the frame.");
            AssertEqual(CameraFrameFlags.Discontinuous, frame?.Flags & CameraFrameFlags.Discontinuous, "Frame flags are preserved.");
        }

        await device.StopAsync().ConfigureAwait(false);
    }

    private static async Task CameraLossSensitiveModeDropsNewest()
    {
        FakeCameraProvider provider = new();
        CameraFormat format = new(2, 2, 30, 1, CameraPixelFormat.Bgra32);
        CameraOpenOptions options = new(sessionOptions: new CameraSessionOptions(new InputDeliveryOptions(2, InputDeliveryOverflowPolicy.DropNewest),
            CameraFrameDeliveryMode.LossSensitive));
        FakeCameraInputDevice device = (FakeCameraInputDevice)await provider.OpenAsync(provider.DefaultDescriptor, options).ConfigureAwait(false);

        await device.OpenAsync().ConfigureAwait(false);
        await device.StartAsync().ConfigureAwait(false);

        AssertTrue(device.TryCapture([1], format), "First loss-sensitive frame should be accepted.");
        AssertTrue(device.TryCapture([2], format), "Second loss-sensitive frame should be accepted.");
        AssertFalse(device.TryCapture([3], format), "Loss-sensitive mode should reject newest frame when full.");

        CameraCaptureStatistics statistics = device.CaptureStatistics;
        AssertEqual(1L, statistics.DroppedNewestCount, "Loss-sensitive overflow drops the newest frame.");
        AssertEqual(2, statistics.QueueDepth, "Loss-sensitive queue remains bounded.");

        AssertTrue(device.TryRead(out CameraFrameLease? first), "First loss-sensitive frame should remain queued.");
        using (first)
            AssertEqual((byte)1, first?.Memory.Span[0], "Oldest loss-sensitive frame is preserved.");
        AssertTrue(device.TryRead(out CameraFrameLease? second), "Second loss-sensitive frame should remain queued.");
        using (second)
            AssertEqual((byte)2, second?.Memory.Span[0], "Second loss-sensitive frame is preserved.");

        await device.StopAsync().ConfigureAwait(false);
    }

    private static void CameraFrameLeaseDisposalInvalidatesMemory()
    {
        CameraFrameLease frame = new([1, 2, 3, 4], new CameraFormat(1, 1, 30, 1, CameraPixelFormat.Bgra32), [new CameraFramePlane(0, 4, 4, 1, 1)],
            new InputTimestamp(10, 1_000, "test"), 7, CameraFrameFlags.FormatChanged, CameraRotation.Rotate90, CameraColorSpace.Rec709);

        AssertEqual(1, frame.Planes.Count, "Camera frame plane metadata is preserved.");
        AssertEqual(CameraRotation.Rotate90, frame.Rotation, "Camera rotation metadata is preserved.");
        AssertEqual(CameraColorSpace.Rec709, frame.ColorSpace, "Camera color metadata is preserved.");
        frame.Dispose();
        AssertTrue(frame.IsDisposed, "Camera frame lease reports disposal.");

        try
        {
            _ = frame.Memory;
            throw new InvalidOperationException("Disposed camera frames should not expose memory.");
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private static async Task CameraLatestFramePreviewAdapterKeepsOnlyLatest()
    {
        FakeCameraProvider provider = new();
        CameraFormat format = new(1, 1, 30, 1, CameraPixelFormat.Gray8);
        FakeCameraInputDevice device = (FakeCameraInputDevice)await provider.OpenAsync(provider.DefaultDescriptor,
            CameraOpenOptions.Default).ConfigureAwait(false);

        await device.OpenAsync().ConfigureAwait(false);
        await device.StartAsync().ConfigureAwait(false);

        using CameraLatestFramePreviewAdapter adapter = new(device);
        device.TryCapture([1], format);
        AssertTrue(device.DrainNext(), "First preview frame should drain into adapter.");
        device.TryCapture([2], format);
        AssertTrue(device.DrainNext(), "Second preview frame should replace adapter frame.");

        AssertTrue(adapter.TryAcquireLatest(out CameraFrameLease? latest), "Preview adapter should expose latest frame.");
        using (latest)
            AssertEqual((byte)2, latest?.Memory.Span[0], "Preview adapter keeps the latest frame only.");
        AssertFalse(adapter.TryAcquireLatest(out _), "Preview adapter transfers ownership when acquired.");

        await device.StopAsync().ConfigureAwait(false);
    }

    private static void WindowsCameraContractsAreIsolated()
    {
        Assembly windowsCamera = typeof(WindowsCameraProvider).Assembly;
        string[] references = [.. windowsCamera.GetReferencedAssemblies().Select(static reference => reference.Name ?? string.Empty)];

        AssertFalse(references.Any(static reference =>
                reference.Contains("AForge", StringComparison.OrdinalIgnoreCase) ||
                reference.Contains("OpenCv", StringComparison.OrdinalIgnoreCase) ||
                reference.Contains("NAudio", StringComparison.OrdinalIgnoreCase) ||
                reference.Contains("PresentationCore", StringComparison.OrdinalIgnoreCase) ||
                reference.Contains("WindowsForms", StringComparison.OrdinalIgnoreCase) ||
                reference.Contains("Broiler.Graphics", StringComparison.Ordinal)),
            "Windows camera provider must not introduce third-party, playback, UI, or graphics dependencies.");
    }

    private static async Task ScannerSyntheticBatchCaptureAndDuplexFlags()
    {
        ManualInputClock clock = new();
        FakeScannerProvider provider = new(clock);
        ScannerRegion region = ScannerRegion.FromMillimeters(0, 0, 210, 297);
        ScannerFormat format = new(ScannerSourceKind.FeederDuplex, 300, 300, ScannerPixelFormat.Rgb24, region);
        ScannerOpenOptions options = new(
            format,
            new ScannerSessionOptions(
                new InputDeliveryOptions(2, InputDeliveryOverflowPolicy.DropNewest),
                ScannerDeliveryMode.LossSensitiveBatch,
                maxPages: 4));

        FakeScannerInputDevice device = (FakeScannerInputDevice)await provider.OpenAsync(
            provider.DefaultDescriptor, options).ConfigureAwait(false);

        await device.OpenAsync().ConfigureAwait(false);
        await device.StartAsync().ConfigureAwait(false);
        AssertEqual(ScannerCaptureState.Scanning, device.CaptureState, "Scanner enters Scanning state when started.");

        ScannerProgressEvent? progress = null;
        device.ProgressChanged += inputEvent => progress = inputEvent;
        device.ReportProgress(0, 0.5, 128);
        AssertEqual(0.5, progress?.ProgressFraction, "Scanner progress event reports fraction.");

        AssertTrue(device.TryCapture([10, 20, 30], 1, 1, format, colorSpace: ScannerColorSpace.SRgb), "Front side page should be accepted.");
        AssertTrue(device.TryCapture([40, 50, 60], 1, 1, format, flags: ScannerPageFlags.DuplexBackSide | ScannerPageFlags.EndOfBatch,
            colorSpace: ScannerColorSpace.SRgb), "Back side page should be accepted and mark end of batch.");
        AssertFalse(device.TryCapture([70, 80, 90], 1, 1, format), "Loss-sensitive scanner queue rejects overflow page.");

        AssertEqual(ScannerCaptureState.Completed, device.CaptureState, "EndOfBatch transitions capture state to Completed.");

        ScannerCaptureStatistics statistics = device.CaptureStatistics;
        AssertEqual(3L, statistics.PagesCapturedCount, "All attempted scanner pages are counted.");
        AssertEqual(1L, statistics.PagesDroppedNewestCount, "Scanner queue overflow drops newest in loss-sensitive mode.");
        AssertEqual(9L, statistics.BytesTransferred, "Scanner transferred byte count is accumulated.");
        AssertEqual(2, statistics.QueueDepth, "Scanner queue remains bounded.");

        AssertTrue(device.TryRead(out ScannerPageLease? first), "First scanned page should be readable.");
        using (first)
        {
            AssertEqual(0L, first?.PageNumber, "First scanned page is page 0.");
            AssertEqual((byte)10, first?.Memory.Span[0], "First scanned page preserves pixel bytes.");
            AssertEqual(210_000, first?.Format.Region?.WidthMicrons, "Physical region width in microns is preserved.");
        }

        ScannerPageReadyEvent? ready = null;
        device.PageReady += inputEvent => ready = inputEvent;
        AssertTrue(device.DrainNext(), "Second scanned page should drain via PageReady.");
        AssertEqual(1L, ready?.Page.PageNumber, "Second scanned page is page 1.");
        AssertEqual(ScannerPageFlags.DuplexBackSide | ScannerPageFlags.EndOfBatch, ready?.Page.Flags, "Duplex and EndOfBatch flags are preserved.");
        ready?.Page.Dispose();

        await device.StopAsync().ConfigureAwait(false);
    }

    private static async Task ScannerPreviewModeAndAdapterKeepsLatestPage()
    {
        FakeScannerProvider provider = new();
        ScannerFormat format = new(ScannerSourceKind.Flatbed, 75, 75, ScannerPixelFormat.Grayscale8);
        ScannerOpenOptions options = new(format, ScannerSessionOptions.PreviewDefault);
        FakeScannerInputDevice device = (FakeScannerInputDevice)await provider.OpenAsync(
            provider.DefaultDescriptor, options).ConfigureAwait(false);

        await device.OpenAsync().ConfigureAwait(false);
        await device.StartAsync().ConfigureAwait(false);

        using ScannerPreviewAdapter adapter = new(device);
        AssertTrue(device.TryCapture([1], 1, 1, format), "First prescan pass is accepted.");
        AssertTrue(device.DrainNext(), "First prescan drains into preview adapter.");
        AssertTrue(device.TryCapture([2], 1, 1, format), "Updated prescan pass is accepted.");
        AssertTrue(device.DrainNext(), "Updated prescan replaces adapter page.");

        AssertTrue(adapter.TryAcquireLatest(out ScannerPageLease? latest), "Scanner preview adapter exposes latest page.");
        using (latest)
        {
            AssertEqual((byte)2, latest?.Memory.Span[0], "Scanner preview adapter keeps the latest page only.");
            AssertEqual(ScannerPageFlags.Preview, latest?.Flags & ScannerPageFlags.Preview, "Preview flag is set on preview session pages.");
        }

        AssertFalse(adapter.TryAcquireLatest(out _), "Scanner preview adapter transfers ownership when acquired.");
        await device.StopAsync().ConfigureAwait(false);
    }

    private static void ScannerPageLeaseDisposalInvalidatesMemory()
    {
        ScannerFormat format = new(ScannerSourceKind.Flatbed, 600, 600, ScannerPixelFormat.Grayscale16);
        ScannerPageLease page = new([1, 2], 1, 1, format, [new ScannerImagePlane(0, 2, 2, 1, 1)],
            new InputTimestamp(10, 1_000, "test"), 3, ScannerPageFlags.FormatChanged,
            ScannerRotation.Rotate180, ScannerColorSpace.LinearGray);

        AssertEqual(1, page.Planes.Count, "Scanner image plane metadata is preserved.");
        AssertEqual(ScannerRotation.Rotate180, page.Rotation, "Scanner rotation metadata is preserved.");
        AssertEqual(ScannerColorSpace.LinearGray, page.ColorSpace, "Scanner color space metadata is preserved.");
        page.Dispose();
        AssertTrue(page.IsDisposed, "Scanner page lease reports disposal.");

        try
        {
            _ = page.Memory;
            throw new InvalidOperationException("Disposed scanner pages should not expose memory.");
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private static async Task ScannerPaperJamFaultTransition()
    {
        FakeScannerProvider provider = new();
        FakeScannerInputDevice device = (FakeScannerInputDevice)await provider.OpenAsync(
            provider.DefaultDescriptor, ScannerOpenOptions.Default).ConfigureAwait(false);

        await device.OpenAsync().ConfigureAwait(false);
        await device.StartAsync().ConfigureAwait(false);

        ScannerCaptureStateChangedEvent? stateChanged = null;
        device.CaptureStateChanged += inputEvent => stateChanged = inputEvent;

        device.SimulatePaperJam();
        AssertEqual(ScannerCaptureState.Faulted, device.CaptureState, "Paper jam transitions scanner capture state to Faulted.");
        AssertEqual(ScannerFaultCode.PaperJam, device.LastFaultCode, "Scanner records PaperJam fault code.");
        AssertEqual(ScannerFaultCode.PaperJam, stateChanged?.FaultCode, "CaptureStateChanged event carries PaperJam fault code.");
        AssertEqual(InputDeviceState.Faulted, device.State, "Device state transitions to Faulted.");
    }

    private static void WindowsScannerContractsAreIsolated()
    {
        Assembly windowsScanner = typeof(WindowsScannerProvider).Assembly;
        string[] references = [.. windowsScanner.GetReferencedAssemblies().Select(static reference => reference.Name ?? string.Empty)];

        AssertFalse(references.Any(static reference =>
                reference.Contains("Camera", StringComparison.OrdinalIgnoreCase) ||
                reference.Contains("Microphone", StringComparison.OrdinalIgnoreCase) ||
                reference.Contains("Keyboard", StringComparison.OrdinalIgnoreCase) ||
                reference.Contains("Mouse", StringComparison.OrdinalIgnoreCase) ||
                reference.Contains("PresentationCore", StringComparison.OrdinalIgnoreCase) ||
                reference.Contains("WindowsForms", StringComparison.OrdinalIgnoreCase) ||
                reference.Contains("Broiler.Graphics", StringComparison.Ordinal)),
            "Windows scanner provider must not reference other device kinds, UI frameworks, or graphics codecs.");

        string id1 = WindowsScannerDeviceEnumerator.ToStableInputId(@"\\?\usb#vid_04a9&pid_190d#123456");
        string id2 = WindowsScannerDeviceEnumerator.ToStableInputId(@"\\?\usb#vid_04a9&pid_190d#123456");
        AssertEqual(id1, id2, "Windows WIA scanner IDs are deterministic.");
        AssertTrue(id1.StartsWith("windows:wia:scanner:", StringComparison.Ordinal), "Windows WIA scanner IDs use the wia prefix.");
    }

    private static void WindowsScannerFaultMappingMatchesWiaCodes()
    {
        InputScannerException paperJam = WindowsScannerFaults.CreateException(WindowsWiaConstants.WIA_ERROR_PAPER_JAM, "Paper jam.");
        AssertEqual(InputErrorCategory.CaptureDiscontinuity, paperJam.Fault.Category, "WIA_ERROR_PAPER_JAM maps to CaptureDiscontinuity.");
        AssertEqual(ScannerFaultCode.PaperJam, paperJam.FaultCode, "WIA_ERROR_PAPER_JAM maps to ScannerFaultCode.PaperJam.");

        InputScannerException paperEmpty = WindowsScannerFaults.CreateException(WindowsWiaConstants.WIA_ERROR_PAPER_EMPTY, "Feeder empty.");
        AssertEqual(InputErrorCategory.CaptureDiscontinuity, paperEmpty.Fault.Category, "WIA_ERROR_PAPER_EMPTY maps to CaptureDiscontinuity.");
        AssertEqual(ScannerFaultCode.FeederEmpty, paperEmpty.FaultCode, "WIA_ERROR_PAPER_EMPTY maps to ScannerFaultCode.FeederEmpty.");

        InputScannerException coverOpen = WindowsScannerFaults.CreateException(WindowsWiaConstants.WIA_ERROR_COVER_OPEN, "Cover open.");
        AssertEqual(InputErrorCategory.CaptureDiscontinuity, coverOpen.Fault.Category, "WIA_ERROR_COVER_OPEN maps to CaptureDiscontinuity.");
        AssertEqual(ScannerFaultCode.CoverOpen, coverOpen.FaultCode, "WIA_ERROR_COVER_OPEN maps to ScannerFaultCode.CoverOpen.");

        InputScannerException multiFeed = WindowsScannerFaults.CreateException(WindowsWiaConstants.WIA_ERROR_MULTI_FEED, "Multi-feed.");
        AssertEqual(InputErrorCategory.CaptureDiscontinuity, multiFeed.Fault.Category, "WIA_ERROR_MULTI_FEED maps to CaptureDiscontinuity.");
        AssertEqual(ScannerFaultCode.MultiFeedDetected, multiFeed.FaultCode, "WIA_ERROR_MULTI_FEED maps to ScannerFaultCode.MultiFeedDetected.");

        InputScannerException lampOff = WindowsScannerFaults.CreateException(WindowsWiaConstants.WIA_ERROR_LAMP_OFF, "Lamp off.");
        AssertEqual(InputErrorCategory.NativeFailure, lampOff.Fault.Category, "WIA_ERROR_LAMP_OFF maps to NativeFailure.");
        AssertEqual(ScannerFaultCode.LampFailure, lampOff.FaultCode, "WIA_ERROR_LAMP_OFF maps to ScannerFaultCode.LampFailure.");

        InputScannerException busy = WindowsScannerFaults.CreateException(WindowsWiaConstants.WIA_ERROR_WARMING_UP, "Warming up.");
        AssertEqual(InputErrorCategory.DeviceBusy, busy.Fault.Category, "WIA_ERROR_WARMING_UP maps to DeviceBusy.");

        InputScannerException offline = WindowsScannerFaults.CreateException(WindowsWiaConstants.WIA_ERROR_OFFLINE, "Offline.");
        AssertEqual(InputErrorCategory.DeviceRemoved, offline.Fault.Category, "WIA_ERROR_OFFLINE maps to DeviceRemoved.");
    }

    private static void WindowsScannerDibParserNormalizesTopDownPlanes()
    {
        // Construct a 2x2 bottom-up 24-bit BMP ("BM" + 14-byte header + 40-byte BITMAPINFOHEADER + 2 padded 8-byte scanlines).
        // Bottom row (row 1 in top-down order): pixel(0,1)=BGR(30,20,10) -> RGB(10,20,30), pixel(1,1)=BGR(60,50,40) -> RGB(40,50,60)
        // Top row (row 0 in top-down order):    pixel(0,0)=BGR(90,80,70) -> RGB(70,80,90), pixel(1,0)=BGR(120,110,100) -> RGB(100,110,120)
        byte[] bmp = new byte[14 + 40 + 16];
        bmp[0] = (byte)'B';
        bmp[1] = (byte)'M';
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(bmp.AsSpan(2, 4), bmp.Length);
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(bmp.AsSpan(10, 4), 54);
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(bmp.AsSpan(14, 4), 40);
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(bmp.AsSpan(18, 4), 2); // width = 2
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(bmp.AsSpan(22, 4), 2); // height = +2 (bottom-up)
        System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(bmp.AsSpan(26, 2), 1); // planes
        System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(bmp.AsSpan(28, 2), 24); // 24 bpp

        // Bottom scanline at offset 54 (8 bytes: 6 pixel bytes + 2 padding)
        bmp[54] = 30; bmp[55] = 20; bmp[56] = 10;
        bmp[57] = 60; bmp[58] = 50; bmp[59] = 40;

        // Top scanline at offset 62 (8 bytes: 6 pixel bytes + 2 padding)
        bmp[62] = 90; bmp[63] = 80; bmp[64] = 70;
        bmp[65] = 120; bmp[66] = 110; bmp[67] = 100;

        ParsedScannerPage parsed = WindowsScannerImageParser.ParseDibOrBmp(bmp);
        AssertEqual(2, parsed.Width, "Parsed BMP width is 2.");
        AssertEqual(2, parsed.Height, "Parsed BMP height is 2.");
        AssertEqual(ScannerPixelFormat.Rgb24, parsed.PixelFormat, "24-bit DIB normalizes to Rgb24.");
        AssertEqual(1, parsed.Planes.Count, "Single packed plane is produced.");
        AssertEqual(6, parsed.Planes[0].Stride, "Top-down RGB24 stride removes DIB row padding.");
        AssertEqual(12, parsed.PixelBytes.Length, "Output byte buffer has 2 rows * 6 bytes.");

        // Top row should be first, with BGR converted to RGB:
        AssertEqual((byte)70, parsed.PixelBytes[0], "Top-left R channel.");
        AssertEqual((byte)80, parsed.PixelBytes[1], "Top-left G channel.");
        AssertEqual((byte)90, parsed.PixelBytes[2], "Top-left B channel.");
        AssertEqual((byte)100, parsed.PixelBytes[3], "Top-right R channel.");
        AssertEqual((byte)10, parsed.PixelBytes[6], "Bottom-left R channel.");
        AssertEqual((byte)40, parsed.PixelBytes[9], "Bottom-right R channel.");
    }
}
