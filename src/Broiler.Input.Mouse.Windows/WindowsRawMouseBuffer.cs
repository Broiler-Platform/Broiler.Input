using System;
using System.Collections.Generic;
using Broiler.Input.Windows;

namespace Broiler.Input.Mouse.Windows;

public sealed class WindowsRawMouseBuffer
{
    private readonly Queue<WindowsRawMouseBufferedEvent> _queue = new();
    private readonly int _capacity;
    private long _acceptedCount;
    private long _dequeuedCount;
    private long _coalescedCount;
    private long _droppedCount;

    public WindowsRawMouseBuffer(int capacity)
    {
        if (capacity <= 0)
            throw new ArgumentOutOfRangeException(nameof(capacity), "Raw mouse buffer capacity must be positive.");

        _capacity = capacity;
    }

    public WindowsRawMouseBufferMetrics Metrics => new(
        _acceptedCount,
        _dequeuedCount,
        _coalescedCount,
        _droppedCount,
        _queue.Count);

    public void Enqueue(WindowsRawMouseReport report)
    {
        WindowsRawMouseBufferedEvent inputEvent = new(
            report.Device,
            report.Timestamp,
            report.DeltaX,
            report.DeltaY,
            report.ButtonFlags,
            report.ButtonData,
            report.IsAbsolute);

        _acceptedCount++;

        if (CanCoalesce(inputEvent) && TryCoalesceTail(inputEvent))
            return;

        if (_queue.Count == _capacity)
        {
            _queue.Dequeue();
            _droppedCount++;
        }

        _queue.Enqueue(inputEvent);
    }

    public bool TryDequeue(out WindowsRawMouseBufferedEvent inputEvent)
    {
        if (_queue.Count == 0)
        {
            inputEvent = default;
            return false;
        }

        inputEvent = _queue.Dequeue();
        _dequeuedCount++;
        return true;
    }

    private static bool CanCoalesce(WindowsRawMouseBufferedEvent inputEvent) =>
        inputEvent.ButtonFlags == 0 &&
        inputEvent.ButtonData == 0 &&
        !inputEvent.IsAbsolute;

    private bool TryCoalesceTail(WindowsRawMouseBufferedEvent inputEvent)
    {
        if (_queue.Count == 0)
            return false;

        WindowsRawMouseBufferedEvent[] snapshot = _queue.ToArray();
        WindowsRawMouseBufferedEvent tail = snapshot[^1];
        if (tail.Device != inputEvent.Device || !CanCoalesce(tail))
            return false;

        snapshot[^1] = tail with
        {
            Timestamp = inputEvent.Timestamp,
            DeltaX = tail.DeltaX + inputEvent.DeltaX,
            DeltaY = tail.DeltaY + inputEvent.DeltaY,
        };

        _queue.Clear();
        foreach (WindowsRawMouseBufferedEvent item in snapshot)
            _queue.Enqueue(item);

        _coalescedCount++;
        return true;
    }
}
