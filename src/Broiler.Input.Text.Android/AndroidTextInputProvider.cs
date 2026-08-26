using System;
using Broiler.Input.Android;

namespace Broiler.Input.Text.Android;

/// <summary>
/// Opens <see cref="AndroidTextInputDevice"/> instances for the Android input method.
/// </summary>
public sealed class AndroidTextInputProvider :
    AndroidInputProvider<TextInputDevice, TextInputOpenOptions>,
    ITextInputProvider
{
    public AndroidTextInputProvider(AndroidUptimeInputClock? clock = null)
        : base(clock)
    {
    }

    /// <summary>
    /// The editor an input method queries. Assigning it after devices are open updates them, so a
    /// host can attach the editor when focus moves rather than when the device is created.
    /// </summary>
    public IAndroidEditorTextSource? EditorTextSource
    {
        get;
        set
        {
            field = value;
            if (TryGetOpenDevice(AndroidInputDescriptors.Text().Id, out TextInputDevice? device) &&
                device is AndroidTextInputDevice androidDevice)
            {
                androidDevice.EditorTextSource = value;
            }
        }
    }

    /// <summary>Registers the input-method device and returns its descriptor.</summary>
    public InputDeviceDescriptor RegisterInputMethod()
    {
        InputDeviceDescriptor descriptor = AndroidInputDescriptors.Text();
        RegisterDevice(descriptor);
        return descriptor;
    }

    protected override TextInputDevice CreateDevice(InputDeviceDescriptor descriptor, TextInputOpenOptions options)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        return new AndroidTextInputDevice(descriptor, Clock, options, EditorTextSource);
    }
}
