using System;

namespace Broiler.Input.Scanner;

public readonly record struct ScannerImagePlane
{
    public ScannerImagePlane(int offset, int length, int stride, int width, int height)
    {
        if (offset < 0)
            throw new ArgumentOutOfRangeException(nameof(offset), "Plane offset must not be negative.");

        if (length < 0)
            throw new ArgumentOutOfRangeException(nameof(length), "Plane length must not be negative.");

        Offset = offset;
        Length = length;
        Stride = stride;
        Width = width;
        Height = height;
    }

    public int Offset { get; }

    public int Length { get; }

    public int Stride { get; }

    public int Width { get; }

    public int Height { get; }
}
