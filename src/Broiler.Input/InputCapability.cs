using System;

namespace Broiler.Input;

public readonly record struct InputCapability
{
    public InputCapability(string name, string value)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Capability names must be non-empty.", nameof(name));

        Name = name;
        Value = value ?? string.Empty;
    }

    public string Name { get; }

    public string Value { get; }
}
