using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Broiler.Input.Linux;

namespace Broiler.Input.Mouse.Linux;

public sealed class LinuxMouseProvider : IMouseInputProvider, IInputDeviceWatcher
{
    private readonly LinuxEvdevProviderOptions _options;
    private readonly IInputClock _clock;
    private readonly Dictionary<InputDeviceId, LinuxEvdevDeviceInfo> _devices = [];

    public LinuxMouseProvider(LinuxEvdevProviderOptions? options = null, IInputClock? clock = null)
    {
        _options = (options ?? new LinuxEvdevProviderOptions()).Normalize();
        _clock = clock ?? StopwatchInputClock.Shared;
    }

    public event Action<InputDeviceChange>? DeviceChanged;

    public static LinuxInputDependencyReport CheckDependencies(
        string inputDirectory = LinuxEventDeviceAccessProbe.DefaultInputDirectory) =>
        LinuxInputDependencies.CheckBaseline(inputDirectory);

    public ValueTask<IReadOnlyList<InputDeviceDescriptor>> GetDevicesAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IReadOnlyList<LinuxEvdevDeviceInfo> devices = LinuxEvdevDeviceDiscovery.Discover(LinuxEvdevDeviceKind.Mouse, _options);
        ReplaceCache(devices);
        return ValueTask.FromResult<IReadOnlyList<InputDeviceDescriptor>>(devices.Select(static device => device.Descriptor).ToArray());
    }

    public async ValueTask RefreshDevicesAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyList<InputDeviceId> previous = _devices.Keys.ToArray();
        IReadOnlyList<InputDeviceDescriptor> current = await GetDevicesAsync(cancellationToken).ConfigureAwait(false);
        HashSet<InputDeviceId> currentIds = current.Select(static descriptor => descriptor.Id).ToHashSet();

        foreach (InputDeviceDescriptor descriptor in current)
        {
            InputDeviceChangeKind kind = previous.Contains(descriptor.Id)
                ? InputDeviceChangeKind.Changed
                : InputDeviceChangeKind.Added;
            DeviceChanged?.Invoke(new InputDeviceChange(kind, descriptor, _clock.GetTimestamp()));
        }

        foreach (InputDeviceId removed in previous)
        {
            if (!currentIds.Contains(removed))
                DeviceChanged?.Invoke(new InputDeviceChange(InputDeviceChangeKind.Removed, new InputDeviceDescriptor(removed, InputKind.Mouse, removed.Value, InputDeviceAvailability.Removed), _clock.GetTimestamp()));
        }
    }

    public async ValueTask<MouseInputDevice> OpenAsync(
        InputDeviceDescriptor descriptor,
        MouseOpenOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentNullException.ThrowIfNull(options);
        cancellationToken.ThrowIfCancellationRequested();

        _options.ValidateRawAccess();
        if (descriptor.Kind != InputKind.Mouse)
            throw new ArgumentException("Linux mouse providers only open mouse descriptors.", nameof(descriptor));

        LinuxEvdevDeviceInfo device = ResolveDevice(descriptor);
        if (device.Descriptor.Availability == InputDeviceAvailability.PermissionDenied)
            throw new LinuxInputException(new InputFault(InputErrorCategory.PermissionDenied, "Linux mouse event device permission denied. Check input group membership, udev rules, container device pass-through, or seat-broker policy.", null, null, "evdev"));

        // A device that matched the mouse kind is either a relative mouse or an
        // absolute touchpad; prefer relative when the device reports both.
        LinuxPointerMotionMode mode = device.Capabilities.IsMouse
            ? LinuxPointerMotionMode.Relative
            : LinuxPointerMotionMode.AbsoluteTouchpad;

        LinuxMouseInputDevice mouse = new(device.Descriptor, options, device.EventPath, device.EventName, _options.PollTimeoutMilliseconds, _clock, mode);
        await mouse.OpenAsync(cancellationToken).ConfigureAwait(false);
        return mouse;
    }

    private LinuxEvdevDeviceInfo ResolveDevice(InputDeviceDescriptor descriptor)
    {
        if (_devices.TryGetValue(descriptor.Id, out LinuxEvdevDeviceInfo? device))
            return device;

        ReplaceCache(LinuxEvdevDeviceDiscovery.Discover(LinuxEvdevDeviceKind.Mouse, _options));
        if (_devices.TryGetValue(descriptor.Id, out device))
            return device;

        throw new LinuxInputException(new InputFault(InputErrorCategory.DeviceNotFound, "Linux mouse event device was not found.", null, null, "evdev"));
    }

    private void ReplaceCache(IEnumerable<LinuxEvdevDeviceInfo> devices)
    {
        _devices.Clear();
        foreach (LinuxEvdevDeviceInfo device in devices)
            _devices[device.Descriptor.Id] = device;
    }
}
