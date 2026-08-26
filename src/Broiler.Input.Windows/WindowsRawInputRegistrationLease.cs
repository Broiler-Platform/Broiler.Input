using System;
using System.Threading;

namespace Broiler.Input.Windows;

public sealed class WindowsRawInputRegistrationLease : IDisposable
{
    private readonly WindowsRawInputRegistrationCoordinator _owner;
    private int _disposed;

    internal WindowsRawInputRegistrationLease(
        WindowsRawInputRegistrationCoordinator owner,
        WindowsRawInputDeviceKind kind,
        IntPtr targetWindow,
        WindowsRawInputRegistrationOptions options)
    {
        _owner = owner;
        Kind = kind;
        TargetWindow = targetWindow;
        Options = options;
    }

    public WindowsRawInputDeviceKind Kind { get; }

    public IntPtr TargetWindow { get; }

    public WindowsRawInputRegistrationOptions Options { get; }

    public bool IsDisposed => Volatile.Read(ref _disposed) != 0;

    public int? LastUnregisterError { get; private set; }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
            _owner.Release(this);
    }

    internal void SetLastUnregisterError(int error) => LastUnregisterError = error;
}
