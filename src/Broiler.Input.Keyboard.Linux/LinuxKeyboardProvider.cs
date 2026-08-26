using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Broiler.Input.Linux;

namespace Broiler.Input.Keyboard.Linux;

public sealed class LinuxKeyboardProvider : IKeyboardInputProvider, IInputDeviceWatcher
{
    private readonly LinuxEvdevProviderOptions _options;
    private readonly IInputClock _clock;
    private readonly Dictionary<InputDeviceId, LinuxEvdevDeviceInfo> _devices = [];

    public LinuxKeyboardProvider(LinuxEvdevProviderOptions? options = null, IInputClock? clock = null)
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
        IReadOnlyList<LinuxEvdevDeviceInfo> devices = LinuxEvdevDeviceDiscovery.Discover(LinuxEvdevDeviceKind.Keyboard, _options);
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
                DeviceChanged?.Invoke(new InputDeviceChange(InputDeviceChangeKind.Removed, new InputDeviceDescriptor(removed, InputKind.Keyboard, removed.Value, InputDeviceAvailability.Removed), _clock.GetTimestamp()));
        }
    }

    public async ValueTask<KeyboardInputDevice> OpenAsync(
        InputDeviceDescriptor descriptor,
        KeyboardOpenOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentNullException.ThrowIfNull(options);
        cancellationToken.ThrowIfCancellationRequested();

        _options.ValidateRawAccess();
        if (descriptor.Kind != InputKind.Keyboard)
            throw new ArgumentException("Linux keyboard providers only open keyboard descriptors.", nameof(descriptor));

        LinuxEvdevDeviceInfo device = ResolveDevice(descriptor);
        if (device.Descriptor.Availability == InputDeviceAvailability.PermissionDenied)
            throw new LinuxInputException(new InputFault(InputErrorCategory.PermissionDenied, "Linux keyboard event device permission denied. Check input group membership, udev rules, container device pass-through, or seat-broker policy.", null, null, "evdev"));

        LinuxKeyboardInputDevice keyboard = new(device.Descriptor, device.EventPath, device.EventName, _options.PollTimeoutMilliseconds, _clock);
        await keyboard.OpenAsync(cancellationToken).ConfigureAwait(false);
        return keyboard;
    }

    private LinuxEvdevDeviceInfo ResolveDevice(InputDeviceDescriptor descriptor)
    {
        if (_devices.TryGetValue(descriptor.Id, out LinuxEvdevDeviceInfo? device))
            return device;

        ReplaceCache(LinuxEvdevDeviceDiscovery.Discover(LinuxEvdevDeviceKind.Keyboard, _options));
        if (_devices.TryGetValue(descriptor.Id, out device))
            return device;

        throw new LinuxInputException(new InputFault(InputErrorCategory.DeviceNotFound, "Linux keyboard event device was not found.", null, null, "evdev"));
    }

    private void ReplaceCache(IEnumerable<LinuxEvdevDeviceInfo> devices)
    {
        _devices.Clear();
        foreach (LinuxEvdevDeviceInfo device in devices)
            _devices[device.Descriptor.Id] = device;
    }
}
