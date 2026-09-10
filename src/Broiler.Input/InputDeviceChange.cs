namespace Broiler.Input;

public sealed class InputDeviceChange(InputDeviceChangeKind kind, InputDeviceDescriptor descriptor, InputTimestamp timestamp)
{
    public InputDeviceChangeKind Kind { get; } = kind;

    public InputDeviceDescriptor Descriptor { get; } = descriptor;

    public InputTimestamp Timestamp { get; } = timestamp;
}
