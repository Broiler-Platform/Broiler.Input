namespace Broiler.Input.Keyboard;

public sealed record KeyboardOpenOptions(
    bool ReceiveText = true,
    bool ConsumeSystemKeyMessages = false);
