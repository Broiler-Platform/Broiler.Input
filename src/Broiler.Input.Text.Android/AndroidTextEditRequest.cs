namespace Broiler.Input.Text.Android;

/// <summary>
/// The kind of editor mutation an input method asked for that
/// <see cref="Broiler.Input.Text.TextInputDevice"/> cannot express.
/// </summary>
public enum AndroidTextEditRequestKind
{
    /// <summary>Delete text around the cursor. Backs <c>InputConnection.deleteSurroundingText</c>.</summary>
    DeleteSurroundingText = 0,

    /// <summary>Move the selection. Backs <c>InputConnection.setSelection</c>.</summary>
    SetSelection,

    /// <summary>
    /// Mark an existing range as the composing region without changing the text. Backs
    /// <c>InputConnection.setComposingRegion</c>.
    /// </summary>
    SetComposingRegion,

    /// <summary>
    /// The IME's action button was pressed — Done, Next, Search, Send. Backs
    /// <c>InputConnection.performEditorAction</c>.
    /// </summary>
    EditorAction,
}

/// <summary>
/// An editor mutation requested by an input method.
/// </summary>
/// <remarks>
/// The neutral <see cref="Broiler.Input.Text.TextInputDevice"/> raises composition and committed
/// text only; it has no way to say "delete two characters before the cursor". Rather than
/// pretending an IME never asks for that, these requests are surfaced on the Android device so a
/// host can service them today, and they mark exactly what the Broiler.UI editor contract has to
/// grow. See the phase A2 text actions in the root roadmap.
/// </remarks>
/// <param name="Kind">Which mutation was requested.</param>
/// <param name="Start">
/// For <see cref="AndroidTextEditRequestKind.DeleteSurroundingText"/>, the number of code units to
/// delete before the cursor. For the selection and composing-region kinds, the start offset. For
/// <see cref="AndroidTextEditRequestKind.EditorAction"/>, the Android editor-action code.
/// </param>
/// <param name="End">
/// For <see cref="AndroidTextEditRequestKind.DeleteSurroundingText"/>, the number of code units to
/// delete after the cursor. For the selection and composing-region kinds, the end offset. Unused
/// for <see cref="AndroidTextEditRequestKind.EditorAction"/>.
/// </param>
public readonly record struct AndroidTextEditRequest(
    AndroidTextEditRequestKind Kind,
    int Start,
    int End = 0)
{
    /// <summary>Builds a delete-surrounding-text request.</summary>
    public static AndroidTextEditRequest DeleteSurrounding(int beforeLength, int afterLength) =>
        new(AndroidTextEditRequestKind.DeleteSurroundingText, beforeLength, afterLength);

    /// <summary>Builds a set-selection request.</summary>
    public static AndroidTextEditRequest Selection(int start, int end) =>
        new(AndroidTextEditRequestKind.SetSelection, start, end);

    /// <summary>Builds a set-composing-region request.</summary>
    public static AndroidTextEditRequest ComposingRegion(int start, int end) =>
        new(AndroidTextEditRequestKind.SetComposingRegion, start, end);

    /// <summary>Builds an editor-action request.</summary>
    public static AndroidTextEditRequest EditorAction(int actionCode) =>
        new(AndroidTextEditRequestKind.EditorAction, actionCode);
}
