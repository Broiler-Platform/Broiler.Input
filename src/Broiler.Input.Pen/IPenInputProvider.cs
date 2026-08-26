namespace Broiler.Input.Pen;

/// <summary>
/// Discovers and opens pen devices, mirroring
/// <c>Broiler.Input.Keyboard.IKeyboardInputProvider</c>.
/// </summary>
public interface IPenInputProvider : IInputProvider<PenInputDevice, PenOpenOptions>
{
}
