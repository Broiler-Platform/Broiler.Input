using System;
using System.Buffers.Binary;
using System.Runtime.InteropServices;

namespace Broiler.Input.Linux;

internal static class LinuxNativeMethods
{
    public const int O_RDONLY = 0;
    public const int O_NONBLOCK = 0x800;
    public const int O_CLOEXEC = 0x80000;

    public const short POLLIN = 0x0001;
    public const short POLLERR = 0x0008;
    public const short POLLHUP = 0x0010;
    public const short POLLNVAL = 0x0020;

    public const int EINTR = 4;
    public const int EIO = 5;
    public const int EAGAIN = 11;
    public const int EACCES = 13;
    public const int EBUSY = 16;
    public const int ENODEV = 19;
    public const int ENOENT = 2;
    public const int EPERM = 1;

    private const nuint EVIOCSCLOCKID = 0x400445a0;
    private const int CLOCK_MONOTONIC = 1;

    [DllImport("libc", EntryPoint = "open", SetLastError = true)]
    public static extern int Open(
        [MarshalAs(UnmanagedType.LPUTF8Str)] string pathname,
        int flags);

    [DllImport("libc", EntryPoint = "read", SetLastError = true)]
    public static extern nint Read(int fd, byte[] buffer, nuint count);

    [DllImport("libc", EntryPoint = "poll", SetLastError = true)]
    public static extern int Poll(
        [In, Out] PollFd[] fds,
        nuint nfds,
        int timeout);

    [DllImport("libc", EntryPoint = "ioctl", SetLastError = true)]
    private static extern int IoctlClockId(int fd, nuint request, ref int clockId);

    [DllImport("libc", EntryPoint = "ioctl", SetLastError = true)]
    private static extern int IoctlAbsInfo(int fd, nuint request, byte[] absInfo);

    public static bool TrySetMonotonicClock(int fd)
    {
        int clockId = CLOCK_MONOTONIC;
        return IoctlClockId(fd, EVIOCSCLOCKID, ref clockId) == 0;
    }

    /// <summary>
    /// Reads a `struct input_absinfo` for an absolute axis via EVIOCGABS so
    /// touchpad deltas can be normalized against the pad's real range/resolution.
    /// </summary>
    public static bool TryGetAbsInfo(int fd, ushort absCode, out int minimum, out int maximum, out int resolution)
    {
        minimum = 0;
        maximum = 0;
        resolution = 0;

        // struct input_absinfo { int32 value, minimum, maximum, fuzz, flat, resolution; } => 24 bytes
        byte[] buffer = new byte[24];
        if (IoctlAbsInfo(fd, EviocgAbs(absCode), buffer) < 0)
            return false;

        minimum = BinaryPrimitives.ReadInt32LittleEndian(buffer.AsSpan(4, 4));
        maximum = BinaryPrimitives.ReadInt32LittleEndian(buffer.AsSpan(8, 4));
        resolution = BinaryPrimitives.ReadInt32LittleEndian(buffer.AsSpan(20, 4));
        return true;
    }

    // EVIOCGABS(abs) = _IOR('E', 0x40 + abs, struct input_absinfo)
    // _IOC(dir=2 read, type='E'=0x45, nr=0x40+abs, size=24).
    private static nuint EviocgAbs(ushort abs) =>
        (nuint)((2u << 30) | (24u << 16) | ((uint)'E' << 8) | (0x40u + abs));

    [StructLayout(LayoutKind.Sequential)]
    public struct PollFd
    {
        public int Fd;
        public short Events;
        public short Revents;
    }
}
