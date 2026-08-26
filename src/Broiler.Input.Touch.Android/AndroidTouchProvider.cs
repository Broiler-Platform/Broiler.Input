using System;
using Broiler.Input.Android;

namespace Broiler.Input.Touch.Android;

/// <summary>
/// Opens <see cref="AndroidTouchInputDevice"/> instances for touch devices the host has registered.
/// </summary>
public sealed class AndroidTouchProvider :
    AndroidInputProvider<TouchInputDevice, TouchOpenOptions>,
    ITouchInputProvider
{
    private readonly AndroidCoordinateSpace _coordinateSpace;

    public AndroidTouchProvider(
        AndroidCoordinateSpace? coordinateSpace = null,
        AndroidUptimeInputClock? clock = null)
        : base(clock)
    {
        _coordinateSpace = coordinateSpace ?? new AndroidCoordinateSpace();
    }

    /// <summary>The display density used to convert event coordinates. Update it on configuration change.</summary>
    public AndroidCoordinateSpace CoordinateSpace => _coordinateSpace;

    /// <summary>
    /// Registers the built-in touch screen and returns its descriptor. A host that enumerates real
    /// Android device ids should call <see cref="AndroidInputProvider{TDevice,TOptions}.RegisterDevice"/>
    /// with an <see cref="AndroidInputDescriptors.Touch"/> descriptor instead.
    /// </summary>
    public InputDeviceDescriptor RegisterDefaultTouchScreen()
    {
        InputDeviceDescriptor descriptor = AndroidInputDescriptors.Touch();
        RegisterDevice(descriptor);
        return descriptor;
    }

    protected override TouchInputDevice CreateDevice(InputDeviceDescriptor descriptor, TouchOpenOptions options)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        return new AndroidTouchInputDevice(descriptor, _coordinateSpace, Clock, options);
    }
}
