using System;

namespace Broiler.Input.Camera;

public sealed record CameraFormat
{
    public CameraFormat(
        int width,
        int height,
        int frameRateNumerator,
        int frameRateDenominator,
        CameraPixelFormat pixelFormat)
    {
        if (width <= 0)
            throw new ArgumentOutOfRangeException(nameof(width), "Camera width must be positive.");
        if (height <= 0)
            throw new ArgumentOutOfRangeException(nameof(height), "Camera height must be positive.");
        if (frameRateNumerator < 0)
            throw new ArgumentOutOfRangeException(nameof(frameRateNumerator), "Frame rate numerator must not be negative.");
        if (frameRateDenominator <= 0)
            throw new ArgumentOutOfRangeException(nameof(frameRateDenominator), "Frame rate denominator must be positive.");

        Width = width;
        Height = height;
        FrameRateNumerator = frameRateNumerator;
        FrameRateDenominator = frameRateDenominator;
        PixelFormat = pixelFormat;
    }

    public int Width { get; }

    public int Height { get; }

    public int FrameRateNumerator { get; }

    public int FrameRateDenominator { get; }

    public CameraPixelFormat PixelFormat { get; }

    public double FramesPerSecond => FrameRateDenominator == 0 ? 0 : (double)FrameRateNumerator / FrameRateDenominator;
}
