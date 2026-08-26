using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Broiler.Input.Windows;

namespace Broiler.Input.Mouse.Windows;

public sealed class WindowsMouseProvider : IMouseInputProvider, IInputDeviceWatcher, IWindowsInputMessageSink
{
    private const int RawInputDeviceArrival = 1;
    private const int RawInputDeviceRemoval = 2;

    private const uint TmeLeave = 0x00000002;

    private static readonly InputDeviceDescriptor s_descriptor = new(
        InputDeviceId.FromOpaqueValue("windows:mouse:semantic-message-source"),
        InputKind.Mouse,
        "Windows mouse message source",
        InputDeviceAvailability.Available,
        [
            new InputCapability("delivery", "semantic-window-messages"),
            new InputCapability("raw-input-registration", "explicit-lease"),
            new InputCapability("buttons", "left,right,middle,x1,x2"),
            new InputCapability("wheel", "vertical,horizontal"),
            new InputCapability("hot-plug", "WM_INPUT_DEVICE_CHANGE"),
        ]);

    private readonly IInputClock _clock;

    public WindowsMouseProvider(IInputClock? clock = null)
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

    public async ValueTask<MouseInputDevice> OpenAsync(
        InputDeviceDescriptor descriptor,
        MouseOpenOptions options,
        CancellationToken cancellationToken = default)
    {
        WindowsMouseInputDevice device = CreateDevice(descriptor, options);
        await device.OpenAsync(cancellationToken).ConfigureAwait(false);
        return device;
    }

    public async ValueTask<WindowsMouseInputDevice> OpenDefaultAsync(
        MouseOpenOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        WindowsMouseInputDevice device = CreateDevice(s_descriptor, options ?? new MouseOpenOptions());
        await device.OpenAsync(cancellationToken).ConfigureAwait(false);
        return device;
    }

    public WindowsRawInputRegistrationLease RegisterRawInput(
        IntPtr targetWindow,
        WindowsRawInputRegistrationCoordinator coordinator,
        WindowsRawInputRegistrationOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(coordinator);
        return coordinator.RegisterMouse(targetWindow, options);
    }

    public static void BeginMouseLeaveTracking(IntPtr targetWindow)
    {
        if (targetWindow == IntPtr.Zero)
            throw new ArgumentException("Mouse leave tracking requires a target window.", nameof(targetWindow));

        var tracking = new WindowsMouseNativeMethods.TRACKMOUSEEVENT
        {
            CbSize = (uint)Marshal.SizeOf<WindowsMouseNativeMethods.TRACKMOUSEEVENT>(),
            Flags = TmeLeave,
            HwndTrack = targetWindow,
            HoverTime = 0,
        };

        if (!WindowsMouseNativeMethods.TrackMouseEvent(ref tracking))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "TrackMouseEvent failed.");
    }

    private WindowsMouseInputDevice CreateDevice(InputDeviceDescriptor descriptor, MouseOpenOptions options)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentNullException.ThrowIfNull(options);

        if (descriptor.Id != s_descriptor.Id)
            throw new ArgumentException("The descriptor did not come from this provider.", nameof(descriptor));

        return new WindowsMouseInputDevice(descriptor, options, _clock);
    }

    private static InputDeviceChangeKind ChangeKindFromWParam(IntPtr wParam) =>
        unchecked((int)(long)wParam) switch
        {
            RawInputDeviceArrival => InputDeviceChangeKind.Added,
            RawInputDeviceRemoval => InputDeviceChangeKind.Removed,
            _ => InputDeviceChangeKind.Changed,
        };
}
