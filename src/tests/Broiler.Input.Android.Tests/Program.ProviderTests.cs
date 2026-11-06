using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Broiler.Input.Keyboard;
using Broiler.Input.Keyboard.Android;
using Broiler.Input.Pen;
using Broiler.Input.Pen.Android;
using Broiler.Input.Text;
using Broiler.Input.Text.Android;
using Broiler.Input.Touch;
using Broiler.Input.Touch.Android;
using Broiler.Input.Scanner;
using Broiler.Input.Scanner.Android;

namespace Broiler.Input.Android.Tests;

internal static partial class Program
{
    // ---- provider lifecycle --------------------------------------------------------------------

    private static async Task ProviderOpenIsIdempotent()
    {
        AndroidTouchProvider provider = new();
        InputDeviceDescriptor descriptor = provider.RegisterDefaultTouchScreen();

        TouchInputDevice first = await provider.OpenAsync(descriptor, new TouchOpenOptions()).ConfigureAwait(false);
        TouchInputDevice second = await provider.OpenAsync(descriptor, new TouchOpenOptions()).ConfigureAwait(false);

        // Two devices for one screen would split contact tracking and desynchronise gestures.
        AssertTrue(ReferenceEquals(first, second), "Opening the same descriptor twice returns one device.");

        IReadOnlyList<InputDeviceDescriptor> devices = await provider.GetDevicesAsync().ConfigureAwait(false);
        AssertEqual(1, devices.Count, "The provider reports the registered device.");
        AssertEqual(InputKind.Touch, devices[0].Kind, "The descriptor is a touch descriptor.");
    }

    private static async Task ProviderRemovalMarksUnavailable()
    {
        (AndroidTouchProvider provider, AndroidTouchInputDevice device, List<TouchContactEvent> events) =
            await OpenTouchAsync().ConfigureAwait(false);

        device.ProcessMotionEvent(Motion(Down, 10, Pointer(1, 5, 5)));
        events.Clear();

        InputDeviceChange? change = null;
        provider.DeviceChanged += observed => change = observed;

        AssertTrue(provider.RemoveDevice(device.Id), "The registered device is removed.");
        AssertEqual(InputDeviceState.Unavailable, device.State, "Removal marks the open device unavailable.");
        AssertEqual(InputErrorCategory.DeviceRemoved, device.LastFault?.Category, "Removal records a device-removed fault.");
        AssertEqual(InputDeviceChangeKind.Removed, change?.Kind, "The provider emits a removed change.");
        AssertEqual(1, events.Count, "The contact in flight is cancelled before the device goes away.");
        AssertEqual(TouchContactState.Cancelled, events[0].State, "The in-flight contact is cancelled.");

        AssertTrue(!provider.RemoveDevice(device.Id), "Removing an already-removed device reports no change.");
    }

    private static async Task ProviderRejectsUnknownDescriptor()
    {
        AndroidTouchProvider provider = new();
        InputDeviceDescriptor unknown = AndroidInputDescriptors.Touch(androidDeviceId: 99);

        try
        {
            await provider.OpenAsync(unknown, new TouchOpenOptions()).ConfigureAwait(false);
            throw new InvalidOperationException("Opening an unregistered descriptor did not throw.");
        }
        catch (InvalidOperationException exception) when (exception.Message.Contains("android:touch:99", StringComparison.Ordinal))
        {
        }
    }

    private static async Task ProviderHonoursCancellation()
    {
        AndroidTouchProvider provider = new();
        InputDeviceDescriptor descriptor = provider.RegisterDefaultTouchScreen();

        using CancellationTokenSource source = new();
        await source.CancelAsync().ConfigureAwait(false);

        try
        {
            await provider.OpenAsync(descriptor, new TouchOpenOptions(), source.Token).ConfigureAwait(false);
            throw new InvalidOperationException("A canceled open did not throw.");
        }
        catch (OperationCanceledException)
        {
        }

        try
        {
            await provider.GetDevicesAsync(source.Token).ConfigureAwait(false);
            throw new InvalidOperationException("A canceled enumeration did not throw.");
        }
        catch (OperationCanceledException)
        {
        }
    }

    private static async Task TimestampsUseAndroidClock()
    {
        (_, AndroidTouchInputDevice device, List<TouchContactEvent> events) = await OpenTouchAsync().ConfigureAwait(false);

        device.ProcessMotionEvent(Motion(Down, 123456, Pointer(1, 5, 5)));

        InputTimestamp timestamp = events[0].Header.Timestamp;
        AssertEqual(AndroidUptimeInputClock.ClockName, timestamp.ClockName, "Events carry the Android uptime timebase.");
        AssertEqual(123456L, timestamp.Ticks, "The event's own time is preserved, not re-stamped on arrival.");
        AssertEqual(1000L, timestamp.Frequency, "Android reports uptime in milliseconds.");
        AssertTrue(events[0].Header.SequenceNumber > 0, "Events carry a monotonic sequence number.");
    }

    // ---- boundary ------------------------------------------------------------------------------

    private static async Task ScannerDeliversHostPagesAndHandlesCaptureLoss()
    {
        AndroidScannerProvider provider = new();
        InputDeviceDescriptor descriptor = provider.RegisterDefaultScanner("Android MLKit Scanner", supportsFeeder: true, supportsDuplex: true);

        ScannerFormat format = new(ScannerSourceKind.FeederDuplex, 300, 300, ScannerPixelFormat.Rgba32);
        ScannerOpenOptions options = new(format, new ScannerSessionOptions(maxPages: 2));
        AndroidScannerInputDevice device = (AndroidScannerInputDevice)await provider.OpenAsync(descriptor, options).ConfigureAwait(false);

        await device.OpenAsync().ConfigureAwait(false);
        await device.StartAsync().ConfigureAwait(false);
        AssertEqual(ScannerCaptureState.Scanning, device.CaptureState, "Android scanner enters Scanning state.");

        ScannerProgressEvent? progress = null;
        device.ProgressChanged += e => progress = e;
        device.ReportProgress(0, 0.5, 64, eventTimeMilliseconds: 1000);
        AssertEqual(0.5, progress?.ProgressFraction, "Android scanner reports progress fraction.");

        List<(long PageNumber, ScannerPageFlags Flags, ScannerRotation Rotation, byte FirstByte)> delivered = [];
        device.PageReady += e =>
        {
            using ScannerPageLease page = e.Page;
            delivered.Add((page.PageNumber, page.Flags, page.Rotation, page.Memory.Span[0]));
        };

        AssertTrue(device.DeliverScannedPage([11, 22, 33, 44], 1, 1, rotation: ScannerRotation.Rotate90, eventTimeMilliseconds: 1100),
            "First host-scanned page is delivered.");
        AssertTrue(device.DeliverScannedPage([55, 66, 77, 88], 1, 1, rotation: ScannerRotation.Rotate270, eventTimeMilliseconds: 1200),
            "Second host-scanned page is delivered and completes batch.");

        AssertEqual(2, delivered.Count, "Two pages delivered.");
        AssertEqual(0L, delivered[0].PageNumber, "Page 0 delivered first.");
        AssertEqual(ScannerRotation.Rotate90, delivered[0].Rotation, "Page 0 preserves rotation.");
        AssertEqual(1L, delivered[1].PageNumber, "Page 1 delivered second.");
        AssertEqual(ScannerPageFlags.DuplexBackSide | ScannerPageFlags.EndOfBatch, delivered[1].Flags,
            "Second duplex page carries DuplexBackSide and EndOfBatch flags.");
        AssertEqual(ScannerCaptureState.Completed, device.CaptureState, "MaxPages completion transitions state to Completed.");

        // Restart and verify NotifyCaptureLost aborts active scan
        await device.StopAsync().ConfigureAwait(false);
        await device.StartAsync().ConfigureAwait(false);
        AssertEqual(ScannerCaptureState.Scanning, device.CaptureState, "Restarted Android scanner is Scanning.");

        provider.NotifyCaptureLost("activity paused");
        AssertEqual(ScannerCaptureState.Faulted, device.CaptureState, "Capture loss faults active scan.");
        AssertEqual(ScannerFaultCode.TransferAborted, device.LastFaultCode, "Capture loss sets TransferAborted fault code.");
    }

    private static void AssembliesAvoidAndroidSdkReferences()
    {
        // The Android backends translate primitives, so they must not drag in Mono.Android. That is
        // what lets them build and be tested without the android workload, and it keeps Android
        // types out of Broiler.Input and Broiler.UI as the architecture requires.
        string[] forbidden = ["Mono.Android", "Java.Interop", "Xamarin.Android", "Microsoft.Maui"];

        Assembly[] assemblies =
        [
            typeof(AndroidMotionSample).Assembly,
            typeof(AndroidTouchInputDevice).Assembly,
            typeof(AndroidPenInputDevice).Assembly,
            typeof(AndroidKeyboardInputDevice).Assembly,
            typeof(AndroidTextInputDevice).Assembly,
            typeof(AndroidScannerInputDevice).Assembly,
        ];

        foreach (Assembly assembly in assemblies)
        {
            foreach (AssemblyName reference in assembly.GetReferencedAssemblies())
            {
                foreach (string name in forbidden)
                {
                    AssertTrue(
                        !reference.Name!.StartsWith(name, StringComparison.OrdinalIgnoreCase),
                        $"{assembly.GetName().Name} must not reference {reference.Name}.");
                }
            }
        }

        // Graphics and UI stay out too, matching the Linux boundary test.
        foreach (Assembly assembly in assemblies)
        {
            foreach (AssemblyName reference in assembly.GetReferencedAssemblies())
            {
                AssertTrue(
                    !reference.Name!.StartsWith("Broiler.Graphics", StringComparison.Ordinal) &&
                    !reference.Name.StartsWith("Broiler.UI", StringComparison.Ordinal),
                    $"{assembly.GetName().Name} must not reference {reference.Name}.");
            }
        }
    }
}
