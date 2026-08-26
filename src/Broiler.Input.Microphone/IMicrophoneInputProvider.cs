using Broiler.Input;

namespace Broiler.Input.Microphone;

public interface IMicrophoneInputProvider : IInputProvider<MicrophoneInputDevice, MicrophoneOpenOptions>
{
}
