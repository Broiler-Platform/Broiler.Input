using System;
using System.Threading;

namespace Broiler.Input.Testing;

public sealed class ManualInputClock : IInputClock
{
    private long _ticks;

    public ManualInputClock(long frequency = TimeSpan.TicksPerSecond)
    {
        if (frequency <= 0)
            throw new ArgumentOutOfRangeException(nameof(frequency), "Clock frequency must be positive.");

        Frequency = frequency;
    }

    public string Name => "Manual";

    public long Frequency { get; }

    public long Ticks => Volatile.Read(ref _ticks);

    public InputTimestamp GetTimestamp() => new(Ticks, Frequency, Name);

    public InputTimestamp Advance(long ticks)
    {
        if (ticks < 0)
            throw new ArgumentOutOfRangeException(nameof(ticks), "Manual clocks cannot move backwards.");

        return new InputTimestamp(Interlocked.Add(ref _ticks, ticks), Frequency, Name);
    }
}
