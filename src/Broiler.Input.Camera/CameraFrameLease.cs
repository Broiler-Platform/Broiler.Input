using System;
using System.Collections.Generic;
using System.Linq;
using Broiler.Input;

namespace Broiler.Input.Camera;

public sealed class CameraFrameLease : IDisposable
{
    private readonly CameraFramePlane[] _planes;
    private byte[]? _buffer;

    public CameraFrameLease(
        byte[] buffer,
        CameraFormat format,
        IEnumerable<CameraFramePlane> planes,
        InputTimestamp timestamp,
        long frameNumber,
        CameraFrameFlags flags = CameraFrameFlags.None,
        CameraRotation rotation = CameraRotation.None,
        CameraColorSpace colorSpace = CameraColorSpace.Unknown)
    {
        _buffer = buffer ?? throw new ArgumentNullException(nameof(buffer));
        Format = format ?? throw new ArgumentNullException(nameof(format));
        _planes = planes?.ToArray() ?? throw new ArgumentNullException(nameof(planes));
        Timestamp = timestamp;
        FrameNumber = frameNumber;
        Flags = flags;
        Rotation = rotation;
        ColorSpace = colorSpace;
    }

    public ReadOnlyMemory<byte> Memory =>
        _buffer ?? throw new ObjectDisposedException(nameof(CameraFrameLease));

    public CameraFormat Format { get; }

    public IReadOnlyList<CameraFramePlane> Planes => _planes;

    public InputTimestamp Timestamp { get; }

    public long FrameNumber { get; }

    public CameraFrameFlags Flags { get; }

    public CameraRotation Rotation { get; }

    public CameraColorSpace ColorSpace { get; }

    public bool IsDisposed => _buffer is null;

    public void Dispose()
    {
        _buffer = null;
    }
}
