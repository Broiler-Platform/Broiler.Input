using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace Broiler.Input.Linux;

public sealed class LinuxEventDeviceReadLoop
{
    private readonly LinuxEventDeviceStream _stream;
    private readonly int _pollTimeoutMilliseconds;

    public LinuxEventDeviceReadLoop(LinuxEventDeviceStream stream, int pollTimeoutMilliseconds)
    {
        _stream = stream ?? throw new ArgumentNullException(nameof(stream));
        _pollTimeoutMilliseconds = pollTimeoutMilliseconds <= 0 ? 50 : pollTimeoutMilliseconds;
    }

    public void Run(
        Action<LinuxInputEvent> deliver,
        Action<InputFault> faulted,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(deliver);
        ArgumentNullException.ThrowIfNull(faulted);

        byte[] readBuffer = new byte[LinuxInputEventParser.InputEvent64Size * 64];
        byte[] pending = new byte[readBuffer.Length + LinuxInputEventParser.InputEvent64Size];
        int pendingLength = 0;

        while (!cancellationToken.IsCancellationRequested)
        {
            int pollResult = PollOnce(cancellationToken, faulted);
            if (pollResult < 0)
                return;

            if (pollResult == 0)
                continue;

            if (!ReadAvailable(readBuffer, pending, ref pendingLength, deliver, faulted, cancellationToken))
                return;
        }
    }

    private int PollOnce(CancellationToken cancellationToken, Action<InputFault> faulted)
    {
        LinuxNativeMethods.PollFd[] fds =
        [
            new()
            {
                Fd = _stream.FileDescriptor,
                Events = LinuxNativeMethods.POLLIN,
            },
        ];

        int result;
        do
        {
            result = LinuxNativeMethods.Poll(fds, 1, _pollTimeoutMilliseconds);
        }
        while (result < 0 && Marshal.GetLastPInvokeError() == LinuxNativeMethods.EINTR && !cancellationToken.IsCancellationRequested);

        if (cancellationToken.IsCancellationRequested)
            return 0;

        if (result < 0)
        {
            faulted(LinuxEvdevFaults.CreateFault(Marshal.GetLastPInvokeError(), "poll", _stream.EventName));
            return -1;
        }

        if ((fds[0].Revents & (LinuxNativeMethods.POLLERR | LinuxNativeMethods.POLLHUP | LinuxNativeMethods.POLLNVAL)) != 0)
        {
            faulted(LinuxEvdevFaults.CreateDeviceRemoved(_stream.EventName));
            return -1;
        }

        return result;
    }

    private bool ReadAvailable(
        byte[] readBuffer,
        byte[] pending,
        ref int pendingLength,
        Action<LinuxInputEvent> deliver,
        Action<InputFault> faulted,
        CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            nint read = LinuxNativeMethods.Read(_stream.FileDescriptor, readBuffer, (nuint)readBuffer.Length);
            if (read > 0)
            {
                int byteCount = checked((int)read);
                if (pendingLength + byteCount > pending.Length)
                {
                    pendingLength = 0;
                    faulted(LinuxEvdevFaults.CreateCaptureDiscontinuity(_stream.EventName));
                    continue;
                }

                Buffer.BlockCopy(readBuffer, 0, pending, pendingLength, byteCount);
                pendingLength += byteCount;
                DispatchParsedEvents(pending, ref pendingLength, deliver, faulted);
                continue;
            }

            if (read == 0)
            {
                faulted(LinuxEvdevFaults.CreateDeviceRemoved(_stream.EventName));
                return false;
            }

            int errno = Marshal.GetLastPInvokeError();
            if (errno == LinuxNativeMethods.EAGAIN)
                return true;

            if (errno == LinuxNativeMethods.EINTR)
                continue;

            faulted(LinuxEvdevFaults.CreateFault(errno, "read", _stream.EventName));
            return false;
        }

        return true;
    }

    private static void DispatchParsedEvents(
        byte[] pending,
        ref int pendingLength,
        Action<LinuxInputEvent> deliver,
        Action<InputFault> faulted)
    {
        int offset = 0;
        while (LinuxInputEventParser.TryRead64(pending.AsSpan(offset, pendingLength - offset), out LinuxInputEvent inputEvent, out int consumed))
        {
            try
            {
                deliver(inputEvent);
            }
            catch (Exception exception)
            {
                faulted(new InputFault(InputErrorCategory.Unknown, "Linux evdev input callback failed.", exception, null, "evdev"));
            }

            offset += consumed;
        }

        if (offset <= 0)
            return;

        int remaining = pendingLength - offset;
        if (remaining > 0)
            Buffer.BlockCopy(pending, offset, pending, 0, remaining);

        pendingLength = remaining;
    }
}
