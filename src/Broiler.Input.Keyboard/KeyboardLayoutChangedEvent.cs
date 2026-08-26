namespace Broiler.Input.Keyboard;

public readonly record struct KeyboardLayoutChangedEvent(
    InputEventHeader Header,
    nint NativeKeyboardLayout,
    int CharacterSet,
    string? LayoutName = null,
    InputEventSource Source = InputEventSource.Semantic);
