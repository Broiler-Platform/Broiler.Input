using Broiler.Input.Windows;
using Broiler.Native.Windows;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;

namespace Broiler.Input.Scanner.Windows;

[SupportedOSPlatform("windows")]
internal static class WindowsScannerDeviceEnumerator
{
    internal const string DeviceIdCapability = "windows.wia.device-id";
    internal const string SourceKindCapability = "scanner.capture.source";
    internal const string SourceKindValue = "wia-2.0";

    public static IReadOnlyList<InputDeviceDescriptor> EnumerateScanners()
    {
        using WindowsComApartmentScope apartment = WindowsComApartmentScope.Enter(
            static hr => WindowsScannerFaults.CreateException(hr, "COM initialization for WIA scanner enumeration failed.", "COM"));

        IWiaDevMgr2? devMgr = TryCreateDeviceManager();
        if (devMgr is null)
            return [];

        IEnumWIA_DEV_INFO? enumDevInfo = null;
        try
        {
            int enumResult = devMgr.EnumDeviceInfo(WindowsWiaConstants.WIA_DEVINFO_ENUM_LOCAL, out enumDevInfo);
            if (enumResult == WindowsWiaConstants.WIA_S_NO_DEVICE_AVAILABLE)
                return [];

            WindowsScannerFaults.ThrowIfFailed(enumResult, "WIA scanner device enumeration failed.");

            List<InputDeviceDescriptor> descriptors = [];
            while (true)
            {
                int nextResult = enumDevInfo.Next(1, out IntPtr storagePointer, out uint fetched);
                if (nextResult != ComNative.S_OK || fetched == 0 || storagePointer == IntPtr.Zero)
                    break;

                IWiaPropertyStorage? storage = WindowsComInterop.Wrap<IWiaPropertyStorage>(storagePointer);
                if (storage is null)
                    continue;

                try
                {
                    InputDeviceDescriptor? descriptor = TryCreateDescriptor(storage);
                    if (descriptor is not null)
                        descriptors.Add(descriptor);
                }
                finally
                {
                    WindowsComInterop.Release(storage);
                }
            }

            return descriptors;
        }
        finally
        {
            WindowsComInterop.Release(enumDevInfo);
            WindowsComInterop.Release(devMgr);
        }
    }

    public static IWiaItem2 ActivateDevice(InputDeviceDescriptor descriptor, out object? devMgrObject, out object? rootItemObject)
    {
        ArgumentNullException.ThrowIfNull(descriptor);

        IWiaDevMgr2 devMgr = CreateDeviceManager();
        devMgrObject = devMgr;
        rootItemObject = null;

        try
        {
            string? wiaDeviceId = GetWiaDeviceId(descriptor) ?? FindMatchingWiaDeviceId(devMgr, descriptor.Id);
            if (string.IsNullOrWhiteSpace(wiaDeviceId))
                throw WindowsScannerFaults.CreateException(ComNative.E_NOTFOUND, "Selected WIA scanner device was not found.");

            int result = devMgr.CreateDevice(0, wiaDeviceId, out IWiaItem2 rootItem);
            WindowsScannerFaults.ThrowIfFailed(result, "WIA scanner device activation failed.");

            rootItemObject = rootItem;
            return rootItem;
        }
        catch
        {
            WindowsComInterop.Release(rootItemObject);
            WindowsComInterop.Release(devMgrObject);
            rootItemObject = null;
            devMgrObject = null;
            throw;
        }
    }

    public static string? GetWiaDeviceId(InputDeviceDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        return descriptor.Capabilities.FirstOrDefault(static c => c.Name == DeviceIdCapability).Value;
    }

    internal static string ToStableInputId(string seed)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(seed));
        string suffix = Convert.ToHexString(hash, 0, 12).ToLowerInvariant();
        return $"windows:wia:scanner:{suffix}";
    }

    private static IWiaDevMgr2? TryCreateDeviceManager()
    {
        Guid clsid = WindowsWiaConstants.ClsidWiaDevMgr2;
        Guid iid = WindowsWiaConstants.IidIWiaDevMgr2;
        int result = ComNative.CoCreateInstance(in clsid, IntPtr.Zero, WindowsWiaConstants.CLSCTX_LOCAL_SERVER, in iid, out IntPtr pointer);

        if (result is WindowsWiaConstants.REGDB_E_CLASSNOTREG or WindowsWiaConstants.WIA_S_NO_DEVICE_AVAILABLE)
            return null;

        WindowsScannerFaults.ThrowIfFailed(result, "WIA 2.0 device manager activation failed.");
        return WindowsComInterop.Wrap<IWiaDevMgr2>(pointer);
    }

    private static IWiaDevMgr2 CreateDeviceManager()
    {
        Guid clsid = WindowsWiaConstants.ClsidWiaDevMgr2;
        Guid iid = WindowsWiaConstants.IidIWiaDevMgr2;
        int result = ComNative.CoCreateInstance(in clsid, IntPtr.Zero, WindowsWiaConstants.CLSCTX_LOCAL_SERVER, in iid, out IntPtr pointer);
        WindowsScannerFaults.ThrowIfFailed(result, "WIA 2.0 device manager activation failed.");

        return WindowsComInterop.Wrap<IWiaDevMgr2>(pointer)
            ?? throw WindowsScannerFaults.CreateException(ComNative.E_NOINTERFACE, "WIA 2.0 device manager interface query failed.");
    }

    private static InputDeviceDescriptor? TryCreateDescriptor(IWiaPropertyStorage storage)
    {
        if (WindowsWiaPropertyHelper.TryReadInt32(storage, WindowsWiaConstants.WIA_DIP_DEV_TYPE, out int devType))
        {
            int highWord = (devType >> 16) & 0xFFFF;
            int lowWord = devType & 0xFFFF;
            bool isScanner = highWord == WindowsWiaConstants.StiDeviceTypeScanner ||
                             lowWord == WindowsWiaConstants.StiDeviceTypeScanner ||
                             devType == WindowsWiaConstants.StiDeviceTypeDefault;
            if (!isScanner)
                return null;
        }

        string? deviceId = WindowsWiaPropertyHelper.TryReadString(storage, WindowsWiaConstants.WIA_DIP_DEV_ID);
        string? friendlyName = WindowsWiaPropertyHelper.TryReadString(storage, WindowsWiaConstants.WIA_DIP_DEV_NAME)
            ?? WindowsWiaPropertyHelper.TryReadString(storage, WindowsWiaConstants.WIA_DIP_DEV_DESC);

        if (string.IsNullOrWhiteSpace(deviceId) && string.IsNullOrWhiteSpace(friendlyName))
            return null;

        string seed = string.IsNullOrWhiteSpace(deviceId) ? friendlyName! : deviceId;
        List<InputCapability> capabilities = [new(SourceKindCapability, SourceKindValue)];
        if (!string.IsNullOrWhiteSpace(deviceId))
            capabilities.Add(new InputCapability(DeviceIdCapability, deviceId));

        return new InputDeviceDescriptor(
            InputDeviceId.FromOpaqueValue(ToStableInputId(seed)),
            InputKind.Scanner,
            friendlyName ?? seed,
            InputDeviceAvailability.Available,
            capabilities);
    }

    private static string? FindMatchingWiaDeviceId(IWiaDevMgr2 devMgr, InputDeviceId targetId)
    {
        if (devMgr.EnumDeviceInfo(WindowsWiaConstants.WIA_DEVINFO_ENUM_LOCAL, out IEnumWIA_DEV_INFO enumDevInfo) < 0)
            return null;

        try
        {
            while (enumDevInfo.Next(1, out IntPtr storagePointer, out uint fetched) == ComNative.S_OK &&
                   fetched > 0 &&
                   storagePointer != IntPtr.Zero)
            {
                IWiaPropertyStorage? storage = WindowsComInterop.Wrap<IWiaPropertyStorage>(storagePointer);
                if (storage is null)
                    continue;

                try
                {
                    InputDeviceDescriptor? candidate = TryCreateDescriptor(storage);
                    if (candidate is not null && candidate.Id == targetId)
                        return GetWiaDeviceId(candidate);
                }
                finally
                {
                    WindowsComInterop.Release(storage);
                }
            }

            return null;
        }
        finally
        {
            WindowsComInterop.Release(enumDevInfo);
        }
    }
}
