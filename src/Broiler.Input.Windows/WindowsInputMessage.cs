using System;
using System.Runtime.Versioning;

namespace Broiler.Input.Windows;

[SupportedOSPlatform("windows")]
public readonly record struct WindowsInputMessage(IntPtr Hwnd, uint Message, IntPtr WParam, IntPtr LParam, InputTimestamp Timestamp)
{
    public WindowsInputMessage(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam)
        : this(hwnd, message, wParam, lParam, WindowsInputClock.Shared.GetTimestamp()) { }
}
