using System;

namespace Broiler.Input;

public sealed class InputDeviceStateChangedEventArgs(InputDeviceState previousState,
    InputDeviceState currentState, InputTimestamp timestamp) : EventArgs
{
    public InputDeviceState PreviousState { get; } = previousState;

    public InputDeviceState CurrentState { get; } = currentState;

    public InputTimestamp Timestamp { get; } = timestamp;
}
