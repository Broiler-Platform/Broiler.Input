namespace Broiler.Input.Keyboard;

public readonly record struct KeyboardDeadKeyEvent(
    InputEventHeader Header,
    string Text,
    int NativeKeyCode,
    bool IsSystemKey = false,
    InputEventSource Source = InputEventSource.Semantic);
