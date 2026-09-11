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
}
