using System;

namespace Broiler.Input.Scanner;

public sealed record ScannerSessionOptions
{
    public ScannerSessionOptions(InputDeliveryOptions? deliveryOptions = null,
        ScannerDeliveryMode deliveryMode = ScannerDeliveryMode.LossSensitiveBatch,
        int maxPages = 0, double brightness = 0.0, double contrast = 0.0, bool reportFormatChanges = true)
    {
        if (maxPages < 0)
            throw new ArgumentOutOfRangeException(nameof(maxPages), "Maximum page count must not be negative.");

        if (brightness is < -1.0 or > 1.0 || double.IsNaN(brightness))
            throw new ArgumentOutOfRangeException(nameof(brightness), "Scanner brightness must be in [-1.0, 1.0].");

        if (contrast is < -1.0 or > 1.0 || double.IsNaN(contrast))
            throw new ArgumentOutOfRangeException(nameof(contrast), "Scanner contrast must be in [-1.0, 1.0].");

        DeliveryOptions = deliveryOptions ?? (deliveryMode == ScannerDeliveryMode.LatestPagePreview
            ? new InputDeliveryOptions(1, InputDeliveryOverflowPolicy.KeepLatest)
            : new InputDeliveryOptions(8, InputDeliveryOverflowPolicy.DropNewest));
        DeliveryMode = deliveryMode;
        MaxPages = maxPages;
        Brightness = brightness;
        Contrast = contrast;
        ReportFormatChanges = reportFormatChanges;
    }

    public InputDeliveryOptions DeliveryOptions { get; }

    public ScannerDeliveryMode DeliveryMode { get; }

    public int MaxPages { get; }

    public double Brightness { get; }

    public double Contrast { get; }

    public bool ReportFormatChanges { get; }

    public static ScannerSessionOptions BatchDefault { get; } = new();

    public static ScannerSessionOptions PreviewDefault { get; } =
        new(new InputDeliveryOptions(1, InputDeliveryOverflowPolicy.KeepLatest),
            ScannerDeliveryMode.LatestPagePreview, maxPages: 1);
}
