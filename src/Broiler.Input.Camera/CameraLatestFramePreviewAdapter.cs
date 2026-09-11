using System;
using System.Threading;

namespace Broiler.Input.Camera;

public sealed class CameraLatestFramePreviewAdapter : IDisposable
{
    private readonly Lock _gate = new();
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
        lock (_gate)
        {
            if (_disposed)
                return;

            _disposed = true;
            _device.FrameReady -= OnFrameReady;
            _latestFrame?.Dispose();
            _latestFrame = null;
        }
    }

    private void OnFrameReady(CameraFrameReadyEvent inputEvent)
    {
        lock (_gate)
        {
            // Unsubscription cannot recall a delegate already being invoked.
            if (_disposed)
            {
                inputEvent.Frame.Dispose();
                return;
            }

            _latestFrame?.Dispose();
            _latestFrame = inputEvent.Frame;
        }
    }
}
