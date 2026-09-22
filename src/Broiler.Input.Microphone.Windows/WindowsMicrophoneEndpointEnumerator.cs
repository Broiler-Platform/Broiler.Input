using Broiler.Native.Windows;
using Broiler.Native.Windows.Wasapi;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;

namespace Broiler.Input.Microphone.Windows;

[SupportedOSPlatform("windows")]
internal static class WindowsMicrophoneEndpointEnumerator
{
    private const string EndpointIdCapability = "windows.wasapi.endpoint-id";
    private const string CaptureModeCapability = "microphone.capture.mode";
    private const string CaptureModeValue = "wasapi-shared-event";
    private const ushort PropVariantString = 31;

    private static readonly PropertyKey FriendlyNameKey = new(new Guid("A45C254E-DF1C-4EFD-8020-67D146A850E0"), 14);

    public static IReadOnlyList<InputDeviceDescriptor> EnumerateCaptureDevices()
    {
        using WindowsComApartmentScope apartment = WindowsComApartmentScope.Enter();
        object? enumeratorObject = null;
        IMMDeviceCollection? collection = null;

        try
        {
            IMMDeviceEnumerator enumerator = CreateEnumerator(out enumeratorObject);
            Dictionary<MicrophoneEndpointRole, string> defaults = GetDefaultEndpointIds(enumerator);

            int result = enumerator.EnumAudioEndpoints(EDataFlow.Capture, DeviceState.Active, out collection);
            WindowsMicrophoneFaults.ThrowIfFailed(result, "Microphone endpoint enumeration failed.");

            result = collection.GetCount(out uint count);
            WindowsMicrophoneFaults.ThrowIfFailed(result, "Microphone endpoint count failed.");

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
                    WindowsComInterop.Release(device);
                }
            }

            return devices;
        }
        finally
        {
            WindowsComInterop.Release(collection);
            WindowsComInterop.Release(enumeratorObject);
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
            if (result == ComNative.E_NOTFOUND)
                return null;

            WindowsMicrophoneFaults.ThrowIfFailed(result, "Default microphone endpoint lookup failed.");
            return CreateDescriptor(device, new Dictionary<MicrophoneEndpointRole, string> { [role] = GetDeviceId(device) });
        }
        finally
        {
            WindowsComInterop.Release(device);
            WindowsComInterop.Release(enumeratorObject);
        }
    }

    public static string? GetNativeEndpointId(InputDeviceDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        return descriptor.Capabilities.FirstOrDefault(static capability => capability.Name == EndpointIdCapability).Value;
    }

    public static IMMDevice GetDevice(InputDeviceDescriptor descriptor, MicrophoneEndpointRole role, out object? enumeratorObject)
    {
        IMMDeviceEnumerator enumerator = CreateEnumerator(out enumeratorObject);
        string? endpointId = GetNativeEndpointId(descriptor);
        int result;

        if (string.IsNullOrWhiteSpace(endpointId))
        {
            result = enumerator.GetDefaultAudioEndpoint(EDataFlow.Capture, ToRole(role), out IMMDevice defaultDevice);
            WindowsMicrophoneFaults.ThrowIfFailed(result, "Default microphone endpoint lookup failed.");
            return defaultDevice;
        }

        result = enumerator.GetDevice(endpointId, out IMMDevice device);
        WindowsMicrophoneFaults.ThrowIfFailed(result, "Microphone endpoint lookup failed.");

        return device;
    }

    private static IMMDeviceEnumerator CreateEnumerator(out object? enumeratorObject)
    {
        Guid classId = WindowsWasapiNative.MMDeviceEnumeratorClassId;
        Guid interfaceId = WindowsWasapiNative.IMMDeviceEnumeratorId;

        // The raw-pointer overload: an object from the built-in COM marshaller cannot be
        // cast to the source-generated IMMDeviceEnumerator.
        int result = ComNative.CoCreateInstance(in classId, IntPtr.Zero, ComNative.CLSCTX_INPROC_SERVER,
                in interfaceId, out IntPtr enumeratorPointer);

        WindowsMicrophoneFaults.ThrowIfFailed(result, "MMDeviceEnumerator activation failed.");

        IMMDeviceEnumerator enumerator = WindowsComInterop.Wrap<IMMDeviceEnumerator>(enumeratorPointer)
            ?? throw WindowsMicrophoneFaults.CreateException(unchecked((int)0x80004002), "MMDeviceEnumerator interface activation failed.");

        enumeratorObject = enumerator;
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
                WindowsComInterop.Release(endpoint);
            }
        }

        return defaults;
    }

    private static InputDeviceDescriptor CreateDescriptor(IMMDevice device, IReadOnlyDictionary<MicrophoneEndpointRole, string> defaults)
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
        return new InputDeviceDescriptor(InputDeviceId.FromOpaqueValue(ToStableInputId(endpointId)), InputKind.Microphone,
            displayName, availability, capabilities);
    }

    private static string GetDeviceId(IMMDevice device)
    {
        WindowsMicrophoneFaults.ThrowIfFailed(device.GetId(out IntPtr id), "Microphone endpoint id lookup failed.");

        try
        {
            return Marshal.PtrToStringUni(id)
                ?? throw WindowsMicrophoneFaults.CreateException(unchecked((int)0x80004003), "Microphone endpoint id lookup returned no id.");
        }
        finally
        {
            ComNative.CoTaskMemFree(id);
        }
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
                _ = WindowsWasapiNative.PropVariantClear(ref value);

            WindowsComInterop.Release(propertyStore);
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
}
