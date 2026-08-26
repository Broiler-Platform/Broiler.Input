using System;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Broiler.Input.Linux;

public sealed class LinuxEventDeviceStream : IDisposable
{
    private readonly SafeFileHandle _handle;
    private bool _disposed;

    private LinuxEventDeviceStream(string eventName, string eventPath, SafeFileHandle handle)
    {
        EventName = eventName;
        EventPath = eventPath;
        _handle = handle;
    }

    public string EventName { get; }

    public string EventPath { get; }

    public int FileDescriptor
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _handle.DangerousGetHandle().ToInt32();
        }
    }

    /// <summary>
    /// Reads the range/resolution of an absolute axis (EVIOCGABS). Used to
    /// normalize touchpad motion; returns false when the axis is unavailable.
    /// </summary>
    public bool TryGetAbsoluteAxis(ushort absCode, out int minimum, out int maximum, out int resolution)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return LinuxNativeMethods.TryGetAbsInfo(FileDescriptor, absCode, out minimum, out maximum, out resolution);
    }

    public static LinuxEventDeviceStream Open(string eventPath, string? eventName = null)
    {
        if (!OperatingSystem.IsLinux())
            throw new PlatformNotSupportedException("Linux evdev event devices can only be opened on Linux.");

        ArgumentException.ThrowIfNullOrWhiteSpace(eventPath);
        string sanitizedName = string.IsNullOrWhiteSpace(eventName)
            ? System.IO.Path.GetFileName(eventPath)
            : eventName;

        int flags = LinuxNativeMethods.O_RDONLY | LinuxNativeMethods.O_CLOEXEC | LinuxNativeMethods.O_NONBLOCK;
        int fd = LinuxNativeMethods.Open(eventPath, flags);
        if (fd < 0)
            throw LinuxEvdevFaults.CreateException(Marshal.GetLastPInvokeError(), "open", sanitizedName);

        SafeFileHandle handle = new(new IntPtr(fd), ownsHandle: true);
        LinuxNativeMethods.TrySetMonotonicClock(fd);
        return new LinuxEventDeviceStream(sanitizedName, eventPath, handle);
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _handle.Dispose();
    }
}
