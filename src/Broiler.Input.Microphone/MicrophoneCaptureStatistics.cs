namespace Broiler.Input.Microphone;

public readonly record struct MicrophoneCaptureStatistics(
    long CapturedCount,
    long DeliveredCount,
    long DroppedNewestCount,
    long DroppedOldestCount,
    long SilentCount,
    long DiscontinuousCount,
    int QueueDepth);
