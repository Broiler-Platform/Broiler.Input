using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Broiler.Input.Scanner;

namespace Broiler.Input.Testing;

public sealed class FakeScannerProvider : IScannerInputProvider, IInputDeviceWatcher
{
    private readonly ManualInputClock _clock;
    private readonly IInputDiagnosticSink _diagnostics;
    private readonly List<InputDeviceDescriptor> _descriptors = [];

    public FakeScannerProvider(ManualInputClock? clock = null, IInputDiagnosticSink? diagnostics = null)
    {
        _clock = clock ?? new ManualInputClock();
        _diagnostics = diagnostics ?? NullInputDiagnosticSink.Shared;
        DefaultDescriptor = AddDevice("fake:scanner:default", "Fake scanner");
    }

    public event Action<InputDeviceChange>? DeviceChanged;

    public InputDeviceDescriptor DefaultDescriptor { get; }

    public InputDeviceDescriptor AddDevice(string id, string displayName)
    {
        InputDeviceDescriptor descriptor = new(
            InputDeviceId.FromOpaqueValue(id),
            InputKind.Scanner,
            displayName,
            InputDeviceAvailability.Available,
            [new InputCapability("scanner.capture.source", "fake")]);
        _descriptors.Add(descriptor);
        DeviceChanged?.Invoke(new InputDeviceChange(InputDeviceChangeKind.Added, descriptor, _clock.GetTimestamp()));

        return descriptor;
    }

    public ValueTask<IReadOnlyList<InputDeviceDescriptor>> GetDevicesAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult<IReadOnlyList<InputDeviceDescriptor>>(_descriptors.ToArray());
    }

    public ValueTask<ScannerInputDevice> OpenAsync(InputDeviceDescriptor descriptor,
        ScannerOpenOptions options, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentNullException.ThrowIfNull(options);

        if (!_descriptors.Any(device => device.Id == descriptor.Id))
            throw new InvalidOperationException("The fake scanner device is not available.");

        ScannerInputDevice device = new FakeScannerInputDevice(descriptor, options, _clock, _diagnostics);
        return ValueTask.FromResult(device);
    }
}
