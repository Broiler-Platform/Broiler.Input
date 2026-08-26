using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Broiler.Input.Windows;

namespace Broiler.Input.Keyboard.Windows;

public sealed class WindowsKeyboardProvider : IKeyboardInputProvider, IInputDeviceWatcher, IWindowsInputMessageSink
{
    private const int RawInputDeviceArrival = 1;
    private const int RawInputDeviceRemoval = 2;

    private static readonly InputDeviceDescriptor s_descriptor = new(
        InputDeviceId.FromOpaqueValue("windows:keyboard:semantic-message-source"),
        InputKind.Keyboard,
        "Windows keyboard message source",
        InputDeviceAvailability.Available,
        [
            new InputCapability("delivery", "semantic-window-messages"),
            new InputCapability("raw-input-registration", "explicit-lease"),
            new InputCapability("text", "WM_CHAR"),
            new InputCapability("hot-plug", "WM_INPUT_DEVICE_CHANGE"),
        ]);

    private readonly IInputClock _clock;

    public WindowsKeyboardProvider(IInputClock? clock = null)
    {
        _clock = clock ?? WindowsInputClock.Shared;
    }

    public event Action<InputDeviceChange>? DeviceChanged;

    public bool ProcessMessage(in WindowsInputMessage message)
    {
        if (message.Message != WindowsMessageIds.InputDeviceChange)
            return false;

        DeviceChanged?.Invoke(new InputDeviceChange(
            ChangeKindFromWParam(message.WParam),
            s_descriptor,
            message.Timestamp));
        return false;
    }

    public ValueTask<IReadOnlyList<InputDeviceDescriptor>> GetDevicesAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult<IReadOnlyList<InputDeviceDescriptor>>([s_descriptor]);
    }

    public async ValueTask<KeyboardInputDevice> OpenAsync(
        InputDeviceDescriptor descriptor,
        KeyboardOpenOptions options,
        CancellationToken cancellationToken = default)
    {
        WindowsKeyboardInputDevice device = CreateDevice(descriptor, options);
        await device.OpenAsync(cancellationToken).ConfigureAwait(false);
        return device;
    }

    public async ValueTask<WindowsKeyboardInputDevice> OpenDefaultAsync(
        KeyboardOpenOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        WindowsKeyboardInputDevice device = CreateDevice(s_descriptor, options ?? new KeyboardOpenOptions());
        await device.OpenAsync(cancellationToken).ConfigureAwait(false);
        return device;
    }

    public WindowsRawInputRegistrationLease RegisterRawInput(
        IntPtr targetWindow,
        WindowsRawInputRegistrationCoordinator coordinator,
        WindowsRawInputRegistrationOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(coordinator);
        return coordinator.RegisterKeyboard(targetWindow, options);
    }

    private WindowsKeyboardInputDevice CreateDevice(
        InputDeviceDescriptor descriptor,
        KeyboardOpenOptions options)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentNullException.ThrowIfNull(options);

        if (descriptor.Id != s_descriptor.Id)
            throw new ArgumentException("The descriptor did not come from this provider.", nameof(descriptor));

        return new WindowsKeyboardInputDevice(descriptor, options, _clock);
    }

    private static InputDeviceChangeKind ChangeKindFromWParam(IntPtr wParam) =>
        unchecked((int)(long)wParam) switch
        {
            RawInputDeviceArrival => InputDeviceChangeKind.Added,
            RawInputDeviceRemoval => InputDeviceChangeKind.Removed,
            _ => InputDeviceChangeKind.Changed,
        };
}
