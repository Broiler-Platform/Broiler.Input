using System;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace Broiler.Input.Microphone.Windows;

internal sealed class WindowsComApartmentScope : IDisposable
{
    private readonly bool _shouldUninitialize;

    private WindowsComApartmentScope(bool shouldUninitialize) => _shouldUninitialize = shouldUninitialize;

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
    public static InputMicrophoneException CreateException(int hresult, string message) => new(CreateFault(hresult, message));

    public static InputFault CreateFault(int hresult, string message)
    {
        InputErrorCategory category = hresult switch
        {
            WindowsWasapiNative.E_ACCESSDENIED => InputErrorCategory.PermissionDenied,
            WindowsWasapiNative.AUDCLNT_E_DEVICE_IN_USE => InputErrorCategory.DeviceBusy,
            WindowsWasapiNative.AUDCLNT_E_UNSUPPORTED_FORMAT => InputErrorCategory.UnsupportedCapability,
            WindowsWasapiNative.AUDCLNT_E_DEVICE_INVALIDATED => InputErrorCategory.DeviceRemoved,
            WindowsWasapiNative.E_NOTFOUND => InputErrorCategory.DeviceNotFound,
            WindowsWasapiNative.AUDCLNT_E_SERVICE_NOT_RUNNING => InputErrorCategory.HostUnavailable,
            _ => InputErrorCategory.NativeFailure,
        };

        return new InputFault(category, FormatNativeFailureMessage(message, hresult), nativeErrorCode: hresult, nativeFacility: "WASAPI");
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

