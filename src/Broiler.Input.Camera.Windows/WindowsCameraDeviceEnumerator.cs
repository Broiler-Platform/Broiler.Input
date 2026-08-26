using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Broiler.Input;

namespace Broiler.Input.Camera.Windows;

internal static class WindowsCameraDeviceEnumerator
{
    private const string SymbolicLinkCapability = "windows.mediafoundation.symbolic-link";
    private const string SourceKindCapability = "camera.capture.source";
    private const string SourceKindValue = "mediafoundation-video-capture";

    public static IReadOnlyList<InputDeviceDescriptor> EnumerateVideoDevices()
    {
        using MediaFoundationPlatformScope platform = new();
        List<ActivateEntry> activates = EnumerateActivates();
        try
        {
            List<InputDeviceDescriptor> descriptors = new(activates.Count);
            foreach (ActivateEntry entry in activates)
                descriptors.Add(CreateDescriptor(entry.Activate));

            return descriptors;
        }
        finally
        {
            foreach (ActivateEntry entry in activates)
                entry.Dispose();
        }
    }

    public static IMFMediaSource ActivateMediaSource(
        InputDeviceDescriptor descriptor,
        out object? activateObject,
        out object? mediaSourceObject)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        string? symbolicLink = GetNativeSymbolicLink(descriptor);
        InputCameraException? activationFailure = null;
        if (!string.IsNullOrWhiteSpace(symbolicLink) &&
            TryActivateMediaSourceFromSymbolicLink(symbolicLink, out IMFMediaSource? linkedMediaSource, out mediaSourceObject, out activationFailure) &&
            linkedMediaSource is not null)
        {
            activateObject = null;
            return linkedMediaSource;
        }

        List<ActivateEntry> activates = EnumerateActivates();
        activateObject = null;
        mediaSourceObject = null;

        try
        {
            foreach (ActivateEntry entry in activates)
            {
                string? entryLink = GetAllocatedString(entry.Activate, WindowsMediaFoundationNative.MF_DEVSOURCE_ATTRIBUTE_SOURCE_TYPE_VIDCAP_SYMBOLIC_LINK);
                bool matches = !string.IsNullOrWhiteSpace(symbolicLink)
                    ? StringComparer.Ordinal.Equals(symbolicLink, entryLink)
                    : descriptor.Id == CreateDescriptor(entry.Activate).Id;

                if (!matches)
                    continue;

                SetFrameServerShareMode(entry.Activate);
                Guid mediaSourceId = WindowsMediaFoundationNative.IMFMediaSourceId;
                int activationResult = entry.Activate.ActivateObject(ref mediaSourceId, out mediaSourceObject);
                if (activationResult < 0)
                {
                    activationFailure ??= WindowsCameraFaults.CreateException(activationResult, "Media Foundation camera activation failed.");
                    break;
                }

                if (mediaSourceObject is not IMFMediaSource mediaSource)
                    throw WindowsCameraFaults.CreateException(unchecked((int)0x80004002), "Media Foundation camera source interface activation failed.");

                activateObject = entry.ActivateObject;
                entry.Detach();
                return mediaSource;
            }
        }
        finally
        {
            foreach (ActivateEntry entry in activates)
                entry.Dispose();
        }

        if (activationFailure is not null)
            throw activationFailure;

        throw WindowsCameraFaults.CreateException(WindowsMediaFoundationNative.E_NOTFOUND, "Selected camera was not found.");
    }

    public static string? GetNativeSymbolicLink(InputDeviceDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        return descriptor.Capabilities
            .FirstOrDefault(static capability => capability.Name == SymbolicLinkCapability)
            .Value;
    }

    private static List<ActivateEntry> EnumerateActivates()
    {
        WindowsCameraFaults.ThrowIfFailed(
            WindowsMediaFoundationNative.MFCreateAttributes(out IMFAttributes attributes, 1),
            "Media Foundation camera attribute store creation failed.");
        object? attributesObject = attributes;

        IntPtr activateArray = IntPtr.Zero;
        List<ActivateEntry> entries = [];
        try
        {
            Guid sourceType = WindowsMediaFoundationNative.MF_DEVSOURCE_ATTRIBUTE_SOURCE_TYPE;
            Guid videoCapture = WindowsMediaFoundationNative.MF_DEVSOURCE_ATTRIBUTE_SOURCE_TYPE_VIDCAP_GUID;
            WindowsCameraFaults.ThrowIfFailed(attributes.SetGUID(ref sourceType, ref videoCapture), "Media Foundation camera source filter failed.");
            WindowsCameraFaults.ThrowIfFailed(attributes.GetGUID(ref sourceType, out _), "Media Foundation camera source filter verification failed.");
            int enumerationResult = WindowsMediaFoundationNative.MFEnumDeviceSources(attributes, out activateArray, out uint count);
            if (enumerationResult == WindowsMediaFoundationNative.MF_E_NO_CAPTURE_DEVICES_AVAILABLE)
                return entries;

            WindowsCameraFaults.ThrowIfFailed(enumerationResult, "Media Foundation camera enumeration failed.");

            for (uint index = 0; index < count; index++)
            {
                IntPtr activatePointer = Marshal.ReadIntPtr(activateArray, checked((int)index * IntPtr.Size));
                object activateObject = Marshal.GetObjectForIUnknown(activatePointer);
                Marshal.Release(activatePointer);
                if (activateObject is IMFActivate activate)
                    entries.Add(new ActivateEntry(activateObject, activate));
                else
                    ReleaseComObject(activateObject);
            }

            return entries;
        }
        finally
        {
            if (activateArray != IntPtr.Zero)
                WindowsMediaFoundationNative.CoTaskMemFree(activateArray);
            ReleaseComObject(attributesObject);
        }
    }

    private static bool TryActivateMediaSourceFromSymbolicLink(
        string symbolicLink,
        out IMFMediaSource? mediaSource,
        out object? mediaSourceObject,
        out InputCameraException? exception)
    {
        mediaSource = null;
        mediaSourceObject = null;
        exception = null;
        object? attributesObject = null;

        try
        {
            WindowsCameraFaults.ThrowIfFailed(
                WindowsMediaFoundationNative.MFCreateAttributes(out IMFAttributes attributes, 2),
                "Media Foundation camera source attribute store creation failed.");
            attributesObject = attributes;

            Guid sourceType = WindowsMediaFoundationNative.MF_DEVSOURCE_ATTRIBUTE_SOURCE_TYPE;
            Guid videoCapture = WindowsMediaFoundationNative.MF_DEVSOURCE_ATTRIBUTE_SOURCE_TYPE_VIDCAP_GUID;
            WindowsCameraFaults.ThrowIfFailed(
                attributes.SetGUID(ref sourceType, ref videoCapture),
                "Media Foundation camera source type selection failed.");

            Guid symbolicLinkKey = WindowsMediaFoundationNative.MF_DEVSOURCE_ATTRIBUTE_SOURCE_TYPE_VIDCAP_SYMBOLIC_LINK;
            WindowsCameraFaults.ThrowIfFailed(
                attributes.SetString(ref symbolicLinkKey, symbolicLink),
                "Media Foundation camera symbolic link selection failed.");

            SetFrameServerShareMode(attributes);
            int result = WindowsMediaFoundationNative.MFCreateDeviceSource(attributes, out IMFMediaSource linkedMediaSource);
            if (result < 0)
            {
                exception = WindowsCameraFaults.CreateException(result, "Media Foundation camera device source creation failed.");
                return false;
            }

            mediaSource = linkedMediaSource;
            mediaSourceObject = linkedMediaSource;
            return true;
        }
        catch (InputCameraException caughtException)
        {
            exception = caughtException;
            return false;
        }
        finally
        {
            ReleaseComObject(attributesObject);
        }
    }

    private static void SetFrameServerShareMode(IMFAttributes attributes)
    {
        Guid frameServerShareMode = WindowsMediaFoundationNative.MF_DEVSOURCE_ATTRIBUTE_FRAMESERVER_SHARE_MODE;
        WindowsCameraFaults.ThrowIfFailed(
            attributes.SetUINT32(ref frameServerShareMode, 1),
            "Media Foundation camera frame-server share mode selection failed.");
    }

    private static InputDeviceDescriptor CreateDescriptor(IMFActivate activate)
    {
        string? friendlyName = GetAllocatedString(activate, WindowsMediaFoundationNative.MF_DEVSOURCE_ATTRIBUTE_FRIENDLY_NAME);
        string? symbolicLink = GetAllocatedString(activate, WindowsMediaFoundationNative.MF_DEVSOURCE_ATTRIBUTE_SOURCE_TYPE_VIDCAP_SYMBOLIC_LINK);
        string stableSeed = string.IsNullOrWhiteSpace(symbolicLink) ? friendlyName ?? "unknown-camera" : symbolicLink;

        List<InputCapability> capabilities =
        [
            new(SourceKindCapability, SourceKindValue),
        ];

        if (!string.IsNullOrWhiteSpace(symbolicLink))
            capabilities.Add(new InputCapability(SymbolicLinkCapability, symbolicLink));

        return new InputDeviceDescriptor(
            InputDeviceId.FromOpaqueValue(ToStableInputId(stableSeed)),
            InputKind.Camera,
            friendlyName ?? stableSeed,
            InputDeviceAvailability.Available,
            capabilities);
    }

    private static string? GetAllocatedString(IMFAttributes attributes, Guid key)
    {
        IntPtr value = IntPtr.Zero;
        try
        {
            int result = attributes.GetAllocatedString(ref key, out value, out _);
            if (result < 0 || value == IntPtr.Zero)
                return null;

            return Marshal.PtrToStringUni(value);
        }
        finally
        {
            if (value != IntPtr.Zero)
                WindowsMediaFoundationNative.CoTaskMemFree(value);
        }
    }

    private static string ToStableInputId(string seed)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(seed));
        string suffix = Convert.ToHexString(hash, 0, 12).ToLowerInvariant();
        return $"windows:mediafoundation:camera:{suffix}";
    }

    private static void ReleaseComObject(object? value)
    {
        if (value is not null && Marshal.IsComObject(value))
            Marshal.ReleaseComObject(value);
    }

    private sealed class ActivateEntry : IDisposable
    {
        private bool _detached;

        public ActivateEntry(object activateObject, IMFActivate activate)
        {
            ActivateObject = activateObject;
            Activate = activate;
        }

        public object ActivateObject { get; }

        public IMFActivate Activate { get; }

        public void Detach()
        {
            _detached = true;
        }

        public void Dispose()
        {
            if (!_detached)
            {
                Activate.ShutdownObject();
                ReleaseComObject(ActivateObject);
            }
        }
    }
}
