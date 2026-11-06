using System;
using System.Threading;

namespace Broiler.Input.Scanner;

public sealed class ScannerPreviewAdapter : IDisposable
{
    private readonly Lock _gate = new();
    private readonly ScannerInputDevice _device;
    private ScannerPageLease? _latestPage;
    private bool _disposed;

    public ScannerPreviewAdapter(ScannerInputDevice device)
    {
        _device = device ?? throw new ArgumentNullException(nameof(device));
        _device.PageReady += OnPageReady;
    }

    public bool TryAcquireLatest(out ScannerPageLease? page)
    {
        lock (_gate)
        {
            page = _latestPage;
            _latestPage = null;
            return page is not null;
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
                return;

            _disposed = true;
            _device.PageReady -= OnPageReady;
            _latestPage?.Dispose();
            _latestPage = null;
        }
    }

    private void OnPageReady(ScannerPageReadyEvent inputEvent)
    {
        lock (_gate)
        {
            // Unsubscription cannot recall a delegate already being invoked.
            if (_disposed)
            {
                inputEvent.Page.Dispose();
                return;
            }

            _latestPage?.Dispose();
            _latestPage = inputEvent.Page;
        }
    }
}
