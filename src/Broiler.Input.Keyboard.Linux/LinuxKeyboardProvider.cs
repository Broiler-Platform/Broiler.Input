using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Broiler.Input.Linux;

namespace Broiler.Input.Keyboard.Linux;

public sealed class LinuxKeyboardProvider(LinuxEvdevProviderOptions? options = null, IInputClock? clock = null)
    : IKeyboardInputProvider, IInputDeviceWatcher
{
    private readonly LinuxEvdevProviderOptions _options = (options ?? new LinuxEvdevProviderOptions()).Normalize();
    private readonly IInputClock _clock = clock ?? StopwatchInputClock.Shared;
    private readonly LinuxEvdevDeviceCache _cache = new(LinuxEvdevDeviceKind.Keyboard, (options ?? new LinuxEvdevProviderOptions()).Normalize());

    public event Action<InputDeviceChange>? DeviceChanged;

    public static LinuxInputDependencyReport CheckDependencies(string inputDirectory =
        LinuxEventDeviceAccessProbe.DefaultInputDirectory) => LinuxInputDependencies.CheckBaseline(inputDirectory);

    public ValueTask<IReadOnlyList<InputDeviceDescriptor>> GetDevicesAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IReadOnlyList<LinuxEvdevDeviceInfo> devices = _cache.Discover();

        return ValueTask.FromResult<IReadOnlyList<InputDeviceDescriptor>>([.. devices.Select(static device => device.Descriptor)]);
    }

    public ValueTask RefreshDevicesAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _cache.Refresh(_clock, change => DeviceChanged?.Invoke(change));
        return ValueTask.CompletedTask;
    }

    public async ValueTask<KeyboardInputDevice> OpenAsync(InputDeviceDescriptor descriptor, 
        KeyboardOpenOptions options, CancellationToken cancellationToken = default)
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

    private LinuxEvdevDeviceInfo ResolveDevice(InputDeviceDescriptor descriptor) =>
        _cache.Resolve(descriptor.Id) ?? throw new LinuxInputException(new InputFault(InputErrorCategory.DeviceNotFound,
            "Linux keyboard event device was not found.", null, null, "evdev"));
}
