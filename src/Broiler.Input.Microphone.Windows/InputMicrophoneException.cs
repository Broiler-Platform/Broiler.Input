using System;

namespace Broiler.Input.Microphone.Windows;

public sealed class InputMicrophoneException(InputFault fault) : InvalidOperationException(fault?.Message)
{
    public InputFault Fault { get; } = fault ?? throw new ArgumentNullException(nameof(fault));
}
