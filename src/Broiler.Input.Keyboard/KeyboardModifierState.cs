using System;

namespace Broiler.Input.Keyboard;

[Flags]
public enum KeyboardModifierState
{
    None = 0,
    Shift = 1,
    Control = 2,
    Alt = 4,
    LeftWindows = 8,
    RightWindows = 16,
    LeftShift = 32,
    RightShift = 64,
    LeftControl = 128,
    RightControl = 256,
    LeftAlt = 512,
    RightAlt = 1024,
}
