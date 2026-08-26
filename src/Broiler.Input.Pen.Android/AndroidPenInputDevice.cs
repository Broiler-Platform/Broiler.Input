using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Broiler.Input.Android;

namespace Broiler.Input.Pen.Android;

/// <summary>
/// A <see cref="PenInputDevice"/> driven by the stylus and eraser pointers in Android
/// <c>MotionEvent</c> data.
/// </summary>
/// <remarks>
/// Pen and touch share one event stream on Android: a stylus arrives through the same
/// <c>onTouchEvent</c> callback as a finger, distinguished only by
/// <c>MotionEvent.getToolType()</c>. This device consumes the stylus and eraser tools and ignores
/// everything else, which is the other half of the routing that keeps a stylus from also being
/// delivered as a touch contact.
///
/// Hover is delivered from <c>onHoverEvent</c>, whose <c>ACTION_HOVER_*</c> actions report a stylus
/// detected above the surface.
/// </remarks>
public sealed class AndroidPenInputDevice : PenInputDevice, IAndroidInputDevice
{
    private readonly AndroidCoordinateSpace _coordinateSpace;
    private readonly AndroidUptimeInputClock _clock;
    private readonly PenOpenOptions _options;
    private bool _isContactDown;

    public AndroidPenInputDevice(
        InputDeviceDescriptor descriptor,
        AndroidCoordinateSpace coordinateSpace,
        AndroidUptimeInputClock clock,
        PenOpenOptions? options = null)
        : base(descriptor, clock)
    {
        _coordinateSpace = coordinateSpace ?? throw new ArgumentNullException(nameof(coordinateSpace));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _options = options ?? new PenOpenOptions();
    }

    /// <summary>True while the pen tip is in contact with the surface.</summary>
    public bool IsContactDown => _isContactDown;

    /// <summary>
    /// Translates one <c>MotionEvent</c> and raises the resulting pen event.
    /// </summary>
    /// <returns>True when the event produced a pen event.</returns>
    public bool ProcessMotionEvent(AndroidMotionSample sample)
    {
        ArgumentNullException.ThrowIfNull(sample);
        ThrowIfDisposed();

        _clock.Observe(sample.EventTimeMilliseconds);

        if (!CanDeliverInput || sample.Pointers.Count == 0)
            return false;

        if (!TryGetPenPointer(sample, out AndroidPointerSample pointer))
            return false;

        PenContactState? state = sample.Action switch
        {
            AndroidMotionEventConstants.ActionDown or
            AndroidMotionEventConstants.ActionPointerDown => PenContactState.Pressed,

            AndroidMotionEventConstants.ActionMove => PenContactState.Moved,

            AndroidMotionEventConstants.ActionUp or
            AndroidMotionEventConstants.ActionPointerUp => PenContactState.Released,

            AndroidMotionEventConstants.ActionCancel or
            AndroidMotionEventConstants.ActionOutside => PenContactState.Cancelled,

            AndroidMotionEventConstants.ActionHoverEnter or
            AndroidMotionEventConstants.ActionHoverMove => _options.ReportHover ? PenContactState.Hover : null,

            // A hover exit with no contact means the pen left detection range. There is no neutral
            // "out of range" state, so it is reported as a cancellation of any tracking in flight.
            AndroidMotionEventConstants.ActionHoverExit => _options.ReportHover ? PenContactState.Cancelled : null,

            _ => null,
        };

        if (state is null)
            return false;

        _isContactDown = state is PenContactState.Pressed or PenContactState.Moved;
        Raise(sample, pointer, state.Value);
        return true;
    }

    /// <summary>Cancels a contact in flight when capture is lost.</summary>
    public bool CancelActiveContact(string reason = "capture lost")
    {
        ThrowIfDisposed();

        if (!_options.CancelContactsOnCaptureLoss || !_isContactDown)
            return false;

        EmitDiagnostic(
            InputDiagnosticLevel.Information,
            "input.pen.capture-lost",
            new Dictionary<string, string> { ["reason"] = reason });

        _isContactDown = false;
        RaiseContactChanged(new PenContactEvent(
            NextEventHeader(_clock.GetTimestamp()),
            InputPoint.ClientDeviceIndependentPixels(0, 0),
            PenContactState.Cancelled,
            PenButtons.None,
            0,
            0,
            0,
            InputEventSource.Raw));
        return true;
    }

    public override ValueTask StopAsync(CancellationToken cancellationToken = default)
    {
        CancelActiveContact("device stopped");
        return base.StopAsync(cancellationToken);
    }

    void IAndroidInputDevice.NotifyRemoved(InputFault fault)
    {
        CancelActiveContact("device removed");
        MarkUnavailable(fault);
    }

    void IAndroidInputDevice.NotifyCaptureLost(string reason) => CancelActiveContact(reason);

    private static bool TryGetPenPointer(AndroidMotionSample sample, out AndroidPointerSample pointer)
    {
        // For a pointer-indexed action the acting pointer decides; otherwise take the first stylus
        // in the event. Android does not support two simultaneous styluses on the devices Broiler
        // targets, so the first is the only one.
        AndroidPointerSample candidate = sample.Pointers[sample.ActionPointerIndex];
        if (IsPen(candidate.ToolType))
        {
            pointer = candidate;
            return true;
        }

        foreach (AndroidPointerSample current in sample.Pointers)
        {
            if (IsPen(current.ToolType))
            {
                pointer = current;
                return true;
            }
        }

        pointer = default;
        return false;
    }

    private static bool IsPen(int toolType) =>
        toolType is AndroidMotionEventConstants.ToolTypeStylus or AndroidMotionEventConstants.ToolTypeEraser;

    private void Raise(AndroidMotionSample sample, AndroidPointerSample pointer, PenContactState state)
    {
        (double tiltX, double tiltY) = _options.ReportTilt
            ? AndroidPenTilt.ToDegrees(pointer.TiltRadians, pointer.OrientationRadians)
            : (0, 0);

        RaiseContactChanged(new PenContactEvent(
            NextEventHeader(sample.Timestamp),
            _coordinateSpace.ToInputPoint(pointer.X, pointer.Y),
            state,
            ToPenButtons(sample.ButtonState, pointer.ToolType),
            ClampPressure(pointer.Pressure),
            tiltX,
            tiltY,
            InputEventSource.Raw));
    }

    /// <summary>
    /// Maps the Android button state onto <see cref="PenButtons"/>. The eraser is reported as a
    /// tool type rather than a button, so an eraser tool sets the eraser flag regardless of which
    /// buttons are pressed.
    /// </summary>
    internal static PenButtons ToPenButtons(int buttonState, int toolType)
    {
        PenButtons buttons = PenButtons.None;

        if ((buttonState & AndroidMotionEventConstants.ButtonStylusPrimary) != 0 ||
            (buttonState & AndroidMotionEventConstants.ButtonSecondary) != 0)
        {
            buttons |= PenButtons.Barrel;
        }

        if (toolType == AndroidMotionEventConstants.ToolTypeEraser ||
            (buttonState & AndroidMotionEventConstants.ButtonStylusSecondary) != 0)
        {
            buttons |= PenButtons.Eraser;
        }

        return buttons;
    }

    private static double ClampPressure(float pressure) =>
        !float.IsFinite(pressure) ? 0 : Math.Clamp(pressure, 0f, 1f);
}
