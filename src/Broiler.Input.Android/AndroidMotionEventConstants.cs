namespace Broiler.Input.Android;

/// <summary>
/// Numeric constants from <c>android.view.MotionEvent</c>.
/// </summary>
/// <remarks>
/// The Android input assemblies deliberately target plain <c>net10.0</c> and accept primitive
/// event data rather than referencing <c>Mono.Android</c>. Every value a provider needs from a
/// <c>MotionEvent</c> or <c>KeyEvent</c> is an <see cref="int"/> or a <see cref="float"/>, so the
/// translation layer stays host-independent and unit-testable without the Android workload, and no
/// <c>Android.Views</c> type reaches Broiler.Input or Broiler.UI. The host — which owns the
/// Activity and the View — reads the real event objects and forwards these primitives.
/// </remarks>
public static class AndroidMotionEventConstants
{
    /// <summary>Masks the action code out of the packed action field.</summary>
    public const int ActionMask = 0x000000ff;

    /// <summary>Masks the pointer index out of the packed action field.</summary>
    public const int ActionPointerIndexMask = 0x0000ff00;

    /// <summary>Bit offset of the pointer index inside the packed action field.</summary>
    public const int ActionPointerIndexShift = 8;

    public const int ActionDown = 0;
    public const int ActionUp = 1;
    public const int ActionMove = 2;
    public const int ActionCancel = 3;
    public const int ActionOutside = 4;
    public const int ActionPointerDown = 5;
    public const int ActionPointerUp = 6;
    public const int ActionHoverMove = 7;
    public const int ActionScroll = 8;
    public const int ActionHoverEnter = 9;
    public const int ActionHoverExit = 10;
    public const int ActionButtonPress = 11;
    public const int ActionButtonRelease = 12;

    public const int ToolTypeUnknown = 0;
    public const int ToolTypeFinger = 1;
    public const int ToolTypeStylus = 2;
    public const int ToolTypeMouse = 3;
    public const int ToolTypeEraser = 4;

    public const int ButtonPrimary = 1;
    public const int ButtonSecondary = 2;
    public const int ButtonTertiary = 4;
    public const int ButtonBack = 8;
    public const int ButtonForward = 16;
    public const int ButtonStylusPrimary = 32;
    public const int ButtonStylusSecondary = 64;

    /// <summary>Extracts the action code from a packed <c>getAction()</c> value.</summary>
    public static int MaskedAction(int packedAction) => packedAction & ActionMask;

    /// <summary>Extracts the pointer index from a packed <c>getAction()</c> value.</summary>
    public static int PointerIndex(int packedAction) =>
        (packedAction & ActionPointerIndexMask) >> ActionPointerIndexShift;
}
