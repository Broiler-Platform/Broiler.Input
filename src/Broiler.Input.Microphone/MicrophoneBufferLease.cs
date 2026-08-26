using System;
using Broiler.Input;

namespace Broiler.Input.Microphone;

public sealed class MicrophoneBufferLease : IDisposable
{
    private byte[]? _buffer;

    public MicrophoneBufferLease(
        byte[] buffer,
        MicrophoneFormat format,
        InputTimestamp timestamp,
        long devicePosition,
        MicrophoneBufferFlags flags)
    {
        _buffer = buffer ?? throw new ArgumentNullException(nameof(buffer));
        Format = format ?? throw new ArgumentNullException(nameof(format));
        Timestamp = timestamp;
        DevicePosition = devicePosition;
        Flags = flags;

        int bytesPerFrame = format.BytesPerFrame;
        FrameCount = bytesPerFrame == 0 ? 0 : buffer.Length / bytesPerFrame;
        Duration = format.SampleRate <= 0
            ? TimeSpan.Zero
            : TimeSpan.FromSeconds((double)FrameCount / format.SampleRate);
    }

    public ReadOnlyMemory<byte> Memory =>
        _buffer ?? throw new ObjectDisposedException(nameof(MicrophoneBufferLease));

    public MicrophoneFormat Format { get; }

    public InputTimestamp Timestamp { get; }

    public long DevicePosition { get; }

    public int FrameCount { get; }

    public TimeSpan Duration { get; }

    public MicrophoneBufferFlags Flags { get; }

    public bool IsDisposed => _buffer is null;

    public void Dispose()
    {
        _buffer = null;
    }
}
