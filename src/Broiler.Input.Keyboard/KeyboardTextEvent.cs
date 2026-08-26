namespace Broiler.Input.Keyboard;

public readonly record struct KeyboardTextEvent(
    InputEventHeader Header,
    string Text,
    bool IsSystemText = false,
    InputEventSource Source = InputEventSource.Semantic);
