using Broiler.Input;

namespace Broiler.Input.Microphone;

public readonly record struct MicrophoneCaptureStateChangedEvent(
    MicrophoneCaptureState PreviousState,
    MicrophoneCaptureState CurrentState,
    InputTimestamp Timestamp,
    InputFault? Fault);
