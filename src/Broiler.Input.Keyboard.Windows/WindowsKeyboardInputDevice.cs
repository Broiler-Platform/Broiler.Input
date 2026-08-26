using System;
using System.Globalization;
using Broiler.Input.Windows;

namespace Broiler.Input.Keyboard.Windows;

public sealed class WindowsKeyboardInputDevice : KeyboardInputDevice, IWindowsInputMessageSink
{
    private const int VkBack = 0x08;
    private const int VkTab = 0x09;
    private const int VkReturn = 0x0D;
    private const int VkShift = 0x10;
    private const int VkControl = 0x11;
    private const int VkMenu = 0x12;
    private const int VkPause = 0x13;
    private const int VkCapsLock = 0x14;
    private const int VkEscape = 0x1B;
    private const int VkSpace = 0x20;
    private const int VkPageUp = 0x21;
    private const int VkPageDown = 0x22;
    private const int VkEnd = 0x23;
    private const int VkHome = 0x24;
    private const int VkLeft = 0x25;
    private const int VkUp = 0x26;
    private const int VkRight = 0x27;
    private const int VkDown = 0x28;
    private const int VkInsert = 0x2D;
    private const int VkDelete = 0x2E;
    private const int VkLWin = 0x5B;
    private const int VkRWin = 0x5C;
    private const int VkNumpad0 = 0x60;
    private const int VkNumpad9 = 0x69;
    private const int VkLShift = 0xA0;
    private const int VkRShift = 0xA1;
    private const int VkLControl = 0xA2;
    private const int VkRControl = 0xA3;
    private const int VkLMenu = 0xA4;
    private const int VkRMenu = 0xA5;
    private const int VkF1 = 0x70;
    private const int VkF24 = 0x87;

    private readonly KeyboardOpenOptions _options;
    private char? _pendingHighSurrogate;
    private InputTimestamp _pendingHighSurrogateTimestamp;

    public WindowsKeyboardInputDevice(
        InputDeviceDescriptor descriptor,
        KeyboardOpenOptions? options = null,
        IInputClock? clock = null)
        : base(descriptor, clock ?? WindowsInputClock.Shared)
    {
        _options = options ?? new KeyboardOpenOptions();
    }

    public bool ProcessMessage(in WindowsInputMessage message)
    {
        switch (message.Message)
        {
            case WindowsMessageIds.KeyDown:
                DispatchKey(message, KeyboardKeyTransition.Down, isSystemKey: false);
                return true;

            case WindowsMessageIds.SysKeyDown:
                DispatchKey(message, KeyboardKeyTransition.Down, isSystemKey: true);
                return _options.ConsumeSystemKeyMessages;

            case WindowsMessageIds.KeyUp:
                DispatchKey(message, KeyboardKeyTransition.Up, isSystemKey: false);
                return true;

            case WindowsMessageIds.SysKeyUp:
                DispatchKey(message, KeyboardKeyTransition.Up, isSystemKey: true);
                return _options.ConsumeSystemKeyMessages;

            case WindowsMessageIds.Char:
                if (_options.ReceiveText)
                {
                    DispatchText(message);
                    return true;
                }

                return false;

            case WindowsMessageIds.DeadChar:
                if (_options.ReceiveText)
                {
                    DispatchDeadKey(message, isSystemKey: false);
                    return true;
                }

                return false;

            case WindowsMessageIds.SysDeadChar:
                if (_options.ReceiveText)
                {
                    DispatchDeadKey(message, isSystemKey: true);
                    return _options.ConsumeSystemKeyMessages;
                }

                return false;

            case WindowsMessageIds.InputLanguageChange:
                DispatchLayoutChanged(message);
                return false;

            case WindowsMessageIds.ImeStartComposition:
                RaiseCompositionChanged(new KeyboardCompositionEvent(
                    NextEventHeader(message.Timestamp),
                    KeyboardCompositionState.Started,
                    Detail: "Windows IME composition started."));
                return false;

            case WindowsMessageIds.ImeComposition:
                RaiseCompositionChanged(new KeyboardCompositionEvent(
                    NextEventHeader(message.Timestamp),
                    KeyboardCompositionState.Unsupported,
                    Detail: "Windows IME composition details are not decoded in this milestone."));
                return false;

            case WindowsMessageIds.ImeEndComposition:
                RaiseCompositionChanged(new KeyboardCompositionEvent(
                    NextEventHeader(message.Timestamp),
                    KeyboardCompositionState.Cancelled,
                    Detail: "Windows IME composition ended."));
                return false;

            default:
                return false;
        }
    }

    private void DispatchKey(in WindowsInputMessage message, KeyboardKeyTransition transition, bool isSystemKey)
    {
        int virtualKey = unchecked((int)(long)message.WParam);
        long parameter = message.LParam.ToInt64();
        int repeatCount = Math.Max(1, LowWord(parameter));
        int scanCode = (int)((parameter >> 16) & 0xFF);
        bool isExtended = ((parameter >> 24) & 0x01) != 0;
        bool wasDown = ((parameter >> 30) & 0x01) != 0;

        RaiseKeyChanged(new KeyboardKeyEvent(
            NextEventHeader(message.Timestamp),
            KeyFromVirtualKey(virtualKey),
            transition,
            ReadModifiers(),
            virtualKey,
            scanCode,
            repeatCount,
            isExtended,
            wasDown,
            LocationFromVirtualKey(virtualKey, scanCode, isExtended),
            InputEventSource.Semantic,
            isSystemKey));
    }

    private void DispatchText(in WindowsInputMessage message)
    {
        int codeUnit = unchecked((ushort)(long)message.WParam);
        if (codeUnit == 0)
            return;

        DispatchTextCodeUnit((char)codeUnit, message.Timestamp);
    }

    private void DispatchDeadKey(in WindowsInputMessage message, bool isSystemKey)
    {
        int codeUnit = unchecked((ushort)(long)message.WParam);
        if (codeUnit == 0)
            return;

        FlushPendingSurrogateAsReplacement(message.Timestamp);
        RaiseDeadKeyInput(new KeyboardDeadKeyEvent(
            NextEventHeader(message.Timestamp),
            new string((char)codeUnit, 1),
            codeUnit,
            isSystemKey,
            InputEventSource.Semantic));
    }

    private void DispatchLayoutChanged(in WindowsInputMessage message)
    {
        FlushPendingSurrogateAsReplacement(message.Timestamp);
        RaiseLayoutChanged(new KeyboardLayoutChangedEvent(
            NextEventHeader(message.Timestamp),
            message.LParam,
            unchecked((int)(long)message.WParam),
            message.LParam.ToInt64().ToString("X", CultureInfo.InvariantCulture),
            InputEventSource.Semantic));
    }

    private void DispatchTextCodeUnit(char codeUnit, InputTimestamp timestamp)
    {
        if (char.IsHighSurrogate(codeUnit))
        {
            FlushPendingSurrogateAsReplacement(timestamp);
            _pendingHighSurrogate = codeUnit;
            _pendingHighSurrogateTimestamp = timestamp;
            return;
        }

        if (char.IsLowSurrogate(codeUnit))
        {
            if (_pendingHighSurrogate is char high)
            {
                _pendingHighSurrogate = null;
                EmitText(new string([high, codeUnit]), _pendingHighSurrogateTimestamp);
            }
            else
            {
                EmitText("\uFFFD", timestamp);
            }

            return;
        }

        FlushPendingSurrogateAsReplacement(timestamp);
        EmitText(new string(codeUnit, 1), timestamp);
    }

    private void FlushPendingSurrogateAsReplacement(InputTimestamp timestamp)
    {
        if (_pendingHighSurrogate is null)
            return;

        _pendingHighSurrogate = null;
        EmitText("\uFFFD", timestamp);
    }

    private void EmitText(string text, InputTimestamp timestamp)
    {
        RaiseTextInput(new KeyboardTextEvent(NextEventHeader(timestamp), text, false, InputEventSource.Semantic));
    }

    private static KeyboardModifierState ReadModifiers()
    {
        KeyboardModifierState modifiers = KeyboardModifierState.None;

        if (IsKeyDown(VkShift))
            modifiers |= KeyboardModifierState.Shift;
        if (IsKeyDown(VkControl))
            modifiers |= KeyboardModifierState.Control;
        if (IsKeyDown(VkMenu))
            modifiers |= KeyboardModifierState.Alt;
        if (IsKeyDown(VkLShift))
            modifiers |= KeyboardModifierState.Shift | KeyboardModifierState.LeftShift;
        if (IsKeyDown(VkRShift))
            modifiers |= KeyboardModifierState.Shift | KeyboardModifierState.RightShift;
        if (IsKeyDown(VkLControl))
            modifiers |= KeyboardModifierState.Control | KeyboardModifierState.LeftControl;
        if (IsKeyDown(VkRControl))
            modifiers |= KeyboardModifierState.Control | KeyboardModifierState.RightControl;
        if (IsKeyDown(VkLMenu))
            modifiers |= KeyboardModifierState.Alt | KeyboardModifierState.LeftAlt;
        if (IsKeyDown(VkRMenu))
            modifiers |= KeyboardModifierState.Alt | KeyboardModifierState.RightAlt;
        if (IsKeyDown(VkLWin))
            modifiers |= KeyboardModifierState.LeftWindows;
        if (IsKeyDown(VkRWin))
            modifiers |= KeyboardModifierState.RightWindows;

        return modifiers;
    }

    private static bool IsKeyDown(int virtualKey) =>
        (WindowsKeyboardNativeMethods.GetKeyState(virtualKey) & unchecked((short)0x8000)) != 0;

    private static KeyboardKey KeyFromVirtualKey(int virtualKey)
    {
        if (virtualKey >= 0x30 && virtualKey <= 0x39)
            return KeyboardKey.FromName("Digit" + (char)virtualKey);

        if (virtualKey >= 0x41 && virtualKey <= 0x5A)
            return KeyboardKey.FromName("Key" + (char)virtualKey);

        if (virtualKey >= VkF1 && virtualKey <= VkF24)
            return KeyboardKey.FromName("F" + (virtualKey - VkF1 + 1).ToString(CultureInfo.InvariantCulture));

        return virtualKey switch
        {
            VkBack => KeyboardKey.FromName("Backspace"),
            VkTab => KeyboardKey.FromName("Tab"),
            VkReturn => KeyboardKey.FromName("Enter"),
            VkPause => KeyboardKey.FromName("Pause"),
            VkCapsLock => KeyboardKey.FromName("CapsLock"),
            VkEscape => KeyboardKey.FromName("Escape"),
            VkSpace => KeyboardKey.FromName("Space"),
            VkPageUp => KeyboardKey.FromName("PageUp"),
            VkPageDown => KeyboardKey.FromName("PageDown"),
            VkEnd => KeyboardKey.FromName("End"),
            VkHome => KeyboardKey.FromName("Home"),
            VkLeft => KeyboardKey.FromName("ArrowLeft"),
            VkUp => KeyboardKey.FromName("ArrowUp"),
            VkRight => KeyboardKey.FromName("ArrowRight"),
            VkDown => KeyboardKey.FromName("ArrowDown"),
            VkInsert => KeyboardKey.FromName("Insert"),
            VkDelete => KeyboardKey.FromName("Delete"),
            VkShift => KeyboardKey.FromName("Shift"),
            VkControl => KeyboardKey.FromName("Control"),
            VkMenu => KeyboardKey.FromName("Alt"),
            VkLShift => KeyboardKey.FromName("ShiftLeft"),
            VkRShift => KeyboardKey.FromName("ShiftRight"),
            VkLControl => KeyboardKey.FromName("ControlLeft"),
            VkRControl => KeyboardKey.FromName("ControlRight"),
            VkLMenu => KeyboardKey.FromName("AltLeft"),
            VkRMenu => KeyboardKey.FromName("AltRight"),
            VkLWin => KeyboardKey.FromName("MetaLeft"),
            VkRWin => KeyboardKey.FromName("MetaRight"),
            _ => KeyboardKey.FromName("VK_" + virtualKey.ToString("X2", CultureInfo.InvariantCulture)),
        };
    }

    private static KeyboardKeyLocation LocationFromVirtualKey(int virtualKey, int scanCode, bool isExtended) =>
        virtualKey switch
        {
            VkShift => scanCode == 0x36 ? KeyboardKeyLocation.Right : KeyboardKeyLocation.Left,
            VkControl => isExtended ? KeyboardKeyLocation.Right : KeyboardKeyLocation.Left,
            VkMenu => isExtended ? KeyboardKeyLocation.Right : KeyboardKeyLocation.Left,
            VkLWin => KeyboardKeyLocation.Left,
            VkRWin => KeyboardKeyLocation.Right,
            VkLShift or VkLControl or VkLMenu => KeyboardKeyLocation.Left,
            VkRShift or VkRControl or VkRMenu => KeyboardKeyLocation.Right,
            _ when virtualKey >= VkNumpad0 && virtualKey <= VkNumpad9 => KeyboardKeyLocation.Numpad,
            _ => KeyboardKeyLocation.Standard,
        };

    private static int LowWord(long value) => unchecked((ushort)(value & 0xFFFF));
}
