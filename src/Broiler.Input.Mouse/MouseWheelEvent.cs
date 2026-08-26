namespace Broiler.Input.Mouse;

public readonly record struct MouseWheelEvent(
    InputEventHeader Header,
    InputPoint Position,
    MouseButtons Buttons,
    MouseWheelAxis Axis,
    double DeltaNotches,
    InputEventSource Source = InputEventSource.Semantic,
    InputModifiers Modifiers = InputModifiers.None);
