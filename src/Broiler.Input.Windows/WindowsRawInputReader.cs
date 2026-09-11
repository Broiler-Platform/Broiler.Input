using static Broiler.Native.Windows.Input.RawInputReaderNative;
using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Broiler.Input.Windows;

[SupportedOSPlatform("windows")]
public sealed partial class WindowsRawInputReader(IInputClock? clock = null)
{

    private readonly IInputClock _clock = clock ?? WindowsInputClock.Shared;

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
                report = new WindowsRawInputReport(new WindowsRawMouseReport(identity, timestamp,
                    mouse.LastX, mouse.LastY, mouse.ButtonFlags, mouse.ButtonData, mouse.RawButtons,
                    (mouse.Flags & MouseMoveAbsolute) != 0));

                return true;
            }

            if (header.Type == RimTypeKeyboard)
            {
                RawKeyboard keyboard = Marshal.PtrToStructure<RawKeyboard>(data);
                report = new WindowsRawInputReport(new WindowsRawKeyboardReport(identity, timestamp,
                    keyboard.MakeCode, keyboard.Flags, keyboard.VKey, keyboard.Message));

                return true;
            }

            return false;
        }
        finally
        {
            handle.Free();
        }
    }

}
