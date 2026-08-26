namespace Broiler.Input.Android;

/// <summary>
/// Numeric source constants from <c>android.view.InputDevice</c>, used to classify the origin of a
/// motion or key event.
/// </summary>
public static class AndroidInputSource
{
    public const int Unknown = 0x00000000;

    public const int ClassButton = 0x00000001;
    public const int ClassPointer = 0x00000002;
    public const int ClassTrackball = 0x00000004;
    public const int ClassPosition = 0x00000008;
    public const int ClassJoystick = 0x00000010;

    public const int Keyboard = 0x00000101;
    public const int Dpad = 0x00000201;
    public const int Gamepad = 0x00000401;
    public const int Touchscreen = 0x00001002;
    public const int Mouse = 0x00002002;
    public const int Stylus = 0x00004002;
    public const int BluetoothStylus = 0x00008002;
    public const int Trackball = 0x00010004;
    public const int MouseRelative = 0x00020004;
    public const int Touchpad = 0x00100008;
    public const int TouchNavigation = 0x00200000;
    public const int Joystick = 0x01000010;

    /// <summary>Returns true when <paramref name="source"/> contains every bit of <paramref name="expected"/>.</summary>
    public static bool Matches(int source, int expected) => (source & expected) == expected;

    /// <summary>Returns true for any stylus source, including Bluetooth styluses.</summary>
    public static bool IsStylus(int source) => Matches(source, Stylus) || Matches(source, BluetoothStylus);

    /// <summary>Returns true for mouse, relative-mouse, and touchpad sources.</summary>
    public static bool IsMouseLike(int source) =>
        Matches(source, Mouse) || Matches(source, MouseRelative) || Matches(source, Touchpad);
}
