using System.Threading;

namespace Broiler.Input.Android;

/// <summary>
/// An <see cref="IInputClock"/> over Android's <c>SystemClock.uptimeMillis()</c>, which is the
/// timebase every <c>MotionEvent</c> and <c>KeyEvent</c> timestamp already uses.
/// </summary>
/// <remarks>
/// Using the event's own timebase keeps the delivered <see cref="InputTimestamp"/> comparable with
/// the platform timestamps, rather than re-stamping events with an unrelated stopwatch reading at
/// the moment the managed code happens to observe them. The host supplies the current reading,
/// because only it can call the Android API; until it does, the clock reports the last event time
/// it saw.
/// </remarks>
public sealed class AndroidUptimeInputClock : IInputClock
{
    /// <summary>Android reports uptime in milliseconds, so one tick is a millisecond.</summary>
    public const long TicksPerSecond = 1000;

    /// <summary>Identifies the timebase on every <see cref="InputTimestamp"/> this clock produces.</summary>
    public const string ClockName = "Android.SystemClock.uptimeMillis";

    private long _lastObservedUptimeMilliseconds;

    public string Name => ClockName;

    public long Frequency => TicksPerSecond;

    /// <summary>
    /// Records the most recent uptime reading. Providers call this as events arrive so a
    /// device-generated timestamp (a synthesized cancellation, say) is not older than the last real
    /// event.
    /// </summary>
    public void Observe(long uptimeMilliseconds)
    {
        long current = Interlocked.Read(ref _lastObservedUptimeMilliseconds);
        while (uptimeMilliseconds > current)
        {
            long previous = Interlocked.CompareExchange(ref _lastObservedUptimeMilliseconds, uptimeMilliseconds, current);
            if (previous == current)
                return;

            current = previous;
        }
    }

    public InputTimestamp GetTimestamp() =>
        FromUptimeMilliseconds(Interlocked.Read(ref _lastObservedUptimeMilliseconds));

    /// <summary>Builds an <see cref="InputTimestamp"/> from an Android uptime reading.</summary>
    public static InputTimestamp FromUptimeMilliseconds(long uptimeMilliseconds) =>
        new(uptimeMilliseconds, TicksPerSecond, ClockName);
}
