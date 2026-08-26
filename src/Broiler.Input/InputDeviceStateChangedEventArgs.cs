using System;

namespace Broiler.Input;

public sealed class InputDeviceStateChangedEventArgs : EventArgs
{
    public InputDeviceStateChangedEventArgs(
        InputDeviceState previousState,
        InputDeviceState currentState,
        InputTimestamp timestamp)
    {
        PreviousState = previousState;
        CurrentState = currentState;
        Timestamp = timestamp;
    }

    public InputDeviceState PreviousState { get; }

    public InputDeviceState CurrentState { get; }

    public InputTimestamp Timestamp { get; }
}
