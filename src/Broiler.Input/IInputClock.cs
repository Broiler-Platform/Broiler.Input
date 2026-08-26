using System.Diagnostics;

namespace Broiler.Input;

public interface IInputClock
{
    string Name { get; }

    long Frequency { get; }

    InputTimestamp GetTimestamp();
}

public sealed class StopwatchInputClock : IInputClock
{
    public static StopwatchInputClock Shared { get; } = new();

    private StopwatchInputClock()
    {
    }

    public string Name => "Stopwatch";

    public long Frequency => Stopwatch.Frequency;

    public InputTimestamp GetTimestamp() => new(Stopwatch.GetTimestamp(), Stopwatch.Frequency, Name);
}
