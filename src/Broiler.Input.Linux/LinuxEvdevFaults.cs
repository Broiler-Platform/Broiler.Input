using System;
using System.IO;

namespace Broiler.Input.Linux;

internal static class LinuxEvdevFaults
{
    public static LinuxInputException CreateException(int errno, string operation, string eventName, Exception? exception = null) =>
        new(CreateFault(errno, operation, eventName, exception));

    public static InputFault CreateFault(int errno, string operation, string eventName, Exception? exception = null)
    {
        InputErrorCategory category = errno switch
        {
            LinuxNativeMethods.EACCES or LinuxNativeMethods.EPERM => InputErrorCategory.PermissionDenied,
            LinuxNativeMethods.ENOENT => InputErrorCategory.DeviceNotFound,
            LinuxNativeMethods.ENODEV or LinuxNativeMethods.EIO => InputErrorCategory.DeviceRemoved,
            LinuxNativeMethods.EBUSY => InputErrorCategory.DeviceBusy,
            _ => InputErrorCategory.NativeFailure,
        };

        return new InputFault(
            category,
            $"Linux evdev {operation} failed for {eventName}: {MessageFor(errno)}",
            exception,
            errno,
            "evdev");
    }

    public static LinuxInputException CreateException(IOException exception, string operation, string eventName) =>
        new(new InputFault(InputErrorCategory.NativeFailure, $"Linux evdev {operation} failed for {eventName}.", exception, null, "evdev"));

    public static InputFault CreateDeviceRemoved(string eventName) =>
        new(InputErrorCategory.DeviceRemoved, $"Linux evdev event device {eventName} was removed or stopped producing readable input.", null, LinuxNativeMethods.ENODEV, "evdev");

    public static InputFault CreateCaptureDiscontinuity(string eventName) =>
        new(InputErrorCategory.CaptureDiscontinuity, $"Linux evdev reported SYN_DROPPED for {eventName}; pending relative input was discarded.", null, null, "evdev");

    private static string MessageFor(int errno) =>
        errno switch
        {
            LinuxNativeMethods.EACCES or LinuxNativeMethods.EPERM => "permission denied. Check input group membership, udev rules, container device pass-through, or seat-broker policy.",
            LinuxNativeMethods.ENOENT => "event device was not found.",
            LinuxNativeMethods.ENODEV or LinuxNativeMethods.EIO => "event device was removed.",
            LinuxNativeMethods.EBUSY => "event device is busy.",
            LinuxNativeMethods.EAGAIN => "event device would block.",
            LinuxNativeMethods.EINTR => "system call was interrupted.",
            _ => "native failure errno " + errno + ".",
        };
}
