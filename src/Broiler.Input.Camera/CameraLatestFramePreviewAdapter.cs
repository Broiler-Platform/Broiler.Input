using System;

namespace Broiler.Input.Camera;

public sealed class CameraLatestFramePreviewAdapter : IDisposable
{
    private readonly object _gate = new();
    private readonly CameraInputDevice _device;
    private CameraFrameLease? _latestFrame;
    private bool _disposed;

    public CameraLatestFramePreviewAdapter(CameraInputDevice device)
    {
        _device = device ?? throw new ArgumentNullException(nameof(device));
        _device.FrameReady += OnFrameReady;
    }

    public bool TryAcquireLatest(out CameraFrameLease? frame)
    {
        lock (_gate)
        {
            frame = _latestFrame;
            _latestFrame = null;
            return frame is not null;
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _device.FrameReady -= OnFrameReady;
        lock (_gate)
        {
            _latestFrame?.Dispose();
            _latestFrame = null;
        }

        _disposed = true;
    }

    private void OnFrameReady(CameraFrameReadyEvent inputEvent)
    {
        lock (_gate)
        {
            _latestFrame?.Dispose();
            _latestFrame = inputEvent.Frame;
        }
    }
}
