using System;

namespace Broiler.Input.Windows;

public readonly record struct WindowsRawInputDeviceIdentity
{
    public WindowsRawInputDeviceIdentity(IntPtr deviceHandle)
    {
        DeviceHandle = deviceHandle;
    }

    public IntPtr DeviceHandle { get; }

    public InputDeviceId ToInputDeviceId() =>
        InputDeviceId.FromOpaqueValue("windows:raw:" + DeviceHandle.ToInt64().ToString("X"));
}
