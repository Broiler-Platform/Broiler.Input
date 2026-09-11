using System;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace Broiler.Input.Camera.Windows;

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
            int result = WindowsMediaFoundationNative.MFStartup(WindowsMediaFoundationNative.MF_VERSION,
                WindowsMediaFoundationNative.MFSTARTUP_NOSOCKET);

            WindowsCameraFaults.ThrowIfFailed(result, "Media Foundation startup failed.");
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
            _ = WindowsMediaFoundationNative.MFShutdown();

        if (_shouldUninitializeCom)
            WindowsMediaFoundationNative.CoUninitialize();

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
            WindowsMediaFoundationNative.E_ACCESSDENIED => InputErrorCategory.PermissionDenied,
            WindowsMediaFoundationNative.E_NOTFOUND or
                WindowsMediaFoundationNative.MF_E_NOT_FOUND or
                WindowsMediaFoundationNative.MF_E_NO_CAPTURE_DEVICES_AVAILABLE or
                WindowsMediaFoundationNative.MF_E_CAPTURE_SOURCE_NO_VIDEO_STREAM_PRESENT => InputErrorCategory.DeviceNotFound,
            WindowsMediaFoundationNative.MF_E_VIDEO_DEVICE_LOCKED or
                WindowsMediaFoundationNative.MF_E_VIDEO_RECORDING_DEVICE_PREEMPTED => InputErrorCategory.DeviceBusy,
            WindowsMediaFoundationNative.MF_E_INVALIDMEDIATYPE => InputErrorCategory.UnsupportedCapability,
            WindowsMediaFoundationNative.MF_E_UNSUPPORTED_CAPTURE_DEVICE_PRESENT => InputErrorCategory.UnsupportedCapability,
            WindowsMediaFoundationNative.MF_E_SHUTDOWN or
                WindowsMediaFoundationNative.MF_E_VIDEO_RECORDING_DEVICE_INVALIDATED => InputErrorCategory.DeviceRemoved,
            WindowsMediaFoundationNative.MF_E_PLATFORM_NOT_INITIALIZED or
                WindowsMediaFoundationNative.MF_E_NOT_INITIALIZED or
                WindowsMediaFoundationNative.MF_E_NOT_AVAILABLE or
                WindowsMediaFoundationNative.MF_E_DISABLED_IN_SAFEMODE => InputErrorCategory.HostUnavailable,
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

