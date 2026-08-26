using Broiler.Input.Keyboard;

namespace Broiler.Input.Android;

/// <summary>
/// Translates an Android <c>metaState</c> bitfield into <see cref="KeyboardModifierState"/>.
/// </summary>
public static class AndroidModifierTranslation
{
    /// <summary>
    /// Maps the meta-state bits Broiler models. Android reports both a general bit
    /// (<c>META_SHIFT_ON</c>) and side-specific bits (<c>META_SHIFT_LEFT_ON</c>); a side bit implies
    /// the general one, so the general flag is set whenever either is present.
    /// </summary>
    public static KeyboardModifierState FromMetaState(int metaState)
    {
        KeyboardModifierState modifiers = KeyboardModifierState.None;

        if (Has(metaState, AndroidKeyEventConstants.MetaShiftLeftOn))
            modifiers |= KeyboardModifierState.Shift | KeyboardModifierState.LeftShift;
        if (Has(metaState, AndroidKeyEventConstants.MetaShiftRightOn))
            modifiers |= KeyboardModifierState.Shift | KeyboardModifierState.RightShift;
        if (Has(metaState, AndroidKeyEventConstants.MetaShiftOn))
            modifiers |= KeyboardModifierState.Shift;

        if (Has(metaState, AndroidKeyEventConstants.MetaCtrlLeftOn))
            modifiers |= KeyboardModifierState.Control | KeyboardModifierState.LeftControl;
        if (Has(metaState, AndroidKeyEventConstants.MetaCtrlRightOn))
            modifiers |= KeyboardModifierState.Control | KeyboardModifierState.RightControl;
        if (Has(metaState, AndroidKeyEventConstants.MetaCtrlOn))
            modifiers |= KeyboardModifierState.Control;

        if (Has(metaState, AndroidKeyEventConstants.MetaAltLeftOn))
            modifiers |= KeyboardModifierState.Alt | KeyboardModifierState.LeftAlt;
        if (Has(metaState, AndroidKeyEventConstants.MetaAltRightOn))
            modifiers |= KeyboardModifierState.Alt | KeyboardModifierState.RightAlt;
        if (Has(metaState, AndroidKeyEventConstants.MetaAltOn))
            modifiers |= KeyboardModifierState.Alt;

        // Broiler models the Meta/Super key with the Windows-key flags.
        if (Has(metaState, AndroidKeyEventConstants.MetaMetaLeftOn))
            modifiers |= KeyboardModifierState.LeftWindows;
        if (Has(metaState, AndroidKeyEventConstants.MetaMetaRightOn))
            modifiers |= KeyboardModifierState.RightWindows;
        if (Has(metaState, AndroidKeyEventConstants.MetaMetaOn) &&
            !Has(metaState, AndroidKeyEventConstants.MetaMetaLeftOn) &&
            !Has(metaState, AndroidKeyEventConstants.MetaMetaRightOn))
        {
            modifiers |= KeyboardModifierState.LeftWindows;
        }

        return modifiers;
    }

    private static bool Has(int metaState, int flag) => (metaState & flag) == flag;
}
