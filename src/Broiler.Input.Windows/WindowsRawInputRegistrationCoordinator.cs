using static Broiler.Native.Windows.Input.RawInputRegistrationNative;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Threading;

namespace Broiler.Input.Windows;

[SupportedOSPlatform("windows")]
public sealed class WindowsRawInputRegistrationCoordinator
{

    private readonly Lock _gate = new();
    private readonly Dictionary<RegistrationKey, WindowsRawInputRegistrationLease> _leases = [];

    public WindowsRawInputRegistrationLease RegisterKeyboard(IntPtr targetWindow, WindowsRawInputRegistrationOptions? options = null) =>
        Register(WindowsRawInputDeviceKind.Keyboard, GenericDesktopUsagePage, KeyboardUsage, targetWindow, options);

    public WindowsRawInputRegistrationLease RegisterMouse(IntPtr targetWindow, WindowsRawInputRegistrationOptions? options = null) =>
        Register(WindowsRawInputDeviceKind.Mouse, GenericDesktopUsagePage, MouseUsage, targetWindow, options);

    private WindowsRawInputRegistrationLease Register(WindowsRawInputDeviceKind kind, ushort usagePage,
        ushort usage, IntPtr targetWindow, WindowsRawInputRegistrationOptions? options)
    {
        WindowsRawInputRegistrationOptions effectiveOptions = options ?? new WindowsRawInputRegistrationOptions();
        effectiveOptions.Validate(targetWindow);

        RegistrationKey key = new(usagePage, usage);

        lock (_gate)
        {
            if (_leases.TryGetValue(key, out WindowsRawInputRegistrationLease? existing) && !existing.IsDisposed)
                throw new InvalidOperationException($"Raw Input {kind} is already registered for this process.");

            uint flags = 0;

            if (effectiveOptions.ReceiveInputWhenNotFocused)
                flags |= RidevInputSink;

            if (effectiveOptions.SuppressLegacyMessages)
                flags |= RidevNoLegacy;

            if (effectiveOptions.ObserveDeviceChanges)
                flags |= RidevDevNotify;

            RegisterNative(usagePage, usage, flags, targetWindow, "RegisterRawInputDevices failed.");

            WindowsRawInputRegistrationLease lease = new(this, kind, targetWindow, effectiveOptions);
            _leases[key] = lease;
            return lease;
        }
    }

    internal void Release(WindowsRawInputRegistrationLease lease)
    {
        RegistrationKey key = lease.Kind switch
        {
            WindowsRawInputDeviceKind.Mouse => new RegistrationKey(GenericDesktopUsagePage, MouseUsage),
            WindowsRawInputDeviceKind.Keyboard => new RegistrationKey(GenericDesktopUsagePage, KeyboardUsage),
            _ => throw new ArgumentOutOfRangeException(nameof(lease)),
        };

        lock (_gate)
        {
            if (_leases.TryGetValue(key, out WindowsRawInputRegistrationLease? registered) &&
                ReferenceEquals(registered, lease))
            {
                _leases.Remove(key);
            }

            if (!TryRegisterNative(key.UsagePage, key.Usage, RidevRemove, IntPtr.Zero, out int error))
                lease.SetLastUnregisterError(error);
        }
    }

    private static void RegisterNative(ushort usagePage, ushort usage, uint flags, IntPtr targetWindow, string message)
    {
        if (!TryRegisterNative(usagePage, usage, flags, targetWindow, out int error))
            throw new Win32Exception(error, message);
    }

    private static bool TryRegisterNative(ushort usagePage, ushort usage, uint flags, IntPtr targetWindow, out int error)
    {
        RawInputDevice[] devices = [
            new RawInputDevice
            {
                UsagePage = usagePage,
                Usage = usage,
                Flags = flags,
                TargetWindow = targetWindow,
            }];

        bool registered = RegisterRawInputDevices(devices, (uint)devices.Length, (uint)Marshal.SizeOf<RawInputDevice>());
        error = registered ? 0 : Marshal.GetLastWin32Error();
        return registered;
    }

    private readonly record struct RegistrationKey(ushort UsagePage, ushort Usage);

}
