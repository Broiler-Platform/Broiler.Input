namespace Broiler.Input.Touch;

public readonly record struct TouchContactEvent(
    InputEventHeader Header,
    long ContactId,
    InputPoint Position,
    TouchContactState State,
    double Pressure = 0,
    InputEventSource Source = InputEventSource.Semantic);

