using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Broiler.Input.Linux;

public sealed record LinuxEventDeviceAccessStatus(
    string InputDirectory,
    bool DirectoryExists,
    int EventDeviceCount,
    int ReadableEventDeviceCount,
    string Diagnostic);

public static class LinuxEventDeviceAccessProbe
{
    public const string DefaultInputDirectory = "/dev/input";

    public static IReadOnlyList<string> GetEventDevicePaths(string inputDirectory = DefaultInputDirectory)
    {
        if (string.IsNullOrWhiteSpace(inputDirectory) || !Directory.Exists(inputDirectory))
            return [];

        try
        {
            return Directory.EnumerateFiles(inputDirectory, "event*")
                .OrderBy(static path => path, StringComparer.Ordinal)
                .ToArray();
        }
        catch (UnauthorizedAccessException)
        {
            return [];
        }
        catch (IOException)
        {
            return [];
        }
    }

    public static LinuxEventDeviceAccessStatus CheckEventDeviceAccess(
        string inputDirectory = DefaultInputDirectory)
    {
        if (string.IsNullOrWhiteSpace(inputDirectory))
        {
            return new LinuxEventDeviceAccessStatus(
                inputDirectory,
                DirectoryExists: false,
                EventDeviceCount: 0,
                ReadableEventDeviceCount: 0,
                Diagnostic: "No input directory was supplied.");
        }

        if (!Directory.Exists(inputDirectory))
        {
            return new LinuxEventDeviceAccessStatus(
                inputDirectory,
                DirectoryExists: false,
                EventDeviceCount: 0,
                ReadableEventDeviceCount: 0,
                Diagnostic: $"{inputDirectory} does not exist. Linux evdev input requires /dev/input/event* devices.");
        }

        IReadOnlyList<string> eventDevices = GetEventDevicePaths(inputDirectory);
        int readable = 0;
        foreach (string eventDevice in eventDevices)
        {
            if (CanOpenForRead(eventDevice))
                readable++;
        }

        string diagnostic = eventDevices.Count switch
        {
            0 => $"{inputDirectory} exists but no event* devices were found.",
            _ when readable == 0 => $"{eventDevices.Count} event device(s) were found, but none could be opened for reading. Check input group, udev rules, container device pass-through, or seat-broker policy.",
            _ => $"{readable} of {eventDevices.Count} event device(s) can be opened for reading.",
        };

        return new LinuxEventDeviceAccessStatus(
            inputDirectory,
            DirectoryExists: true,
            eventDevices.Count,
            readable,
            diagnostic);
    }

    private static bool CanOpenForRead(string path)
    {
        try
        {
            using FileStream stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            return stream.CanRead;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
        catch (IOException)
        {
            return false;
        }
    }
}
