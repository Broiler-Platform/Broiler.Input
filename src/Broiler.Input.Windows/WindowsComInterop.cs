using Broiler.Native.Windows;
using System;
using System.Runtime.Versioning;

namespace Broiler.Input.Windows;

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
