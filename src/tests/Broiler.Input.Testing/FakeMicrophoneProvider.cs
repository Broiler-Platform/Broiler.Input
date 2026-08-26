using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Broiler.Input.Microphone;

namespace Broiler.Input.Testing;

public sealed class FakeMicrophoneProvider : IMicrophoneInputProvider, IInputDeviceWatcher
{
    private readonly ManualInputClock _clock;
    private readonly IInputDiagnosticSink _diagnostics;
    private readonly List<InputDeviceDescriptor> _descriptors = [];

    public FakeMicrophoneProvider(ManualInputClock? clock = null, IInputDiagnosticSink? diagnostics = null)
    {
        _clock = clock ?? new ManualInputClock();
        _diagnostics = diagnostics ?? NullInputDiagnosticSink.Shared;
        DefaultDescriptor = AddDevice("fake:microphone:default", "Fake microphone");
    }

    public event Action<InputDeviceChange>? DeviceChanged;

    public InputDeviceDescriptor DefaultDescriptor { get; private set; }

    public InputDeviceDescriptor AddDevice(string id, string displayName)
    {
        InputDeviceDescriptor descriptor = new(
            InputDeviceId.FromOpaqueValue(id),
            InputKind.Microphone,
            displayName,
            InputDeviceAvailability.Available,
            [new InputCapability("microphone.capture.mode", "fake")]);
        _descriptors.Add(descriptor);
        DeviceChanged?.Invoke(new InputDeviceChange(InputDeviceChangeKind.Added, descriptor, _clock.GetTimestamp()));
        return descriptor;
    }

    public void SwitchDefaultDevice(InputDeviceDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        if (!_descriptors.Any(device => device.Id == descriptor.Id))
            _descriptors.Add(descriptor);

        DefaultDescriptor = descriptor;
        DeviceChanged?.Invoke(new InputDeviceChange(InputDeviceChangeKind.DefaultChanged, descriptor, _clock.GetTimestamp()));
    }

    public ValueTask<IReadOnlyList<InputDeviceDescriptor>> GetDevicesAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult<IReadOnlyList<InputDeviceDescriptor>>(_descriptors.ToArray());
    }

    public ValueTask<MicrophoneInputDevice> OpenAsync(
        InputDeviceDescriptor descriptor,
        MicrophoneOpenOptions options,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentNullException.ThrowIfNull(options);

        if (!_descriptors.Any(device => device.Id == descriptor.Id))
            throw new InvalidOperationException("The fake microphone device is not available.");

        MicrophoneInputDevice device = new FakeMicrophoneInputDevice(descriptor, options, _clock, _diagnostics);
        return ValueTask.FromResult(device);
    }
}
