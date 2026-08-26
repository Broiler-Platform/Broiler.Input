using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Broiler.Input.Windows;

[SupportedOSPlatform("windows")]
public sealed partial class WindowsRawInputReader
{
    private const uint RidInput = 0x10000003;
    private const uint RimTypeMouse = 0;
    private const uint RimTypeKeyboard = 1;
    private const ushort MouseMoveAbsolute = 0x0001;

    private readonly IInputClock _clock;

    public WindowsRawInputReader(IInputClock? clock = null)
    {
        _clock = clock ?? WindowsInputClock.Shared;
    }

    public bool TryRead(IntPtr rawInputHandle, out WindowsRawInputReport report)
    {
        report = default;
        if (rawInputHandle == IntPtr.Zero)
            return false;

        uint size = 0;
        uint headerSize = (uint)Marshal.SizeOf<RawInputHeader>();
        uint queryResult = GetRawInputData(rawInputHandle, RidInput, IntPtr.Zero, ref size, headerSize);
        if (queryResult == uint.MaxValue || size == 0)
            return false;

        byte[] buffer = new byte[size];
        GCHandle handle = GCHandle.Alloc(buffer, GCHandleType.Pinned);
        try
        {
            IntPtr pointer = handle.AddrOfPinnedObject();
            uint read = GetRawInputData(rawInputHandle, RidInput, pointer, ref size, headerSize);
            if (read == uint.MaxValue || read != size)
                throw new Win32Exception(Marshal.GetLastWin32Error(), "GetRawInputData failed.");

            RawInputHeader header = Marshal.PtrToStructure<RawInputHeader>(pointer);
            IntPtr data = IntPtr.Add(pointer, Marshal.SizeOf<RawInputHeader>());
            InputTimestamp timestamp = _clock.GetTimestamp();
            WindowsRawInputDeviceIdentity identity = new(header.Device);

            if (header.Type == RimTypeMouse)
            {
                RawMouse mouse = Marshal.PtrToStructure<RawMouse>(data);
                report = new WindowsRawInputReport(new WindowsRawMouseReport(
                    identity,
                    timestamp,
                    mouse.LastX,
                    mouse.LastY,
                    mouse.ButtonFlags,
                    mouse.ButtonData,
                    mouse.RawButtons,
                    (mouse.Flags & MouseMoveAbsolute) != 0));
                return true;
            }

            if (header.Type == RimTypeKeyboard)
            {
                RawKeyboard keyboard = Marshal.PtrToStructure<RawKeyboard>(data);
                report = new WindowsRawInputReport(new WindowsRawKeyboardReport(
                    identity,
                    timestamp,
                    keyboard.MakeCode,
                    keyboard.Flags,
                    keyboard.VKey,
                    keyboard.Message));
                return true;
            }

            return false;
        }
        finally
        {
            handle.Free();
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RawInputHeader
    {
        public uint Type;
        public uint Size;
        public IntPtr Device;
        public IntPtr WParam;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RawMouse
    {
        public ushort Flags;
        public ushort ButtonFlags;
        public ushort ButtonData;
        public uint RawButtons;
        public int LastX;
        public int LastY;
        public uint ExtraInformation;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RawKeyboard
    {
        public ushort MakeCode;
        public ushort Flags;
        public ushort Reserved;
        public ushort VKey;
        public uint Message;
        public uint ExtraInformation;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetRawInputData(
        IntPtr rawInput,
        uint command,
        IntPtr data,
        ref uint size,
        uint headerSize);
}
