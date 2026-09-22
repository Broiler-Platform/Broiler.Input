using Broiler.Native.Windows;
using Broiler.Native.Windows.Wasapi;
using System;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using System.Runtime.Versioning;

namespace Broiler.Input.Microphone.Windows;

internal sealed class WindowsComApartmentScope : IDisposable
{
    private readonly bool _shouldUninitialize;

    private WindowsComApartmentScope(bool shouldUninitialize) => _shouldUninitialize = shouldUninitialize;

    public static WindowsComApartmentScope Enter()
    {
        int result = ComNative.CoInitializeEx(IntPtr.Zero, ComNative.COINIT_MULTITHREADED);

        if (result == ComNative.S_OK || result == ComNative.S_FALSE)
            return new WindowsComApartmentScope(shouldUninitialize: true);

        if (result == ComNative.RPC_E_CHANGED_MODE)
            return new WindowsComApartmentScope(shouldUninitialize: false);

        throw WindowsMicrophoneFaults.CreateException(result, "COM initialization failed.");
    }

    public void Dispose()
    {
        if (_shouldUninitialize)
            ComNative.CoUninitialize();
    }
}

/// <summary>
/// Bridges raw COM pointers into the source-generated interop of Broiler.Native.Windows.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class WindowsComInterop
{
    /// <summary>
    /// Wraps a raw interface pointer the caller owns and releases that reference: the wrapper
    /// holds its own. Returns <see langword="null"/> for a null pointer or when the object does
    /// not implement <typeparamref name="TInterface"/>.
    /// </summary>
    public static TInterface? Wrap<TInterface>(IntPtr pointer) where TInterface : class
    {
        if (pointer == IntPtr.Zero)
            return null;

        try
        {
            return ComNative.GetOrCreateComObject<TInterface>(pointer);
        }
        catch (InvalidCastException)
        {
            return null;
        }
        finally
        {
            ComNative.ReleaseIUnknown(pointer);
        }
    }

    /// <summary>
    /// Releases a COM object early where the interop allows it. Source-generated wrappers are
    /// cached per COM identity and released by their finalizer, so this is a no-op for them.
    /// </summary>
    public static void Release(object? value)
    {
        if (value is not null)
            ComNative.ReleaseComObject(value);
    }
}

internal static class WindowsMicrophoneFaults
{
    public static InputMicrophoneException CreateException(int hresult, string message) => new(CreateFault(hresult, message));

    public static InputFault CreateFault(int hresult, string message)
    {
        InputErrorCategory category = hresult switch
        {
            ComNative.E_ACCESSDENIED => InputErrorCategory.PermissionDenied,
            WindowsWasapiNative.AUDCLNT_E_DEVICE_IN_USE => InputErrorCategory.DeviceBusy,
            WindowsWasapiNative.AUDCLNT_E_UNSUPPORTED_FORMAT => InputErrorCategory.UnsupportedCapability,
            WindowsWasapiNative.AUDCLNT_E_DEVICE_INVALIDATED => InputErrorCategory.DeviceRemoved,
            ComNative.E_NOTFOUND => InputErrorCategory.DeviceNotFound,
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
        ComNative.E_ACCESSDENIED => "E_ACCESSDENIED",
        ComNative.E_NOINTERFACE => "E_NOINTERFACE",
        ComNative.E_NOTFOUND => "E_NOTFOUND",
        ComNative.RPC_E_CHANGED_MODE => "RPC_E_CHANGED_MODE",
        WindowsWasapiNative.AUDCLNT_E_DEVICE_INVALIDATED => "AUDCLNT_E_DEVICE_INVALIDATED",
        WindowsWasapiNative.AUDCLNT_E_UNSUPPORTED_FORMAT => "AUDCLNT_E_UNSUPPORTED_FORMAT",
        WindowsWasapiNative.AUDCLNT_E_DEVICE_IN_USE => "AUDCLNT_E_DEVICE_IN_USE",
        WindowsWasapiNative.AUDCLNT_E_SERVICE_NOT_RUNNING => "AUDCLNT_E_SERVICE_NOT_RUNNING",
        _ => null,
    };
}

