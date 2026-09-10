using System;

namespace Broiler.Input.Camera.Windows;

public sealed class InputCameraException(InputFault fault) : InvalidOperationException(fault?.Message, fault?.Exception)
{
    public InputFault Fault { get; } = fault ?? throw new ArgumentNullException(nameof(fault));
}
