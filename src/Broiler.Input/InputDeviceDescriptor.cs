using System;
using System.Collections.Generic;
using System.Linq;

namespace Broiler.Input;

public sealed class InputDeviceDescriptor
{
    private readonly InputCapability[] _capabilities;

    public InputDeviceDescriptor(
        InputDeviceId id,
        InputKind kind,
        string displayName,
        InputDeviceAvailability availability = InputDeviceAvailability.Available,
        IEnumerable<InputCapability>? capabilities = null)
    {
        if (kind == InputKind.Unknown)
            throw new ArgumentException("Device descriptors must name a concrete input kind.", nameof(kind));

        Id = id;
        Kind = kind;
        DisplayName = string.IsNullOrWhiteSpace(displayName) ? id.Value : displayName;
        Availability = availability;
        _capabilities = capabilities?.ToArray() ?? [];
    }

    public InputDeviceId Id { get; }

    public InputKind Kind { get; }

    public string DisplayName { get; }

    public InputDeviceAvailability Availability { get; }

    public IReadOnlyList<InputCapability> Capabilities => _capabilities;
}
