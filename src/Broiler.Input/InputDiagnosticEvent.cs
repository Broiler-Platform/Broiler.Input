using System;
using System.Collections.Generic;

namespace Broiler.Input;

public sealed class InputDiagnosticEvent
{
    private readonly IReadOnlyDictionary<string, string> _properties;

    public InputDiagnosticEvent(
        InputDiagnosticLevel level,
        string name,
        InputTimestamp timestamp,
        InputDeviceId? deviceId = null,
        InputErrorCategory? errorCategory = null,
        IReadOnlyDictionary<string, string>? properties = null)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Diagnostic event names must be non-empty.", nameof(name));

        Level = level;
        Name = name;
        Timestamp = timestamp;
        DeviceId = deviceId;
        ErrorCategory = errorCategory;
        _properties = properties ?? new Dictionary<string, string>();
    }

    public InputDiagnosticLevel Level { get; }

    public string Name { get; }

    public InputTimestamp Timestamp { get; }

    public InputDeviceId? DeviceId { get; }

    public InputErrorCategory? ErrorCategory { get; }

    public IReadOnlyDictionary<string, string> Properties => _properties;
}
