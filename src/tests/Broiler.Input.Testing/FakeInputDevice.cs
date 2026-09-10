using System.Threading;
using System.Threading.Tasks;

namespace Broiler.Input.Testing;

public sealed class FakeInputDevice(InputDeviceDescriptor descriptor, FakeInputOpenOptions options, ManualInputClock clock,
    IInputDiagnosticSink? diagnostics = null) : InputDevice(descriptor, clock, diagnostics)
{
    private readonly FakeBoundedInputQueue<string> _queue = new(options.EffectiveDeliveryOptions);

    public InputDeliveryMetrics DeliveryMetrics => _queue.Metrics;

    public bool TryDeliver(string value)
    {
        ThrowIfDisposed();
        return _queue.TryEnqueue(value);
    }

    public bool TryRead(out string? value)
    {
        ThrowIfDisposed();
        return _queue.TryDequeue(out value);
    }

    public void SimulateRemoval() => MarkUnavailable(new InputFault(InputErrorCategory.DeviceRemoved, "Fake input device was removed."));

    public override ValueTask OpenAsync(CancellationToken cancellationToken = default)
    {
        ManualAdvance();
        return base.OpenAsync(cancellationToken);
    }

    public override ValueTask StartAsync(CancellationToken cancellationToken = default)
    {
        ManualAdvance();
        return base.StartAsync(cancellationToken);
    }

    public override ValueTask StopAsync(CancellationToken cancellationToken = default)
    {
        ManualAdvance();
        return base.StopAsync(cancellationToken);
    }

    public override ValueTask CloseAsync(CancellationToken cancellationToken = default)
    {
        ManualAdvance();
        return base.CloseAsync(cancellationToken);
    }

    private void ManualAdvance()
    {
        if (Clock is ManualInputClock manual)
            manual.Advance(1);
    }
}
