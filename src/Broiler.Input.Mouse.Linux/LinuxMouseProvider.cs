using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Broiler.Input.Linux;

namespace Broiler.Input.Mouse.Linux;

public sealed class LinuxMouseProvider(LinuxEvdevProviderOptions? options = null, IInputClock? clock = null) :
    IMouseInputProvider, IInputDeviceWatcher
{
    private readonly LinuxEvdevProviderOptions _options = (options ?? new LinuxEvdevProviderOptions()).Normalize();
    private readonly IInputClock _clock = clock ?? StopwatchInputClock.Shared;
    private readonly LinuxEvdevDeviceCache _cache = new(LinuxEvdevDeviceKind.Mouse, (options ?? new LinuxEvdevProviderOptions()).Normalize());

    public event Action<InputDeviceChange>? DeviceChanged;

    public static LinuxInputDependencyReport CheckDependencies(string inputDirectory = LinuxEventDeviceAccessProbe.DefaultInputDirectory) =>
        LinuxInputDependencies.CheckBaseline(inputDirectory);

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

    public async ValueTask<MouseInputDevice> OpenAsync(InputDeviceDescriptor descriptor, 
        MouseOpenOptions options, CancellationToken cancellationToken = default)
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

    private LinuxEvdevDeviceInfo ResolveDevice(InputDeviceDescriptor descriptor) =>
        _cache.Resolve(descriptor.Id) ?? throw new LinuxInputException(new InputFault(InputErrorCategory.DeviceNotFound,
            "Linux mouse event device was not found.", null, null, "evdev"));
}
