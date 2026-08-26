using Broiler.Input;

namespace Broiler.Input.Microphone;

public readonly record struct MicrophoneBufferReadyEvent(
    InputEventHeader Header,
    MicrophoneBufferLease Buffer);
