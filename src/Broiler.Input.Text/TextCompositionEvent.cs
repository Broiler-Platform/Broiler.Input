namespace Broiler.Input.Text;

public readonly record struct TextCompositionEvent(
    InputEventHeader Header,
    string Text,
    TextCompositionState State,
    int SelectionStart = 0,
    int SelectionLength = 0,
    InputEventSource Source = InputEventSource.Semantic);
