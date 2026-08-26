using System;

namespace Broiler.Input.Keyboard;

public abstract class KeyboardInputDevice : InputDevice
{
    protected KeyboardInputDevice(InputDeviceDescriptor descriptor, IInputClock? clock = null)
        : base(descriptor, clock)
    {
        if (descriptor.Kind != InputKind.Keyboard)
            throw new ArgumentException("Keyboard devices require a keyboard descriptor.", nameof(descriptor));
    }

    public event Action<KeyboardKeyEvent>? KeyChanged;

    public event Action<KeyboardTextEvent>? TextInput;

    public event Action<KeyboardDeadKeyEvent>? DeadKeyInput;

    public event Action<KeyboardCompositionEvent>? CompositionChanged;

    public event Action<KeyboardLayoutChangedEvent>? LayoutChanged;

    protected void RaiseKeyChanged(KeyboardKeyEvent inputEvent)
    {
        if (CanDeliverInput)
            KeyChanged?.Invoke(inputEvent);
    }

    protected void RaiseTextInput(KeyboardTextEvent inputEvent)
    {
        if (CanDeliverInput)
            TextInput?.Invoke(inputEvent);
    }

    protected void RaiseDeadKeyInput(KeyboardDeadKeyEvent inputEvent)
    {
        if (CanDeliverInput)
            DeadKeyInput?.Invoke(inputEvent);
    }

    protected void RaiseCompositionChanged(KeyboardCompositionEvent inputEvent)
    {
        if (CanDeliverInput)
            CompositionChanged?.Invoke(inputEvent);
    }

    protected void RaiseLayoutChanged(KeyboardLayoutChangedEvent inputEvent)
    {
        if (CanDeliverInput)
            LayoutChanged?.Invoke(inputEvent);
    }
}
