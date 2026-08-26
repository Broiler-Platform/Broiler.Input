using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Broiler.Input.Linux;

public sealed class LinuxEvdevCapabilitySet
{
    private readonly HashSet<int> _eventTypes;
    private readonly HashSet<int> _keyCodes;
    private readonly HashSet<int> _relativeAxes;
    private readonly HashSet<int> _absoluteAxes;

    public LinuxEvdevCapabilitySet(
        IEnumerable<int>? eventTypes = null,
        IEnumerable<int>? keyCodes = null,
        IEnumerable<int>? relativeAxes = null,
        IEnumerable<int>? absoluteAxes = null)
    {
        _eventTypes = [.. eventTypes ?? []];
        _keyCodes = [.. keyCodes ?? []];
        _relativeAxes = [.. relativeAxes ?? []];
        _absoluteAxes = [.. absoluteAxes ?? []];
    }

    public static LinuxEvdevCapabilitySet Empty { get; } = new();

    public IReadOnlyList<int> EventTypes => Sorted(_eventTypes);

    public IReadOnlyList<int> KeyCodes => Sorted(_keyCodes);

    public IReadOnlyList<int> RelativeAxes => Sorted(_relativeAxes);

    public IReadOnlyList<int> AbsoluteAxes => Sorted(_absoluteAxes);

    public bool HasEventType(int type) => _eventTypes.Contains(type);

    public bool HasKeyCode(int code) => _keyCodes.Contains(code);

    public bool HasRelativeAxis(int axis) => _relativeAxes.Contains(axis);

    public bool HasAbsoluteAxis(int axis) => _absoluteAxes.Contains(axis);

    public bool IsKeyboard =>
        HasEventType(LinuxEvdevConstants.EvKey) &&
        _keyCodes.Any(static code => code is >= LinuxEvdevConstants.KeyEsc and < LinuxEvdevConstants.BtnMisc);

    public bool IsMouse =>
        HasEventType(LinuxEvdevConstants.EvKey) &&
        HasEventType(LinuxEvdevConstants.EvRel) &&
        (HasRelativeAxis(LinuxEvdevConstants.RelX) || HasRelativeAxis(LinuxEvdevConstants.RelY)) &&
        (HasKeyCode(LinuxEvdevConstants.BtnLeft) ||
         HasKeyCode(LinuxEvdevConstants.BtnRight) ||
         HasKeyCode(LinuxEvdevConstants.BtnMiddle) ||
         HasKeyCode(LinuxEvdevConstants.BtnSide) ||
         HasKeyCode(LinuxEvdevConstants.BtnExtra));

    /// <summary>
    /// A touchpad (or absolute pointer): reports absolute X/Y with a touch or
    /// click contact. These do not emit EV_REL, so <see cref="IsMouse"/> skips
    /// them; the mouse pipeline treats them as an absolute pointing device.
    /// </summary>
    public bool IsTouchpad =>
        HasEventType(LinuxEvdevConstants.EvAbs) &&
        HasAbsoluteAxis(LinuxEvdevConstants.AbsX) &&
        HasAbsoluteAxis(LinuxEvdevConstants.AbsY) &&
        (HasKeyCode(LinuxEvdevConstants.BtnTouch) ||
         HasKeyCode(LinuxEvdevConstants.BtnToolFinger) ||
         HasKeyCode(LinuxEvdevConstants.BtnLeft));

    public static IReadOnlyList<int> ParseSysfsBitmap(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return [];

        string[] words = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        List<int> bits = [];
        int wordIndex = 0;
        for (int i = words.Length - 1; i >= 0; i--, wordIndex++)
        {
            string word = words[i].Trim();
            if (word.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                word = word[2..];

            if (!ulong.TryParse(word, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ulong value))
                continue;

            for (int bit = 0; bit < 64; bit++)
            {
                if (((value >> bit) & 1UL) != 0)
                    bits.Add((wordIndex * 64) + bit);
            }
        }

        bits.Sort();
        return bits;
    }

    private static IReadOnlyList<int> Sorted(HashSet<int> values)
    {
        int[] sorted = values.ToArray();
        Array.Sort(sorted);
        return sorted;
    }
}
