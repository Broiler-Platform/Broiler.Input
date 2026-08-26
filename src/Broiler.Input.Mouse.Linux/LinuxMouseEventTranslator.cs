using System;
using System.Collections.Generic;
using Broiler.Input.Linux;

namespace Broiler.Input.Mouse.Linux;

public enum LinuxMouseTranslatedEventKind
{
    Move = 0,
    Button,
    Wheel,
}

/// <summary>How a pointing device reports motion.</summary>
public enum LinuxPointerMotionMode
{
    /// <summary>A mouse: relative counts via EV_REL.</summary>
    Relative = 0,

    /// <summary>A touchpad: absolute finger positions via EV_ABS, converted to relative motion.</summary>
    AbsoluteTouchpad,
}

/// <summary>Range/resolution of one absolute axis, with a derived pixels-per-unit scale.</summary>
public readonly record struct LinuxAbsAxis(int Minimum = 0, int Maximum = 0, int Resolution = 0)
{
    // Pointer speed for resolution-aware pads (device reports units/mm).
    private const double PixelsPerMillimeter = 6.0;

    // When resolution is unknown, map a full-pad traversal to this many pixels
    // so speed stays sane regardless of the pad's raw unit range.
    private const double FullTraversalPixels = 1600.0;
    private const double DefaultScale = 0.5;

    public double PixelScale =>
        Resolution > 0
            ? PixelsPerMillimeter / Resolution
            : (Maximum > Minimum ? FullTraversalPixels / (Maximum - Minimum) : DefaultScale);
}

public readonly record struct LinuxMouseTranslatedEvent(
    LinuxMouseTranslatedEventKind Kind,
    MouseMoveEvent? Move = null,
    MouseButtonEvent? Button = null,
    MouseWheelEvent? Wheel = null)
{
    public static LinuxMouseTranslatedEvent FromMove(MouseMoveEvent inputEvent) =>
        new(LinuxMouseTranslatedEventKind.Move, Move: inputEvent);

    public static LinuxMouseTranslatedEvent FromButton(MouseButtonEvent inputEvent) =>
        new(LinuxMouseTranslatedEventKind.Button, Button: inputEvent);

    public static LinuxMouseTranslatedEvent FromWheel(MouseWheelEvent inputEvent) =>
        new(LinuxMouseTranslatedEventKind.Wheel, Wheel: inputEvent);
}

public sealed class LinuxMouseEventTranslator
{
    // Tap-to-click thresholds: a brief, near-stationary contact is a left click.
    private const long TapMaxDurationMicroseconds = 180_000;
    private const double TapMaxMovementPixels = 8.0;

    private readonly LinuxPointerMotionMode _mode;
    private readonly LinuxAbsAxis _absXAxis;
    private readonly LinuxAbsAxis _absYAxis;

    private int _pendingX;
    private int _pendingY;
    private int _pendingVerticalWheel;
    private int _pendingHorizontalWheel;
    private int _pendingVerticalWheelHiRes;
    private int _pendingHorizontalWheelHiRes;
    private MouseButtons _buttons;

    // Absolute-touchpad tracking state.
    private int _absX;
    private int _absY;
    private bool _haveAbsX;
    private bool _haveAbsY;
    private int _lastAbsX;
    private int _lastAbsY;
    private bool _haveLastAbs;
    private bool _touching;
    private double _carryX;
    private double _carryY;
    private long _touchStartMicroseconds;
    private double _touchMovementPixels;
    private bool _physicalButtonDuringTouch;

    public LinuxMouseEventTranslator()
        : this(LinuxPointerMotionMode.Relative, default, default)
    {
    }

    public LinuxMouseEventTranslator(LinuxPointerMotionMode mode, LinuxAbsAxis absXAxis, LinuxAbsAxis absYAxis)
    {
        _mode = mode;
        _absXAxis = absXAxis;
        _absYAxis = absYAxis;
    }

    public void Process(
        in LinuxInputEvent inputEvent,
        Func<InputTimestamp, InputEventHeader> createHeader,
        MouseOpenOptions options,
        ICollection<LinuxMouseTranslatedEvent> output)
    {
        ArgumentNullException.ThrowIfNull(createHeader);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(output);

        switch (inputEvent.Type)
        {
            case LinuxEvdevConstants.EvRel:
                ProcessRelative(inputEvent);
                break;

            case LinuxEvdevConstants.EvAbs:
                ProcessAbsolute(inputEvent);
                break;

            case LinuxEvdevConstants.EvKey:
                ProcessButton(inputEvent, createHeader, output);
                break;

            case LinuxEvdevConstants.EvSyn when inputEvent.Code == LinuxEvdevConstants.SynReport:
                if (_mode == LinuxPointerMotionMode.AbsoluteTouchpad)
                    AccumulateTouchpadMotion();
                Flush(inputEvent.Timestamp, createHeader, options, output);
                break;

            case LinuxEvdevConstants.EvSyn when inputEvent.Code == LinuxEvdevConstants.SynDropped:
                ResetPending();
                _haveLastAbs = false;
                break;
        }
    }

    private void ProcessRelative(in LinuxInputEvent inputEvent)
    {
        switch (inputEvent.Code)
        {
            case LinuxEvdevConstants.RelX:
                _pendingX += inputEvent.Value;
                break;
            case LinuxEvdevConstants.RelY:
                _pendingY += inputEvent.Value;
                break;
            case LinuxEvdevConstants.RelWheel:
                _pendingVerticalWheel += inputEvent.Value;
                break;
            case LinuxEvdevConstants.RelHWheel:
                _pendingHorizontalWheel += inputEvent.Value;
                break;
            case LinuxEvdevConstants.RelWheelHiRes:
                _pendingVerticalWheelHiRes += inputEvent.Value;
                break;
            case LinuxEvdevConstants.RelHWheelHiRes:
                _pendingHorizontalWheelHiRes += inputEvent.Value;
                break;
        }
    }

    private void ProcessAbsolute(in LinuxInputEvent inputEvent)
    {
        switch (inputEvent.Code)
        {
            case LinuxEvdevConstants.AbsX:
            case LinuxEvdevConstants.AbsMtPositionX:
                _absX = inputEvent.Value;
                _haveAbsX = true;
                break;
            case LinuxEvdevConstants.AbsY:
            case LinuxEvdevConstants.AbsMtPositionY:
                _absY = inputEvent.Value;
                _haveAbsY = true;
                break;
        }
    }

    private void AccumulateTouchpadMotion()
    {
        if (!_touching || !_haveAbsX || !_haveAbsY)
            return;

        if (_haveLastAbs)
        {
            double dxPixels = ((_absX - _lastAbsX) * _absXAxis.PixelScale) + _carryX;
            double dyPixels = ((_absY - _lastAbsY) * _absYAxis.PixelScale) + _carryY;

            int dx = (int)Math.Truncate(dxPixels);
            int dy = (int)Math.Truncate(dyPixels);
            _carryX = dxPixels - dx;
            _carryY = dyPixels - dy;

            _pendingX += dx;
            _pendingY += dy;
            _touchMovementPixels += Math.Abs(dxPixels) + Math.Abs(dyPixels);
        }

        _lastAbsX = _absX;
        _lastAbsY = _absY;
        _haveLastAbs = true;
    }

    private void ProcessButton(
        in LinuxInputEvent inputEvent,
        Func<InputTimestamp, InputEventHeader> createHeader,
        ICollection<LinuxMouseTranslatedEvent> output)
    {
        if (inputEvent.Value is not (0 or 1))
            return;

        // BTN_TOUCH is contact state on a touchpad, not a clickable button. It
        // drives motion tracking and tap-to-click, but is never a mouse button.
        if (inputEvent.Code == LinuxEvdevConstants.BtnTouch)
        {
            HandleTouchContact(inputEvent, createHeader, output);
            return;
        }

        MouseButton button = ButtonFromEvdevCode(inputEvent.Code);
        if (button == MouseButton.None)
            return;

        MouseButtons flag = ButtonFlag(button);
        MouseButtonTransition transition = inputEvent.Value == 0 ? MouseButtonTransition.Up : MouseButtonTransition.Down;
        if (transition == MouseButtonTransition.Down)
        {
            _buttons |= flag;
            if (_touching)
                _physicalButtonDuringTouch = true;
        }
        else
        {
            _buttons &= ~flag;
        }

        output.Add(LinuxMouseTranslatedEvent.FromButton(new MouseButtonEvent(
            createHeader(inputEvent.Timestamp),
            RawPoint(0, 0),
            _buttons,
            button,
            transition,
            InputEventSource.Raw)));
    }

    private void HandleTouchContact(
        in LinuxInputEvent inputEvent,
        Func<InputTimestamp, InputEventHeader> createHeader,
        ICollection<LinuxMouseTranslatedEvent> output)
    {
        if (inputEvent.Value == 1)
        {
            _touching = true;
            _haveLastAbs = false;
            _carryX = 0;
            _carryY = 0;
            _touchMovementPixels = 0;
            _physicalButtonDuringTouch = false;
            _touchStartMicroseconds = inputEvent.Timestamp.Ticks;
            return;
        }

        // Contact released: emit a synthetic left click for a brief, still tap.
        bool wasTap =
            !_physicalButtonDuringTouch &&
            _touchMovementPixels <= TapMaxMovementPixels &&
            (inputEvent.Timestamp.Ticks - _touchStartMicroseconds) <= TapMaxDurationMicroseconds;

        _touching = false;
        _haveLastAbs = false;

        if (wasTap)
            EmitClick(inputEvent.Timestamp, createHeader, output);
    }

    private void EmitClick(
        InputTimestamp timestamp,
        Func<InputTimestamp, InputEventHeader> createHeader,
        ICollection<LinuxMouseTranslatedEvent> output)
    {
        _buttons |= MouseButtons.Left;
        output.Add(LinuxMouseTranslatedEvent.FromButton(new MouseButtonEvent(
            createHeader(timestamp),
            RawPoint(0, 0),
            _buttons,
            MouseButton.Left,
            MouseButtonTransition.Down,
            InputEventSource.Raw)));

        _buttons &= ~MouseButtons.Left;
        output.Add(LinuxMouseTranslatedEvent.FromButton(new MouseButtonEvent(
            createHeader(timestamp),
            RawPoint(0, 0),
            _buttons,
            MouseButton.Left,
            MouseButtonTransition.Up,
            InputEventSource.Raw)));
    }

    private void Flush(
        InputTimestamp timestamp,
        Func<InputTimestamp, InputEventHeader> createHeader,
        MouseOpenOptions options,
        ICollection<LinuxMouseTranslatedEvent> output)
    {
        if (options.ReceiveMovement && (_pendingX != 0 || _pendingY != 0))
        {
            output.Add(LinuxMouseTranslatedEvent.FromMove(new MouseMoveEvent(
                createHeader(timestamp),
                RawPoint(_pendingX, _pendingY),
                _buttons,
                InputEventSource.Raw)));
        }

        if (options.ReceiveWheel)
        {
            double verticalNotches = _pendingVerticalWheelHiRes != 0
                ? _pendingVerticalWheelHiRes / (double)LinuxEvdevConstants.WheelHiResUnitsPerDetent
                : _pendingVerticalWheel;
            double horizontalNotches = _pendingHorizontalWheelHiRes != 0
                ? _pendingHorizontalWheelHiRes / (double)LinuxEvdevConstants.WheelHiResUnitsPerDetent
                : _pendingHorizontalWheel;

            if (verticalNotches != 0)
            {
                output.Add(LinuxMouseTranslatedEvent.FromWheel(new MouseWheelEvent(
                    createHeader(timestamp),
                    RawPoint(0, 0),
                    _buttons,
                    MouseWheelAxis.Vertical,
                    verticalNotches,
                    InputEventSource.Raw)));
            }

            if (horizontalNotches != 0)
            {
                output.Add(LinuxMouseTranslatedEvent.FromWheel(new MouseWheelEvent(
                    createHeader(timestamp),
                    RawPoint(0, 0),
                    _buttons,
                    MouseWheelAxis.Horizontal,
                    horizontalNotches,
                    InputEventSource.Raw)));
            }
        }

        ResetPending();
    }

    private void ResetPending()
    {
        _pendingX = 0;
        _pendingY = 0;
        _pendingVerticalWheel = 0;
        _pendingHorizontalWheel = 0;
        _pendingVerticalWheelHiRes = 0;
        _pendingHorizontalWheelHiRes = 0;
    }

    private static InputPoint RawPoint(double x, double y) => new(x, y, "raw-relative-counts");

    private static MouseButton ButtonFromEvdevCode(int code) =>
        code switch
        {
            LinuxEvdevConstants.BtnLeft => MouseButton.Left,
            LinuxEvdevConstants.BtnRight => MouseButton.Right,
            LinuxEvdevConstants.BtnMiddle => MouseButton.Middle,
            LinuxEvdevConstants.BtnSide => MouseButton.X1,
            LinuxEvdevConstants.BtnExtra => MouseButton.X2,
            _ => MouseButton.None,
        };

    private static MouseButtons ButtonFlag(MouseButton button) =>
        button switch
        {
            MouseButton.Left => MouseButtons.Left,
            MouseButton.Right => MouseButtons.Right,
            MouseButton.Middle => MouseButtons.Middle,
            MouseButton.X1 => MouseButtons.X1,
            MouseButton.X2 => MouseButtons.X2,
            _ => MouseButtons.None,
        };
}
