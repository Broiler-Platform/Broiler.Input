using System;

namespace Broiler.Input.Mouse;

public abstract class MouseInputDevice : InputDevice
{
    protected MouseInputDevice(InputDeviceDescriptor descriptor, IInputClock? clock = null)
        : base(descriptor, clock)
    {
        if (descriptor.Kind != InputKind.Mouse)
            throw new ArgumentException("Mouse devices require a mouse descriptor.", nameof(descriptor));
    }

    public event Action<MouseMoveEvent>? Moved;

    public event Action<MouseButtonEvent>? ButtonChanged;

    public event Action<MouseWheelEvent>? WheelChanged;

    public event Action<MouseLeaveEvent>? Left;

    public event Action<MouseCaptureLostEvent>? CaptureLost;

    protected void RaiseMoved(MouseMoveEvent inputEvent)
    {
        if (CanDeliverInput)
            Moved?.Invoke(inputEvent);
    }

    protected void RaiseButtonChanged(MouseButtonEvent inputEvent)
    {
        if (CanDeliverInput)
            ButtonChanged?.Invoke(inputEvent);
    }

    protected void RaiseWheelChanged(MouseWheelEvent inputEvent)
    {
        if (CanDeliverInput)
            WheelChanged?.Invoke(inputEvent);
    }

    protected void RaiseLeft(MouseLeaveEvent inputEvent)
    {
        if (CanDeliverInput)
            Left?.Invoke(inputEvent);
    }

    protected void RaiseCaptureLost(MouseCaptureLostEvent inputEvent)
    {
        if (CanDeliverInput)
            CaptureLost?.Invoke(inputEvent);
    }
}
