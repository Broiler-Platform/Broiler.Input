using System;
using System.Threading;

namespace Broiler.Input.Windows;

public sealed class WindowsInputMessageSubscription : IDisposable
{
    private readonly Action _dispose;
    private int _disposed;

    internal WindowsInputMessageSubscription(Action dispose)
    {
        _dispose = dispose ?? throw new ArgumentNullException(nameof(dispose));
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
            _dispose();
    }
}
