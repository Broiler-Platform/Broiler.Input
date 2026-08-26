namespace Broiler.Input.Keyboard;

public enum KeyboardCompositionState
{
    Started = 0,
    Updated,
    Committed,
    Cancelled,
    Unsupported,
}
