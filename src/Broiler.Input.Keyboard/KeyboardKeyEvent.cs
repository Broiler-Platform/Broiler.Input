namespace Broiler.Input.Keyboard;

public readonly record struct KeyboardKeyEvent(
    InputEventHeader Header,
    KeyboardKey Key,
    KeyboardKeyTransition Transition,
    KeyboardModifierState Modifiers,
    int NativeKeyCode,
    int ScanCode,
    int RepeatCount,
    bool IsExtended,
    bool WasDown,
    KeyboardKeyLocation Location = KeyboardKeyLocation.Standard,
    InputEventSource Source = InputEventSource.Semantic,
    bool IsSystemKey = false);
