namespace Broiler.Input.Mouse;

public readonly record struct MouseButtonEvent(
    InputEventHeader Header,
    InputPoint Position,
    MouseButtons Buttons,
    MouseButton Button,
    MouseButtonTransition Transition,
    InputEventSource Source = InputEventSource.Semantic,
    InputModifiers Modifiers = InputModifiers.None);
