using Broiler.Native.Windows;
using Broiler.Native.Windows.MediaFoundation;
using System;
using System.Runtime.Versioning;

namespace Broiler.Input.Camera.Windows;

[SupportedOSPlatform("windows")]
internal sealed class MediaFoundationPlatformScope : IDisposable
{
    private readonly bool _shouldUninitializeCom;
    private bool _mediaFoundationStarted;
    private bool _disposed;

    public MediaFoundationPlatformScope()
    {
        int comResult = ComNative.CoInitializeEx(IntPtr.Zero, ComNative.COINIT_MULTITHREADED);

        if (comResult == ComNative.S_OK || comResult == ComNative.S_FALSE)
            _shouldUninitializeCom = true;
        else if (comResult != ComNative.RPC_E_CHANGED_MODE)
            throw WindowsCameraFaults.CreateException(comResult, "COM initialization failed.", "COM");

        try
        {
            int result = MediaFoundationPlatformNative.MFStartup(MediaFoundationPlatformNative.MF_VERSION,
                MediaFoundationPlatformNative.MFSTARTUP_NOSOCKET);

            WindowsCameraFaults.ThrowIfFailed(result, "Media Foundation startup failed.");
            _mediaFoundationStarted = true;
        }
        catch
        {
            if (_shouldUninitializeCom)
                ComNative.CoUninitialize();

            throw;
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        if (_mediaFoundationStarted)
            _ = MediaFoundationPlatformNative.MFShutdown();

        if (_shouldUninitializeCom)
            ComNative.CoUninitialize();

        _disposed = true;
    }
}

internal static class WindowsCameraFaults
{
    public static InputCameraException CreateException(int hresult, string message, string nativeFacility = "MediaFoundation") =>
        new(CreateFault(hresult, message, nativeFacility));

    public static InputFault CreateFault(int hresult, string message, string nativeFacility = "MediaFoundation")
    {
        InputErrorCategory category = hresult switch
        {
            ComNative.E_ACCESSDENIED => InputErrorCategory.PermissionDenied,
            ComNative.E_NOTFOUND or
                MediaFoundationPlatformNative.MF_E_NOT_FOUND or
                MediaFoundationPlatformNative.MF_E_NO_CAPTURE_DEVICES_AVAILABLE or
                MediaFoundationPlatformNative.MF_E_CAPTURE_SOURCE_NO_VIDEO_STREAM_PRESENT => InputErrorCategory.DeviceNotFound,
            MediaFoundationPlatformNative.MF_E_VIDEO_DEVICE_LOCKED or
                MediaFoundationPlatformNative.MF_E_VIDEO_RECORDING_DEVICE_PREEMPTED => InputErrorCategory.DeviceBusy,
            MediaFoundationPlatformNative.MF_E_INVALIDMEDIATYPE => InputErrorCategory.UnsupportedCapability,
            MediaFoundationPlatformNative.MF_E_UNSUPPORTED_CAPTURE_DEVICE_PRESENT => InputErrorCategory.UnsupportedCapability,
            MediaFoundationPlatformNative.MF_E_SHUTDOWN or
                MediaFoundationPlatformNative.MF_E_VIDEO_RECORDING_DEVICE_INVALIDATED => InputErrorCategory.DeviceRemoved,
            MediaFoundationPlatformNative.MF_E_PLATFORM_NOT_INITIALIZED or
                MediaFoundationPlatformNative.MF_E_NOT_INITIALIZED or
                MediaFoundationPlatformNative.MF_E_NOT_AVAILABLE or
                MediaFoundationPlatformNative.MF_E_DISABLED_IN_SAFEMODE => InputErrorCategory.HostUnavailable,
            _ => InputErrorCategory.NativeFailure,
        };

        return new InputFault(category, FormatNativeFailureMessage(message, hresult, nativeFacility),
            nativeErrorCode: hresult, nativeFacility: nativeFacility);
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
        ComNative.E_ACCESSDENIED => "E_ACCESSDENIED",
        ComNative.E_NOTFOUND => "E_NOTFOUND",
        ComNative.RPC_E_CHANGED_MODE => "RPC_E_CHANGED_MODE",
        MediaFoundationPlatformNative.MF_E_PLATFORM_NOT_INITIALIZED => "MF_E_PLATFORM_NOT_INITIALIZED",
        MediaFoundationPlatformNative.MF_E_INVALIDMEDIATYPE => "MF_E_INVALIDMEDIATYPE",
        MediaFoundationPlatformNative.MF_E_NOT_INITIALIZED => "MF_E_NOT_INITIALIZED",
        MediaFoundationPlatformNative.MF_E_NO_MORE_TYPES => "MF_E_NO_MORE_TYPES",
        MediaFoundationPlatformNative.MF_E_NOT_FOUND => "MF_E_NOT_FOUND",
        MediaFoundationPlatformNative.MF_E_NOT_AVAILABLE => "MF_E_NOT_AVAILABLE",
        MediaFoundationPlatformNative.MF_E_ATTRIBUTENOTFOUND => "MF_E_ATTRIBUTENOTFOUND",
        MediaFoundationPlatformNative.MF_E_DISABLED_IN_SAFEMODE => "MF_E_DISABLED_IN_SAFEMODE",
        MediaFoundationPlatformNative.MF_E_SHUTDOWN => "MF_E_SHUTDOWN",
        MediaFoundationPlatformNative.MF_E_VIDEO_RECORDING_DEVICE_INVALIDATED => "MF_E_VIDEO_RECORDING_DEVICE_INVALIDATED",
        MediaFoundationPlatformNative.MF_E_VIDEO_RECORDING_DEVICE_PREEMPTED => "MF_E_VIDEO_RECORDING_DEVICE_PREEMPTED",
        MediaFoundationPlatformNative.MF_E_VIDEO_DEVICE_LOCKED => "MF_E_VIDEO_DEVICE_LOCKED",
        MediaFoundationPlatformNative.MF_E_NO_CAPTURE_DEVICES_AVAILABLE => "MF_E_NO_CAPTURE_DEVICES_AVAILABLE",
        MediaFoundationPlatformNative.MF_E_CAPTURE_SOURCE_NO_VIDEO_STREAM_PRESENT => "MF_E_CAPTURE_SOURCE_NO_VIDEO_STREAM_PRESENT",
        MediaFoundationPlatformNative.MF_E_UNSUPPORTED_CAPTURE_DEVICE_PRESENT => "MF_E_UNSUPPORTED_CAPTURE_DEVICE_PRESENT",
        _ => null,
    };
}

