namespace Broiler.Input;

public sealed class InputDeviceChange
{
    public InputDeviceChange(InputDeviceChangeKind kind, InputDeviceDescriptor descriptor, InputTimestamp timestamp)
    {
        Kind = kind;
        Descriptor = descriptor;
        Timestamp = timestamp;
    }

    public InputDeviceChangeKind Kind { get; }

    public InputDeviceDescriptor Descriptor { get; }

    public InputTimestamp Timestamp { get; }
}
