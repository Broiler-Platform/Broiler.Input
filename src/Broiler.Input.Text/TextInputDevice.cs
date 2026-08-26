using System;

namespace Broiler.Input.Text;

public abstract class TextInputDevice : InputDevice
{
    protected TextInputDevice(InputDeviceDescriptor descriptor, IInputClock? clock = null)
        : base(descriptor, clock)
    {
        if (descriptor.Kind != InputKind.Keyboard)
            throw new ArgumentException("Text input devices currently use keyboard descriptors.", nameof(descriptor));
    }

    public event Action<TextInputEvent>? TextInput;

    public event Action<TextCompositionEvent>? CompositionChanged;

    protected void RaiseTextInput(TextInputEvent inputEvent)
    {
        if (CanDeliverInput)
            TextInput?.Invoke(inputEvent);
    }

    protected void RaiseCompositionChanged(TextCompositionEvent inputEvent)
    {
        if (CanDeliverInput)
            CompositionChanged?.Invoke(inputEvent);
    }
}

