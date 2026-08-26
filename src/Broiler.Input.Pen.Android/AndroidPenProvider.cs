using System;
using Broiler.Input.Android;

namespace Broiler.Input.Pen.Android;

/// <summary>
/// Opens <see cref="AndroidPenInputDevice"/> instances for stylus devices the host has registered.
/// </summary>
public sealed class AndroidPenProvider :
    AndroidInputProvider<PenInputDevice, PenOpenOptions>,
    IPenInputProvider
{
    private readonly AndroidCoordinateSpace _coordinateSpace;

    public AndroidPenProvider(
        AndroidCoordinateSpace? coordinateSpace = null,
        AndroidUptimeInputClock? clock = null)
        : base(clock)
    {
        _coordinateSpace = coordinateSpace ?? new AndroidCoordinateSpace();
    }

    /// <summary>The display density used to convert event coordinates.</summary>
    public AndroidCoordinateSpace CoordinateSpace => _coordinateSpace;

    /// <summary>
    /// Registers a stylus and returns its descriptor. Hosts that enumerate real Android device ids
    /// should register an <see cref="AndroidInputDescriptors.Pen"/> descriptor with the reported
    /// capabilities instead of relying on this default.
    /// </summary>
    public InputDeviceDescriptor RegisterDefaultStylus(bool supportsTilt = false, bool supportsEraser = false)
    {
        InputDeviceDescriptor descriptor = AndroidInputDescriptors.Pen(
            supportsTilt: supportsTilt,
            supportsEraser: supportsEraser);
        RegisterDevice(descriptor);
        return descriptor;
    }

    protected override PenInputDevice CreateDevice(InputDeviceDescriptor descriptor, PenOpenOptions options)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        return new AndroidPenInputDevice(descriptor, _coordinateSpace, Clock, options);
    }
}
