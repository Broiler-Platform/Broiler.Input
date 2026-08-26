using System;

namespace Broiler.Input.Windows;

public readonly record struct WindowsInputMessage(
    IntPtr Hwnd,
    uint Message,
    IntPtr WParam,
    IntPtr LParam,
    InputTimestamp Timestamp)
{
    public WindowsInputMessage(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam)
        : this(hwnd, message, wParam, lParam, WindowsInputClock.Shared.GetTimestamp())
    {
    }
}
