using System;
using Broiler.Input;

namespace Broiler.Input.Camera;

public sealed record CameraSessionOptions
{
    public CameraSessionOptions(
        InputDeliveryOptions? deliveryOptions = null,
        CameraFrameDeliveryMode deliveryMode = CameraFrameDeliveryMode.LatestFramePreview,
        bool reportFormatChanges = true,
        bool reportDiscontinuities = true)
    {
        DeliveryOptions = deliveryOptions ?? new InputDeliveryOptions(1, InputDeliveryOverflowPolicy.KeepLatest);
        DeliveryMode = deliveryMode;
        ReportFormatChanges = reportFormatChanges;
        ReportDiscontinuities = reportDiscontinuities;
    }

    public InputDeliveryOptions DeliveryOptions { get; }

    public CameraFrameDeliveryMode DeliveryMode { get; }

    public bool ReportFormatChanges { get; }

    public bool ReportDiscontinuities { get; }

    public static CameraSessionOptions PreviewDefault { get; } = new();

    public static CameraSessionOptions LossSensitiveDefault { get; } = new(
        new InputDeliveryOptions(4, InputDeliveryOverflowPolicy.DropNewest),
        CameraFrameDeliveryMode.LossSensitive);
}
