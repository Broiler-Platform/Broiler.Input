namespace Broiler.Input.Touch;

/// <summary>
/// Discovers and opens touch devices, mirroring
/// <c>Broiler.Input.Keyboard.IKeyboardInputProvider</c>.
/// </summary>
public interface ITouchInputProvider : IInputProvider<TouchInputDevice, TouchOpenOptions>
{
}
