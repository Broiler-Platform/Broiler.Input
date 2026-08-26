namespace Broiler.Input.Android;

/// <summary>
/// The lifecycle notifications an Android provider sends to a device it opened.
/// </summary>
/// <remarks>
/// <c>InputDevice.MarkUnavailable</c> and the fault helpers are protected, so a provider in a
/// different assembly cannot drive them directly. Each Android device implements this interface to
/// expose exactly the two transitions a provider needs, without widening the base class surface.
/// </remarks>
public interface IAndroidInputDevice
{
    /// <summary>
    /// The backing Android device is gone. The device marks itself unavailable and records the
    /// fault.
    /// </summary>
    void NotifyRemoved(InputFault fault);

    /// <summary>
    /// Input capture ended while the device was running — the Activity paused, the window lost
    /// focus, or a parent view intercepted the gesture. Anything in flight is cancelled.
    /// </summary>
    void NotifyCaptureLost(string reason);
}
