using System;

namespace Broiler.Input;

public readonly record struct InputDeviceId
{
    public InputDeviceId(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Input device IDs must be non-empty opaque values.", nameof(value));

        Value = value;
    }

    public string Value { get; }

    public static InputDeviceId FromOpaqueValue(string value) => new(value);

    public override string ToString() => Value;
}
