using System;

namespace Broiler.Input.Touch;

public abstract class TouchInputDevice : InputDevice
{
    protected TouchInputDevice(InputDeviceDescriptor descriptor, IInputClock? clock = null)
        : base(descriptor, clock)
    {
        if (descriptor.Kind != InputKind.Touch)
            throw new ArgumentException("Touch devices require a touch descriptor.", nameof(descriptor));
    }

    public event Action<TouchContactEvent>? ContactChanged;

    protected void RaiseContactChanged(TouchContactEvent inputEvent)
    {
        if (CanDeliverInput)
            ContactChanged?.Invoke(inputEvent);
    }
}

