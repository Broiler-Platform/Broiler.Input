using System;
using System.Collections.Generic;
using System.Linq;

namespace Broiler.Input.Scanner;

public sealed class ScannerPageLease : IDisposable
{
    private readonly ScannerImagePlane[] _planes;
    private byte[]? _buffer;

    public ScannerPageLease(byte[] buffer, int width, int height, ScannerFormat format,
        IEnumerable<ScannerImagePlane> planes, InputTimestamp timestamp, long pageNumber,
        ScannerPageFlags flags = ScannerPageFlags.None, ScannerRotation rotation = ScannerRotation.None,
        ScannerColorSpace colorSpace = ScannerColorSpace.Unknown)
    {
        _buffer = buffer ?? throw new ArgumentNullException(nameof(buffer));

        if (width <= 0)
            throw new ArgumentOutOfRangeException(nameof(width), "Scanned page width must be positive.");

        if (height <= 0)
            throw new ArgumentOutOfRangeException(nameof(height), "Scanned page height must be positive.");

        if (pageNumber < 0)
            throw new ArgumentOutOfRangeException(nameof(pageNumber), "Scanned page number must not be negative.");

        Width = width;
        Height = height;
        Format = format ?? throw new ArgumentNullException(nameof(format));
        _planes = planes?.ToArray() ?? throw new ArgumentNullException(nameof(planes));
        Timestamp = timestamp;
        PageNumber = pageNumber;
        Flags = flags;
        Rotation = rotation;
        ColorSpace = colorSpace;
    }

    public ReadOnlyMemory<byte> Memory => _buffer ?? throw new ObjectDisposedException(nameof(ScannerPageLease));

    public int Width { get; }

    public int Height { get; }

    public ScannerFormat Format { get; }

    public IReadOnlyList<ScannerImagePlane> Planes => _planes;

    public InputTimestamp Timestamp { get; }

    public long PageNumber { get; }

    public ScannerPageFlags Flags { get; }

    public ScannerRotation Rotation { get; }

    public ScannerColorSpace ColorSpace { get; }

    public bool IsDisposed => _buffer is null;

    public void Dispose() => _buffer = null;
}
