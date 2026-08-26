using System;
using System.Collections.Generic;

namespace Broiler.Input.Windows;

public sealed class WindowsInputMessageDispatcher : IDisposable
{
    private readonly IWindowsInputHost _host;
    private readonly object _gate = new();
    private readonly List<IWindowsInputMessageSink> _sinks = [];
    private bool _disposed;

    public WindowsInputMessageDispatcher(IWindowsInputHost host)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
        _host.MessageReceived += OnMessageReceived;
    }

    public WindowsInputMessageSubscription AddSink(IWindowsInputMessageSink sink)
    {
        ArgumentNullException.ThrowIfNull(sink);
        ThrowIfDisposed();

        lock (_gate)
            _sinks.Add(sink);

        return new WindowsInputMessageSubscription(() => RemoveSink(sink));
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _host.MessageReceived -= OnMessageReceived;
        lock (_gate)
            _sinks.Clear();

        _disposed = true;
    }

    private void OnMessageReceived(WindowsInputMessage message)
    {
        IWindowsInputMessageSink[] snapshot;
        lock (_gate)
            snapshot = _sinks.ToArray();

        foreach (IWindowsInputMessageSink sink in snapshot)
        {
            if (sink.ProcessMessage(message))
                return;
        }
    }

    private void RemoveSink(IWindowsInputMessageSink sink)
    {
        lock (_gate)
            _sinks.Remove(sink);
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(GetType().FullName);
    }
}
