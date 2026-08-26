namespace Broiler.Input.Mouse;

public readonly record struct MouseLeaveEvent(
    InputEventHeader Header,
    MouseButtons Buttons,
    InputEventSource Source = InputEventSource.Semantic);
