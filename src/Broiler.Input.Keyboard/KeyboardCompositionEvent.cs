namespace Broiler.Input.Keyboard;

public readonly record struct KeyboardCompositionEvent(
    InputEventHeader Header,
    KeyboardCompositionState State,
    string Text = "",
    string? Detail = null,
    InputEventSource Source = InputEventSource.Semantic);
