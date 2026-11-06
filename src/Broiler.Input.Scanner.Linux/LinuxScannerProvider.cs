using Broiler.Input.Linux;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Broiler.Input.Scanner.Linux;

public sealed class LinuxScannerProvider : IScannerInputProvider, IInputDeviceWatcher
{
    internal const string SourceKindCapability = "scanner.capture.source";
    internal const string SourceKindValue = "sane";

    private readonly ILinuxSaneInterop _interop;
    private readonly IInputClock _clock;
    private readonly IInputDiagnosticSink _diagnostics;
    private readonly Dictionary<InputDeviceId, InputDeviceDescriptor> _knownDevices = [];
    private readonly Dictionary<InputDeviceId, string> _deviceNamesById = [];
    private readonly Lock _gate = new();

    public LinuxScannerProvider(IInputClock? clock = null, IInputDiagnosticSink? diagnostics = null)
        : this(NativeLinuxSaneInterop.Shared, clock, diagnostics)
    {
    }

    internal LinuxScannerProvider(
        ILinuxSaneInterop interop,
        IInputClock? clock = null,
        IInputDiagnosticSink? diagnostics = null)
    {
        _interop = interop ?? throw new ArgumentNullException(nameof(interop));
        _clock = clock ?? StopwatchInputClock.Shared;
        _diagnostics = diagnostics ?? NullInputDiagnosticSink.Shared;
    }

    public event Action<InputDeviceChange>? DeviceChanged;

    public static LinuxInputNativeLibraryStatus CheckDependencies() =>
        LinuxInputNativeLibraryProbe.Check(LinuxInputNativeLibraryProbe.Sane);

    public ValueTask<IReadOnlyList<InputDeviceDescriptor>> GetDevicesAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IReadOnlyList<InputDeviceDescriptor> descriptors = DiscoverDevices();
        return ValueTask.FromResult(descriptors);
    }

    public async ValueTask RefreshDevicesAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyList<InputDeviceDescriptor> current = await GetDevicesAsync(cancellationToken).ConfigureAwait(false);
        Dictionary<InputDeviceId, InputDeviceDescriptor> currentById = current.ToDictionary(static d => d.Id);

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
            throw new ArgumentException("Linux scanner providers only open scanner descriptors.", nameof(descriptor));

        if (descriptor.Availability != InputDeviceAvailability.Available)
            throw new InvalidOperationException("The selected scanner is not available.");

        string? deviceName = ResolveBackendDeviceName(descriptor.Id);
        if (string.IsNullOrWhiteSpace(deviceName))
            throw LinuxScannerFaults.CreateException(SaneStatus.Inval, "Selected SANE scanner device was not found.");

        ScannerInputDevice device = new LinuxScannerInputDevice(descriptor, deviceName, options, _interop, _clock, _diagnostics);
        return ValueTask.FromResult(device);
    }

    internal static string ToStableInputId(string backendDeviceName)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(backendDeviceName));
        string suffix = Convert.ToHexString(hash, 0, 12).ToLowerInvariant();
        return $"linux:sane:scanner:{suffix}";
    }

    private IReadOnlyList<InputDeviceDescriptor> DiscoverDevices()
    {
        SaneStatus initStatus = _interop.Init(out _);
        if (initStatus != SaneStatus.Good)
            return [];

        try
        {
            SaneStatus status = _interop.GetDevices(localOnly: false, out IReadOnlyList<SaneDeviceInfo> saneDevices);
            if (status != SaneStatus.Good || saneDevices.Count == 0)
                return [];

            List<InputDeviceDescriptor> descriptors = new(saneDevices.Count);
            lock (_gate)
            {
                _deviceNamesById.Clear();
                foreach (SaneDeviceInfo info in saneDevices)
                {
                    if (string.IsNullOrWhiteSpace(info.Name))
                        continue;

                    InputDeviceId id = InputDeviceId.FromOpaqueValue(ToStableInputId(info.Name));
                    _deviceNamesById[id] = info.Name;

                    string displayName = BuildDisplayName(info);
                    List<InputCapability> capabilities = [new(SourceKindCapability, SourceKindValue)];
                    if (!string.IsNullOrWhiteSpace(info.Vendor))
                        capabilities.Add(new InputCapability("scanner.vendor", info.Vendor));
                    if (!string.IsNullOrWhiteSpace(info.Model))
                        capabilities.Add(new InputCapability("scanner.model", info.Model));
                    if (!string.IsNullOrWhiteSpace(info.Type))
                        capabilities.Add(new InputCapability("scanner.type", info.Type));

                    descriptors.Add(new InputDeviceDescriptor(id, InputKind.Scanner, displayName, InputDeviceAvailability.Available, capabilities));
                }
            }

            return descriptors;
        }
        finally
        {
            _interop.Exit();
        }
    }

    private string? ResolveBackendDeviceName(InputDeviceId id)
    {
        lock (_gate)
        {
            if (_deviceNamesById.TryGetValue(id, out string? cached))
                return cached;
        }

        _ = DiscoverDevices();

        lock (_gate)
        {
            return _deviceNamesById.TryGetValue(id, out string? discovered) ? discovered : null;
        }
    }

    private static string BuildDisplayName(SaneDeviceInfo info)
    {
        if (!string.IsNullOrWhiteSpace(info.Vendor) && !string.IsNullOrWhiteSpace(info.Model))
            return $"{info.Vendor} {info.Model}";

        if (!string.IsNullOrWhiteSpace(info.Model))
            return info.Model;

        if (!string.IsNullOrWhiteSpace(info.Vendor))
            return info.Vendor;

        return "Linux SANE Scanner";
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
