using System;
using System.Runtime.InteropServices;

namespace Broiler.Input.Microphone.Windows;

internal static class WindowsWasapiNative
{
    internal const int S_OK = 0;
    internal const int S_FALSE = 1;
    internal const int E_ACCESSDENIED = unchecked((int)0x80070005);
    internal const int E_NOINTERFACE = unchecked((int)0x80004002);
    internal const int E_NOTFOUND = unchecked((int)0x80070490);
    internal const int RPC_E_CHANGED_MODE = unchecked((int)0x80010106);
    internal const int AUDCLNT_E_DEVICE_INVALIDATED = unchecked((int)0x88890004);
    internal const int AUDCLNT_E_UNSUPPORTED_FORMAT = unchecked((int)0x88890008);
    internal const int AUDCLNT_E_DEVICE_IN_USE = unchecked((int)0x8889000A);
    internal const int AUDCLNT_E_SERVICE_NOT_RUNNING = unchecked((int)0x88890010);

    internal const uint CLSCTX_INPROC_SERVER = 0x1;
    internal const uint COINIT_MULTITHREADED = 0x0;
    internal const uint WAIT_OBJECT_0 = 0;
    internal const uint WAIT_TIMEOUT = 258;
    internal const uint WAIT_FAILED = 0xFFFFFFFF;

    internal static readonly Guid MMDeviceEnumeratorClassId = new("BCDE0395-E52F-467C-8E3D-C4579291692E");
    internal static readonly Guid IMMDeviceEnumeratorId = new("A95664D2-9614-4F35-A746-DE8DB63617E6");
    internal static readonly Guid IAudioClientId = new("1CB9AD4C-DBFA-4c32-B178-C2F568A703B2");
    internal static readonly Guid IAudioCaptureClientId = new("C8ADBD64-E71E-48a0-A4DE-185C395CD317");

    internal static readonly Guid PcmSubFormat = new("00000001-0000-0010-8000-00aa00389b71");
    internal static readonly Guid IeeeFloatSubFormat = new("00000003-0000-0010-8000-00aa00389b71");

    [DllImport("ole32.dll")]
    internal static extern int CoInitializeEx(IntPtr reserved, uint coInit);

    [DllImport("ole32.dll")]
    internal static extern void CoUninitialize();

    [DllImport("ole32.dll")]
    internal static extern int CoCreateInstance(
        ref Guid classId,
        IntPtr outerUnknown,
        uint classContext,
        ref Guid interfaceId,
        [MarshalAs(UnmanagedType.IUnknown)] out object? instance);

    [DllImport("ole32.dll")]
    internal static extern void CoTaskMemFree(IntPtr value);

    [DllImport("ole32.dll")]
    internal static extern int PropVariantClear(ref PropVariant value);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern IntPtr CreateEventW(
        IntPtr eventAttributes,
        [MarshalAs(UnmanagedType.Bool)] bool manualReset,
        [MarshalAs(UnmanagedType.Bool)] bool initialState,
        string? name);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetEvent(IntPtr handle);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool CloseHandle(IntPtr handle);

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern uint WaitForSingleObject(IntPtr handle, uint milliseconds);
}

internal sealed class WindowsComApartmentScope : IDisposable
{
    private readonly bool _shouldUninitialize;

    private WindowsComApartmentScope(bool shouldUninitialize)
    {
        _shouldUninitialize = shouldUninitialize;
    }

    public static WindowsComApartmentScope Enter()
    {
        int result = WindowsWasapiNative.CoInitializeEx(IntPtr.Zero, WindowsWasapiNative.COINIT_MULTITHREADED);
        if (result == WindowsWasapiNative.S_OK || result == WindowsWasapiNative.S_FALSE)
            return new WindowsComApartmentScope(shouldUninitialize: true);
        if (result == WindowsWasapiNative.RPC_E_CHANGED_MODE)
            return new WindowsComApartmentScope(shouldUninitialize: false);

        throw WindowsMicrophoneFaults.CreateException(result, "COM initialization failed.");
    }

    public void Dispose()
    {
        if (_shouldUninitialize)
            WindowsWasapiNative.CoUninitialize();
    }
}

internal static class WindowsMicrophoneFaults
{
    public static InputMicrophoneException CreateException(int hresult, string message) =>
        new(CreateFault(hresult, message));

    public static Broiler.Input.InputFault CreateFault(int hresult, string message)
    {
        Broiler.Input.InputErrorCategory category = hresult switch
        {
            WindowsWasapiNative.E_ACCESSDENIED => Broiler.Input.InputErrorCategory.PermissionDenied,
            WindowsWasapiNative.AUDCLNT_E_DEVICE_IN_USE => Broiler.Input.InputErrorCategory.DeviceBusy,
            WindowsWasapiNative.AUDCLNT_E_UNSUPPORTED_FORMAT => Broiler.Input.InputErrorCategory.UnsupportedCapability,
            WindowsWasapiNative.AUDCLNT_E_DEVICE_INVALIDATED => Broiler.Input.InputErrorCategory.DeviceRemoved,
            WindowsWasapiNative.E_NOTFOUND => Broiler.Input.InputErrorCategory.DeviceNotFound,
            WindowsWasapiNative.AUDCLNT_E_SERVICE_NOT_RUNNING => Broiler.Input.InputErrorCategory.HostUnavailable,
            _ => Broiler.Input.InputErrorCategory.NativeFailure,
        };

        return new Broiler.Input.InputFault(
            category,
            FormatNativeFailureMessage(message, hresult),
            nativeErrorCode: hresult,
            nativeFacility: "WASAPI");
    }

    public static void ThrowIfFailed(int hresult, string message)
    {
        if (hresult < 0)
            throw CreateException(hresult, message);
    }

    private static string FormatNativeFailureMessage(string message, int hresult)
    {
        string formattedCode = "0x" + unchecked((uint)hresult).ToString("X8");
        string? name = GetNativeErrorName(hresult);
        string suffix = name is null
            ? "WASAPI HRESULT " + formattedCode
            : "WASAPI HRESULT " + formattedCode + " (" + name + ")";
        return message + " Native error: " + suffix + ".";
    }

    private static string? GetNativeErrorName(int hresult) => hresult switch
    {
        WindowsWasapiNative.E_ACCESSDENIED => "E_ACCESSDENIED",
        WindowsWasapiNative.E_NOINTERFACE => "E_NOINTERFACE",
        WindowsWasapiNative.E_NOTFOUND => "E_NOTFOUND",
        WindowsWasapiNative.RPC_E_CHANGED_MODE => "RPC_E_CHANGED_MODE",
        WindowsWasapiNative.AUDCLNT_E_DEVICE_INVALIDATED => "AUDCLNT_E_DEVICE_INVALIDATED",
        WindowsWasapiNative.AUDCLNT_E_UNSUPPORTED_FORMAT => "AUDCLNT_E_UNSUPPORTED_FORMAT",
        WindowsWasapiNative.AUDCLNT_E_DEVICE_IN_USE => "AUDCLNT_E_DEVICE_IN_USE",
        WindowsWasapiNative.AUDCLNT_E_SERVICE_NOT_RUNNING => "AUDCLNT_E_SERVICE_NOT_RUNNING",
        _ => null,
    };
}

internal enum EDataFlow
{
    Render = 0,
    Capture = 1,
    All = 2,
}

internal enum ERole
{
    Console = 0,
    Multimedia = 1,
    Communications = 2,
}

[Flags]
internal enum DeviceState : uint
{
    Active = 0x00000001,
    Disabled = 0x00000002,
    NotPresent = 0x00000004,
    Unplugged = 0x00000008,
    All = 0x0000000F,
}

internal enum StorageAccess
{
    Read = 0,
}

internal enum AudioClientShareMode
{
    Shared = 0,
    Exclusive = 1,
}

[Flags]
internal enum AudioClientStreamFlags : uint
{
    None = 0,
    EventCallback = 0x00040000,
}

[Flags]
internal enum AudioClientBufferFlags : uint
{
    None = 0,
    DataDiscontinuity = 0x1,
    Silent = 0x2,
    TimestampError = 0x4,
}

[StructLayout(LayoutKind.Sequential)]
internal struct PropertyKey
{
    public PropertyKey(Guid formatId, uint propertyId)
    {
        FormatId = formatId;
        PropertyId = propertyId;
    }

    public Guid FormatId;

    public uint PropertyId;
}

[StructLayout(LayoutKind.Sequential)]
internal struct PropVariant
{
    public ushort ValueType;
    private ushort _reserved1;
    private ushort _reserved2;
    private ushort _reserved3;
    public IntPtr PointerValue;
}

[StructLayout(LayoutKind.Sequential, Pack = 2)]
internal struct WaveFormatEx
{
    public ushort FormatTag;
    public ushort Channels;
    public uint SamplesPerSec;
    public uint AvgBytesPerSec;
    public ushort BlockAlign;
    public ushort BitsPerSample;
    public ushort Size;
}

[StructLayout(LayoutKind.Sequential, Pack = 2)]
internal struct WaveFormatExtensible
{
    public WaveFormatEx Format;
    public ushort ValidBitsPerSample;
    public uint ChannelMask;
    public Guid SubFormat;
}

[ComImport]
[Guid("A95664D2-9614-4F35-A746-DE8DB63617E6")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMMDeviceEnumerator
{
    [PreserveSig]
    int EnumAudioEndpoints(EDataFlow dataFlow, DeviceState stateMask, out IMMDeviceCollection devices);

    [PreserveSig]
    int GetDefaultAudioEndpoint(EDataFlow dataFlow, ERole role, out IMMDevice endpoint);

    [PreserveSig]
    int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IMMDevice device);

    [PreserveSig]
    int RegisterEndpointNotificationCallback(IMMNotificationClient client);

    [PreserveSig]
    int UnregisterEndpointNotificationCallback(IMMNotificationClient client);
}

[ComImport]
[Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMMDeviceCollection
{
    [PreserveSig]
    int GetCount(out uint count);

    [PreserveSig]
    int Item(uint itemIndex, out IMMDevice device);
}

[ComImport]
[Guid("D666063F-1587-4E43-81F1-B948E807363F")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMMDevice
{
    [PreserveSig]
    int Activate(
        ref Guid interfaceId,
        uint classContext,
        IntPtr activationParams,
        [MarshalAs(UnmanagedType.IUnknown)] out object? activatedInterface);

    [PreserveSig]
    int OpenPropertyStore(StorageAccess access, out IPropertyStore properties);

    [PreserveSig]
    int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);

    [PreserveSig]
    int GetState(out DeviceState state);
}

[ComImport]
[Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IPropertyStore
{
    [PreserveSig]
    int GetCount(out uint propertyCount);

    [PreserveSig]
    int GetAt(uint propertyIndex, out PropertyKey key);

    [PreserveSig]
    int GetValue(ref PropertyKey key, out PropVariant value);

    [PreserveSig]
    int SetValue(ref PropertyKey key, ref PropVariant value);

    [PreserveSig]
    int Commit();
}

[ComImport]
[Guid("7991EEC9-7E89-4D85-8390-6C703CEC60C0")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMMNotificationClient
{
    [PreserveSig]
    int OnDeviceStateChanged([MarshalAs(UnmanagedType.LPWStr)] string deviceId, DeviceState newState);

    [PreserveSig]
    int OnDeviceAdded([MarshalAs(UnmanagedType.LPWStr)] string deviceId);

    [PreserveSig]
    int OnDeviceRemoved([MarshalAs(UnmanagedType.LPWStr)] string deviceId);

    [PreserveSig]
    int OnDefaultDeviceChanged(EDataFlow flow, ERole role, [MarshalAs(UnmanagedType.LPWStr)] string defaultDeviceId);

    [PreserveSig]
    int OnPropertyValueChanged([MarshalAs(UnmanagedType.LPWStr)] string deviceId, PropertyKey key);
}

[ComImport]
[Guid("1CB9AD4C-DBFA-4C32-B178-C2F568A703B2")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioClient
{
    [PreserveSig]
    int Initialize(
        AudioClientShareMode shareMode,
        AudioClientStreamFlags streamFlags,
        long bufferDuration,
        long periodicity,
        IntPtr format,
        IntPtr audioSessionGuid);

    [PreserveSig]
    int GetBufferSize(out uint bufferFrameCount);

    [PreserveSig]
    int GetStreamLatency(out long latency);

    [PreserveSig]
    int GetCurrentPadding(out uint currentPaddingFrameCount);

    [PreserveSig]
    int IsFormatSupported(
        AudioClientShareMode shareMode,
        IntPtr format,
        out IntPtr closestMatch);

    [PreserveSig]
    int GetMixFormat(out IntPtr deviceFormat);

    [PreserveSig]
    int GetDevicePeriod(out long defaultDevicePeriod, out long minimumDevicePeriod);

    [PreserveSig]
    int Start();

    [PreserveSig]
    int Stop();

    [PreserveSig]
    int Reset();

    [PreserveSig]
    int SetEventHandle(IntPtr eventHandle);

    [PreserveSig]
    int GetService(
        ref Guid interfaceId,
        [MarshalAs(UnmanagedType.IUnknown)] out object? serviceInterface);
}

[ComImport]
[Guid("C8ADBD64-E71E-48A0-A4DE-185C395CD317")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioCaptureClient
{
    [PreserveSig]
    int GetBuffer(
        out IntPtr data,
        out uint framesToRead,
        out AudioClientBufferFlags flags,
        out ulong devicePosition,
        out ulong qpcPosition);

    [PreserveSig]
    int ReleaseBuffer(uint framesRead);

    [PreserveSig]
    int GetNextPacketSize(out uint framesInNextPacket);
}
