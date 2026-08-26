using System;
using Broiler.Input.Keyboard;
using Broiler.Input.Mouse;

namespace Broiler.Input.Legacy;

public sealed class LegacyWindowInputAdapter : IDisposable
{
    private readonly KeyboardInputDevice? _keyboard;
    private readonly MouseInputDevice? _mouse;
    private readonly Action<KeyboardKeyEvent> _keyHandler;
    private readonly Action<KeyboardTextEvent> _textHandler;
    private readonly Action<MouseMoveEvent> _moveHandler;
    private readonly Action<MouseButtonEvent> _buttonHandler;
    private readonly Action<MouseWheelEvent> _wheelHandler;
    private readonly Action<MouseLeaveEvent> _leaveHandler;
    private readonly Action<MouseCaptureLostEvent> _captureLostHandler;
    private bool _disposed;

    public LegacyWindowInputAdapter(KeyboardInputDevice? keyboard, MouseInputDevice? mouse)
    {
        _keyboard = keyboard;
        _mouse = mouse;
        _keyHandler = OnKeyChanged;
        _textHandler = OnTextInput;
        _moveHandler = OnMouseMoved;
        _buttonHandler = OnMouseButtonChanged;
        _wheelHandler = OnMouseWheelChanged;
        _leaveHandler = OnMouseLeft;
        _captureLostHandler = OnMouseCaptureLost;

        if (_keyboard is not null)
        {
            _keyboard.KeyChanged += _keyHandler;
            _keyboard.TextInput += _textHandler;
        }

        if (_mouse is not null)
        {
            _mouse.Moved += _moveHandler;
            _mouse.ButtonChanged += _buttonHandler;
            _mouse.WheelChanged += _wheelHandler;
            _mouse.Left += _leaveHandler;
            _mouse.CaptureLost += _captureLostHandler;
        }
    }

    public event Action<LegacyPointerEvent>? PointerDown;

    public event Action<LegacyPointerEvent>? PointerMove;

    public event Action<LegacyPointerEvent>? PointerUp;

    public event Action? PointerLeave;

    public event Action? PointerCaptureLost;

    public event Action<LegacyMouseWheelEvent>? MouseWheel;

    public event Action<LegacyKeyEvent>? KeyDown;

    public event Action<LegacyKeyEvent>? KeyUp;

    public event Action<LegacyTextInputEvent>? TextInput;

    public void Dispose()
    {
        if (_disposed)
            return;

        if (_keyboard is not null)
        {
            _keyboard.KeyChanged -= _keyHandler;
            _keyboard.TextInput -= _textHandler;
        }

        if (_mouse is not null)
        {
            _mouse.Moved -= _moveHandler;
            _mouse.ButtonChanged -= _buttonHandler;
            _mouse.WheelChanged -= _wheelHandler;
            _mouse.Left -= _leaveHandler;
            _mouse.CaptureLost -= _captureLostHandler;
        }

        _disposed = true;
    }

    private void OnKeyChanged(KeyboardKeyEvent inputEvent)
    {
        LegacyKeyEvent legacyEvent = new(
            inputEvent.NativeKeyCode,
            (inputEvent.Modifiers & KeyboardModifierState.Control) != 0,
            (inputEvent.Modifiers & KeyboardModifierState.Shift) != 0,
            (inputEvent.Modifiers & KeyboardModifierState.Alt) != 0,
            inputEvent.ScanCode,
            inputEvent.Location,
            inputEvent.IsSystemKey);

        if (inputEvent.Transition == KeyboardKeyTransition.Down)
            KeyDown?.Invoke(legacyEvent);
        else
            KeyUp?.Invoke(legacyEvent);
    }

    private void OnTextInput(KeyboardTextEvent inputEvent)
    {
        TextInput?.Invoke(new LegacyTextInputEvent(inputEvent.Text));
    }

    private void OnMouseMoved(MouseMoveEvent inputEvent)
    {
        PointerMove?.Invoke(new LegacyPointerEvent(inputEvent.Position, inputEvent.Buttons));
    }

    private void OnMouseButtonChanged(MouseButtonEvent inputEvent)
    {
        LegacyPointerEvent legacyEvent = new(inputEvent.Position, inputEvent.Buttons, inputEvent.Button);
        if (inputEvent.Transition == MouseButtonTransition.Down)
            PointerDown?.Invoke(legacyEvent);
        else
            PointerUp?.Invoke(legacyEvent);
    }

    private void OnMouseWheelChanged(MouseWheelEvent inputEvent)
    {
        MouseWheel?.Invoke(new LegacyMouseWheelEvent(
            inputEvent.Position,
            inputEvent.DeltaNotches,
            inputEvent.Buttons,
            inputEvent.Axis));
    }

    private void OnMouseLeft(MouseLeaveEvent inputEvent)
    {
        PointerLeave?.Invoke();
    }

    private void OnMouseCaptureLost(MouseCaptureLostEvent inputEvent)
    {
        PointerCaptureLost?.Invoke();
    }
}
