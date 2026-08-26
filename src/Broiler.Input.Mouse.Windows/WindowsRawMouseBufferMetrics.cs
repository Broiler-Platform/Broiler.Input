namespace Broiler.Input.Mouse.Windows;

public readonly record struct WindowsRawMouseBufferMetrics(
    long AcceptedCount,
    long DequeuedCount,
    long CoalescedCount,
    long DroppedCount,
    int QueueDepth);
