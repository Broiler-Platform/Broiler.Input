using System;
using System.Buffers.Binary;
using System.Collections.Generic;

namespace Broiler.Input.Scanner.Windows;

internal readonly record struct ParsedScannerPage(
    byte[] PixelBytes,
    int Width,
    int Height,
    ScannerPixelFormat PixelFormat,
    IReadOnlyList<ScannerImagePlane> Planes);

internal static class WindowsScannerImageParser
{
    private const int BitmapFileHeaderSize = 14;
    private const int BitmapInfoHeaderMinSize = 40;
    private const uint BiRgb = 0;

    public static ParsedScannerPage ParseDibOrBmp(ReadOnlySpan<byte> data)
    {
        if (data.Length < BitmapInfoHeaderMinSize)
            throw WindowsScannerFaults.CreateException(WindowsWiaConstants.WIA_ERROR_INVALID_DRIVER_RESPONSE,
                "WIA scanner image buffer is smaller than a DIB header.");

        bool hasFileHeader = data.Length >= BitmapFileHeaderSize + BitmapInfoHeaderMinSize &&
            data[0] == (byte)'B' &&
            data[1] == (byte)'M';

        int dibOffset = hasFileHeader ? BitmapFileHeaderSize : 0;
        int headerSize = BinaryPrimitives.ReadInt32LittleEndian(data.Slice(dibOffset, 4));
        if (headerSize < BitmapInfoHeaderMinSize || dibOffset + headerSize > data.Length)
            throw WindowsScannerFaults.CreateException(WindowsWiaConstants.WIA_ERROR_INVALID_DRIVER_RESPONSE,
                "WIA scanner image buffer contains an invalid DIB header size.");

        int width = BinaryPrimitives.ReadInt32LittleEndian(data.Slice(dibOffset + 4, 4));
        int rawHeight = BinaryPrimitives.ReadInt32LittleEndian(data.Slice(dibOffset + 8, 4));
        ushort bitsPerPixel = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(dibOffset + 14, 2));
        uint compression = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(dibOffset + 16, 4));
        uint colorsUsed = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(dibOffset + 32, 4));

        if (width <= 0 || rawHeight == 0 || rawHeight == int.MinValue)
            throw WindowsScannerFaults.CreateException(WindowsWiaConstants.WIA_ERROR_INVALID_DRIVER_RESPONSE,
                "WIA scanner image buffer reported invalid pixel dimensions.");

        if (compression != BiRgb)
            throw WindowsScannerFaults.CreateException(WindowsWiaConstants.WIA_ERROR_INVALID_DRIVER_RESPONSE,
                $"WIA scanner image buffer uses unsupported DIB compression {compression}.");

        bool bottomUp = rawHeight > 0;
        int height = Math.Abs(rawHeight);

        (ScannerPixelFormat pixelFormat, int destinationStride) = bitsPerPixel switch
        {
            1 => (ScannerPixelFormat.BlackAndWhite1Bit, (width + 7) / 8),
            8 => (ScannerPixelFormat.Grayscale8, width),
            16 => (ScannerPixelFormat.Grayscale16, checked(width * 2)),
            24 => (ScannerPixelFormat.Rgb24, checked(width * 3)),
            32 => (ScannerPixelFormat.Bgra32, checked(width * 4)),
            _ => throw WindowsScannerFaults.CreateException(WindowsWiaConstants.WIA_ERROR_INVALID_DRIVER_RESPONSE,
                $"WIA scanner image buffer uses unsupported bit depth {bitsPerPixel}."),
        };

        int sourceStride = checked(((width * bitsPerPixel + 31) / 32) * 4);
        int pixelDataOffset;
        if (hasFileHeader)
        {
            pixelDataOffset = BinaryPrimitives.ReadInt32LittleEndian(data.Slice(10, 4));
        }
        else
        {
            int paletteEntries = colorsUsed != 0
                ? checked((int)colorsUsed)
                : bitsPerPixel <= 8 ? 1 << bitsPerPixel : 0;
            pixelDataOffset = checked(dibOffset + headerSize + paletteEntries * 4);
        }

        long requiredSourceBytes = (long)pixelDataOffset + (long)sourceStride * height;
        if (pixelDataOffset < dibOffset + headerSize || requiredSourceBytes > data.Length)
            throw WindowsScannerFaults.CreateException(WindowsWiaConstants.WIA_ERROR_INVALID_DRIVER_RESPONSE,
                "WIA scanner image buffer is truncated.");

        int destinationLength = checked(destinationStride * height);
        byte[] output = new byte[destinationLength];

        for (int destRow = 0; destRow < height; destRow++)
        {
            int sourceRow = bottomUp ? (height - 1 - destRow) : destRow;
            ReadOnlySpan<byte> srcLine = data.Slice(pixelDataOffset + sourceRow * sourceStride, destinationStride);
            Span<byte> dstLine = output.AsSpan(destRow * destinationStride, destinationStride);

            if (bitsPerPixel == 24)
            {
                for (int x = 0; x < width; x++)
                {
                    int offset = x * 3;
                    dstLine[offset] = srcLine[offset + 2];
                    dstLine[offset + 1] = srcLine[offset + 1];
                    dstLine[offset + 2] = srcLine[offset];
                }
            }
            else
            {
                srcLine.CopyTo(dstLine);
            }
        }

        IReadOnlyList<ScannerImagePlane> planes = [new ScannerImagePlane(0, destinationLength, destinationStride, width, height)];
        return new ParsedScannerPage(output, width, height, pixelFormat, planes);
    }

    public static IReadOnlyList<ScannerImagePlane> CreatePlanes(ScannerPixelFormat pixelFormat, int width, int height, int byteLength)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(byteLength);

        int minStride = pixelFormat switch
        {
            ScannerPixelFormat.BlackAndWhite1Bit => (width + 7) / 8,
            ScannerPixelFormat.Grayscale8 => width,
            ScannerPixelFormat.Grayscale16 => width * 2,
            ScannerPixelFormat.Rgb24 => width * 3,
            ScannerPixelFormat.Rgba32 or ScannerPixelFormat.Bgra32 => width * 4,
            _ => Math.Max(1, byteLength / height),
        };

        int rowStride = Math.Max(minStride, byteLength / height);
        return [new ScannerImagePlane(0, byteLength, rowStride, width, height)];
    }
}
