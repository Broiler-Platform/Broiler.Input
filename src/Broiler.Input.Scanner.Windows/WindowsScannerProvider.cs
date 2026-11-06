using Broiler.Input.Windows;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;

namespace Broiler.Input.Scanner.Windows;

[SupportedOSPlatform("windows")]
public sealed class WindowsScannerProvider(IInputClock? clock = null, IInputDiagnosticSink? diagnostics = null)
    : IScannerInputProvider, IInputDeviceWatcher
{
    private readonly IInputClock _clock = clock ?? WindowsInputClock.Shared;
    private readonly IInputDiagnosticSink _diagnostics = diagnostics ?? NullInputDiagnosticSink.Shared;
    private readonly Dictionary<InputDeviceId, InputDeviceDescriptor> _knownDevices = [];

    public event Action<InputDeviceChange>? DeviceChanged;

    public ValueTask<IReadOnlyList<InputDeviceDescriptor>> GetDevicesAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IReadOnlyList<InputDeviceDescriptor> devices = WindowsScannerDeviceEnumerator.EnumerateScanners();
        return ValueTask.FromResult(devices);
    }

    public async ValueTask RefreshDevicesAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyList<InputDeviceDescriptor> current = await GetDevicesAsync(cancellationToken).ConfigureAwait(false);
        Dictionary<InputDeviceId, InputDeviceDescriptor> currentById = current.ToDictionary(static descriptor => descriptor.Id);

        foreach (InputDeviceDescriptor descriptor in current)
        {
            if (!_knownDevices.TryGetValue(descriptor.Id, out InputDeviceDescriptor? previous))
            {
                RaiseDeviceChanged(InputDeviceChangeKind.Added, descriptor);
            }
            else if (previous.DisplayName != descriptor.DisplayName ||
                     previous.Availability != descriptor.Availability ||
                     !CapabilitiesMatch(previous.Capabilities, descriptor.Capabilities))
            {
                RaiseDeviceChanged(InputDeviceChangeKind.Changed, descriptor);
            }
        }

        foreach (InputDeviceDescriptor removed in _knownDevices.Values)
        {
            if (!currentById.ContainsKey(removed.Id))
                RaiseDeviceChanged(InputDeviceChangeKind.Removed, removed);
        }

        _knownDevices.Clear();

        foreach (InputDeviceDescriptor descriptor in current)
            _knownDevices[descriptor.Id] = descriptor;
    }

    public ValueTask<ScannerInputDevice> OpenAsync(
        InputDeviceDescriptor descriptor,
        ScannerOpenOptions options,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentNullException.ThrowIfNull(options);

        if (descriptor.Kind != InputKind.Scanner)
            throw new ArgumentException("Windows scanner providers only open scanner descriptors.", nameof(descriptor));

        if (descriptor.Availability != InputDeviceAvailability.Available)
            throw new InvalidOperationException("The selected scanner is not available.");

        ScannerInputDevice device = new WindowsScannerInputDevice(descriptor, options, _clock, _diagnostics);
        return ValueTask.FromResult(device);
    }

    private static bool CapabilitiesMatch(IReadOnlyList<InputCapability> previous, IReadOnlyList<InputCapability> current)
    {
        if (previous.Count != current.Count)
            return false;

        for (int index = 0; index < previous.Count; index++)
        {
            if (previous[index] != current[index])
                return false;
        }

        return true;
    }

    private void RaiseDeviceChanged(InputDeviceChangeKind kind, InputDeviceDescriptor descriptor) =>
        DeviceChanged?.Invoke(new InputDeviceChange(kind, descriptor, _clock.GetTimestamp()));
}
