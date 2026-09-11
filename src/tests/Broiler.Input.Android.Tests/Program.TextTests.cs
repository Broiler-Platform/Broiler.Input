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
    // ---- IME ----------------------------------------------------------------------------------

    private static async Task ImeCompositionCommitsOnce()
    {
        (AndroidTextInputDevice device, List<TextCompositionEvent> composition, List<TextInputEvent> text) = 
            await OpenImeAsync().ConfigureAwait(false);

        device.SetComposingText("こ", 1, 1000);
        device.SetComposingText("こん", 1, 1010);
        device.CommitText("今", 1, 1020);

        AssertEqual(3, composition.Count, "Two updates and one commit produce three composition events.");
        AssertEqual(TextCompositionState.Started, composition[0].State, "The first composing text starts a composition.");
        AssertEqual(TextCompositionState.Updated, composition[1].State, "The second updates it.");
        AssertEqual(TextCompositionState.Committed, composition[2].State, "The commit terminates it.");
        AssertEqual("今", composition[2].Text, "The committed event carries the final text.");

        // The delivery rule: committed text arrives once, as a composition event, never also as a
        // text event. Emitting both would make an editor insert the text twice.
        AssertEqual(0, text.Count, "A commit that ends a composition raises no separate text event.");
        AssertTrue(!device.IsComposing, "The composition is finished after the commit.");
    }

    private static async Task ImeCommitWithoutComposition()
    {
        (AndroidTextInputDevice device, List<TextCompositionEvent> composition, List<TextInputEvent> text) =
            await OpenImeAsync().ConfigureAwait(false);

        device.CommitText("hello", 1, 2000);

        AssertEqual(1, text.Count, "Text committed with no composition arrives as a text event.");
        AssertEqual("hello", text[0].Text, "The committed text is delivered verbatim.");
        AssertEqual(0, composition.Count, "No composition event is raised when nothing was composing.");
    }

    private static async Task ImeCancelsOnFocusLoss()
    {
        (AndroidTextInputDevice device, List<TextCompositionEvent> composition, _) = await OpenImeAsync().ConfigureAwait(false);

        device.SetComposingText("partial", 1, 3000);
        composition.Clear();

        AssertTrue(device.NotifyFocusLost(), "Focus loss cancels a composition in flight.");
        AssertEqual(1, composition.Count, "Cancellation raises one event.");
        AssertEqual(TextCompositionState.Cancelled, composition[0].State, "The pre-edit is cancelled, not committed.");
        AssertTrue(!device.IsComposing, "No composition remains after cancellation.");

        // An empty composing text is Android's way of clearing the pre-edit.
        device.SetComposingText("again", 1, 3100);
        composition.Clear();
        device.SetComposingText(string.Empty, 1, 3110);
        AssertEqual(TextCompositionState.Cancelled, composition[^1].State, "Empty composing text clears the pre-edit.");
    }

    private static void ImeResolvesCursorPosition()
    {
        // Android: newCursorPosition > 0 counts from the end of the text, <= 0 from its start.
        AssertEqual(5, AndroidTextInputDevice.ResolveCaret(5, 1), "A value of 1 puts the caret after the text.");
        AssertEqual(0, AndroidTextInputDevice.ResolveCaret(5, 0), "A value of 0 puts the caret before the text.");
        AssertEqual(0, AndroidTextInputDevice.ResolveCaret(5, -3), "Negative values count back from the start and clamp.");
        AssertEqual(5, AndroidTextInputDevice.ResolveCaret(5, 9), "A caret past the end clamps to the text length.");
    }

    private static async Task ImeSurfacesEditRequests()
    {
        (AndroidTextInputDevice device, _, _) = await OpenImeAsync().ConfigureAwait(false);

        List<AndroidTextEditRequest> requests = [];
        device.EditRequested += requests.Add;

        device.DeleteSurroundingText(2, 1);
        device.SetSelection(4, 9);
        device.PerformEditorAction(5);

        AssertEqual(3, requests.Count, "Each mutation the neutral contract cannot express is surfaced.");
        AssertEqual(AndroidTextEditRequestKind.DeleteSurroundingText, requests[0].Kind, "Deletion is reported.");
        AssertEqual(2, requests[0].Start, "The before-length is carried.");
        AssertEqual(1, requests[0].End, "The after-length is carried.");
        AssertEqual(AndroidTextEditRequestKind.SetSelection, requests[1].Kind, "Selection changes are reported.");
        AssertEqual(AndroidTextEditRequestKind.EditorAction, requests[2].Kind, "Editor actions are reported.");

        AssertTrue(!device.DeleteSurroundingText(0, 0), "A no-op deletion raises nothing.");

        // With no editor attached the IME queries degrade to empty rather than throwing.
        AssertEqual(string.Empty, device.GetTextBeforeCursor(10), "An unattached editor returns no text.");
        AssertEqual(string.Empty, device.GetSelectedText(), "An unattached editor reports no selection text.");
    }
}
