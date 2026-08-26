using System;

namespace Broiler.Input.Android;

/// <summary>
/// Builds the <see cref="InputFault"/> values the Android providers report.
/// </summary>
public static class AndroidInputFaults
{
    /// <summary>The device id is not one this provider knows about.</summary>
    public static InputFault DeviceNotFound(InputDeviceId id) =>
        new(InputErrorCategory.DeviceNotFound, "No Android input device is registered for '" + id.Value + "'.");

    /// <summary>The device was unplugged, or the Activity lost the input it was reading.</summary>
    public static InputFault DeviceRemoved(InputDeviceId id) =>
        new(InputErrorCategory.DeviceRemoved, "The Android input device '" + id.Value + "' was removed.");

    /// <summary>
    /// The window lost focus, the Activity was paused, or the gesture was taken over by a parent
    /// view. Contacts in flight are cancelled rather than left pressed.
    /// </summary>
    public static InputFault CaptureLost(string reason) =>
        new(InputErrorCategory.CaptureDiscontinuity, "Android input capture was lost: " + reason + ".");

    /// <summary>The host has not attached a view, so no events can arrive.</summary>
    public static InputFault HostUnavailable() =>
        new(InputErrorCategory.HostUnavailable, "No Android view is attached to deliver input events.");

    /// <summary>A capability the caller asked for is not offered by this device.</summary>
    public static InputFault UnsupportedCapability(string capability) =>
        new(InputErrorCategory.UnsupportedCapability, "The Android input device does not support " + capability + ".");

    /// <summary>An Android API call failed.</summary>
    public static InputFault NativeFailure(string operation, Exception? exception = null) =>
        new(InputErrorCategory.NativeFailure, "The Android input operation '" + operation + "' failed.", exception);
}
