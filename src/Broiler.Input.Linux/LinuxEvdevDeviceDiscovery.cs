using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace Broiler.Input.Linux;

public static class LinuxEvdevDeviceDiscovery
{
    public static IReadOnlyList<LinuxEvdevDeviceInfo> Discover(
        LinuxEvdevDeviceKind kind,
        LinuxEvdevProviderOptions? options = null)
    {
        LinuxEvdevProviderOptions effective = (options ?? new LinuxEvdevProviderOptions()).Normalize();
        List<LinuxEvdevDeviceInfo> devices = [];

        foreach (string eventPath in LinuxEventDeviceAccessProbe.GetEventDevicePaths(effective.InputDirectory))
        {
            string eventName = Path.GetFileName(eventPath);
            string deviceDirectory = Path.Combine(effective.SysfsInputRoot, eventName, "device");
            LinuxEvdevCapabilitySet capabilities = ReadCapabilities(deviceDirectory);

            if (!MatchesKind(kind, capabilities))
                continue;

            string displayName = ReadFirstLine(Path.Combine(deviceDirectory, "name"))
                ?? $"{CultureInfo.InvariantCulture.TextInfo.ToTitleCase(kind.ToString().ToLowerInvariant())} {eventName}";
            string opaqueId = CreateOpaqueId(kind, eventName, displayName, deviceDirectory, capabilities);
            InputDeviceDescriptor descriptor = new(
                InputDeviceId.FromOpaqueValue(opaqueId),
                ToInputKind(kind),
                displayName,
                GetAvailability(eventPath),
                CreateCapabilities(kind, eventName, capabilities));

            devices.Add(new LinuxEvdevDeviceInfo(
                kind,
                eventName,
                eventPath,
                displayName,
                descriptor,
                capabilities));
        }

        return devices.OrderBy(static device => device.EventName, StringComparer.Ordinal).ToArray();
    }

    public static IReadOnlyList<LinuxEvdevDeviceInfo> DiscoverAll(LinuxEvdevProviderOptions? options = null)
    {
        List<LinuxEvdevDeviceInfo> devices = [];
        devices.AddRange(Discover(LinuxEvdevDeviceKind.Keyboard, options));
        devices.AddRange(Discover(LinuxEvdevDeviceKind.Mouse, options));
        return devices
            .OrderBy(static device => device.EventName, StringComparer.Ordinal)
            .ThenBy(static device => device.Kind)
            .ToArray();
    }

    private static LinuxEvdevCapabilitySet ReadCapabilities(string deviceDirectory)
    {
        string capabilitiesDirectory = Path.Combine(deviceDirectory, "capabilities");
        return new LinuxEvdevCapabilitySet(
            LinuxEvdevCapabilitySet.ParseSysfsBitmap(ReadFirstLine(Path.Combine(capabilitiesDirectory, "ev"))),
            LinuxEvdevCapabilitySet.ParseSysfsBitmap(ReadFirstLine(Path.Combine(capabilitiesDirectory, "key"))),
            LinuxEvdevCapabilitySet.ParseSysfsBitmap(ReadFirstLine(Path.Combine(capabilitiesDirectory, "rel"))),
            LinuxEvdevCapabilitySet.ParseSysfsBitmap(ReadFirstLine(Path.Combine(capabilitiesDirectory, "abs"))));
    }

    private static bool MatchesKind(LinuxEvdevDeviceKind kind, LinuxEvdevCapabilitySet capabilities) =>
        kind switch
        {
            LinuxEvdevDeviceKind.Keyboard => capabilities.IsKeyboard,
            // Touchpads are absolute pointers; the mouse pipeline adapts them.
            LinuxEvdevDeviceKind.Mouse => capabilities.IsMouse || capabilities.IsTouchpad,
            _ => false,
        };

    private static InputKind ToInputKind(LinuxEvdevDeviceKind kind) =>
        kind switch
        {
            LinuxEvdevDeviceKind.Keyboard => InputKind.Keyboard,
            LinuxEvdevDeviceKind.Mouse => InputKind.Mouse,
            _ => InputKind.Unknown,
        };

    private static InputDeviceAvailability GetAvailability(string eventPath)
    {
        try
        {
            using FileStream stream = File.Open(eventPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            return stream.CanRead ? InputDeviceAvailability.Available : InputDeviceAvailability.Unavailable;
        }
        catch (UnauthorizedAccessException)
        {
            return InputDeviceAvailability.PermissionDenied;
        }
        catch (IOException)
        {
            return InputDeviceAvailability.Unavailable;
        }
    }

    private static IReadOnlyList<InputCapability> CreateCapabilities(
        LinuxEvdevDeviceKind kind,
        string eventName,
        LinuxEvdevCapabilitySet capabilities)
    {
        List<InputCapability> values =
        [
            new("delivery", "evdev"),
            new("event-device", eventName),
            new("event-types", string.Join(",", capabilities.EventTypes.Select(static type => type.ToString(CultureInfo.InvariantCulture)))),
        ];

        if (kind == LinuxEvdevDeviceKind.Keyboard)
        {
            values.Add(new InputCapability("keys", "raw-key-transitions"));
            values.Add(new InputCapability("text", "unsupported"));
        }
        else if (!capabilities.IsMouse && capabilities.IsTouchpad)
        {
            values.Add(new InputCapability("movement", "absolute-touchpad"));
            values.Add(new InputCapability("coordinate-space", "raw-relative-counts"));
            values.Add(new InputCapability("buttons", "left,tap"));
            values.Add(new InputCapability("pointer", "touchpad"));
        }
        else
        {
            values.Add(new InputCapability("movement", "relative"));
            values.Add(new InputCapability("coordinate-space", "raw-relative-counts"));
            values.Add(new InputCapability("buttons", "left,right,middle,x1,x2"));
            values.Add(new InputCapability("wheel", "vertical,horizontal,hi-res"));
        }

        return values;
    }

    private static string CreateOpaqueId(
        LinuxEvdevDeviceKind kind,
        string eventName,
        string displayName,
        string deviceDirectory,
        LinuxEvdevCapabilitySet capabilities)
    {
        StringBuilder builder = new();
        builder.Append(kind).Append('|')
            .Append(eventName).Append('|')
            .Append(displayName).Append('|')
            .Append(ReadFirstLine(Path.Combine(deviceDirectory, "phys")) ?? string.Empty).Append('|')
            .Append(ReadFirstLine(Path.Combine(deviceDirectory, "uniq")) ?? string.Empty).Append('|')
            .Append(ReadFirstLine(Path.Combine(deviceDirectory, "id", "bustype")) ?? string.Empty).Append('|')
            .Append(ReadFirstLine(Path.Combine(deviceDirectory, "id", "vendor")) ?? string.Empty).Append('|')
            .Append(ReadFirstLine(Path.Combine(deviceDirectory, "id", "product")) ?? string.Empty).Append('|')
            .Append(ReadFirstLine(Path.Combine(deviceDirectory, "id", "version")) ?? string.Empty).Append('|')
            .Append(string.Join(",", capabilities.EventTypes)).Append('|')
            .Append(string.Join(",", capabilities.KeyCodes)).Append('|')
            .Append(string.Join(",", capabilities.RelativeAxes));

        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString()));
        return "linux:evdev:" + kind.ToString().ToLowerInvariant() + ":" + Convert.ToHexString(hash, 0, 8).ToLowerInvariant();
    }

    private static string? ReadFirstLine(string path)
    {
        try
        {
            if (!File.Exists(path))
                return null;

            string? line = File.ReadLines(path).FirstOrDefault();
            return string.IsNullOrWhiteSpace(line) ? null : line.Trim();
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
    }
}
