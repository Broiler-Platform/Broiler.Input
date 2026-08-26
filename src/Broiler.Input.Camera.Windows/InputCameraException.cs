using System;
using Broiler.Input;

namespace Broiler.Input.Camera.Windows;

public sealed class InputCameraException : InvalidOperationException
{
    public InputCameraException(InputFault fault)
        : base(fault?.Message, fault?.Exception)
    {
        Fault = fault ?? throw new ArgumentNullException(nameof(fault));
    }

    public InputFault Fault { get; }
}
