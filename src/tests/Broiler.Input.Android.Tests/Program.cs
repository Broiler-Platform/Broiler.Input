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

namespace Broiler.Input.Android.Tests;

internal static partial class Program
{
    private static async Task<int> Main()
    {
        var tests = new List<(string Name, Func<Task> Body)>
        {
            ("packed action splits into action and pointer index", () => RunSync(PackedActionSplits)),
            ("coordinate space converts physical pixels to dips", () => RunSync(CoordinateSpaceConvertsDensity)),
            ("coordinate space rejects a non-positive density", () => RunSync(CoordinateSpaceRejectsBadDensity)),
            ("key map matches the linux code vocabulary", () => RunSync(KeyMapMatchesCodeVocabulary)),
            ("key map falls back to a virtual-key name", () => RunSync(KeyMapFallsBackToVirtualKey)),
            ("meta state maps to modifiers with sides", () => RunSync(MetaStateMapsModifiers)),
            ("tool type routes fingers, styluses, and mice apart", () => RunSync(ToolTypeRoutes)),
            ("touch tracks two contacts by pointer id", TouchTracksTwoContacts),
            ("touch keeps identity when a finger lifts", TouchKeepsIdentityWhenFingerLifts),
            ("touch cancels every contact on capture loss", TouchCancelsOnCaptureLoss),
            ("touch ignores stylus and mouse pointers", TouchIgnoresNonFingerPointers),
            ("pen reports pressure, buttons, and eraser", PenReportsPressureAndButtons),
            ("pen tilt converts polar to cartesian degrees", () => RunSync(PenTiltConverts)),
            ("keyboard maps a key press to a name and text", KeyboardMapsPressAndText),
            ("keyboard filters control characters from text", () => RunSync(KeyboardFiltersControlCharacters)),
            ("keyboard releases held keys on focus loss", KeyboardReleasesHeldKeys),
            ("ime composition starts, updates, and commits once", ImeCompositionCommitsOnce),
            ("ime commit without composition raises text only", ImeCommitWithoutComposition),
            ("ime cancels composition on focus loss", ImeCancelsOnFocusLoss),
            ("ime resolves the android cursor position", () => RunSync(ImeResolvesCursorPosition)),
            ("ime surfaces edit requests the contract cannot express", ImeSurfacesEditRequests),
            ("provider open is idempotent per descriptor", ProviderOpenIsIdempotent),
            ("provider reopens disposed devices", ProviderRegressionTests.ReopenDisposedDevice),
            ("provider removes disposed devices", ProviderRegressionTests.RemoveDisposedDevice),
            ("provider handles removal during open", ProviderRegressionTests.RemovalDuringOpen),
            ("provider removal marks the open device unavailable", ProviderRemovalMarksUnavailable),
            ("provider rejects an unregistered descriptor", ProviderRejectsUnknownDescriptor),
            ("provider open honours cancellation", ProviderHonoursCancellation),
            ("device timestamps use the android uptime clock", TimestampsUseAndroidClock),
            ("android assemblies avoid android sdk references", () => RunSync(AssembliesAvoidAndroidSdkReferences)),
        };

        int failures = 0;
        foreach ((string name, Func<Task> body) in tests)
        {
            try
            {
                await body().ConfigureAwait(false);
                Console.WriteLine($"PASS {name}");
            }
            catch (Exception exception)
            {
                failures++;
                Console.Error.WriteLine($"FAIL {name}: {exception.Message}");
            }
        }

        Console.WriteLine();
        Console.WriteLine(failures == 0
            ? $"All {tests.Count} Android input tests passed."
            : $"{failures} of {tests.Count} Android input tests failed.");
        return failures;
    }

    private static Task RunSync(Action action)
    {
        action();
        return Task.CompletedTask;
    }
}
