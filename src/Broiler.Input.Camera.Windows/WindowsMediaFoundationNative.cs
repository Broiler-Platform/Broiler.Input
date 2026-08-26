using System;
using System.Runtime.InteropServices;

namespace Broiler.Input.Camera.Windows;

internal static class WindowsMediaFoundationNative
{
    internal const int S_OK = 0;
    internal const int S_FALSE = 1;
    internal const int MF_VERSION = 0x00020070;
    internal const int MFSTARTUP_NOSOCKET = 0x1;
    internal const uint COINIT_MULTITHREADED = 0x0;

    internal const int E_ACCESSDENIED = unchecked((int)0x80070005);
    internal const int E_NOTFOUND = unchecked((int)0x80070490);
    internal const int RPC_E_CHANGED_MODE = unchecked((int)0x80010106);
    internal const int MF_E_PLATFORM_NOT_INITIALIZED = unchecked((int)0xC00D36B0);
    internal const int MF_E_INVALIDMEDIATYPE = unchecked((int)0xC00D36B4);
    internal const int MF_E_NOT_INITIALIZED = unchecked((int)0xC00D36B6);
    internal const int MF_E_NO_MORE_TYPES = unchecked((int)0xC00D36B9);
    internal const int MF_E_NOT_FOUND = unchecked((int)0xC00D36D5);
    internal const int MF_E_NOT_AVAILABLE = unchecked((int)0xC00D36D6);
    internal const int MF_E_ATTRIBUTENOTFOUND = unchecked((int)0xC00D36E6);
    internal const int MF_E_DISABLED_IN_SAFEMODE = unchecked((int)0xC00D36EF);
    internal const int MF_E_SHUTDOWN = unchecked((int)0xC00D3E85);
    internal const int MF_E_VIDEO_RECORDING_DEVICE_INVALIDATED = unchecked((int)0xC00D3EA2);
    internal const int MF_E_VIDEO_RECORDING_DEVICE_PREEMPTED = unchecked((int)0xC00D3EA3);
    internal const int MF_E_VIDEO_DEVICE_LOCKED = unchecked((int)0xC00D4E24);
    internal const int MF_E_NO_CAPTURE_DEVICES_AVAILABLE = unchecked((int)0xC00DABE0);
    internal const int MF_E_CAPTURE_SOURCE_NO_VIDEO_STREAM_PRESENT = unchecked((int)0xC00DABE7);
    internal const int MF_E_UNSUPPORTED_CAPTURE_DEVICE_PRESENT = unchecked((int)0xC00DABED);

    internal const int MF_SOURCE_READER_FIRST_VIDEO_STREAM = unchecked((int)0xFFFFFFFC);
    internal const int MF_SOURCE_READER_CURRENT_TYPE_INDEX = unchecked((int)0xFFFFFFFF);

    internal static readonly Guid IMFMediaSourceId = new("279A808D-AEC7-40C8-9C6B-A6B492C78A66");
    internal static readonly Guid MFMediaTypeVideo = new("73646976-0000-0010-8000-00AA00389B71");
    internal static readonly Guid MFVideoFormatRgb32 = new("00000016-0000-0010-8000-00AA00389B71");
    internal static readonly Guid MFVideoFormatRgb24 = new("00000014-0000-0010-8000-00AA00389B71");
    internal static readonly Guid MFVideoFormatNv12 = new("3231564E-0000-0010-8000-00AA00389B71");
    internal static readonly Guid MFVideoFormatYuy2 = new("32595559-0000-0010-8000-00AA00389B71");
    internal static readonly Guid MFVideoFormatMjpg = new("47504A4D-0000-0010-8000-00AA00389B71");
    internal static readonly Guid MFVideoFormatL8 = new("00000032-0000-0010-8000-00AA00389B71");

    internal static readonly Guid MF_DEVSOURCE_ATTRIBUTE_SOURCE_TYPE = new("C60AC5FE-252A-478F-A0EF-BC8FA5F7CAD3");
    internal static readonly Guid MF_DEVSOURCE_ATTRIBUTE_SOURCE_TYPE_VIDCAP_GUID = new("8AC3587A-4AE7-42D8-99E0-0A6013EEF90F");
    internal static readonly Guid MF_DEVSOURCE_ATTRIBUTE_FRIENDLY_NAME = new("60D0E559-52F8-4FA2-BBCE-ACDB34A8EC01");
    internal static readonly Guid MF_DEVSOURCE_ATTRIBUTE_SOURCE_TYPE_VIDCAP_SYMBOLIC_LINK = new("58F0AAD8-22BF-4F8A-BB3D-D2C4978C6E2F");
    internal static readonly Guid MF_DEVSOURCE_ATTRIBUTE_FRAMESERVER_SHARE_MODE = new("44D1A9BC-2999-4238-AE43-0730CEB2AB1B");

    internal static readonly Guid MF_MT_MAJOR_TYPE = new("48EBA18E-F8C9-4687-BF11-0A74C9F96A8F");
    internal static readonly Guid MF_MT_SUBTYPE = new("F7E34C9A-42E8-4714-B74B-CB29D72C35E5");
    internal static readonly Guid MF_MT_FRAME_SIZE = new("1652C33D-D6B2-4012-B834-72030849A37D");
    internal static readonly Guid MF_MT_FRAME_RATE = new("C459A2E8-3D2C-4E44-B132-FEE5156C7BB0");
    internal static readonly Guid MF_MT_DEFAULT_STRIDE = new("644B4E48-1E02-4516-B0EB-C01CA9D49AC6");
    internal static readonly Guid MF_SOURCE_READER_ENABLE_ADVANCED_VIDEO_PROCESSING = new("0F81DA2C-B537-4672-A8B2-A681B17307A3");
    internal static readonly Guid MF_SOURCE_READER_DISCONNECT_MEDIASOURCE_ON_SHUTDOWN = new("56B67165-219E-456D-A22E-2D3004C7FE56");

    [DllImport("mfplat.dll", ExactSpelling = true)]
    internal static extern int MFStartup(int version, int flags);

    [DllImport("mfplat.dll", ExactSpelling = true)]
    internal static extern int MFShutdown();

    [DllImport("mfplat.dll", ExactSpelling = true)]
    internal static extern int MFCreateAttributes(out IMFAttributes attributes, uint initialSize);

    [DllImport("mf.dll", ExactSpelling = true)]
    internal static extern int MFEnumDeviceSources(
        IMFAttributes attributes,
        out IntPtr devices,
        out uint count);

    [DllImport("mf.dll", ExactSpelling = true)]
    internal static extern int MFCreateDeviceSource(
        IMFAttributes attributes,
        out IMFMediaSource mediaSource);

    [DllImport("mfreadwrite.dll", ExactSpelling = true)]
    internal static extern int MFCreateSourceReaderFromMediaSource(
        IMFMediaSource mediaSource,
        IMFAttributes? attributes,
        out IMFSourceReader sourceReader);

    [DllImport("ole32.dll")]
    internal static extern void CoTaskMemFree(IntPtr value);

    [DllImport("ole32.dll")]
    internal static extern int CoInitializeEx(IntPtr reserved, uint coInit);

    [DllImport("ole32.dll")]
    internal static extern void CoUninitialize();
}

internal sealed class MediaFoundationPlatformScope : IDisposable
{
    private readonly bool _shouldUninitializeCom;
    private bool _mediaFoundationStarted;
    private bool _disposed;

    public MediaFoundationPlatformScope()
    {
        int comResult = WindowsMediaFoundationNative.CoInitializeEx(IntPtr.Zero, WindowsMediaFoundationNative.COINIT_MULTITHREADED);
        if (comResult == WindowsMediaFoundationNative.S_OK || comResult == WindowsMediaFoundationNative.S_FALSE)
            _shouldUninitializeCom = true;
        else if (comResult != WindowsMediaFoundationNative.RPC_E_CHANGED_MODE)
            throw WindowsCameraFaults.CreateException(comResult, "COM initialization failed.", "COM");

        try
        {
            WindowsCameraFaults.ThrowIfFailed(
                WindowsMediaFoundationNative.MFStartup(
                    WindowsMediaFoundationNative.MF_VERSION,
                    WindowsMediaFoundationNative.MFSTARTUP_NOSOCKET),
                "Media Foundation startup failed.");
            _mediaFoundationStarted = true;
        }
        catch
        {
            if (_shouldUninitializeCom)
                WindowsMediaFoundationNative.CoUninitialize();

            throw;
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        if (_mediaFoundationStarted)
            WindowsMediaFoundationNative.MFShutdown();
        if (_shouldUninitializeCom)
            WindowsMediaFoundationNative.CoUninitialize();
        _disposed = true;
    }
}

internal static class WindowsCameraFaults
{
    public static InputCameraException CreateException(int hresult, string message, string nativeFacility = "MediaFoundation") =>
        new(CreateFault(hresult, message, nativeFacility));

    public static Broiler.Input.InputFault CreateFault(int hresult, string message, string nativeFacility = "MediaFoundation")
    {
        Broiler.Input.InputErrorCategory category = hresult switch
        {
            WindowsMediaFoundationNative.E_ACCESSDENIED => Broiler.Input.InputErrorCategory.PermissionDenied,
            WindowsMediaFoundationNative.E_NOTFOUND or
                WindowsMediaFoundationNative.MF_E_NOT_FOUND or
                WindowsMediaFoundationNative.MF_E_NO_CAPTURE_DEVICES_AVAILABLE or
                WindowsMediaFoundationNative.MF_E_CAPTURE_SOURCE_NO_VIDEO_STREAM_PRESENT => Broiler.Input.InputErrorCategory.DeviceNotFound,
            WindowsMediaFoundationNative.MF_E_VIDEO_DEVICE_LOCKED or
                WindowsMediaFoundationNative.MF_E_VIDEO_RECORDING_DEVICE_PREEMPTED => Broiler.Input.InputErrorCategory.DeviceBusy,
            WindowsMediaFoundationNative.MF_E_INVALIDMEDIATYPE => Broiler.Input.InputErrorCategory.UnsupportedCapability,
            WindowsMediaFoundationNative.MF_E_UNSUPPORTED_CAPTURE_DEVICE_PRESENT => Broiler.Input.InputErrorCategory.UnsupportedCapability,
            WindowsMediaFoundationNative.MF_E_SHUTDOWN or
                WindowsMediaFoundationNative.MF_E_VIDEO_RECORDING_DEVICE_INVALIDATED => Broiler.Input.InputErrorCategory.DeviceRemoved,
            WindowsMediaFoundationNative.MF_E_PLATFORM_NOT_INITIALIZED or
                WindowsMediaFoundationNative.MF_E_NOT_INITIALIZED or
                WindowsMediaFoundationNative.MF_E_NOT_AVAILABLE or
                WindowsMediaFoundationNative.MF_E_DISABLED_IN_SAFEMODE => Broiler.Input.InputErrorCategory.HostUnavailable,
            _ => Broiler.Input.InputErrorCategory.NativeFailure,
        };

        return new Broiler.Input.InputFault(
            category,
            FormatNativeFailureMessage(message, hresult, nativeFacility),
            nativeErrorCode: hresult,
            nativeFacility: nativeFacility);
    }

    public static void ThrowIfFailed(int hresult, string message)
    {
        if (hresult < 0)
            throw CreateException(hresult, message);
    }

    private static string FormatNativeFailureMessage(string message, int hresult, string nativeFacility)
    {
        string formattedCode = "0x" + unchecked((uint)hresult).ToString("X8");
        string? name = GetNativeErrorName(hresult);
        string suffix = name is null
            ? nativeFacility + " HRESULT " + formattedCode
            : nativeFacility + " HRESULT " + formattedCode + " (" + name + ")";
        return message + " Native error: " + suffix + ".";
    }

    private static string? GetNativeErrorName(int hresult) => hresult switch
    {
        WindowsMediaFoundationNative.E_ACCESSDENIED => "E_ACCESSDENIED",
        WindowsMediaFoundationNative.E_NOTFOUND => "E_NOTFOUND",
        WindowsMediaFoundationNative.RPC_E_CHANGED_MODE => "RPC_E_CHANGED_MODE",
        WindowsMediaFoundationNative.MF_E_PLATFORM_NOT_INITIALIZED => "MF_E_PLATFORM_NOT_INITIALIZED",
        WindowsMediaFoundationNative.MF_E_INVALIDMEDIATYPE => "MF_E_INVALIDMEDIATYPE",
        WindowsMediaFoundationNative.MF_E_NOT_INITIALIZED => "MF_E_NOT_INITIALIZED",
        WindowsMediaFoundationNative.MF_E_NO_MORE_TYPES => "MF_E_NO_MORE_TYPES",
        WindowsMediaFoundationNative.MF_E_NOT_FOUND => "MF_E_NOT_FOUND",
        WindowsMediaFoundationNative.MF_E_NOT_AVAILABLE => "MF_E_NOT_AVAILABLE",
        WindowsMediaFoundationNative.MF_E_ATTRIBUTENOTFOUND => "MF_E_ATTRIBUTENOTFOUND",
        WindowsMediaFoundationNative.MF_E_DISABLED_IN_SAFEMODE => "MF_E_DISABLED_IN_SAFEMODE",
        WindowsMediaFoundationNative.MF_E_SHUTDOWN => "MF_E_SHUTDOWN",
        WindowsMediaFoundationNative.MF_E_VIDEO_RECORDING_DEVICE_INVALIDATED => "MF_E_VIDEO_RECORDING_DEVICE_INVALIDATED",
        WindowsMediaFoundationNative.MF_E_VIDEO_RECORDING_DEVICE_PREEMPTED => "MF_E_VIDEO_RECORDING_DEVICE_PREEMPTED",
        WindowsMediaFoundationNative.MF_E_VIDEO_DEVICE_LOCKED => "MF_E_VIDEO_DEVICE_LOCKED",
        WindowsMediaFoundationNative.MF_E_NO_CAPTURE_DEVICES_AVAILABLE => "MF_E_NO_CAPTURE_DEVICES_AVAILABLE",
        WindowsMediaFoundationNative.MF_E_CAPTURE_SOURCE_NO_VIDEO_STREAM_PRESENT => "MF_E_CAPTURE_SOURCE_NO_VIDEO_STREAM_PRESENT",
        WindowsMediaFoundationNative.MF_E_UNSUPPORTED_CAPTURE_DEVICE_PRESENT => "MF_E_UNSUPPORTED_CAPTURE_DEVICE_PRESENT",
        _ => null,
    };
}

[Flags]
internal enum SourceReaderFlags
{
    None = 0,
    Error = 0x00000001,
    EndOfStream = 0x00000002,
    NewStream = 0x00000004,
    NativeMediaTypeChanged = 0x00000010,
    CurrentMediaTypeChanged = 0x00000020,
    StreamTick = 0x00000100,
}

[ComImport]
[Guid("2CD2D921-C447-44A7-A13C-4ADABFC247E3")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMFAttributes
{
    [PreserveSig]
    int GetItem(ref Guid key, IntPtr value);

    [PreserveSig]
    int GetItemType(ref Guid key, out int type);

    [PreserveSig]
    int CompareItem(ref Guid key, IntPtr value, [MarshalAs(UnmanagedType.Bool)] out bool result);

    [PreserveSig]
    int Compare(IMFAttributes attributes, int matchType, [MarshalAs(UnmanagedType.Bool)] out bool result);

    [PreserveSig]
    int GetUINT32(ref Guid key, out int value);

    [PreserveSig]
    int GetUINT64(ref Guid key, out long value);

    [PreserveSig]
    int GetDouble(ref Guid key, out double value);

    [PreserveSig]
    int GetGUID(ref Guid key, out Guid value);

    [PreserveSig]
    int GetStringLength(ref Guid key, out int length);

    [PreserveSig]
    int GetString(ref Guid key, IntPtr value, int size, out int length);

    [PreserveSig]
    int GetAllocatedString(ref Guid key, out IntPtr value, out int length);

    [PreserveSig]
    int GetBlobSize(ref Guid key, out int size);

    [PreserveSig]
    int GetBlob(ref Guid key, IntPtr buffer, int bufferSize, out int blobSize);

    [PreserveSig]
    int GetAllocatedBlob(ref Guid key, out IntPtr buffer, out int size);

    [PreserveSig]
    int GetUnknown(
        ref Guid key,
        ref Guid interfaceId,
        [MarshalAs(UnmanagedType.IUnknown)] out object? value);

    [PreserveSig]
    int SetItem(ref Guid key, IntPtr value);

    [PreserveSig]
    int DeleteItem(ref Guid key);

    [PreserveSig]
    int DeleteAllItems();

    [PreserveSig]
    int SetUINT32(ref Guid key, int value);

    [PreserveSig]
    int SetUINT64(ref Guid key, long value);

    [PreserveSig]
    int SetDouble(ref Guid key, double value);

    [PreserveSig]
    int SetGUID(ref Guid key, ref Guid value);

    [PreserveSig]
    int SetString(ref Guid key, [MarshalAs(UnmanagedType.LPWStr)] string value);

    [PreserveSig]
    int SetBlob(ref Guid key, IntPtr buffer, int size);

    [PreserveSig]
    int SetUnknown(ref Guid key, [MarshalAs(UnmanagedType.IUnknown)] object? value);

    [PreserveSig]
    int LockStore();

    [PreserveSig]
    int UnlockStore();

    [PreserveSig]
    int GetCount(out int count);

    [PreserveSig]
    int GetItemByIndex(int index, out Guid key, IntPtr value);

    [PreserveSig]
    int CopyAllItems(IMFAttributes destination);
}

[ComImport]
[Guid("7FEE9E9A-4A89-47A6-899C-B6A53A70FB67")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMFActivate : IMFAttributes
{
    [PreserveSig]
    int ActivateObject(
        ref Guid interfaceId,
        [MarshalAs(UnmanagedType.IUnknown)] out object? activatedObject);

    [PreserveSig]
    int ShutdownObject();

    [PreserveSig]
    int DetachObject();
}

[ComImport]
[Guid("279A808D-AEC7-40C8-9C6B-A6B492C78A66")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMFMediaSource
{
    [PreserveSig]
    int GetEvent(
        int flags,
        [MarshalAs(UnmanagedType.IUnknown)] out object? mediaEvent);

    [PreserveSig]
    int BeginGetEvent(
        [MarshalAs(UnmanagedType.IUnknown)] object? callback,
        [MarshalAs(UnmanagedType.IUnknown)] object? state);

    [PreserveSig]
    int EndGetEvent(
        [MarshalAs(UnmanagedType.IUnknown)] object result,
        [MarshalAs(UnmanagedType.IUnknown)] out object? mediaEvent);

    [PreserveSig]
    int QueueEvent(
        int mediaEventType,
        ref Guid extendedType,
        int status,
        IntPtr value);

    [PreserveSig]
    int GetCharacteristics(out int characteristics);

    [PreserveSig]
    int CreatePresentationDescriptor([MarshalAs(UnmanagedType.IUnknown)] out object? presentationDescriptor);

    [PreserveSig]
    int Start(
        [MarshalAs(UnmanagedType.IUnknown)] object presentationDescriptor,
        ref Guid timeFormat,
        IntPtr startPosition);

    [PreserveSig]
    int Stop();

    [PreserveSig]
    int Pause();

    [PreserveSig]
    int Shutdown();
}

[ComImport]
[Guid("44AE0FA8-EA31-4109-8D2E-4CAE4997C555")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMFMediaType : IMFAttributes
{
    [PreserveSig]
    int GetMajorType(out Guid majorType);

    [PreserveSig]
    int IsCompressedFormat([MarshalAs(UnmanagedType.Bool)] out bool compressed);

    [PreserveSig]
    int IsEqual(IMFMediaType mediaType, out int flags);

    [PreserveSig]
    int GetRepresentation(Guid representation, out IntPtr representationData);

    [PreserveSig]
    int FreeRepresentation(Guid representation, IntPtr representationData);
}

[ComImport]
[Guid("70AE66F2-C809-4E4F-8915-BDCB406B7993")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMFSourceReader
{
    [PreserveSig]
    int GetStreamSelection(int streamIndex, [MarshalAs(UnmanagedType.Bool)] out bool selected);

    [PreserveSig]
    int SetStreamSelection(int streamIndex, [MarshalAs(UnmanagedType.Bool)] bool selected);

    [PreserveSig]
    int GetNativeMediaType(int streamIndex, int mediaTypeIndex, out IMFMediaType mediaType);

    [PreserveSig]
    int GetCurrentMediaType(int streamIndex, out IMFMediaType mediaType);

    [PreserveSig]
    int SetCurrentMediaType(int streamIndex, IntPtr reserved, IMFMediaType mediaType);

    [PreserveSig]
    int SetCurrentPosition(ref Guid timeFormat, IntPtr position);

    [PreserveSig]
    int ReadSample(
        int streamIndex,
        int controlFlags,
        out int actualStreamIndex,
        out SourceReaderFlags streamFlags,
        out long timestamp,
        out IMFSample? sample);

    [PreserveSig]
    int Flush(int streamIndex);

    [PreserveSig]
    int GetServiceForStream(
        int streamIndex,
        ref Guid service,
        ref Guid interfaceId,
        [MarshalAs(UnmanagedType.IUnknown)] out object? serviceObject);

    [PreserveSig]
    int GetPresentationAttribute(int streamIndex, ref Guid attribute, IntPtr value);
}

[ComImport]
[Guid("C40A00F2-B93A-4D80-AE8C-5A1C634F58E4")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMFSample
{
    [PreserveSig]
    int GetItem(ref Guid key, IntPtr value);

    [PreserveSig]
    int GetItemType(ref Guid key, out int type);

    [PreserveSig]
    int CompareItem(ref Guid key, IntPtr value, [MarshalAs(UnmanagedType.Bool)] out bool result);

    [PreserveSig]
    int Compare(IMFAttributes attributes, int matchType, [MarshalAs(UnmanagedType.Bool)] out bool result);

    [PreserveSig]
    int GetUINT32(ref Guid key, out int value);

    [PreserveSig]
    int GetUINT64(ref Guid key, out long value);

    [PreserveSig]
    int GetDouble(ref Guid key, out double value);

    [PreserveSig]
    int GetGUID(ref Guid key, out Guid value);

    [PreserveSig]
    int GetStringLength(ref Guid key, out int length);

    [PreserveSig]
    int GetString(ref Guid key, IntPtr value, int size, out int length);

    [PreserveSig]
    int GetAllocatedString(ref Guid key, out IntPtr value, out int length);

    [PreserveSig]
    int GetBlobSize(ref Guid key, out int size);

    [PreserveSig]
    int GetBlob(ref Guid key, IntPtr buffer, int bufferSize, out int blobSize);

    [PreserveSig]
    int GetAllocatedBlob(ref Guid key, out IntPtr buffer, out int size);

    [PreserveSig]
    int GetUnknown(
        ref Guid key,
        ref Guid interfaceId,
        [MarshalAs(UnmanagedType.IUnknown)] out object? value);

    [PreserveSig]
    int SetItem(ref Guid key, IntPtr value);

    [PreserveSig]
    int DeleteItem(ref Guid key);

    [PreserveSig]
    int DeleteAllItems();

    [PreserveSig]
    int SetUINT32(ref Guid key, int value);

    [PreserveSig]
    int SetUINT64(ref Guid key, long value);

    [PreserveSig]
    int SetDouble(ref Guid key, double value);

    [PreserveSig]
    int SetGUID(ref Guid key, ref Guid value);

    [PreserveSig]
    int SetString(ref Guid key, [MarshalAs(UnmanagedType.LPWStr)] string value);

    [PreserveSig]
    int SetBlob(ref Guid key, IntPtr buffer, int size);

    [PreserveSig]
    int SetUnknown(ref Guid key, [MarshalAs(UnmanagedType.IUnknown)] object? value);

    [PreserveSig]
    int LockStore();

    [PreserveSig]
    int UnlockStore();

    [PreserveSig]
    int GetCount(out int count);

    [PreserveSig]
    int GetItemByIndex(int index, out Guid key, IntPtr value);

    [PreserveSig]
    int CopyAllItems(IMFAttributes destination);

    [PreserveSig]
    int GetSampleFlags(out int flags);

    [PreserveSig]
    int SetSampleFlags(int flags);

    [PreserveSig]
    int GetSampleTime(out long sampleTime);

    [PreserveSig]
    int SetSampleTime(long sampleTime);

    [PreserveSig]
    int GetSampleDuration(out long sampleDuration);

    [PreserveSig]
    int SetSampleDuration(long sampleDuration);

    [PreserveSig]
    int GetBufferCount(out int bufferCount);

    [PreserveSig]
    int GetBufferByIndex(int index, out IMFMediaBuffer buffer);

    [PreserveSig]
    int ConvertToContiguousBuffer(out IMFMediaBuffer buffer);

    [PreserveSig]
    int AddBuffer(IMFMediaBuffer buffer);

    [PreserveSig]
    int RemoveBufferByIndex(int index);

    [PreserveSig]
    int RemoveAllBuffers();

    [PreserveSig]
    int GetTotalLength(out int totalLength);

    [PreserveSig]
    int CopyToBuffer(IMFMediaBuffer buffer);
}

[ComImport]
[Guid("045FA593-8799-42B8-BC8D-8968C6453507")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMFMediaBuffer
{
    [PreserveSig]
    int Lock(out IntPtr buffer, out int maxLength, out int currentLength);

    [PreserveSig]
    int Unlock();

    [PreserveSig]
    int GetCurrentLength(out int currentLength);

    [PreserveSig]
    int SetCurrentLength(int currentLength);

    [PreserveSig]
    int GetMaxLength(out int maxLength);
}
