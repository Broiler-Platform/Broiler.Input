using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Broiler.Input;
using Broiler.Input.Microphone;

namespace Broiler.Input.Microphone.Windows;

internal static class WindowsMicrophoneEndpointEnumerator
{
    private const string EndpointIdCapability = "windows.wasapi.endpoint-id";
    private const string CaptureModeCapability = "microphone.capture.mode";
    private const string CaptureModeValue = "wasapi-shared-event";
    private const ushort PropVariantString = 31;

    private static readonly PropertyKey FriendlyNameKey = new(
        new Guid("A45C254E-DF1C-4EFD-8020-67D146A850E0"),
        14);

    public static IReadOnlyList<InputDeviceDescriptor> EnumerateCaptureDevices()
    {
        using WindowsComApartmentScope apartment = WindowsComApartmentScope.Enter();
        object? enumeratorObject = null;
        IMMDeviceCollection? collection = null;

        try
        {
            IMMDeviceEnumerator enumerator = CreateEnumerator(out enumeratorObject);
            Dictionary<MicrophoneEndpointRole, string> defaults = GetDefaultEndpointIds(enumerator);
            WindowsMicrophoneFaults.ThrowIfFailed(
                enumerator.EnumAudioEndpoints(EDataFlow.Capture, DeviceState.Active, out collection),
                "Microphone endpoint enumeration failed.");
            WindowsMicrophoneFaults.ThrowIfFailed(collection.GetCount(out uint count), "Microphone endpoint count failed.");

            List<InputDeviceDescriptor> devices = new((int)count);
            for (uint index = 0; index < count; index++)
            {
                WindowsMicrophoneFaults.ThrowIfFailed(collection.Item(index, out IMMDevice device), "Microphone endpoint lookup failed.");
                try
                {
                    devices.Add(CreateDescriptor(device, defaults));
                }
                finally
                {
                    ReleaseComObject(device);
                }
            }

            return devices;
        }
        finally
        {
            ReleaseComObject(collection);
            ReleaseComObject(enumeratorObject);
        }
    }

    public static InputDeviceDescriptor? TryGetDefaultCaptureDevice(MicrophoneEndpointRole role)
    {
        using WindowsComApartmentScope apartment = WindowsComApartmentScope.Enter();
        object? enumeratorObject = null;
        IMMDevice? device = null;

        try
        {
            IMMDeviceEnumerator enumerator = CreateEnumerator(out enumeratorObject);
            int result = enumerator.GetDefaultAudioEndpoint(EDataFlow.Capture, ToRole(role), out device);
            if (result == WindowsWasapiNative.E_NOTFOUND)
                return null;

            WindowsMicrophoneFaults.ThrowIfFailed(result, "Default microphone endpoint lookup failed.");
            return CreateDescriptor(device, new Dictionary<MicrophoneEndpointRole, string> { [role] = GetDeviceId(device) });
        }
        finally
        {
            ReleaseComObject(device);
            ReleaseComObject(enumeratorObject);
        }
    }

    public static string? GetNativeEndpointId(InputDeviceDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        return descriptor.Capabilities
            .FirstOrDefault(static capability => capability.Name == EndpointIdCapability)
            .Value;
    }

    public static IMMDevice GetDevice(InputDeviceDescriptor descriptor, MicrophoneEndpointRole role, out object? enumeratorObject)
    {
        IMMDeviceEnumerator enumerator = CreateEnumerator(out enumeratorObject);
        string? endpointId = GetNativeEndpointId(descriptor);
        if (string.IsNullOrWhiteSpace(endpointId))
        {
            WindowsMicrophoneFaults.ThrowIfFailed(
                enumerator.GetDefaultAudioEndpoint(EDataFlow.Capture, ToRole(role), out IMMDevice defaultDevice),
                "Default microphone endpoint lookup failed.");
            return defaultDevice;
        }

        WindowsMicrophoneFaults.ThrowIfFailed(
            enumerator.GetDevice(endpointId, out IMMDevice device),
            "Microphone endpoint lookup failed.");
        return device;
    }

    private static IMMDeviceEnumerator CreateEnumerator(out object? enumeratorObject)
    {
        Guid classId = WindowsWasapiNative.MMDeviceEnumeratorClassId;
        Guid interfaceId = WindowsWasapiNative.IMMDeviceEnumeratorId;
        WindowsMicrophoneFaults.ThrowIfFailed(
            WindowsWasapiNative.CoCreateInstance(
                ref classId,
                IntPtr.Zero,
                WindowsWasapiNative.CLSCTX_INPROC_SERVER,
                ref interfaceId,
                out enumeratorObject),
            "MMDeviceEnumerator activation failed.");

        if (enumeratorObject is not IMMDeviceEnumerator enumerator)
            throw WindowsMicrophoneFaults.CreateException(unchecked((int)0x80004002), "MMDeviceEnumerator interface activation failed.");

        return enumerator;
    }

    private static Dictionary<MicrophoneEndpointRole, string> GetDefaultEndpointIds(IMMDeviceEnumerator enumerator)
    {
        Dictionary<MicrophoneEndpointRole, string> defaults = [];
        foreach (MicrophoneEndpointRole role in Enum.GetValues<MicrophoneEndpointRole>())
        {
            IMMDevice? endpoint = null;
            try
            {
                int result = enumerator.GetDefaultAudioEndpoint(EDataFlow.Capture, ToRole(role), out endpoint);
                if (result >= 0)
                    defaults[role] = GetDeviceId(endpoint);
            }
            finally
            {
                ReleaseComObject(endpoint);
            }
        }

        return defaults;
    }

    private static InputDeviceDescriptor CreateDescriptor(
        IMMDevice device,
        IReadOnlyDictionary<MicrophoneEndpointRole, string> defaults)
    {
        string endpointId = GetDeviceId(device);
        string displayName = GetFriendlyName(device) ?? endpointId;
        List<InputCapability> capabilities =
        [
            new(EndpointIdCapability, endpointId),
            new(CaptureModeCapability, CaptureModeValue),
        ];

        foreach (KeyValuePair<MicrophoneEndpointRole, string> entry in defaults)
        {
            if (StringComparer.Ordinal.Equals(entry.Value, endpointId))
                capabilities.Add(new InputCapability($"microphone.default.{entry.Key.ToString().ToLowerInvariant()}", "true"));
        }

        InputDeviceAvailability availability = GetAvailability(device);
        return new InputDeviceDescriptor(
            InputDeviceId.FromOpaqueValue(ToStableInputId(endpointId)),
            InputKind.Microphone,
            displayName,
            availability,
            capabilities);
    }

    private static string GetDeviceId(IMMDevice device)
    {
        WindowsMicrophoneFaults.ThrowIfFailed(device.GetId(out string id), "Microphone endpoint id lookup failed.");
        return id;
    }

    private static string? GetFriendlyName(IMMDevice device)
    {
        IPropertyStore? propertyStore = null;
        PropVariant value = default;
        try
        {
            int openResult = device.OpenPropertyStore(StorageAccess.Read, out propertyStore);
            if (openResult < 0)
                return null;

            PropertyKey key = FriendlyNameKey;
            int valueResult = propertyStore.GetValue(ref key, out value);
            if (valueResult < 0 || value.ValueType != PropVariantString || value.PointerValue == IntPtr.Zero)
                return null;

            return Marshal.PtrToStringUni(value.PointerValue);
        }
        finally
        {
            if (value.ValueType != 0)
                WindowsWasapiNative.PropVariantClear(ref value);
            ReleaseComObject(propertyStore);
        }
    }

    private static InputDeviceAvailability GetAvailability(IMMDevice device)
    {
        int result = device.GetState(out DeviceState state);
        if (result < 0)
            return InputDeviceAvailability.Unknown;

        return (state & DeviceState.Active) != 0 ? InputDeviceAvailability.Available : InputDeviceAvailability.Unavailable;
    }

    private static string ToStableInputId(string endpointId)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(endpointId));
        string suffix = Convert.ToHexString(hash, 0, 12).ToLowerInvariant();
        return $"windows:wasapi:microphone:{suffix}";
    }

    private static ERole ToRole(MicrophoneEndpointRole role) => role switch
    {
        MicrophoneEndpointRole.Console => ERole.Console,
        MicrophoneEndpointRole.Multimedia => ERole.Multimedia,
        MicrophoneEndpointRole.Communications => ERole.Communications,
        _ => throw new ArgumentOutOfRangeException(nameof(role)),
    };

    private static void ReleaseComObject(object? value)
    {
        if (value is not null && Marshal.IsComObject(value))
            Marshal.ReleaseComObject(value);
    }
}
