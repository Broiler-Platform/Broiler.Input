namespace Broiler.Input.Windows;

public readonly record struct WindowsRawMouseReport(
    WindowsRawInputDeviceIdentity Device,
    InputTimestamp Timestamp,
    int DeltaX,
    int DeltaY,
    ushort ButtonFlags,
    ushort ButtonData,
    uint RawButtons,
    bool IsAbsolute);
