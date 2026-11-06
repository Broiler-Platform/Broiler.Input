using Broiler.Native.Windows;
using System;
using System.Runtime.Versioning;

namespace Broiler.Input.Windows;

/// <summary>
/// Manages a thread's COM apartment initialization scope across Windows input providers.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class WindowsComApartmentScope : IDisposable
{
    private bool _shouldUninitialize;

    private WindowsComApartmentScope(bool shouldUninitialize) => _shouldUninitialize = shouldUninitialize;

    public static WindowsComApartmentScope Enter(Func<int, Exception> createException)
    {
        ArgumentNullException.ThrowIfNull(createException);

        int result = ComNative.CoInitializeEx(IntPtr.Zero, ComNative.COINIT_MULTITHREADED);

        if (result == ComNative.S_OK || result == ComNative.S_FALSE)
            return new WindowsComApartmentScope(shouldUninitialize: true);

        if (result == ComNative.RPC_E_CHANGED_MODE)
            return new WindowsComApartmentScope(shouldUninitialize: false);

        throw createException(result);
    }

    public static WindowsComApartmentScope EnterBestEffort()
    {
        int result = ComNative.CoInitializeEx(IntPtr.Zero, ComNative.COINIT_MULTITHREADED);
        return new WindowsComApartmentScope(shouldUninitialize: result is ComNative.S_OK or ComNative.S_FALSE);
    }

    public void Dispose()
    {
        if (!_shouldUninitialize)
            return;

        _shouldUninitialize = false;
        ComNative.CoUninitialize();
    }
}
