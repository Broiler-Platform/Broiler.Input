using System;

namespace Broiler.Input.Pen;

public abstract class PenInputDevice : InputDevice
{
    protected PenInputDevice(InputDeviceDescriptor descriptor, IInputClock? clock = null)
        : base(descriptor, clock)
    {
        if (descriptor.Kind != InputKind.Pen)
            throw new ArgumentException("Pen devices require a pen descriptor.", nameof(descriptor));
    }

    public event Action<PenContactEvent>? ContactChanged;

    protected void RaiseContactChanged(PenContactEvent inputEvent)
    {
        if (CanDeliverInput)
            ContactChanged?.Invoke(inputEvent);
    }
}

