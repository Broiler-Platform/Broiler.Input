using System;

namespace Broiler.Input.Microphone;

[Flags]
public enum MicrophoneBufferFlags
{
    None = 0,
    Silent = 1 << 0,
    Discontinuous = 1 << 1,
    TimestampError = 1 << 2,
}
