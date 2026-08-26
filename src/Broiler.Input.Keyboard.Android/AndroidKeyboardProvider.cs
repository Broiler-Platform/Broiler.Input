using System;
using Broiler.Input.Android;

namespace Broiler.Input.Keyboard.Android;

/// <summary>
/// Opens <see cref="AndroidKeyboardInputDevice"/> instances for keyboards the host has registered.
/// </summary>
public sealed class AndroidKeyboardProvider :
    AndroidInputProvider<KeyboardInputDevice, KeyboardOpenOptions>,
    IKeyboardInputProvider
{
    public AndroidKeyboardProvider(AndroidUptimeInputClock? clock = null)
        : base(clock)
    {
    }

    /// <summary>
    /// Registers the logical keyboard that carries virtual keys from the soft keyboard and any
    /// unattributed key event. A host that tracks physical keyboards through
    /// <c>InputManager.InputDeviceListener</c> registers those separately so hot-plug is visible.
    /// </summary>
    public InputDeviceDescriptor RegisterDefaultKeyboard()
    {
        InputDeviceDescriptor descriptor = AndroidInputDescriptors.Keyboard();
        RegisterDevice(descriptor);
        return descriptor;
    }

    protected override KeyboardInputDevice CreateDevice(InputDeviceDescriptor descriptor, KeyboardOpenOptions options)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        return new AndroidKeyboardInputDevice(descriptor, Clock, options);
    }
}
