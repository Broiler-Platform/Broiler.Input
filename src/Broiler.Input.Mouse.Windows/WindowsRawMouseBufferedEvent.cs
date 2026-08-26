using Broiler.Input.Windows;

namespace Broiler.Input.Mouse.Windows;

public readonly record struct WindowsRawMouseBufferedEvent(
    WindowsRawInputDeviceIdentity Device,
    InputTimestamp Timestamp,
    int DeltaX,
    int DeltaY,
    ushort ButtonFlags,
    ushort ButtonData,
    bool IsAbsolute);
