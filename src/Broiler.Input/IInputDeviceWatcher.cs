using System;

namespace Broiler.Input;

public interface IInputDeviceWatcher
{
    event Action<InputDeviceChange>? DeviceChanged;
}
