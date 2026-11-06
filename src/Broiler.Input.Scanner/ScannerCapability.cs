using System;
using System.Collections.Generic;
using System.Linq;

namespace Broiler.Input.Scanner;

public sealed record ScannerCapability
{
    public ScannerCapability(ScannerSourceKind source, int minDpi, int maxDpi, int stepDpi,
        int maxWidthMicrons, int maxHeightMicrons, IEnumerable<ScannerPixelFormat> supportedPixelFormats,
        bool supportsDuplex = false, bool isDefault = false)
    {
        if (minDpi <= 0)
            throw new ArgumentOutOfRangeException(nameof(minDpi), "Minimum scanner DPI must be positive.");

        if (maxDpi < minDpi)
            throw new ArgumentOutOfRangeException(nameof(maxDpi), "Maximum scanner DPI must be at least the minimum DPI.");

        if (stepDpi < 0)
            throw new ArgumentOutOfRangeException(nameof(stepDpi), "Scanner DPI step must not be negative.");

        if (maxWidthMicrons <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxWidthMicrons), "Maximum scan width must be positive.");

        if (maxHeightMicrons <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxHeightMicrons), "Maximum scan height must be positive.");

        ArgumentNullException.ThrowIfNull(supportedPixelFormats);

        Source = source;
        MinDpi = minDpi;
        MaxDpi = maxDpi;
        StepDpi = stepDpi;
        MaxWidthMicrons = maxWidthMicrons;
        MaxHeightMicrons = maxHeightMicrons;
        SupportedPixelFormats = supportedPixelFormats.ToArray();
        SupportsDuplex = supportsDuplex;
        IsDefault = isDefault;
    }

    public ScannerSourceKind Source { get; }

    public int MinDpi { get; }

    public int MaxDpi { get; }

    public int StepDpi { get; }

    public int MaxWidthMicrons { get; }

    public int MaxHeightMicrons { get; }

    public IReadOnlyList<ScannerPixelFormat> SupportedPixelFormats { get; }

    public bool SupportsDuplex { get; }

    public bool IsDefault { get; }
}
