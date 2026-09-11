using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace Broiler.Input.Linux;

internal sealed class LinuxEvdevDeviceCache(LinuxEvdevDeviceKind kind, LinuxEvdevProviderOptions options)
{
    private readonly Lock _gate = new();
    private Dictionary<InputDeviceId, LinuxEvdevDeviceInfo> _devices = [];

    public IReadOnlyList<LinuxEvdevDeviceInfo> Discover()
    {
        IReadOnlyList<LinuxEvdevDeviceInfo> current = LinuxEvdevDeviceDiscovery.Discover(kind, options);
        lock (_gate)
            _devices = current.ToDictionary(static device => device.Descriptor.Id);
        return current;
    }

    public LinuxEvdevDeviceInfo? Resolve(InputDeviceId id)
    {
        lock (_gate)
        {
            if (_devices.TryGetValue(id, out LinuxEvdevDeviceInfo? device))
                return device;
        }

        Discover();
        lock (_gate)
            return _devices.GetValueOrDefault(id);
    }

    public void Refresh(IInputClock clock, Action<InputDeviceChange> changed)
    {
        Dictionary<InputDeviceId, LinuxEvdevDeviceInfo> previous;
        lock (_gate) previous = _devices;
        IReadOnlyList<LinuxEvdevDeviceInfo> current = Discover();
        HashSet<InputDeviceId> currentIds = [];

        foreach (LinuxEvdevDeviceInfo device in current)
        {
            InputDeviceDescriptor descriptor = device.Descriptor;
            currentIds.Add(descriptor.Id);
            changed(new InputDeviceChange(previous.ContainsKey(descriptor.Id)
                ? InputDeviceChangeKind.Changed : InputDeviceChangeKind.Added, descriptor, clock.GetTimestamp()));
        }

        foreach ((InputDeviceId id, LinuxEvdevDeviceInfo device) in previous)
        {
            if (!currentIds.Contains(id))
                changed(new InputDeviceChange(InputDeviceChangeKind.Removed,
                    new InputDeviceDescriptor(id, device.Descriptor.Kind, device.Descriptor.DisplayName,
                        InputDeviceAvailability.Removed), clock.GetTimestamp()));
        }
    }
}
