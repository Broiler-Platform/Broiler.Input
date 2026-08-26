using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Broiler.Input.Testing;

public sealed class FakeInputProvider : IInputProvider<FakeInputDevice, FakeInputOpenOptions>, IInputDeviceWatcher
{
    private readonly ManualInputClock _clock;
    private readonly IInputDiagnosticSink _diagnostics;
    private readonly List<InputDeviceDescriptor> _descriptors = [];
    private readonly List<FakeInputDevice> _openDevices = [];

    public FakeInputProvider(ManualInputClock? clock = null, IInputDiagnosticSink? diagnostics = null)
    {
        _clock = clock ?? new ManualInputClock();
        _diagnostics = diagnostics ?? NullInputDiagnosticSink.Shared;
        AddDevice("fake:keyboard:primary", InputKind.Keyboard, "Fake keyboard");
    }

    public event Action<InputDeviceChange>? DeviceChanged;

    public ManualInputClock Clock => _clock;

    public InputDeviceDescriptor AddDevice(string id, InputKind kind, string displayName)
    {
        InputDeviceDescriptor descriptor = new(InputDeviceId.FromOpaqueValue(id), kind, displayName);
        _descriptors.Add(descriptor);
        DeviceChanged?.Invoke(new InputDeviceChange(InputDeviceChangeKind.Added, descriptor, _clock.GetTimestamp()));
        return descriptor;
    }

    public bool RemoveDevice(InputDeviceId id)
    {
        InputDeviceDescriptor? descriptor = _descriptors.FirstOrDefault(device => device.Id == id);
        if (descriptor is null)
            return false;

        _descriptors.Remove(descriptor);
        foreach (FakeInputDevice device in _openDevices.Where(device => device.Id == id).ToArray())
            device.SimulateRemoval();

        DeviceChanged?.Invoke(new InputDeviceChange(InputDeviceChangeKind.Removed, descriptor, _clock.GetTimestamp()));
        return true;
    }

    public ValueTask<IReadOnlyList<InputDeviceDescriptor>> GetDevicesAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult<IReadOnlyList<InputDeviceDescriptor>>(_descriptors.ToArray());
    }

    public ValueTask<FakeInputDevice> OpenAsync(
        InputDeviceDescriptor descriptor,
        FakeInputOpenOptions options,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentNullException.ThrowIfNull(options);

        if (!_descriptors.Any(device => device.Id == descriptor.Id))
            throw new InvalidOperationException("The fake input device is not available.");

        FakeInputDevice device = new(descriptor, options, _clock, _diagnostics);
        _openDevices.Add(device);
        return ValueTask.FromResult(device);
    }
}
