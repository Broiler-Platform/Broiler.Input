using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Broiler.Input.Scanner;
using Broiler.Input.Scanner.Linux;

namespace Broiler.Input.Linux.Tests;

internal static partial class Program
{
    private static async Task LinuxScannerDiscoveryRedactsNativeDeviceNameAndTracksChanges()
    {
        FakeLinuxSaneInterop interop = new();
        interop.Devices.Add(new SaneDeviceInfo(
            "pixma:/dev/bus/usb/001/005",
            "Canon",
            "PIXMA TS8300",
            "multi-function peripheral"));

        LinuxScannerProvider provider = new(interop);
        List<InputDeviceChange> changes = [];
        provider.DeviceChanged += change => changes.Add(change);

        IReadOnlyList<InputDeviceDescriptor> devices = await provider.GetDevicesAsync().ConfigureAwait(false);
        AssertEqual(1, devices.Count, "Single SANE scanner device should be discovered.");
        AssertEqual("Canon PIXMA TS8300", devices[0].DisplayName, "Display name combines vendor and model.");
        AssertTrue(devices[0].Id.Value.StartsWith("linux:sane:scanner:", StringComparison.Ordinal),
            "SANE device ID uses the linux:sane:scanner prefix.");
        AssertFalse(devices[0].Capabilities.Any(static c => c.Value.Contains("/dev/bus/usb", StringComparison.Ordinal)),
            "Descriptor capabilities must not leak raw native device paths.");
        AssertEqual("sane", CapabilityValue(devices[0], "scanner.capture.source"),
            "Scanner capture source capability is 'sane'.");

        await provider.RefreshDevicesAsync().ConfigureAwait(false);
        AssertEqual(1, changes.Count, "First refresh reports Added.");
        AssertEqual(InputDeviceChangeKind.Added, changes[0].Kind, "First change is Added.");

        interop.Devices.Clear();
        await provider.RefreshDevicesAsync().ConfigureAwait(false);
        AssertEqual(2, changes.Count, "Second refresh reports Removed.");
        AssertEqual(InputDeviceChangeKind.Removed, changes[1].Kind, "Second change is Removed.");
    }

    private static async Task LinuxScannerNegotiatesOptionsAndCompletesMultiPageBatchOnNoDocs()
    {
        FakeLinuxSaneInterop interop = new();
        interop.Devices.Add(new SaneDeviceInfo("airscan:e0:OfficeADF", "Brother", "ADS-2700W", "sheetfed scanner"));
        interop.PagesToProduce.Enqueue(new FakeSanePage(
            new SaneParameters(SaneFrame.Rgb, true, BytesPerLine: 6, PixelsPerLine: 2, Lines: 1, Depth: 8),
            [10, 20, 30, 40, 50, 60]));
        interop.PagesToProduce.Enqueue(new FakeSanePage(
            new SaneParameters(SaneFrame.Rgb, true, BytesPerLine: 6, PixelsPerLine: 2, Lines: 1, Depth: 8),
            [70, 80, 90, 100, 110, 120]));

        LinuxScannerProvider provider = new(interop);
        IReadOnlyList<InputDeviceDescriptor> devices = await provider.GetDevicesAsync().ConfigureAwait(false);

        ScannerRegion region = ScannerRegion.FromMillimeters(10, 15, 200, 280);
        ScannerFormat format = new(ScannerSourceKind.FeederDuplex, 300, 300, ScannerPixelFormat.Rgb24, region);
        ScannerSessionOptions sessionOptions = new(
            new InputDeliveryOptions(4, InputDeliveryOverflowPolicy.DropNewest),
            ScannerDeliveryMode.LossSensitiveBatch,
            maxPages: 0,
            brightness: 0.25,
            contrast: -0.5);
        ScannerOpenOptions openOptions = new(format, sessionOptions);

        using ScannerInputDevice device = await provider.OpenAsync(devices[0], openOptions).ConfigureAwait(false);
        await device.OpenAsync().ConfigureAwait(false);

        List<(long PageNumber, ScannerPageFlags Flags, byte FirstByte)> deliveredPages = [];
        List<ScannerProgressEvent> progressEvents = [];
        TaskCompletionSource completedTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);

        device.ProgressChanged += progress => progressEvents.Add(progress);
        device.PageReady += e =>
        {
            using ScannerPageLease lease = e.Page;
            deliveredPages.Add((lease.PageNumber, lease.Flags, lease.Memory.Span[0]));
        };
        device.CaptureStateChanged += e =>
        {
            if (e.CurrentState == ScannerCaptureState.Completed)
                completedTcs.TrySetResult();
        };

        await device.StartAsync().ConfigureAwait(false);
        await completedTcs.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);

        AssertEqual("ADF Duplex", interop.StringOptions["source"], "Duplex source maps to SANE 'ADF Duplex'.");
        AssertEqual("Color", interop.StringOptions["mode"], "Rgb24 maps to SANE 'Color' mode.");
        AssertEqual(8.0, interop.NumericOptions["depth"], "Rgb24 sets 8 bits per channel.");
        AssertEqual(300.0, interop.NumericOptions["resolution"], "DPI sets SANE resolution.");
        AssertEqual(10.0, interop.NumericOptions["tl-x"], "Region X offset converts microns to mm.");
        AssertEqual(15.0, interop.NumericOptions["tl-y"], "Region Y offset converts microns to mm.");
        AssertEqual(210.0, interop.NumericOptions["br-x"], "Region right edge converts microns to mm.");
        AssertEqual(295.0, interop.NumericOptions["br-y"], "Region bottom edge converts microns to mm.");
        AssertEqual(25.0, interop.NumericOptions["brightness"], "Normalized brightness scales to SANE percentage.");
        AssertEqual(-50.0, interop.NumericOptions["contrast"], "Normalized contrast scales to SANE percentage.");

        AssertEqual(2, deliveredPages.Count, "Two pages should be delivered before SANE_STATUS_NO_DOCS.");
        AssertEqual(0L, deliveredPages[0].PageNumber, "First page is 0.");
        AssertEqual(ScannerPageFlags.None, deliveredPages[0].Flags, "First page is front side.");
        AssertEqual((byte)10, deliveredPages[0].FirstByte, "First page payload matches.");
        AssertEqual(1L, deliveredPages[1].PageNumber, "Second page is 1.");
        AssertEqual(ScannerPageFlags.DuplexBackSide, deliveredPages[1].Flags, "Second duplex page carries DuplexBackSide flag.");
        AssertEqual((byte)70, deliveredPages[1].FirstByte, "Second page payload matches.");
        AssertTrue(progressEvents.Count >= 2, "Progress events are emitted during page reads.");
        AssertEqual(ScannerCaptureState.Completed, device.CaptureState, "SANE_STATUS_NO_DOCS after page > 0 completes batch.");
        AssertTrue(interop.CancelCount > 0, "sane_cancel is invoked when batch ends.");

        await device.CloseAsync().ConfigureAwait(false);
        AssertTrue(interop.CloseCount > 0, "sane_close is invoked when device closes.");
    }

    private static async Task LinuxScannerMapsFaultsAndSupportsCallbackStop()
    {
        // 1. Verify status -> fault code mappings
        InputScannerException jammed = LinuxScannerFaults.CreateException(SaneStatus.Jammed, "Paper jam.");
        AssertEqual(InputErrorCategory.CaptureDiscontinuity, jammed.Fault.Category, "SANE_STATUS_JAMMED maps to CaptureDiscontinuity.");
        AssertEqual(ScannerFaultCode.PaperJam, jammed.FaultCode, "SANE_STATUS_JAMMED maps to PaperJam.");

        InputScannerException noDocs = LinuxScannerFaults.CreateException(SaneStatus.NoDocs, "Feeder empty.");
        AssertEqual(InputErrorCategory.CaptureDiscontinuity, noDocs.Fault.Category, "SANE_STATUS_NO_DOCS maps to CaptureDiscontinuity.");
        AssertEqual(ScannerFaultCode.FeederEmpty, noDocs.FaultCode, "SANE_STATUS_NO_DOCS maps to FeederEmpty.");

        InputScannerException coverOpen = LinuxScannerFaults.CreateException(SaneStatus.CoverOpen, "Cover open.");
        AssertEqual(InputErrorCategory.CaptureDiscontinuity, coverOpen.Fault.Category, "SANE_STATUS_COVER_OPEN maps to CaptureDiscontinuity.");
        AssertEqual(ScannerFaultCode.CoverOpen, coverOpen.FaultCode, "SANE_STATUS_COVER_OPEN maps to CoverOpen.");

        InputScannerException accessDenied = LinuxScannerFaults.CreateException(SaneStatus.AccessDenied, "Access denied.");
        AssertEqual(InputErrorCategory.PermissionDenied, accessDenied.Fault.Category, "SANE_STATUS_ACCESS_DENIED maps to PermissionDenied.");

        // 2. Verify SANE_STATUS_NO_DOCS on page 0 transitions to Faulted with FeederEmpty
        FakeLinuxSaneInterop emptyFeederInterop = new();
        emptyFeederInterop.Devices.Add(new SaneDeviceInfo("test:0", "SANE", "Virtual", "scanner"));
        LinuxScannerProvider emptyProvider = new(emptyFeederInterop);
        IReadOnlyList<InputDeviceDescriptor> emptyDevices = await emptyProvider.GetDevicesAsync().ConfigureAwait(false);

        using (ScannerInputDevice emptyDevice = await emptyProvider.OpenAsync(
            emptyDevices[0],
            ScannerOpenOptions.WithPreferredFormat(new ScannerFormat(ScannerSourceKind.FeederSimplex, 150, 150, ScannerPixelFormat.Grayscale8))).ConfigureAwait(false))
        {
            await emptyDevice.OpenAsync().ConfigureAwait(false);
            TaskCompletionSource faultedTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
            emptyDevice.CaptureStateChanged += e =>
            {
                if (e.CurrentState == ScannerCaptureState.Faulted)
                    faultedTcs.TrySetResult();
            };

            await emptyDevice.StartAsync().ConfigureAwait(false);
            await faultedTcs.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            AssertEqual(ScannerFaultCode.FeederEmpty, emptyDevice.LastFaultCode, "Empty feeder on page 0 records FeederEmpty fault code.");
        }

        // 3. Verify StopAsync inside PageReady callback does not deadlock and invokes sane_cancel
        FakeLinuxSaneInterop stopInterop = new();
        stopInterop.Devices.Add(new SaneDeviceInfo("test:1", "SANE", "Virtual", "scanner"));
        stopInterop.PagesToProduce.Enqueue(new FakeSanePage(
            new SaneParameters(SaneFrame.Gray, true, BytesPerLine: 2, PixelsPerLine: 2, Lines: 1, Depth: 8),
            [1, 2]));
        stopInterop.PagesToProduce.Enqueue(new FakeSanePage(
            new SaneParameters(SaneFrame.Gray, true, BytesPerLine: 2, PixelsPerLine: 2, Lines: 1, Depth: 8),
            [3, 4]));

        LinuxScannerProvider stopProvider = new(stopInterop);
        IReadOnlyList<InputDeviceDescriptor> stopDevices = await stopProvider.GetDevicesAsync().ConfigureAwait(false);
        using ScannerInputDevice stopDevice = await stopProvider.OpenAsync(
            stopDevices[0],
            ScannerOpenOptions.WithPreferredFormat(new ScannerFormat(ScannerSourceKind.FeederSimplex, 150, 150, ScannerPixelFormat.Grayscale8))).ConfigureAwait(false);

        await stopDevice.OpenAsync().ConfigureAwait(false);
        TaskCompletionSource stoppedInCallback = new(TaskCreationOptions.RunContinuationsAsynchronously);
        stopDevice.PageReady += e =>
        {
            e.Page.Dispose();
            _ = stopDevice.StopAsync().AsTask().ContinueWith(_ => stoppedInCallback.TrySetResult(), TaskScheduler.Default);
        };

        await stopDevice.StartAsync().ConfigureAwait(false);
        await stoppedInCallback.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        AssertTrue(stopInterop.CancelCount > 0, "Stopping from PageReady callback invokes sane_cancel.");
    }

    private sealed record FakeSanePage(SaneParameters Parameters, byte[] Data);

    private sealed class FakeLinuxSaneInterop : ILinuxSaneInterop
    {
        private FakeSanePage? _activePage;
        private int _readOffset;

        public List<SaneDeviceInfo> Devices { get; } = [];

        public Queue<FakeSanePage> PagesToProduce { get; } = new();

        public Dictionary<string, string> StringOptions { get; } = new(StringComparer.OrdinalIgnoreCase);

        public Dictionary<string, double> NumericOptions { get; } = new(StringComparer.OrdinalIgnoreCase);

        public int CancelCount;
        public int CloseCount;

        public SaneStatus Init(out int versionCode)
        {
            versionCode = 1;
            return SaneStatus.Good;
        }

        public void Exit()
        {
        }

        public SaneStatus GetDevices(bool localOnly, out IReadOnlyList<SaneDeviceInfo> devices)
        {
            devices = [.. Devices];
            return SaneStatus.Good;
        }

        public SaneStatus Open(string deviceName, out IntPtr handle)
        {
            handle = new IntPtr(0x1234);
            return SaneStatus.Good;
        }

        public void Close(IntPtr handle) => Interlocked.Increment(ref CloseCount);

        public bool TrySetStringOption(IntPtr handle, string optionName, string value)
        {
            StringOptions[optionName] = value;
            return true;
        }

        public bool TrySetIntOrFixedOption(IntPtr handle, string optionName, double value)
        {
            NumericOptions[optionName] = value;
            return true;
        }

        public SaneStatus Start(IntPtr handle)
        {
            if (!PagesToProduce.TryDequeue(out _activePage))
                return SaneStatus.NoDocs;

            _readOffset = 0;
            return SaneStatus.Good;
        }

        public SaneStatus GetParameters(IntPtr handle, out SaneParameters parameters)
        {
            if (_activePage is null)
            {
                parameters = default;
                return SaneStatus.Inval;
            }

            parameters = _activePage.Parameters;
            return SaneStatus.Good;
        }

        public SaneStatus Read(IntPtr handle, Span<byte> buffer, out int bytesRead)
        {
            if (_activePage is null)
            {
                bytesRead = 0;
                return SaneStatus.Inval;
            }

            int remaining = _activePage.Data.Length - _readOffset;
            if (remaining <= 0)
            {
                bytesRead = 0;
                return SaneStatus.Eof;
            }

            bytesRead = Math.Min(buffer.Length, remaining);
            _activePage.Data.AsSpan(_readOffset, bytesRead).CopyTo(buffer);
            _readOffset += bytesRead;
            return SaneStatus.Good;
        }

        public void Cancel(IntPtr handle) => Interlocked.Increment(ref CancelCount);
    }
}
