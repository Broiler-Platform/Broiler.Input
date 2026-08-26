namespace Broiler.Input;

public readonly record struct InputDeliveryMetrics(
    long EnqueuedCount,
    long DequeuedCount,
    long DroppedNewestCount,
    long DroppedOldestCount,
    int QueueDepth);
