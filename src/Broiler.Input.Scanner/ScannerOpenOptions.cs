using System;

namespace Broiler.Input.Scanner;

public sealed record ScannerOpenOptions
{
    public ScannerOpenOptions(ScannerFormat? preferredFormat = null, ScannerSessionOptions? sessionOptions = null)
    {
        PreferredFormat = preferredFormat;
        SessionOptions = sessionOptions ?? ScannerSessionOptions.BatchDefault;
    }

    public ScannerFormat? PreferredFormat { get; }

    public ScannerSessionOptions SessionOptions { get; }

    public static ScannerOpenOptions Default { get; } = new();

    public static ScannerOpenOptions WithPreferredFormat(ScannerFormat format)
    {
        ArgumentNullException.ThrowIfNull(format);
        return new ScannerOpenOptions(format);
    }
}
