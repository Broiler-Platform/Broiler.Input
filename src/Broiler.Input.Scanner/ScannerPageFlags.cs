using System;

namespace Broiler.Input.Scanner;

[Flags]
public enum ScannerPageFlags
{
    None = 0,
    Preview = 1 << 0,
    DuplexBackSide = 1 << 1,
    EndOfBatch = 1 << 2,
    FormatChanged = 1 << 3,
    TimestampError = 1 << 4,
}
