namespace Broiler.Input;

public enum InputErrorCategory
{
    Unknown = 0,
    DeviceNotFound,
    DeviceRemoved,
    DeviceBusy,
    PermissionDenied,
    UnsupportedCapability,
    NativeFailure,
    CaptureDiscontinuity,
    HostUnavailable,
    OperationCanceled,
}
