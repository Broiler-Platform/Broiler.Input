using System;

namespace Broiler.Input.Camera;

[Flags]
public enum CameraFrameFlags
{
    None = 0,
    Discontinuous = 1 << 0,
    FormatChanged = 1 << 1,
    EndOfStream = 1 << 2,
    TimestampError = 1 << 3,
}
