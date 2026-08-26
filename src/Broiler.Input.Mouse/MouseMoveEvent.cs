namespace Broiler.Input.Mouse;

public readonly record struct MouseMoveEvent(
    InputEventHeader Header,
    InputPoint Position,
    MouseButtons Buttons,
    InputEventSource Source = InputEventSource.Semantic,
    InputModifiers Modifiers = InputModifiers.None);
