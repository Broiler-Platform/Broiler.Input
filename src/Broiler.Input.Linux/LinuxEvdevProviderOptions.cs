using System;

namespace Broiler.Input.Linux;

public sealed record LinuxEvdevProviderOptions(
    string InputDirectory = LinuxEventDeviceAccessProbe.DefaultInputDirectory,
    string SysfsInputRoot = "/sys/class/input",
    bool AcknowledgeRawBackgroundInput = false,
    int PollTimeoutMilliseconds = 50)
{
    public void ValidateRawAccess()
    {
        if (!AcknowledgeRawBackgroundInput)
            throw new InvalidOperationException("Linux evdev input reads raw keyboard and mouse events directly from event devices and requires explicit acknowledgement.");

        if (PollTimeoutMilliseconds <= 0)
            throw new ArgumentOutOfRangeException(nameof(PollTimeoutMilliseconds), "Linux evdev poll timeout must be positive.");
    }

    public LinuxEvdevProviderOptions Normalize() =>
        this with
        {
            InputDirectory = string.IsNullOrWhiteSpace(InputDirectory)
                ? LinuxEventDeviceAccessProbe.DefaultInputDirectory
                : InputDirectory,
            SysfsInputRoot = string.IsNullOrWhiteSpace(SysfsInputRoot)
                ? "/sys/class/input"
                : SysfsInputRoot,
            PollTimeoutMilliseconds = PollTimeoutMilliseconds <= 0 ? 50 : PollTimeoutMilliseconds,
        };
}
