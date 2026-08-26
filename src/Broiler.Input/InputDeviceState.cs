namespace Broiler.Input;

public enum InputDeviceState
{
    Discovered = 0,
    Opening,
    Open,
    Starting,
    Running,
    Stopping,
    Closed,
    Unavailable,
    Faulted,
    Disposed,
}
