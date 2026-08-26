namespace Broiler.Input.Text.Android;

/// <summary>
/// The editor's answers to the questions an input method asks.
/// </summary>
/// <remarks>
/// <c>InputConnection</c> is a two-way protocol. Alongside the text an IME sends, it queries the
/// editor — text around the cursor, the current selection — to decide what to compose, how to
/// auto-capitalise, and which candidates to offer. <c>IUiTextInputHost</c> exposes only
/// <c>PublishCaret</c>/<c>ClearCaret</c> today and cannot answer any of it.
///
/// This interface is the Android-side statement of what is missing. The editor-side contract itself
/// belongs in Broiler.UI, because Windows TSF and browser composition need the same queries; when
/// that lands, this becomes a thin adapter over it rather than a parallel abstraction. Until then
/// the host implements it directly over whatever editor it is driving, and an implementation that
/// returns empty strings degrades an IME rather than breaking it.
/// </remarks>
public interface IAndroidEditorTextSource
{
    /// <summary>
    /// Text immediately before the cursor, at most <paramref name="maxLength"/> UTF-16 code units.
    /// Backs <c>InputConnection.getTextBeforeCursor</c>.
    /// </summary>
    string GetTextBeforeCursor(int maxLength);

    /// <summary>
    /// Text immediately after the cursor, at most <paramref name="maxLength"/> UTF-16 code units.
    /// Backs <c>InputConnection.getTextAfterCursor</c>.
    /// </summary>
    string GetTextAfterCursor(int maxLength);

    /// <summary>The selected text, or an empty string for a caret. Backs <c>getSelectedText</c>.</summary>
    string GetSelectedText();

    /// <summary>The current selection and composing region. Backs <c>getExtractedText</c> reporting.</summary>
    AndroidEditorSelection GetSelection();
}
