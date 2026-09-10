using System;
using System.Collections.Generic;
using System.Linq;

namespace Broiler.Input.Camera;

public sealed class CameraFrameLease(byte[] buffer, CameraFormat format, IEnumerable<CameraFramePlane> planes,
    InputTimestamp timestamp, long frameNumber, CameraFrameFlags flags = CameraFrameFlags.None,
    CameraRotation rotation = CameraRotation.None, CameraColorSpace colorSpace = CameraColorSpace.Unknown) : IDisposable
{
    private readonly CameraFramePlane[] _planes = planes?.ToArray() ?? throw new ArgumentNullException(nameof(planes));
    private byte[]? _buffer = buffer ?? throw new ArgumentNullException(nameof(buffer));

    public ReadOnlyMemory<byte> Memory => _buffer ?? throw new ObjectDisposedException(nameof(CameraFrameLease));

    public CameraFormat Format { get; } = format ?? throw new ArgumentNullException(nameof(format));

    public IReadOnlyList<CameraFramePlane> Planes => _planes;

    public InputTimestamp Timestamp { get; } = timestamp;

    public long FrameNumber { get; } = frameNumber;

    public CameraFrameFlags Flags { get; } = flags;

    public CameraRotation Rotation { get; } = rotation;

    public CameraColorSpace ColorSpace { get; } = colorSpace;

    public bool IsDisposed => _buffer is null;

    public void Dispose() => _buffer = null;
}
