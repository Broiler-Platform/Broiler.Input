using System;

namespace Broiler.Input;

public readonly record struct InputTimestamp(long Ticks, long Frequency, string ClockName)
{
    public bool IsValid => Frequency > 0;

    public TimeSpan ToElapsedTime() =>
        Frequency <= 0 ? TimeSpan.Zero : TimeSpan.FromSeconds((double)Ticks / Frequency);
}
