using System;

namespace Broiler.Input.Android;

/// <summary>Which neutral device an Android pointer belongs to.</summary>
public enum AndroidPointerDestination
{
    /// <summary>The pointer is not delivered to any device.</summary>
    None = 0,

    /// <summary>A finger contact, delivered to the touch device.</summary>
    Touch,

    /// <summary>A stylus or eraser contact, delivered to the pen device.</summary>
    Pen,

    /// <summary>A mouse or trackpad pointer.</summary>
    Mouse,
}

/// <summary>
/// Decides which device each Android pointer belongs to.
/// </summary>
/// <remarks>
/// Android multiplexes fingers, styluses, and mice onto one <c>MotionEvent</c> stream, so a single
/// physical gesture can be delivered more than once if the receiver routes on the event
/// <em>source</em> — a stylus press arrives at <c>onTouchEvent</c> with a touchscreen source, and a
/// mouse click arrives there too. Routing on <c>getToolType()</c> instead gives each pointer exactly
/// one destination, which is what suppresses the duplicate compatibility pointer.
///
/// This is a pure classification helper. The glue that calls the touch and pen devices lives in the
/// host coordinator, matching where <c>LinuxInputCoordinator</c> sits for the desktop applications.
/// </remarks>
public static class AndroidPointerRouting
{
    /// <summary>
    /// Classifies one tool type. An unknown tool is treated as a finger, because that is what
    /// Android reports for touchscreens that predate the tool-type API and for synthetic events in
    /// instrumentation tests.
    /// </summary>
    public static AndroidPointerDestination Classify(int toolType) =>
        toolType switch
        {
            AndroidMotionEventConstants.ToolTypeFinger => AndroidPointerDestination.Touch,
            AndroidMotionEventConstants.ToolTypeUnknown => AndroidPointerDestination.Touch,
            AndroidMotionEventConstants.ToolTypeStylus => AndroidPointerDestination.Pen,
            AndroidMotionEventConstants.ToolTypeEraser => AndroidPointerDestination.Pen,
            AndroidMotionEventConstants.ToolTypeMouse => AndroidPointerDestination.Mouse,
            _ => AndroidPointerDestination.None,
        };

    /// <summary>True when the event carries at least one pointer bound for <paramref name="destination"/>.</summary>
    public static bool HasPointerFor(AndroidMotionSample sample, AndroidPointerDestination destination)
    {
        ArgumentNullException.ThrowIfNull(sample);

        foreach (AndroidPointerSample pointer in sample.Pointers)
        {
            if (Classify(pointer.ToolType) == destination)
                return true;
        }

        return false;
    }

    /// <summary>
    /// Classifies the pointer the action refers to, which is the one that decides where a
    /// press or release is delivered.
    /// </summary>
    public static AndroidPointerDestination ClassifyActionPointer(AndroidMotionSample sample)
    {
        ArgumentNullException.ThrowIfNull(sample);

        return sample.Pointers.Count == 0
            ? AndroidPointerDestination.None
            : Classify(sample.Pointers[sample.ActionPointerIndex].ToolType);
    }
}
