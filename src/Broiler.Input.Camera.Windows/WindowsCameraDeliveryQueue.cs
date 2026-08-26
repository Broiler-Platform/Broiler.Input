using System;
using System.Collections.Generic;
using Broiler.Input;
using Broiler.Input.Camera;

namespace Broiler.Input.Camera.Windows;

internal sealed class WindowsCameraDeliveryQueue
{
    private readonly Queue<CameraFrameLease> _queue = new();
    private readonly InputDeliveryOptions _options;

    public WindowsCameraDeliveryQueue(InputDeliveryOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public long DroppedNewestCount { get; private set; }

    public long DroppedOldestCount { get; private set; }

    public int QueueDepth => _queue.Count;

    public bool TryEnqueue(CameraFrameLease lease)
    {
        ArgumentNullException.ThrowIfNull(lease);

        if (_queue.Count < _options.Capacity)
        {
            _queue.Enqueue(lease);
            return true;
        }

        switch (_options.OverflowPolicy)
        {
            case InputDeliveryOverflowPolicy.DropNewest:
                DroppedNewestCount++;
                lease.Dispose();
                return false;

            case InputDeliveryOverflowPolicy.DropOldest:
                _queue.Dequeue().Dispose();
                DroppedOldestCount++;
                _queue.Enqueue(lease);
                return true;

            case InputDeliveryOverflowPolicy.KeepLatest:
                DroppedOldestCount += _queue.Count;
                while (_queue.Count > 0)
                    _queue.Dequeue().Dispose();
                _queue.Enqueue(lease);
                return true;

            case InputDeliveryOverflowPolicy.Fail:
                lease.Dispose();
                throw new InvalidOperationException("The camera delivery queue is full.");

            default:
                lease.Dispose();
                throw new ArgumentOutOfRangeException(nameof(_options.OverflowPolicy));
        }
    }

    public bool TryDequeue(out CameraFrameLease? lease)
    {
        if (_queue.Count == 0)
        {
            lease = null;
            return false;
        }

        lease = _queue.Dequeue();
        return true;
    }

    public void DisposeAll()
    {
        while (_queue.Count > 0)
            _queue.Dequeue().Dispose();
    }
}
