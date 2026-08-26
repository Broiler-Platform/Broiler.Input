namespace Broiler.Input.Windows;

public readonly record struct WindowsRawKeyboardReport(
    WindowsRawInputDeviceIdentity Device,
    InputTimestamp Timestamp,
    ushort MakeCode,
    ushort Flags,
    ushort VirtualKey,
    uint Message);
