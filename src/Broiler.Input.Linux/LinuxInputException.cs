using System;

namespace Broiler.Input.Linux;

public sealed class LinuxInputException(InputFault fault) : InvalidOperationException(fault?.Message, fault?.Exception)
{
    public InputFault Fault { get; } = fault ?? throw new ArgumentNullException(nameof(fault));
}
