using static Broiler.Native.Windows.PerformanceCounterNative;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Broiler.Input.Windows;

[SupportedOSPlatform("windows")]
public sealed partial class WindowsInputClock : IInputClock
{
    public static WindowsInputClock Shared { get; } = new();

    private readonly long _frequency;

    private WindowsInputClock()
    {
        if (!QueryPerformanceFrequency(out long frequency) || frequency <= 0)
            frequency = Stopwatch.Frequency;

        _frequency = frequency;
    }

    public string Name => "Windows.QueryPerformanceCounter";

    public long Frequency => _frequency;

    public InputTimestamp GetTimestamp()
    {
        if (QueryPerformanceCounter(out long ticks))
            return new InputTimestamp(ticks, _frequency, Name);

        return new InputTimestamp(Stopwatch.GetTimestamp(), Stopwatch.Frequency, StopwatchInputClock.Shared.Name);
    }

}
