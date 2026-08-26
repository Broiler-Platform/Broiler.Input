using System;
using System.Collections.Generic;
using Broiler.Input.Camera;

namespace Broiler.Input.Camera.Windows;

internal static class WindowsCameraMediaType
{
    public static CameraFormat ReadFormat(IMFMediaType mediaType)
    {
        ArgumentNullException.ThrowIfNull(mediaType);

        Guid majorTypeKey = WindowsMediaFoundationNative.MF_MT_MAJOR_TYPE;
        Guid subtypeKey = WindowsMediaFoundationNative.MF_MT_SUBTYPE;
        Guid frameSizeKey = WindowsMediaFoundationNative.MF_MT_FRAME_SIZE;
        Guid frameRateKey = WindowsMediaFoundationNative.MF_MT_FRAME_RATE;

        WindowsCameraFaults.ThrowIfFailed(mediaType.GetGUID(ref majorTypeKey, out Guid majorType), "Media Foundation camera media major type lookup failed.");
        if (majorType != WindowsMediaFoundationNative.MFMediaTypeVideo)
            throw WindowsCameraFaults.CreateException(WindowsMediaFoundationNative.MF_E_INVALIDMEDIATYPE, "Camera source returned a non-video media type.");

        WindowsCameraFaults.ThrowIfFailed(mediaType.GetGUID(ref subtypeKey, out Guid subtype), "Media Foundation camera media subtype lookup failed.");
        (uint width, uint height) = GetPackedUInt32Pair(mediaType, ref frameSizeKey, "Media Foundation camera frame-size lookup failed.");

        uint frameRateNumerator = 0;
        uint frameRateDenominator = 1;
        int frameRateResult = TryGetPackedUInt32Pair(mediaType, ref frameRateKey, out uint numerator, out uint denominator);
        if (frameRateResult >= 0 && denominator != 0)
        {
            frameRateNumerator = numerator;
            frameRateDenominator = denominator;
        }

        return new CameraFormat(
            checked((int)width),
            checked((int)height),
            checked((int)frameRateNumerator),
            checked((int)frameRateDenominator),
            ToPixelFormat(subtype));
    }

    public static IReadOnlyList<CameraFramePlane> CreatePlanes(CameraFormat format, int byteLength)
    {
        ArgumentNullException.ThrowIfNull(format);
        if (byteLength < 0)
            throw new ArgumentOutOfRangeException(nameof(byteLength));

        int width = format.Width;
        int height = format.Height;
        return format.PixelFormat switch
        {
            CameraPixelFormat.Bgra32 or CameraPixelFormat.Rgba32 => [new CameraFramePlane(0, byteLength, width * 4, width, height)],
            CameraPixelFormat.Rgb24 => [new CameraFramePlane(0, byteLength, width * 3, width, height)],
            CameraPixelFormat.Gray8 => [new CameraFramePlane(0, byteLength, width, width, height)],
            CameraPixelFormat.Yuy2 => [new CameraFramePlane(0, byteLength, width * 2, width, height)],
            CameraPixelFormat.Nv12 => CreateNv12Planes(width, height, byteLength),
            _ => [new CameraFramePlane(0, byteLength, 0, width, height)],
        };
    }

    private static (uint High, uint Low) GetPackedUInt32Pair(IMFAttributes attributes, ref Guid key, string message)
    {
        int result = TryGetPackedUInt32Pair(attributes, ref key, out uint high, out uint low);
        WindowsCameraFaults.ThrowIfFailed(result, message);
        return (high, low);
    }

    private static int TryGetPackedUInt32Pair(IMFAttributes attributes, ref Guid key, out uint high, out uint low)
    {
        high = 0;
        low = 0;
        int result = attributes.GetUINT64(ref key, out long packedValue);
        if (result < 0)
            return result;

        ulong packed = unchecked((ulong)packedValue);
        high = (uint)(packed >> 32);
        low = (uint)packed;
        return result;
    }

    private static IReadOnlyList<CameraFramePlane> CreateNv12Planes(int width, int height, int byteLength)
    {
        int yLength = Math.Min(byteLength, width * height);
        int uvLength = Math.Max(0, byteLength - yLength);
        return
        [
            new CameraFramePlane(0, yLength, width, width, height),
            new CameraFramePlane(yLength, uvLength, width, width, height / 2),
        ];
    }

    private static CameraPixelFormat ToPixelFormat(Guid subtype)
    {
        if (subtype == WindowsMediaFoundationNative.MFVideoFormatRgb32)
            return CameraPixelFormat.Bgra32;
        if (subtype == WindowsMediaFoundationNative.MFVideoFormatRgb24)
            return CameraPixelFormat.Rgb24;
        if (subtype == WindowsMediaFoundationNative.MFVideoFormatNv12)
            return CameraPixelFormat.Nv12;
        if (subtype == WindowsMediaFoundationNative.MFVideoFormatYuy2)
            return CameraPixelFormat.Yuy2;
        if (subtype == WindowsMediaFoundationNative.MFVideoFormatMjpg)
            return CameraPixelFormat.Mjpeg;
        if (subtype == WindowsMediaFoundationNative.MFVideoFormatL8)
            return CameraPixelFormat.Gray8;

        return CameraPixelFormat.Unknown;
    }
}
