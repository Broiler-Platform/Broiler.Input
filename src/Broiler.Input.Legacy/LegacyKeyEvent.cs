using Broiler.Input.Keyboard;

namespace Broiler.Input.Legacy;

public readonly record struct LegacyKeyEvent(
    int VirtualKey,
    bool Control,
    bool Shift,
    bool Alt,
    int ScanCode = 0,
    KeyboardKeyLocation Location = KeyboardKeyLocation.Standard,
    bool IsSystemKey = false);
