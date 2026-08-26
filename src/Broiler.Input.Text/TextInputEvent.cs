namespace Broiler.Input.Text;

public readonly record struct TextInputEvent(
    InputEventHeader Header,
    string Text,
    InputEventSource Source = InputEventSource.Semantic);
