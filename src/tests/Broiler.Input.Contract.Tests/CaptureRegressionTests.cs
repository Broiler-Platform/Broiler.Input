using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Broiler.Input.Camera;
using Broiler.Input.Microphone;
using Broiler.Input.Windows;

namespace Broiler.Input.Contract.Tests;

internal static class CaptureRegressionTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    public static async Task BoundedDelivery()
    {
        foreach (InputDeliveryOverflowPolicy policy in new[] { InputDeliveryOverflowPolicy.DropNewest,
            InputDeliveryOverflowPolicy.DropOldest, InputDeliveryOverflowPolicy.KeepLatest })
        {
            using ManualResetEventSlim entered = new(), release = new(), produced = new(), interrupted = new();
            List<int> delivered = [];
            Lease[] leases = Enumerable.Range(0, 4).Select(static id => new Lease(id)).ToArray();
            int[] expected = policy switch
            {
                InputDeliveryOverflowPolicy.DropNewest => [0, 1, 2],
                InputDeliveryOverflowPolicy.DropOldest => [0, 2, 3],
                _ => [0, 3],
            };
            TaskCompletionSource drained = Completion();
            int runs = 0;
            WindowsCaptureWorker<Lease> worker = null!;
            worker = Create(new InputDeliveryOptions(2, policy), () =>
            {
                worker.SignalStarted();
                if (Interlocked.Increment(ref runs) > 1)
                    return;
                worker.TryEnqueue(leases[0]);
                Require(entered.Wait(Timeout), "Consumer did not start.");
                foreach (Lease lease in leases.Skip(1)) worker.TryEnqueue(lease);
                produced.Set();
                interrupted.Wait();
            }, interrupted.Set, lease =>
            {
                if (lease.Id == 0)
                {
                    entered.Set();
                    Require(release.Wait(Timeout), "Consumer was not released.");
                }
                delivered.Add(lease.Id);
                lease.Dispose();
                if (delivered.Count == expected.Length) drained.TrySetResult();
            });

            try
            {
                await worker.StartAsync(default);
                worker.EnableDelivery();
                Require(produced.Wait(Timeout), "Slow consumer blocked native capture.");
                InputDeliveryMetrics metrics = worker.Metrics;
                Require(metrics.QueueDepth <= 2, "Queue exceeded its capacity.");
                Require(metrics.DroppedNewestCount == (policy == InputDeliveryOverflowPolicy.DropNewest ? 1 : 0), "Wrong newest-drop count.");
                Require(metrics.DroppedOldestCount == (policy == InputDeliveryOverflowPolicy.DropOldest ? 1 : policy == InputDeliveryOverflowPolicy.KeepLatest ? 2 : 0), "Wrong oldest-drop count.");
                release.Set();
                await drained.Task.WaitAsync(Timeout);
                await Stop(worker);
                Require(delivered.SequenceEqual(expected), "Wrong delivery order for " + policy);
                Require(worker.Metrics.DequeuedCount == expected.Length, "Delivered count was not published.");
                Require(leases.All(static lease => lease.DisposeCount == 1), "A lease leaked or was disposed twice.");

                // A new session starts with clean metrics, including drop counts.
                await worker.StartAsync(default);
                Require(worker.Metrics.DroppedNewestCount == 0 && worker.Metrics.DroppedOldestCount == 0,
                    "Drop counts leaked into a restarted session.");
            }
            finally
            {
                release.Set();
                await Stop(worker);
            }
        }
    }

    public static async Task StopFromCallback()
    {
        for (int operation = 0; operation < 3; operation++)
        {
            using ManualResetEventSlim interrupted = new();
            TaskCompletionSource callbackEnded = Completion();
            bool cleanedUp = false;
            WindowsCaptureWorker<Lease> worker = null!;
            worker = Create(InputDeliveryOptions.DiscreteDefault, () =>
            {
                try
                {
                    worker.SignalStarted();
                    worker.TryEnqueue(new Lease(0));
                    interrupted.Wait();
                }
                finally { cleanedUp = true; }
            }, interrupted.Set, lease =>
            {
                if (operation == 0) worker.StopAsync(default).GetAwaiter().GetResult();
                else if (operation == 1) worker.Dispose();
                else worker.DisposeAsync().GetAwaiter().GetResult();
                Require(cleanedUp, "Stop returned before native cleanup.");
                lease.Dispose();
                callbackEnded.TrySetResult();
            });
            try
            {
                await worker.StartAsync(default);
                worker.EnableDelivery();
                await callbackEnded.Task.WaitAsync(Timeout);
            }
            finally { await Stop(worker); }
        }
    }

    public static async Task RuntimeFaults()
    {
        foreach (bool overflow in new[] { false, true })
        {
            using ManualResetEventSlim fail = new();
            TaskCompletionSource<Exception> reported = new(TaskCreationOptions.RunContinuationsAsynchronously);
            Exception expected = new InvalidOperationException("native capture failed");
            Lease first = new(1), rejected = new(2);
            WindowsCaptureWorker<Lease> worker = null!;
            worker = Create(new InputDeliveryOptions(1, InputDeliveryOverflowPolicy.Fail), () =>
            {
                worker.SignalStarted();
                fail.Wait();
                if (overflow)
                {
                    worker.TryEnqueue(first);
                    worker.TryEnqueue(rejected);
                }
                else throw expected;
            }, fail.Set, lease => lease.Dispose(), exception =>
            {
                // Fault handlers have the same shutdown guarantee as sample callbacks.
                worker.Dispose();
                reported.TrySetResult(exception);
            });
            try
            {
                await worker.StartAsync(default);
                fail.Set();
                // Capture fails before delivery is enabled, exercising the startup race.
                Require(SpinWait.SpinUntil(() => overflow ? first.DisposeCount == 1 && rejected.DisposeCount == 1 : worker.Metrics.QueueDepth == 0, Timeout), "Capture did not fail.");
                if (overflow)
                    Require(first.DisposeCount == 1 && rejected.DisposeCount == 1, "Overflow leaked leases.");
                worker.EnableDelivery();
                Exception actual = await reported.Task.WaitAsync(Timeout);
                Require(overflow ? actual is InvalidOperationException : ReferenceEquals(expected, actual), "Runtime fault was lost.");
            }
            finally { await Stop(worker); }
        }
    }

    public static async Task StartupFailureAndCancellation()
    {
        Exception expected = new InvalidOperationException("startup failed");
        var worker = Create(InputDeliveryOptions.DiscreteDefault, () => throw expected, () => { }, lease => lease.Dispose());
        try
        {
            await worker.StartAsync(default);
            throw new Exception("Startup failure was swallowed.");
        }
        catch (InvalidOperationException actual) when (ReferenceEquals(actual, expected)) { }
        await Stop(worker);

        using ManualResetEventSlim entered = new(), interrupted = new();
        bool cleanedUp = false;
        worker = Create(InputDeliveryOptions.DiscreteDefault, () =>
        {
            entered.Set();
            interrupted.Wait();
            cleanedUp = true;
        }, interrupted.Set, lease => lease.Dispose());
        using CancellationTokenSource cancellation = new();
        Task start = worker.StartAsync(cancellation.Token);
        Require(entered.Wait(Timeout), "Capture did not start.");
        cancellation.Cancel();
        try { await start.WaitAsync(Timeout); throw new Exception("Startup ignored cancellation."); }
        catch (OperationCanceledException) { }
        Require(cleanedUp, "Cancelled startup did not clean up native capture.");
        await Stop(worker);
    }

    public static async Task ExternalStopWaitsForCallback()
    {
        using ManualResetEventSlim entered = new(), release = new(), interrupted = new();
        WindowsCaptureWorker<Lease> worker = null!;
        worker = Create(InputDeliveryOptions.DiscreteDefault, () =>
        {
            worker.SignalStarted();
            worker.TryEnqueue(new Lease(0));
            interrupted.Wait();
        }, interrupted.Set, lease => { entered.Set(); release.Wait(); lease.Dispose(); });
        try
        {
            await worker.StartAsync(default);
            worker.EnableDelivery();
            Require(entered.Wait(Timeout), "Callback did not start.");
            using CancellationTokenSource cancellation = new();
            Task stop = worker.StopAsync(cancellation.Token).AsTask();
            Require(!stop.IsCompleted, "External stop did not wait for the in-flight callback.");
            cancellation.Cancel();
            try { await stop; throw new Exception("Stop ignored cancellation."); }
            catch (OperationCanceledException) { }
        }
        finally { release.Set(); await Stop(worker); }
    }

    public static async Task PreviewDisposalRace()
    {
        using TestCamera camera = new();
        using ManualResetEventSlim entered = new(), release = new();
        camera.FrameReady += _ => { entered.Set(); release.Wait(); };
        using CameraLatestFramePreviewAdapter adapter = new(camera);
        CameraFrameLease frame = new([0], new CameraFormat(1, 1, 30, 1, CameraPixelFormat.Gray8), [], default, 0);
        Task delivery = Task.Run(() => camera.Emit(frame));
        try
        {
            Require(entered.Wait(Timeout), "Frame delivery did not start.");
            adapter.Dispose();
        }
        finally { release.Set(); }
        await delivery.WaitAsync(Timeout);
        Require(frame.IsDisposed, "The late frame was retained after disposal.");
        Require(!adapter.TryAcquireLatest(out _), "Disposed adapter accepted a frame.");
    }

    public static Task DisposeFromFaultNotification()
    {
        foreach (InputErrorCategory category in new[] { InputErrorCategory.NativeFailure, InputErrorCategory.DeviceRemoved })
        {
            using TestCamera camera = new();
            camera.CaptureStateChanged += _ => camera.Dispose();
            camera.Fail(new InputFault(category, "capture failed"));
            Require(camera.State == InputDeviceState.Disposed, "Camera fault overwrote callback disposal.");

            using TestMicrophone microphone = new();
            microphone.CaptureStateChanged += _ => microphone.Dispose();
            microphone.Fail(new InputFault(category, "capture failed"));
            Require(microphone.State == InputDeviceState.Disposed, "Microphone fault overwrote callback disposal.");
        }
        return Task.CompletedTask;
    }

    private static WindowsCaptureWorker<Lease> Create(InputDeliveryOptions options, Action capture, Action interrupt,
        Action<Lease> deliver, Action<Exception>? faulted = null) =>
        new("Capture regression", options, capture, interrupt, deliver, static exception => exception,
            faulted ?? (_ => { }), _ => { }, () => { });

    private static async Task Stop(WindowsCaptureWorker<Lease> worker)
    {
        using CancellationTokenSource cancellation = new(Timeout);
        await worker.StopAsync(cancellation.Token);
    }

    private static TaskCompletionSource Completion() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed class Lease(int id) : IDisposable
    {
        private int _disposeCount;
        public int Id => id;
        public int DisposeCount => Volatile.Read(ref _disposeCount);
        public void Dispose() => Interlocked.Increment(ref _disposeCount);
    }

    private sealed class TestCamera() : CameraInputDevice(new InputDeviceDescriptor(
        InputDeviceId.FromOpaqueValue("regression:camera"), InputKind.Camera, "Regression camera"))
    {
        public void Emit(CameraFrameLease frame) => RaiseFrameReady(frame);
        public void Fail(InputFault fault)
        {
            if (fault.Category == InputErrorCategory.DeviceRemoved) MarkCaptureInvalidated(fault);
            else MarkCaptureFaulted(fault);
        }
    }

    private sealed class TestMicrophone() : MicrophoneInputDevice(new InputDeviceDescriptor(
        InputDeviceId.FromOpaqueValue("regression:microphone"), InputKind.Microphone, "Regression microphone"))
    {
        public void Fail(InputFault fault)
        {
            if (fault.Category == InputErrorCategory.DeviceRemoved) MarkCaptureInvalidated(fault);
            else MarkCaptureFaulted(fault);
        }
    }
}
