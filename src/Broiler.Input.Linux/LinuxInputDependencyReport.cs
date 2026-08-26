using System.Collections.Generic;

namespace Broiler.Input.Linux;

public sealed record LinuxInputDependencyReport(
    IReadOnlyList<LinuxInputNativeLibraryStatus> NativeLibraries,
    LinuxEventDeviceAccessStatus EventDevices);

public static class LinuxInputDependencies
{
    public static LinuxInputDependencyReport CheckBaseline(
        string inputDirectory = LinuxEventDeviceAccessProbe.DefaultInputDirectory) =>
        new(
            LinuxInputNativeLibraryProbe.CheckBaseline(),
            LinuxEventDeviceAccessProbe.CheckEventDeviceAccess(inputDirectory));
}
