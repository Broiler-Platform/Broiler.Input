using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace Broiler.Input.Scanner.Linux;

internal enum SaneStatus
{
    Good = 0,
    Unsupported = 1,
    Cancelled = 2,
    DeviceBusy = 3,
    Inval = 4,
    Eof = 5,
    Jammed = 6,
    NoDocs = 7,
    CoverOpen = 8,
    IoError = 9,
    NoMem = 10,
    AccessDenied = 11,
}

internal enum SaneFrame
{
    Gray = 0,
    Rgb = 1,
    Red = 2,
    Green = 3,
    Blue = 4,
}

internal enum SaneValueType
{
    Bool = 0,
    Int = 1,
    Fixed = 2,
    String = 3,
    Button = 4,
    Group = 5,
}

internal enum SaneUnit
{
    None = 0,
    Pixel = 1,
    Bit = 2,
    Mm = 3,
    Dpi = 4,
    Percent = 5,
    Microsecond = 6,
}

internal sealed record SaneDeviceInfo(
    string Name,
    string Vendor,
    string Model,
    string Type);

internal readonly record struct SaneParameters(
    SaneFrame Format,
    bool LastFrame,
    int BytesPerLine,
    int PixelsPerLine,
    int Lines,
    int Depth);

internal interface ILinuxSaneInterop
{
    SaneStatus Init(out int versionCode);

    void Exit();

    SaneStatus GetDevices(bool localOnly, out IReadOnlyList<SaneDeviceInfo> devices);

    SaneStatus Open(string deviceName, out IntPtr handle);

    void Close(IntPtr handle);

    bool TrySetStringOption(IntPtr handle, string optionName, string value);

    bool TrySetIntOrFixedOption(IntPtr handle, string optionName, double value);

    SaneStatus GetParameters(IntPtr handle, out SaneParameters parameters);

    SaneStatus Start(IntPtr handle);

    SaneStatus Read(IntPtr handle, Span<byte> buffer, out int bytesRead);

    void Cancel(IntPtr handle);
}

internal sealed class NativeLinuxSaneInterop : ILinuxSaneInterop
{
    private const string LibraryName = "libsane.so.1";
    private const int SaneActionGetValue = 0;
    private const int SaneActionSetValue = 1;
    private const int SaneFixedScaleShift = 16;

    public static NativeLinuxSaneInterop Shared { get; } = new();

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeSaneDevice
    {
        public IntPtr Name;
        public IntPtr Vendor;
        public IntPtr Model;
        public IntPtr Type;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeSaneOptionDescriptor
    {
        public IntPtr Name;
        public IntPtr Title;
        public IntPtr Desc;
        public SaneValueType Type;
        public SaneUnit Unit;
        public int Size;
        public int Cap;
        public int ConstraintType;
        public IntPtr Constraint;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeSaneParameters
    {
        public SaneFrame Format;
        public int LastFrame;
        public int BytesPerLine;
        public int PixelsPerLine;
        public int Lines;
        public int Depth;
    }

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    private static extern SaneStatus sane_init(out int versionCode, IntPtr authorize);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    private static extern void sane_exit();

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    private static extern SaneStatus sane_get_devices(out IntPtr deviceList, int localOnly);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    private static extern SaneStatus sane_open([MarshalAs(UnmanagedType.LPUTF8Str)] string deviceName, out IntPtr handle);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    private static extern void sane_close(IntPtr handle);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    private static extern IntPtr sane_get_option_descriptor(IntPtr handle, int option);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    private static extern SaneStatus sane_control_option(IntPtr handle, int option, int action, IntPtr value, out int info);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    private static extern SaneStatus sane_get_parameters(IntPtr handle, out NativeSaneParameters parameters);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    private static extern SaneStatus sane_start(IntPtr handle);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    private static extern SaneStatus sane_read(IntPtr handle, IntPtr data, int maxLength, out int length);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    private static extern void sane_cancel(IntPtr handle);

    public SaneStatus Init(out int versionCode)
    {
        try
        {
            return sane_init(out versionCode, IntPtr.Zero);
        }
        catch (DllNotFoundException)
        {
            versionCode = 0;
            return SaneStatus.Unsupported;
        }
    }

    public void Exit()
    {
        try
        {
            sane_exit();
        }
        catch (DllNotFoundException)
        {
        }
    }

    public SaneStatus GetDevices(bool localOnly, out IReadOnlyList<SaneDeviceInfo> devices)
    {
        devices = [];
        try
        {
            SaneStatus status = sane_get_devices(out IntPtr deviceList, localOnly ? 1 : 0);
            if (status != SaneStatus.Good || deviceList == IntPtr.Zero)
                return status;

            List<SaneDeviceInfo> list = [];
            for (int index = 0; ; index++)
            {
                IntPtr entryPtr = Marshal.ReadIntPtr(deviceList, index * IntPtr.Size);
                if (entryPtr == IntPtr.Zero)
                    break;

                NativeSaneDevice native = Marshal.PtrToStructure<NativeSaneDevice>(entryPtr);
                string? name = Marshal.PtrToStringUTF8(native.Name);
                if (string.IsNullOrWhiteSpace(name))
                    continue;

                string vendor = Marshal.PtrToStringUTF8(native.Vendor) ?? string.Empty;
                string model = Marshal.PtrToStringUTF8(native.Model) ?? string.Empty;
                string type = Marshal.PtrToStringUTF8(native.Type) ?? string.Empty;
                list.Add(new SaneDeviceInfo(name, vendor, model, type));
            }

            devices = list;
            return SaneStatus.Good;
        }
        catch (DllNotFoundException)
        {
            return SaneStatus.Unsupported;
        }
    }

    public SaneStatus Open(string deviceName, out IntPtr handle)
    {
        try
        {
            return sane_open(deviceName, out handle);
        }
        catch (DllNotFoundException)
        {
            handle = IntPtr.Zero;
            return SaneStatus.Unsupported;
        }
    }

    public void Close(IntPtr handle)
    {
        if (handle != IntPtr.Zero)
            sane_close(handle);
    }

    public bool TrySetStringOption(IntPtr handle, string optionName, string value)
    {
        if (!TryFindOption(handle, optionName, out int index, out NativeSaneOptionDescriptor descriptor) ||
            descriptor.Type != SaneValueType.String)
        {
            return false;
        }

        IntPtr utf8 = Marshal.StringToCoTaskMemUTF8(value);
        try
        {
            return sane_control_option(handle, index, SaneActionSetValue, utf8, out _) == SaneStatus.Good;
        }
        finally
        {
            Marshal.FreeCoTaskMem(utf8);
        }
    }

    public unsafe bool TrySetIntOrFixedOption(IntPtr handle, string optionName, double value)
    {
        if (!TryFindOption(handle, optionName, out int index, out NativeSaneOptionDescriptor descriptor))
            return false;

        int rawValue = descriptor.Type switch
        {
            SaneValueType.Int => (int)Math.Round(value),
            SaneValueType.Fixed => (int)Math.Round(value * (1 << SaneFixedScaleShift)),
            _ => 0,
        };

        if (descriptor.Type is not (SaneValueType.Int or SaneValueType.Fixed))
            return false;

        return sane_control_option(handle, index, SaneActionSetValue, (IntPtr)(&rawValue), out _) == SaneStatus.Good;
    }

    public SaneStatus GetParameters(IntPtr handle, out SaneParameters parameters)
    {
        SaneStatus status = sane_get_parameters(handle, out NativeSaneParameters native);
        parameters = new SaneParameters(
            native.Format,
            native.LastFrame != 0,
            native.BytesPerLine,
            native.PixelsPerLine,
            native.Lines,
            native.Depth);
        return status;
    }

    public SaneStatus Start(IntPtr handle) => sane_start(handle);

    public unsafe SaneStatus Read(IntPtr handle, Span<byte> buffer, out int bytesRead)
    {
        fixed (byte* ptr = buffer)
        {
            return sane_read(handle, (IntPtr)ptr, buffer.Length, out bytesRead);
        }
    }

    public void Cancel(IntPtr handle)
    {
        if (handle != IntPtr.Zero)
            sane_cancel(handle);
    }

    private static unsafe bool TryFindOption(IntPtr handle, string optionName, out int optionIndex, out NativeSaneOptionDescriptor descriptor)
    {
        optionIndex = -1;
        descriptor = default;

        int count = 0;
        if (sane_control_option(handle, 0, SaneActionGetValue, (IntPtr)(&count), out _) != SaneStatus.Good || count <= 0)
            return false;

        for (int i = 1; i < count; i++)
        {
            IntPtr descPtr = sane_get_option_descriptor(handle, i);
            if (descPtr == IntPtr.Zero)
                continue;

            NativeSaneOptionDescriptor candidate = Marshal.PtrToStructure<NativeSaneOptionDescriptor>(descPtr);
            string? name = Marshal.PtrToStringUTF8(candidate.Name);
            if (string.Equals(name, optionName, StringComparison.OrdinalIgnoreCase))
            {
                optionIndex = i;
                descriptor = candidate;
                return true;
            }
        }

        return false;
    }
}

internal static class LinuxScannerFaults
{
    public static InputScannerException CreateException(SaneStatus status, string message)
    {
        (InputFault fault, ScannerFaultCode faultCode) = CreateFault(status, message);
        return new InputScannerException(fault, faultCode);
    }

    public static (InputFault Fault, ScannerFaultCode FaultCode) CreateFault(SaneStatus status, string message)
    {
        (InputErrorCategory category, ScannerFaultCode faultCode) = status switch
        {
            SaneStatus.AccessDenied => (InputErrorCategory.PermissionDenied, ScannerFaultCode.None),
            SaneStatus.DeviceBusy => (InputErrorCategory.DeviceBusy, ScannerFaultCode.None),
            SaneStatus.Unsupported or SaneStatus.Inval => (InputErrorCategory.UnsupportedCapability, ScannerFaultCode.None),
            SaneStatus.Cancelled => (InputErrorCategory.CaptureDiscontinuity, ScannerFaultCode.TransferAborted),
            SaneStatus.Jammed => (InputErrorCategory.CaptureDiscontinuity, ScannerFaultCode.PaperJam),
            SaneStatus.NoDocs => (InputErrorCategory.CaptureDiscontinuity, ScannerFaultCode.FeederEmpty),
            SaneStatus.CoverOpen => (InputErrorCategory.CaptureDiscontinuity, ScannerFaultCode.CoverOpen),
            SaneStatus.IoError => (InputErrorCategory.DeviceRemoved, ScannerFaultCode.None),
            _ => (InputErrorCategory.NativeFailure, ScannerFaultCode.None),
        };

        string fullMessage = $"{message} Native error: SANE status {(int)status} ({GetStatusName(status)}).";
        InputFault fault = new(category, fullMessage, nativeErrorCode: (int)status, nativeFacility: "SANE");
        return (fault, faultCode);
    }

    public static void ThrowIfFailed(SaneStatus status, string message)
    {
        if (status != SaneStatus.Good)
            throw CreateException(status, message);
    }

    private static string GetStatusName(SaneStatus status) => status switch
    {
        SaneStatus.Good => "SANE_STATUS_GOOD",
        SaneStatus.Unsupported => "SANE_STATUS_UNSUPPORTED",
        SaneStatus.Cancelled => "SANE_STATUS_CANCELLED",
        SaneStatus.DeviceBusy => "SANE_STATUS_DEVICE_BUSY",
        SaneStatus.Inval => "SANE_STATUS_INVAL",
        SaneStatus.Eof => "SANE_STATUS_EOF",
        SaneStatus.Jammed => "SANE_STATUS_JAMMED",
        SaneStatus.NoDocs => "SANE_STATUS_NO_DOCS",
        SaneStatus.CoverOpen => "SANE_STATUS_COVER_OPEN",
        SaneStatus.IoError => "SANE_STATUS_IO_ERROR",
        SaneStatus.NoMem => "SANE_STATUS_NO_MEM",
        SaneStatus.AccessDenied => "SANE_STATUS_ACCESS_DENIED",
        _ => "SANE_STATUS_UNKNOWN",
    };
}
