using System;
using System.Collections.Generic;

namespace Broiler.Input.Testing;

public sealed class FakeBoundedInputQueue<T>
{
    private readonly Queue<T> _queue = new();
    private readonly InputDeliveryOptions _options;
    private long _enqueuedCount;
    private long _dequeuedCount;
    private long _droppedNewestCount;
    private long _droppedOldestCount;

    public FakeBoundedInputQueue(InputDeliveryOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public InputDeliveryMetrics Metrics => new(
        _enqueuedCount,
        _dequeuedCount,
        _droppedNewestCount,
        _droppedOldestCount,
        _queue.Count);

    public bool TryEnqueue(T item)
    {
        if (_queue.Count < _options.Capacity)
        {
            Enqueue(item);
            return true;
        }

        switch (_options.OverflowPolicy)
        {
            case InputDeliveryOverflowPolicy.DropNewest:
                _droppedNewestCount++;
                return false;

            case InputDeliveryOverflowPolicy.DropOldest:
                _queue.Dequeue();
                _droppedOldestCount++;
                Enqueue(item);
                return true;

            case InputDeliveryOverflowPolicy.KeepLatest:
                _droppedOldestCount += _queue.Count;
                _queue.Clear();
                Enqueue(item);
                return true;

            case InputDeliveryOverflowPolicy.Fail:
                throw new InvalidOperationException("The fake input delivery queue is full.");

            default:
                throw new ArgumentOutOfRangeException(nameof(_options.OverflowPolicy));
        }
    }

    public bool TryDequeue(out T? item)
    {
        if (_queue.Count == 0)
        {
            item = default;
            return false;
        }

        item = _queue.Dequeue();
        _dequeuedCount++;
        return true;
    }

    private void Enqueue(T item)
    {
        _queue.Enqueue(item);
        _enqueuedCount++;
    }
}
