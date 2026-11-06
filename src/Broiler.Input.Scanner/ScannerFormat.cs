using System;

namespace Broiler.Input.Scanner;

public sealed record ScannerFormat
{
    public ScannerFormat(ScannerSourceKind source, int dpiX, int dpiY, ScannerPixelFormat pixelFormat,
        ScannerRegion? region = null)
    {
        if (dpiX <= 0)
            throw new ArgumentOutOfRangeException(nameof(dpiX), "Scanner horizontal DPI must be positive.");

        if (dpiY <= 0)
            throw new ArgumentOutOfRangeException(nameof(dpiY), "Scanner vertical DPI must be positive.");

        Source = source;
        DpiX = dpiX;
        DpiY = dpiY;
        PixelFormat = pixelFormat;
        Region = region;
    }

    public ScannerSourceKind Source { get; }

    public int DpiX { get; }

    public int DpiY { get; }

    public ScannerPixelFormat PixelFormat { get; }

    public ScannerRegion? Region { get; }
}
