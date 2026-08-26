using System;
using System.Buffers.Binary;
using System.Collections.Generic;

namespace Broiler.Input.Linux;

public static class LinuxInputEventParser
{
    public const int InputEvent64Size = 24;
    public const long TimestampFrequency = 1_000_000;
    public const string TimestampClockName = "Linux evdev";

    public static bool TryRead64(ReadOnlySpan<byte> bytes, out LinuxInputEvent inputEvent, out int bytesConsumed)
    {
        inputEvent = default;
        bytesConsumed = 0;

        if (bytes.Length < InputEvent64Size)
            return false;

        long seconds = ReadInt64(bytes[..8]);
        long microseconds = ReadInt64(bytes.Slice(8, 8));
        ushort type = ReadUInt16(bytes.Slice(16, 2));
        ushort code = ReadUInt16(bytes.Slice(18, 2));
        int value = ReadInt32(bytes.Slice(20, 4));

        inputEvent = new LinuxInputEvent(
            new InputTimestamp((seconds * TimestampFrequency) + microseconds, TimestampFrequency, TimestampClockName),
            type,
            code,
            value);
        bytesConsumed = InputEvent64Size;
        return true;
    }

    public static int ReadAll64(ReadOnlySpan<byte> bytes, ICollection<LinuxInputEvent> events)
    {
        ArgumentNullException.ThrowIfNull(events);

        int consumed = 0;
        while (TryRead64(bytes[consumed..], out LinuxInputEvent inputEvent, out int eventSize))
        {
            events.Add(inputEvent);
            consumed += eventSize;
        }

        return consumed;
    }

    private static long ReadInt64(ReadOnlySpan<byte> value) =>
        BitConverter.IsLittleEndian
            ? BinaryPrimitives.ReadInt64LittleEndian(value)
            : BinaryPrimitives.ReadInt64BigEndian(value);

    private static int ReadInt32(ReadOnlySpan<byte> value) =>
        BitConverter.IsLittleEndian
            ? BinaryPrimitives.ReadInt32LittleEndian(value)
            : BinaryPrimitives.ReadInt32BigEndian(value);

    private static ushort ReadUInt16(ReadOnlySpan<byte> value) =>
        BitConverter.IsLittleEndian
            ? BinaryPrimitives.ReadUInt16LittleEndian(value)
            : BinaryPrimitives.ReadUInt16BigEndian(value);
}
