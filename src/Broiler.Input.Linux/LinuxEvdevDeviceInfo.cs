namespace Broiler.Input.Linux;

public sealed record LinuxEvdevDeviceInfo(
    LinuxEvdevDeviceKind Kind,
    string EventName,
    string EventPath,
    string DisplayName,
    InputDeviceDescriptor Descriptor,
    LinuxEvdevCapabilitySet Capabilities);
