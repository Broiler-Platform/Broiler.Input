namespace Broiler.Input;

public readonly record struct InputEventHeader(
    InputDeviceId DeviceId,
    InputTimestamp Timestamp,
    long SequenceNumber);
