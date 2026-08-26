namespace Broiler.Input.Camera;

public readonly record struct CameraCaptureStatistics(
    long CapturedCount,
    long DeliveredCount,
    long DroppedNewestCount,
    long DroppedOldestCount,
    long FormatChangedCount,
    long DiscontinuousCount,
    int QueueDepth);
