namespace Broiler.Input.Text;

/// <summary>
/// Discovers and opens text/IME devices, mirroring
/// <c>Broiler.Input.Keyboard.IKeyboardInputProvider</c>.
/// </summary>
public interface ITextInputProvider : IInputProvider<TextInputDevice, TextInputOpenOptions>
{
}
