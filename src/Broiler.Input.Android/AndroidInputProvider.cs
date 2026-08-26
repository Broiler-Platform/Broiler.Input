using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Broiler.Input.Android;

/// <summary>
/// Shared discovery and lifecycle behavior for the Android input providers.
/// </summary>
/// <remarks>
/// Android has no equivalent of the evdev sysfs scan the Linux providers use: only the Activity can
/// enumerate <c>InputDevice.getDeviceIds()</c> and observe
/// <c>InputManager.InputDeviceListener</c> callbacks. So discovery here is host-driven — the host
/// calls <see cref="RegisterDevice"/> and <see cref="RemoveDevice"/>, and the provider turns those
/// into the same <see cref="InputDeviceChange"/> notifications every other provider emits.
/// </remarks>
public abstract class AndroidInputProvider<TDevice, TOptions> : IInputProvider<TDevice, TOptions>, IInputDeviceWatcher
    where TDevice : InputDevice
{
    private readonly Dictionary<InputDeviceId, InputDeviceDescriptor> _descriptors = [];
    private readonly Dictionary<InputDeviceId, TDevice> _openDevices = [];
    private readonly object _gate = new();

    protected AndroidInputProvider(AndroidUptimeInputClock? clock = null)
    {
        Clock = clock ?? new AndroidUptimeInputClock();
    }

    public event Action<InputDeviceChange>? DeviceChanged;

    protected AndroidUptimeInputClock Clock { get; }

    /// <summary>
    /// Adds or updates a device descriptor. Returns true when the descriptor is new, false when it
    /// replaced an existing entry for the same id.
    /// </summary>
    public bool RegisterDevice(InputDeviceDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);

        bool added;
        lock (_gate)
        {
            added = _descriptors.TryAdd(descriptor.Id, descriptor);
            if (!added)
                _descriptors[descriptor.Id] = descriptor;
        }

        DeviceChanged?.Invoke(new InputDeviceChange(
            added ? InputDeviceChangeKind.Added : InputDeviceChangeKind.Changed,
            descriptor,
            Clock.GetTimestamp()));
        return added;
    }

    /// <summary>
    /// Removes a device. Any open device for that id is marked unavailable with a device-removed
    /// fault rather than being left running against hardware that is gone.
    /// </summary>
    public bool RemoveDevice(InputDeviceId id)
    {
        InputDeviceDescriptor? descriptor;
        TDevice? openDevice;
        lock (_gate)
        {
            if (!_descriptors.Remove(id, out descriptor))
                return false;

            _openDevices.Remove(id, out openDevice);
        }

        if (openDevice is IAndroidInputDevice removable)
            removable.NotifyRemoved(AndroidInputFaults.DeviceRemoved(id));

        DeviceChanged?.Invoke(new InputDeviceChange(
            InputDeviceChangeKind.Removed,
            new InputDeviceDescriptor(id, descriptor!.Kind, descriptor.DisplayName, InputDeviceAvailability.Removed),
            Clock.GetTimestamp()));
        return true;
    }

    /// <summary>
    /// Tells every open device that input capture ended. The host calls this from
    /// <c>onPause</c>, on window-focus loss, and when a parent view intercepts a gesture.
    /// </summary>
    public void NotifyCaptureLost(string reason = "capture lost")
    {
        TDevice[] devices;
        lock (_gate)
            devices = [.. _openDevices.Values];

        foreach (TDevice device in devices)
        {
            if (device is IAndroidInputDevice capturable)
                capturable.NotifyCaptureLost(reason);
        }
    }

    public ValueTask<IReadOnlyList<InputDeviceDescriptor>> GetDevicesAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
            return ValueTask.FromResult<IReadOnlyList<InputDeviceDescriptor>>([.. _descriptors.Values]);
    }

    public ValueTask<TDevice> OpenAsync(
        InputDeviceDescriptor descriptor,
        TOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            if (!_descriptors.ContainsKey(descriptor.Id))
                throw new InvalidOperationException(AndroidInputFaults.DeviceNotFound(descriptor.Id).Message);

            if (_openDevices.TryGetValue(descriptor.Id, out TDevice? existing))
                return ValueTask.FromResult(existing);
        }

        TDevice device = CreateDevice(descriptor, options);

        lock (_gate)
        {
            // Another caller may have opened the same descriptor while the device was being
            // constructed; keep one device per id so contact tracking is not split across two.
            if (_openDevices.TryGetValue(descriptor.Id, out TDevice? raced))
            {
                device.Dispose();
                return ValueTask.FromResult(raced);
            }

            _openDevices[descriptor.Id] = device;
        }

        return ValueTask.FromResult(device);
    }

    /// <summary>Returns the open device for an id, if one exists.</summary>
    public bool TryGetOpenDevice(InputDeviceId id, out TDevice? device)
    {
        lock (_gate)
            return _openDevices.TryGetValue(id, out device);
    }

    /// <summary>Creates the concrete device for a descriptor.</summary>
    protected abstract TDevice CreateDevice(InputDeviceDescriptor descriptor, TOptions options);
}
