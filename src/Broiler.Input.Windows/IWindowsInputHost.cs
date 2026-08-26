using System;

namespace Broiler.Input.Windows;

public interface IWindowsInputHost
{
    event Action<WindowsInputMessage>? MessageReceived;

    IntPtr MessageWindowHandle { get; }

    bool IsOnHostThread { get; }

    bool TryPost(Action callback);
}
