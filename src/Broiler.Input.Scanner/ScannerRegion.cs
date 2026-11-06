using System;

namespace Broiler.Input.Scanner;

public readonly record struct ScannerRegion
{
    private const double MicronsPerMillimeter = 1_000.0;
    private const double MicronsPerInch = 25_400.0;
    private const double MicronsPerThousandthOfInch = 25.4;

    public ScannerRegion(int offsetXMicrons, int offsetYMicrons, int widthMicrons, int heightMicrons)
    {
        if (offsetXMicrons < 0)
            throw new ArgumentOutOfRangeException(nameof(offsetXMicrons), "Scanner region X offset must not be negative.");

        if (offsetYMicrons < 0)
            throw new ArgumentOutOfRangeException(nameof(offsetYMicrons), "Scanner region Y offset must not be negative.");

        if (widthMicrons <= 0)
            throw new ArgumentOutOfRangeException(nameof(widthMicrons), "Scanner region width must be positive.");

        if (heightMicrons <= 0)
            throw new ArgumentOutOfRangeException(nameof(heightMicrons), "Scanner region height must be positive.");

        OffsetXMicrons = offsetXMicrons;
        OffsetYMicrons = offsetYMicrons;
        WidthMicrons = widthMicrons;
        HeightMicrons = heightMicrons;
    }

    public int OffsetXMicrons { get; }

    public int OffsetYMicrons { get; }

    public int WidthMicrons { get; }

    public int HeightMicrons { get; }

    public static ScannerRegion FromMillimeters(double offsetXMm, double offsetYMm, double widthMm, double heightMm) =>
        new(
            checked((int)Math.Round(offsetXMm * MicronsPerMillimeter)),
            checked((int)Math.Round(offsetYMm * MicronsPerMillimeter)),
            checked((int)Math.Round(widthMm * MicronsPerMillimeter)),
            checked((int)Math.Round(heightMm * MicronsPerMillimeter)));

    public static ScannerRegion FromInches(double offsetXInches, double offsetYInches, double widthInches, double heightInches) =>
        new(
            checked((int)Math.Round(offsetXInches * MicronsPerInch)),
            checked((int)Math.Round(offsetYInches * MicronsPerInch)),
            checked((int)Math.Round(widthInches * MicronsPerInch)),
            checked((int)Math.Round(heightInches * MicronsPerInch)));

    public static ScannerRegion FromThousandthsOfInch(int offsetXMils, int offsetYMils, int widthMils, int heightMils) =>
        new(
            checked((int)Math.Round(offsetXMils * MicronsPerThousandthOfInch)),
            checked((int)Math.Round(offsetYMils * MicronsPerThousandthOfInch)),
            checked((int)Math.Round(widthMils * MicronsPerThousandthOfInch)),
            checked((int)Math.Round(heightMils * MicronsPerThousandthOfInch)));
}
