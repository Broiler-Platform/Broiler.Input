namespace Broiler.Input.Linux;

public readonly record struct LinuxInputEvent(
    InputTimestamp Timestamp,
    ushort Type,
    ushort Code,
    int Value);
