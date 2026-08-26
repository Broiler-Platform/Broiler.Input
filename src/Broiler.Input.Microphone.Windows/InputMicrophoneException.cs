using System;
using Broiler.Input;

namespace Broiler.Input.Microphone.Windows;

public sealed class InputMicrophoneException : InvalidOperationException
{
    public InputMicrophoneException(InputFault fault)
        : base(fault?.Message)
    {
        Fault = fault ?? throw new ArgumentNullException(nameof(fault));
    }

    public InputFault Fault { get; }
}
