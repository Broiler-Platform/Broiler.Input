using System;

namespace Broiler.Input.Linux;

public sealed class LinuxInputException : InvalidOperationException
{
    public LinuxInputException(InputFault fault)
        : base(fault?.Message, fault?.Exception)
    {
        Fault = fault ?? throw new ArgumentNullException(nameof(fault));
    }

    public InputFault Fault { get; }
}
