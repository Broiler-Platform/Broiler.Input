using System;
using System.Collections.Generic;
using Broiler.Input;
using Broiler.Input.Microphone;

namespace Broiler.Input.Microphone.Windows;

internal sealed class WindowsMicrophoneDeliveryQueue
{
    private readonly Queue<MicrophoneBufferLease> _queue = new();
    private readonly InputDeliveryOptions _options;

    public WindowsMicrophoneDeliveryQueue(InputDeliveryOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public long DroppedNewestCount { get; private set; }

    public long DroppedOldestCount { get; private set; }

    public int QueueDepth => _queue.Count;

    public bool TryEnqueue(MicrophoneBufferLease lease)
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
                MicrophoneBufferLease old = _queue.Dequeue();
                old.Dispose();
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
                throw new InvalidOperationException("The microphone delivery queue is full.");

            default:
                lease.Dispose();
                throw new ArgumentOutOfRangeException(nameof(_options.OverflowPolicy));
        }
    }

    public bool TryDequeue(out MicrophoneBufferLease? lease)
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
