namespace Broiler.Input.Pen;

public readonly record struct PenContactEvent(
    InputEventHeader Header,
    InputPoint Position,
    PenContactState State,
    PenButtons Buttons,
    double Pressure = 0,
    double TiltX = 0,
    double TiltY = 0,
    InputEventSource Source = InputEventSource.Semantic);

