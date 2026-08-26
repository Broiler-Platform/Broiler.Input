using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Broiler.Input.Android;

namespace Broiler.Input.Touch.Android;

/// <summary>
/// A <see cref="TouchInputDevice"/> driven by Android <c>MotionEvent</c> data.
/// </summary>
/// <remarks>
/// The host forwards each event from <c>View.onTouchEvent</c> as an
/// <see cref="AndroidMotionSample"/>; this device turns it into per-contact
/// <see cref="TouchContactEvent"/> values. It tracks the pointer <em>ids</em> that are currently
/// down, because Android's pointer <em>index</em> is only a position within one event and shifts
/// whenever a finger lifts.
/// </remarks>
public sealed class AndroidTouchInputDevice : TouchInputDevice, IAndroidInputDevice
{
    private readonly AndroidCoordinateSpace _coordinateSpace;
    private readonly AndroidUptimeInputClock _clock;
    private readonly TouchOpenOptions _options;
    private readonly HashSet<int> _activeContacts = [];

    public AndroidTouchInputDevice(
        InputDeviceDescriptor descriptor,
        AndroidCoordinateSpace coordinateSpace,
        AndroidUptimeInputClock clock,
        TouchOpenOptions? options = null)
        : base(descriptor, clock)
    {
        _coordinateSpace = coordinateSpace ?? throw new ArgumentNullException(nameof(coordinateSpace));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _options = options ?? new TouchOpenOptions();
    }

    /// <summary>The pointer ids currently in contact with the screen.</summary>
    public IReadOnlyCollection<int> ActiveContacts => _activeContacts;

    /// <summary>
    /// Translates one <c>MotionEvent</c> and raises the resulting contact events.
    /// </summary>
    /// <returns>
    /// True when the event produced at least one contact event. A host can return this from
    /// <c>onTouchEvent</c> so Android knows the gesture was consumed.
    /// </returns>
    public bool ProcessMotionEvent(AndroidMotionSample sample)
    {
        ArgumentNullException.ThrowIfNull(sample);
        ThrowIfDisposed();

        _clock.Observe(sample.EventTimeMilliseconds);

        if (!CanDeliverInput)
            return false;

        // Route by tool type, not by source. A stylus and a mouse both arrive through onTouchEvent
        // on many devices, and forwarding them here is what produces the classic duplicate contact:
        // once as a touch and once as the compatibility pointer. The pen device owns stylus and
        // eraser tools; mouse tools are dropped outright.
        if (!ContainsFingerPointer(sample))
            return false;

        return sample.Action switch
        {
            AndroidMotionEventConstants.ActionDown or
            AndroidMotionEventConstants.ActionPointerDown => RaiseForActionPointer(sample, TouchContactState.Pressed),

            AndroidMotionEventConstants.ActionMove => RaiseForAllPointers(sample, TouchContactState.Moved),

            AndroidMotionEventConstants.ActionUp or
            AndroidMotionEventConstants.ActionPointerUp => RaiseForActionPointer(sample, TouchContactState.Released),

            AndroidMotionEventConstants.ActionCancel => CancelAll(sample.Timestamp),

            // ACTION_OUTSIDE means the gesture left the window. Treat it as a cancellation so no
            // contact is left pressed forever.
            AndroidMotionEventConstants.ActionOutside => CancelAll(sample.Timestamp),

            _ => false,
        };
    }

    /// <summary>
    /// Cancels every contact still down. The host calls this when the Activity pauses, the window
    /// loses focus, or a parent view intercepts the gesture.
    /// </summary>
    public bool CancelActiveContacts(string reason = "capture lost")
    {
        ThrowIfDisposed();

        if (!_options.CancelContactsOnCaptureLoss || _activeContacts.Count == 0)
            return false;

        EmitDiagnostic(
            InputDiagnosticLevel.Information,
            "input.touch.capture-lost",
            new Dictionary<string, string>
            {
                ["reason"] = reason,
                ["contacts"] = _activeContacts.Count.ToString(System.Globalization.CultureInfo.InvariantCulture),
            });

        return CancelAll(_clock.GetTimestamp().IsValid ? _clock.GetTimestamp() : null);
    }

    public override ValueTask StopAsync(CancellationToken cancellationToken = default)
    {
        CancelActiveContacts("device stopped");
        return base.StopAsync(cancellationToken);
    }

    void IAndroidInputDevice.NotifyRemoved(InputFault fault)
    {
        CancelActiveContacts("device removed");
        MarkUnavailable(fault);
    }

    void IAndroidInputDevice.NotifyCaptureLost(string reason) => CancelActiveContacts(reason);

    protected override void Dispose(bool disposing)
    {
        _activeContacts.Clear();
        base.Dispose(disposing);
    }

    private static bool ContainsFingerPointer(AndroidMotionSample sample)
    {
        foreach (AndroidPointerSample pointer in sample.Pointers)
        {
            if (IsFinger(pointer.ToolType))
                return true;
        }

        return false;
    }

    private static bool IsFinger(int toolType) =>
        toolType is AndroidMotionEventConstants.ToolTypeFinger or AndroidMotionEventConstants.ToolTypeUnknown;

    private bool RaiseForActionPointer(AndroidMotionSample sample, TouchContactState state)
    {
        if (sample.Pointers.Count == 0)
            return false;

        AndroidPointerSample pointer = sample.Pointers[sample.ActionPointerIndex];
        if (!IsFinger(pointer.ToolType))
            return false;

        if (state == TouchContactState.Pressed)
            _activeContacts.Add(pointer.PointerId);
        else
            _activeContacts.Remove(pointer.PointerId);

        Raise(sample, pointer, state);
        return true;
    }

    private bool RaiseForAllPointers(AndroidMotionSample sample, TouchContactState state)
    {
        bool raised = false;
        foreach (AndroidPointerSample pointer in sample.Pointers)
        {
            if (!IsFinger(pointer.ToolType))
                continue;

            // A move for a contact that never went down would leave the consumer tracking a
            // gesture it never saw start; adopt it so the id set stays consistent with what was
            // delivered.
            _activeContacts.Add(pointer.PointerId);
            Raise(sample, pointer, state);
            raised = true;
        }

        return raised;
    }

    private bool CancelAll(InputTimestamp? timestamp)
    {
        if (_activeContacts.Count == 0)
            return false;

        int[] contacts = [.. _activeContacts];
        _activeContacts.Clear();

        foreach (int pointerId in contacts)
        {
            RaiseContactChanged(new TouchContactEvent(
                NextEventHeader(timestamp),
                pointerId,
                InputPoint.ClientDeviceIndependentPixels(0, 0),
                TouchContactState.Cancelled,
                0,
                InputEventSource.Raw));
        }

        return true;
    }

    private void Raise(AndroidMotionSample sample, AndroidPointerSample pointer, TouchContactState state)
    {
        RaiseContactChanged(new TouchContactEvent(
            NextEventHeader(sample.Timestamp),
            pointer.PointerId,
            _coordinateSpace.ToInputPoint(pointer.X, pointer.Y),
            state,
            _options.ReportPressure ? ClampPressure(pointer.Pressure) : 0,
            InputEventSource.Raw));
    }

    // Android documents getPressure as "normally" 0..1 but allows values above 1 on devices with a
    // wider calibrated range, so the value is clamped rather than trusted.
    private static double ClampPressure(float pressure) =>
        !float.IsFinite(pressure) ? 0 : Math.Clamp(pressure, 0f, 1f);
}
