using Broiler.Input;

namespace Broiler.Input.Camera;

public readonly record struct CameraCaptureStateChangedEvent(
    CameraCaptureState PreviousState,
    CameraCaptureState CurrentState,
    InputTimestamp Timestamp,
    InputFault? Fault);
