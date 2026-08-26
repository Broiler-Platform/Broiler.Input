namespace Broiler.Input.Windows;

public readonly record struct WindowsRawInputReport
{
    public WindowsRawInputReport(WindowsRawMouseReport mouse)
    {
        Kind = WindowsRawInputDeviceKind.Mouse;
        Mouse = mouse;
        Keyboard = null;
    }

    public WindowsRawInputReport(WindowsRawKeyboardReport keyboard)
    {
        Kind = WindowsRawInputDeviceKind.Keyboard;
        Mouse = null;
        Keyboard = keyboard;
    }

    public WindowsRawInputDeviceKind Kind { get; }

    public WindowsRawMouseReport? Mouse { get; }

    public WindowsRawKeyboardReport? Keyboard { get; }
}
