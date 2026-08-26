using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;
using Broiler.Input;
using Broiler.Input.Microphone;
using Broiler.Input.Windows;

namespace Broiler.Input.Microphone.Windows;

[SupportedOSPlatform("windows")]
public sealed class WindowsMicrophoneProvider : IMicrophoneInputProvider, IInputDeviceWatcher
{
    private readonly IInputClock _clock;
    private readonly IInputDiagnosticSink _diagnostics;
    private readonly Dictionary<InputDeviceId, InputDeviceDescriptor> _knownDevices = [];
    private readonly Dictionary<MicrophoneEndpointRole, InputDeviceId> _knownDefaults = [];

    public WindowsMicrophoneProvider(IInputClock? clock = null, IInputDiagnosticSink? diagnostics = null)
    {
        _clock = clock ?? WindowsInputClock.Shared;
        _diagnostics = diagnostics ?? NullInputDiagnosticSink.Shared;
    }

    public event Action<InputDeviceChange>? DeviceChanged;

    public ValueTask<IReadOnlyList<InputDeviceDescriptor>> GetDevicesAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IReadOnlyList<InputDeviceDescriptor> devices = WindowsMicrophoneEndpointEnumerator.EnumerateCaptureDevices();
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

        foreach (MicrophoneEndpointRole role in Enum.GetValues<MicrophoneEndpointRole>())
        {
            InputDeviceDescriptor? currentDefault = WindowsMicrophoneEndpointEnumerator.TryGetDefaultCaptureDevice(role);
            if (currentDefault is null)
                continue;

            if (!_knownDefaults.TryGetValue(role, out InputDeviceId previousDefault) ||
                previousDefault != currentDefault.Id)
            {
                _knownDefaults[role] = currentDefault.Id;
                RaiseDeviceChanged(InputDeviceChangeKind.DefaultChanged, currentDefault);
            }
        }

        _knownDevices.Clear();
        foreach (InputDeviceDescriptor descriptor in current)
            _knownDevices[descriptor.Id] = descriptor;
    }

    public ValueTask<MicrophoneInputDevice> OpenAsync(
        InputDeviceDescriptor descriptor,
        MicrophoneOpenOptions options,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentNullException.ThrowIfNull(options);

        if (descriptor.Kind != InputKind.Microphone)
            throw new ArgumentException("Windows microphone providers only open microphone descriptors.", nameof(descriptor));
        if (descriptor.Availability != InputDeviceAvailability.Available)
            throw new InvalidOperationException("The selected microphone endpoint is not available.");

        MicrophoneInputDevice device = new WindowsMicrophoneInputDevice(descriptor, options, _clock, _diagnostics);
        return ValueTask.FromResult(device);
    }

    private static bool CapabilitiesMatch(
        IReadOnlyList<InputCapability> previous,
        IReadOnlyList<InputCapability> current)
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

    private void RaiseDeviceChanged(InputDeviceChangeKind kind, InputDeviceDescriptor descriptor)
    {
        DeviceChanged?.Invoke(new InputDeviceChange(kind, descriptor, _clock.GetTimestamp()));
    }
}
