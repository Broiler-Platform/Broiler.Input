namespace Broiler.Input.Text;

/// <summary>
/// Options for opening a <see cref="TextInputDevice"/>.
/// </summary>
/// <param name="ReportComposition">
/// Deliver <see cref="TextCompositionEvent"/> while an input method is composing. Turning it off
/// leaves only committed text, which loses the pre-edit an IME needs to display inline.
/// </param>
/// <param name="CancelCompositionOnFocusLoss">
/// Emit <see cref="TextCompositionState.Cancelled"/> for an in-flight composition when the editor
/// loses focus, rather than leaving a stale pre-edit in the document.
/// </param>
public sealed record TextInputOpenOptions(
    bool ReportComposition = true,
    bool CancelCompositionOnFocusLoss = true);
