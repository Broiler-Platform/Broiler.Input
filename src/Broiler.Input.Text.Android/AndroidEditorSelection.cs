namespace Broiler.Input.Text.Android;

/// <summary>
/// The editor state an input method needs to reason about: where the selection is, and which span
/// (if any) is currently being composed.
/// </summary>
/// <param name="SelectionStart">Start offset of the selection, in UTF-16 code units.</param>
/// <param name="SelectionEnd">End offset of the selection. Equal to the start for a caret.</param>
/// <param name="ComposingStart">Start of the composing region, or -1 when nothing is composing.</param>
/// <param name="ComposingEnd">End of the composing region, or -1 when nothing is composing.</param>
public readonly record struct AndroidEditorSelection(
    int SelectionStart,
    int SelectionEnd,
    int ComposingStart = -1,
    int ComposingEnd = -1)
{
    /// <summary>True when a composing region is active.</summary>
    public bool HasComposingRegion => ComposingStart >= 0 && ComposingEnd >= ComposingStart;

    /// <summary>True when the selection is an insertion point rather than a range.</summary>
    public bool IsCaret => SelectionStart == SelectionEnd;
}
