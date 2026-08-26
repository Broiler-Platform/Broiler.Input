using System;

namespace Broiler.Input.Keyboard;

public readonly record struct KeyboardKey
{
    public KeyboardKey(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Keyboard key names must be non-empty.", nameof(name));

        Name = name;
    }

    public string Name { get; }

    public static KeyboardKey Unknown { get; } = new("Unknown");

    public static KeyboardKey FromName(string name) => new(name);

    public override string ToString() => Name;
}
