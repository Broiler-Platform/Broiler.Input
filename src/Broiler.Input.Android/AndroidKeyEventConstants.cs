namespace Broiler.Input.Android;

/// <summary>
/// Numeric constants from <c>android.view.KeyEvent</c>: actions, meta-state bits, and the key codes
/// the keyboard translation maps.
/// </summary>
public static class AndroidKeyEventConstants
{
    public const int ActionDown = 0;
    public const int ActionUp = 1;
    public const int ActionMultiple = 2;

    public const int MetaShiftOn = 0x00000001;
    public const int MetaAltOn = 0x00000002;
    public const int MetaSymOn = 0x00000004;
    public const int MetaFunctionOn = 0x00000008;
    public const int MetaAltLeftOn = 0x00000010;
    public const int MetaAltRightOn = 0x00000020;
    public const int MetaShiftLeftOn = 0x00000040;
    public const int MetaShiftRightOn = 0x00000080;
    public const int MetaCtrlOn = 0x00001000;
    public const int MetaCtrlLeftOn = 0x00002000;
    public const int MetaCtrlRightOn = 0x00004000;
    public const int MetaMetaOn = 0x00010000;
    public const int MetaMetaLeftOn = 0x00020000;
    public const int MetaMetaRightOn = 0x00040000;
    public const int MetaCapsLockOn = 0x00100000;
    public const int MetaNumLockOn = 0x00200000;
    public const int MetaScrollLockOn = 0x00400000;

    public const int KeycodeUnknown = 0;
    public const int KeycodeSoftLeft = 1;
    public const int KeycodeSoftRight = 2;
    public const int KeycodeHome = 3;
    public const int KeycodeBack = 4;

    public const int Keycode0 = 7;
    public const int Keycode9 = 16;

    public const int KeycodeDpadUp = 19;
    public const int KeycodeDpadDown = 20;
    public const int KeycodeDpadLeft = 21;
    public const int KeycodeDpadRight = 22;
    public const int KeycodeDpadCenter = 23;
    public const int KeycodeVolumeUp = 24;
    public const int KeycodeVolumeDown = 25;
    public const int KeycodePower = 26;

    public const int KeycodeA = 29;
    public const int KeycodeZ = 54;

    public const int KeycodeComma = 55;
    public const int KeycodePeriod = 56;
    public const int KeycodeAltLeft = 57;
    public const int KeycodeAltRight = 58;
    public const int KeycodeShiftLeft = 59;
    public const int KeycodeShiftRight = 60;
    public const int KeycodeTab = 61;
    public const int KeycodeSpace = 62;
    public const int KeycodeSym = 63;
    public const int KeycodeEnter = 66;

    /// <summary>Backspace. Android's <c>KEYCODE_DEL</c> is backspace, not forward delete.</summary>
    public const int KeycodeDel = 67;

    public const int KeycodeGrave = 68;
    public const int KeycodeMinus = 69;
    public const int KeycodeEquals = 70;
    public const int KeycodeLeftBracket = 71;
    public const int KeycodeRightBracket = 72;
    public const int KeycodeBackslash = 73;
    public const int KeycodeSemicolon = 74;
    public const int KeycodeApostrophe = 75;
    public const int KeycodeSlash = 76;
    public const int KeycodeAt = 77;
    public const int KeycodePlus = 81;
    public const int KeycodeMenu = 82;
    public const int KeycodeSearch = 84;
    public const int KeycodePageUp = 92;
    public const int KeycodePageDown = 93;
    public const int KeycodeEscape = 111;

    /// <summary>Forward delete. Android's <c>KEYCODE_FORWARD_DEL</c>.</summary>
    public const int KeycodeForwardDel = 112;

    public const int KeycodeCtrlLeft = 113;
    public const int KeycodeCtrlRight = 114;
    public const int KeycodeCapsLock = 115;
    public const int KeycodeScrollLock = 116;
    public const int KeycodeMetaLeft = 117;
    public const int KeycodeMetaRight = 118;
    public const int KeycodeFunction = 119;
    public const int KeycodeSysrq = 120;
    public const int KeycodeBreak = 121;
    public const int KeycodeMoveHome = 122;
    public const int KeycodeMoveEnd = 123;
    public const int KeycodeInsert = 124;

    public const int KeycodeF1 = 131;
    public const int KeycodeF12 = 142;

    public const int KeycodeNumLock = 143;
    public const int KeycodeNumpad0 = 144;
    public const int KeycodeNumpad9 = 153;
    public const int KeycodeNumpadDivide = 154;
    public const int KeycodeNumpadMultiply = 155;
    public const int KeycodeNumpadSubtract = 156;
    public const int KeycodeNumpadAdd = 157;
    public const int KeycodeNumpadDot = 158;
    public const int KeycodeNumpadComma = 159;
    public const int KeycodeNumpadEnter = 160;
    public const int KeycodeNumpadEquals = 161;
}
