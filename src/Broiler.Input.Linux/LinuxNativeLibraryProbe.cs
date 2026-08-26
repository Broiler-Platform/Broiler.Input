using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace Broiler.Input.Linux;

public sealed record LinuxInputNativeLibraryRequirement(
    string Id,
    string DisplayName,
    IReadOnlyList<string> LibraryNames,
    bool Required = true);

public sealed record LinuxInputNativeLibraryStatus(
    string Id,
    string DisplayName,
    IReadOnlyList<string> LibraryNames,
    bool IsAvailable,
    string? LoadedLibraryName,
    string Diagnostic);

public static class LinuxInputNativeLibraryProbe
{
    public static LinuxInputNativeLibraryRequirement Udev { get; } = new(
        "udev",
        "udev device discovery",
        ["libudev.so.1", "libudev.so"],
        Required: false);

    public static IReadOnlyList<LinuxInputNativeLibraryStatus> CheckBaseline() =>
        [Check(Udev)];

    public static LinuxInputNativeLibraryStatus Check(LinuxInputNativeLibraryRequirement requirement)
    {
        ArgumentNullException.ThrowIfNull(requirement);

        foreach (string libraryName in requirement.LibraryNames)
        {
            if (string.IsNullOrWhiteSpace(libraryName))
                continue;

            if (NativeLibrary.TryLoad(libraryName, out IntPtr handle))
            {
                NativeLibrary.Free(handle);
                return new LinuxInputNativeLibraryStatus(
                    requirement.Id,
                    requirement.DisplayName,
                    requirement.LibraryNames,
                    IsAvailable: true,
                    LoadedLibraryName: libraryName,
                    Diagnostic: $"{requirement.DisplayName} is available via {libraryName}.");
            }
        }

        string tried = string.Join(", ", requirement.LibraryNames);
        string required = requirement.Required ? "required" : "optional";
        return new LinuxInputNativeLibraryStatus(
            requirement.Id,
            requirement.DisplayName,
            requirement.LibraryNames,
            IsAvailable: false,
            LoadedLibraryName: null,
            Diagnostic: $"{requirement.DisplayName} is {required} but no candidate native library could be loaded. Tried: {tried}.");
    }
}
