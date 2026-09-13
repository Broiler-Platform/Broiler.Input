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
    private static async Task<int> Main()
    {
        var checks = new List<(string Name, Func<Task> Check)>
        {
            ("fake lifecycle", () => InputContractAssert.ProvesLifecycleAsync(new FakeInputProvider())),
            ("fake cancellation", () => InputContractAssert.ProvesCancellationAsync(new FakeInputProvider())),
            ("fake removal", () => InputContractAssert.ProvesRemovalAsync(new FakeInputProvider())),
            ("fake bounded delivery", () => InputContractAssert.ProvesBoundedDeliveryAsync(new FakeInputProvider())),
            ("fake diagnostics", InputContractAssert.ProvesDiagnosticsAsync),
            ("windows keyboard cooked translation", WindowsKeyboardCookedTranslationMatchesPhase2Contract),
            ("windows mouse cooked translation", WindowsMouseCookedTranslationMatchesPhase2Contract),
            ("windows provider hot plug messages", WindowsProvidersReportHotPlugMessages),
            ("legacy window adapter", LegacyWindowAdapterMatchesCallbackCategories),
            ("dispatcher stops after handled", () => RunSync(DispatcherStopsAfterHandledMessage)),
            ("windows keyboard phase3 text hardening", WindowsKeyboardPhase3TextHardening),
            ("windows keyboard phase3 composition and layout", WindowsKeyboardPhase3CompositionAndLayout),
            ("raw input background acknowledgement", () => RunSync(RawInputBackgroundRequiresAcknowledgement)),
            ("raw mouse coalescing and identity", () => RunSync(RawMouseCoalescingPreservesPhysicalIdentity)),
            ("microphone synthetic bounded capture", MicrophoneSyntheticCaptureHasBoundedDelivery),
            ("microphone lease ownership", () => RunSync(MicrophoneLeaseDisposalInvalidatesMemory)),
            ("microphone default device changes", () => RunSync(MicrophoneDefaultDeviceChangeIsObservable)),
            ("windows microphone isolation", () => RunSync(WindowsMicrophoneContractsAreIsolated)),
            ("camera synthetic preview latest frame", CameraSyntheticPreviewKeepsLatestFrame),
            ("camera loss-sensitive bounded delivery", CameraLossSensitiveModeDropsNewest),
            ("camera frame lease ownership", () => RunSync(CameraFrameLeaseDisposalInvalidatesMemory)),
            ("camera preview adapter", CameraLatestFramePreviewAdapterKeepsOnlyLatest),
            ("production capture bounded delivery", CaptureRegressionTests.BoundedDelivery),
            ("production capture callback shutdown", CaptureRegressionTests.StopFromCallback),
            ("production capture runtime faults", CaptureRegressionTests.RuntimeFaults),
            ("production capture startup failure and cancellation", CaptureRegressionTests.StartupFailureAndCancellation),
            ("production capture external stop", CaptureRegressionTests.ExternalStopWaitsForCallback),
            ("camera preview disposal race", CaptureRegressionTests.PreviewDisposalRace),
            ("capture disposal from fault notification", CaptureRegressionTests.DisposeFromFaultNotification),
            ("windows camera isolation", () => RunSync(WindowsCameraContractsAreIsolated)),
            ("core has no windows references", () => RunSync(CoreHasNoWindowsReferences)),
            ("keyboard mouse no media references", () => RunSync(KeyboardMouseDoNotReferenceCameraOrMicrophone)),
            ("projects use declared packages", () => RunSync(ProjectsUseDeclaredPackages)),
            ("pointer modifiers mirror keyboard modifiers", () => RunSync(PointerModifiersMirrorKeyboardModifiers)),
            ("mouse does not reference keyboard", () => RunSync(MouseDoesNotReferenceKeyboard)),
            ("pointer events carry modifiers", () => RunSync(PointerEventsCarryModifiers)),
            ("public api baseline", () => RunSync(PublicApiBaselineMatches)),
        };

        foreach ((string name, Func<Task> check) in checks)
        {
            try
            {
                await check().ConfigureAwait(false);
                Console.WriteLine($"PASS {name}");
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine($"FAIL {name}: {exception.Message}");
                return 1;
            }
        }

        return 0;
    }

    private static Task RunSync(Action action)
    {
        action();
        return Task.CompletedTask;
    }
}
