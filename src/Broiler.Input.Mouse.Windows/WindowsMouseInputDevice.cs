using System;
using Broiler.Input.Windows;

namespace Broiler.Input.Mouse.Windows;

public sealed class WindowsMouseInputDevice : MouseInputDevice, IWindowsInputMessageSink
{
    private const int MkLeftButton = 0x0001;
    private const int MkRightButton = 0x0002;
    private const int MkMiddleButton = 0x0010;
    private const int MkXButton1 = 0x0020;
    private const int MkXButton2 = 0x0040;

    // WM_*BUTTON* and WM_MOUSEMOVE report Shift and Ctrl in the same wParam word as
    // the button state. Alt is not among them: Windows delivers it to the window as
    // WM_SYSCOMMAND rather than in the mouse message, so an Alt-click reports no
    // modifier here and a consumer that needs it must track the keyboard.
    private const int MkShift = 0x0004;
    private const int MkControl = 0x0008;

    private const int XButton1 = 0x0001;
    private const int XButton2 = 0x0002;
    private const int WheelDelta = 120;

    private readonly MouseOpenOptions _options;
    private readonly WindowsMouseMessageOptions _messageOptions;

    public WindowsMouseInputDevice(
        InputDeviceDescriptor descriptor,
        MouseOpenOptions? options = null,
        IInputClock? clock = null,
        WindowsMouseMessageOptions? messageOptions = null)
        : base(descriptor, clock ?? WindowsInputClock.Shared)
    {
        _options = options ?? new MouseOpenOptions();
        _messageOptions = messageOptions ?? new WindowsMouseMessageOptions();
    }

    public bool ProcessMessage(in WindowsInputMessage message) => ProcessMessage(message, _messageOptions);

    public bool ProcessMessage(in WindowsInputMessage message, WindowsMouseMessageOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        switch (message.Message)
        {
            case WindowsMessageIds.MouseMove:
                if (!_options.ReceiveMovement)
                    return false;

                RaiseMoved(new MouseMoveEvent(
                    NextEventHeader(message.Timestamp),
                    PositionFromLParam(message.LParam, options),
                    ButtonsFromWParam(message.WParam),
                    InputEventSource.Semantic,
                    ModifiersFromWParam(message.WParam)));
                return true;

            case WindowsMessageIds.LeftButtonDown:
            case WindowsMessageIds.RightButtonDown:
            case WindowsMessageIds.MiddleButtonDown:
            case WindowsMessageIds.XButtonDown:
                DispatchButton(message, options, MouseButtonTransition.Down);
                return true;

            case WindowsMessageIds.LeftButtonUp:
            case WindowsMessageIds.RightButtonUp:
            case WindowsMessageIds.MiddleButtonUp:
            case WindowsMessageIds.XButtonUp:
                DispatchButton(message, options, MouseButtonTransition.Up);
                return true;

            case WindowsMessageIds.MouseWheel:
                if (!_options.ReceiveWheel)
                    return false;

                DispatchWheel(message, options, MouseWheelAxis.Vertical);
                return true;

            case WindowsMessageIds.MouseHorizontalWheel:
                if (!_options.ReceiveWheel)
                    return false;

                DispatchWheel(message, options, MouseWheelAxis.Horizontal);
                return true;

            case WindowsMessageIds.MouseLeave:
                RaiseLeft(new MouseLeaveEvent(
                    NextEventHeader(message.Timestamp),
                    ButtonsFromWParam(message.WParam),
                    InputEventSource.Semantic));
                return true;

            case WindowsMessageIds.CaptureChanged:
                RaiseCaptureLost(new MouseCaptureLostEvent(
                    NextEventHeader(message.Timestamp),
                    null,
                    InputEventSource.Semantic));
                return true;

            default:
                return false;
        }
    }

    private void DispatchButton(
        in WindowsInputMessage message,
        WindowsMouseMessageOptions options,
        MouseButtonTransition transition)
    {
        RaiseButtonChanged(new MouseButtonEvent(
            NextEventHeader(message.Timestamp),
            PositionFromLParam(message.LParam, options),
            ButtonsFromWParam(message.WParam),
            ButtonFromMessage(message.Message, message.WParam),
            transition,
            InputEventSource.Semantic,
            ModifiersFromWParam(message.WParam)));
    }

    private void DispatchWheel(
        in WindowsInputMessage message,
        WindowsMouseMessageOptions options,
        MouseWheelAxis axis)
    {
        double delta = SignedHighWord(message.WParam) / (double)WheelDelta;

        RaiseWheelChanged(new MouseWheelEvent(
            NextEventHeader(message.Timestamp),
            WheelPositionFromLParam(message, options),
            ButtonsFromWParam(message.WParam),
            axis,
            delta,
            InputEventSource.Semantic,
            ModifiersFromWParam(message.WParam)));
    }

    private static InputPoint PositionFromLParam(IntPtr lParam, WindowsMouseMessageOptions options) =>
        ToPoint(SignedLowWord(lParam), SignedHighWord(lParam), options);

    private static InputPoint WheelPositionFromLParam(
        in WindowsInputMessage message,
        WindowsMouseMessageOptions options)
    {
        int x = SignedLowWord(message.LParam);
        int y = SignedHighWord(message.LParam);

        if (options.ConvertWheelScreenPointToClient && message.Hwnd != IntPtr.Zero)
        {
            var point = new WindowsMouseNativeMethods.POINT { X = x, Y = y };
            if (WindowsMouseNativeMethods.ScreenToClient(message.Hwnd, ref point))
            {
                x = point.X;
                y = point.Y;
            }
        }

        return ToPoint(x, y, options);
    }

    private static InputPoint ToPoint(int x, int y, WindowsMouseMessageOptions options)
    {
        double scale = options.CoordinateScale <= 0 ? 1.0 : options.CoordinateScale;
        return new InputPoint(x / scale, y / scale, string.IsNullOrWhiteSpace(options.CoordinateSpace) ? "client-pixels" : options.CoordinateSpace);
    }

    private static InputModifiers ModifiersFromWParam(IntPtr wParam)
    {
        int keys = LowWord(wParam);
        InputModifiers modifiers = InputModifiers.None;

        if ((keys & MkShift) != 0)
            modifiers |= InputModifiers.Shift;
        if ((keys & MkControl) != 0)
            modifiers |= InputModifiers.Control;

        return modifiers;
    }

    private static MouseButtons ButtonsFromWParam(IntPtr wParam)
    {
        int keys = LowWord(wParam);
        MouseButtons buttons = MouseButtons.None;

        if ((keys & MkLeftButton) != 0)
            buttons |= MouseButtons.Left;
        if ((keys & MkRightButton) != 0)
            buttons |= MouseButtons.Right;
        if ((keys & MkMiddleButton) != 0)
            buttons |= MouseButtons.Middle;
        if ((keys & MkXButton1) != 0)
            buttons |= MouseButtons.X1;
        if ((keys & MkXButton2) != 0)
            buttons |= MouseButtons.X2;

        return buttons;
    }

    private static MouseButton ButtonFromMessage(uint message, IntPtr wParam) => message switch
    {
        WindowsMessageIds.LeftButtonDown or WindowsMessageIds.LeftButtonUp => MouseButton.Left,
        WindowsMessageIds.RightButtonDown or WindowsMessageIds.RightButtonUp => MouseButton.Right,
        WindowsMessageIds.MiddleButtonDown or WindowsMessageIds.MiddleButtonUp => MouseButton.Middle,
        WindowsMessageIds.XButtonDown or WindowsMessageIds.XButtonUp => HighWord(wParam) switch
        {
            XButton1 => MouseButton.X1,
            XButton2 => MouseButton.X2,
            _ => MouseButton.None,
        },
        _ => MouseButton.None,
    };

    private static int LowWord(IntPtr value) => unchecked((ushort)((long)value & 0xFFFF));

    private static int HighWord(IntPtr value) => unchecked((ushort)(((long)value >> 16) & 0xFFFF));

    private static int SignedLowWord(IntPtr value) => unchecked((short)((long)value & 0xFFFF));

    private static int SignedHighWord(IntPtr value) => unchecked((short)(((long)value >> 16) & 0xFFFF));
}
