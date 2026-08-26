using System;
using System.Collections.Generic;
using System.Linq;

namespace Broiler.Input.Android;

/// <summary>
/// A neutral snapshot of one <c>MotionEvent</c>, carrying everything the touch, pen, and mouse
/// translation needs. The host builds one of these inside <c>View.onTouchEvent</c>,
/// <c>onGenericMotionEvent</c>, or <c>onHoverEvent</c> and forwards it.
/// </summary>
public sealed class AndroidMotionSample
{
    private readonly AndroidPointerSample[] _pointers;

    public AndroidMotionSample(
        int packedAction,
        IEnumerable<AndroidPointerSample> pointers,
        long eventTimeMilliseconds,
        long downTimeMilliseconds = 0,
        int metaState = 0,
        int buttonState = 0,
        int source = AndroidInputSource.Touchscreen)
    {
        ArgumentNullException.ThrowIfNull(pointers);

        _pointers = pointers.ToArray();
        PackedAction = packedAction;
        EventTimeMilliseconds = eventTimeMilliseconds;
        DownTimeMilliseconds = downTimeMilliseconds;
        MetaState = metaState;
        ButtonState = buttonState;
        Source = source;

        int index = AndroidMotionEventConstants.PointerIndex(packedAction);
        ActionPointerIndex = index >= 0 && index < _pointers.Length ? index : 0;
    }

    /// <summary>The raw <c>getAction()</c> value, still carrying the pointer index.</summary>
    public int PackedAction { get; }

    /// <summary>The action code with the pointer index masked off.</summary>
    public int Action => AndroidMotionEventConstants.MaskedAction(PackedAction);

    /// <summary>
    /// The index into <see cref="Pointers"/> that <see cref="Action"/> refers to. Only meaningful
    /// for <c>ACTION_POINTER_DOWN</c> and <c>ACTION_POINTER_UP</c>; clamped into range so a
    /// malformed action cannot index out of bounds.
    /// </summary>
    public int ActionPointerIndex { get; }

    public IReadOnlyList<AndroidPointerSample> Pointers => _pointers;

    /// <summary><c>MotionEvent.getEventTime()</c>, in <c>SystemClock.uptimeMillis()</c> units.</summary>
    public long EventTimeMilliseconds { get; }

    /// <summary><c>MotionEvent.getDownTime()</c>, in <c>SystemClock.uptimeMillis()</c> units.</summary>
    public long DownTimeMilliseconds { get; }

    public int MetaState { get; }

    public int ButtonState { get; }

    public int Source { get; }

    public InputTimestamp Timestamp => AndroidUptimeInputClock.FromUptimeMilliseconds(EventTimeMilliseconds);

    /// <summary>
    /// Convenience factory for a single-pointer event, which is every event a mouse or a
    /// one-finger gesture produces.
    /// </summary>
    public static AndroidMotionSample SinglePointer(
        int action,
        int pointerId,
        float x,
        float y,
        long eventTimeMilliseconds,
        float pressure = 1f,
        int toolType = AndroidMotionEventConstants.ToolTypeFinger,
        int metaState = 0,
        int buttonState = 0,
        int source = AndroidInputSource.Touchscreen) =>
        new(
            action,
            [new AndroidPointerSample(pointerId, x, y, pressure, toolType)],
            eventTimeMilliseconds,
            eventTimeMilliseconds,
            metaState,
            buttonState,
            source);
}
