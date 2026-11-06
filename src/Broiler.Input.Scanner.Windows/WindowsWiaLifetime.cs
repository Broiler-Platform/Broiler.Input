using Broiler.Native.Windows;
using Broiler.Native.Windows.Wasapi;
using System;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using System.Runtime.Versioning;

namespace Broiler.Input.Scanner.Windows;

internal static class WindowsWiaConstants
{
    public const int WIA_DEVINFO_ENUM_ALL = 0x0000000F;
    public const int WIA_DEVINFO_ENUM_LOCAL = 0x00000010;

    public const int StiDeviceTypeDefault = 0;
    public const int StiDeviceTypeScanner = 1;

    public const uint PRSPEC_PROPID = 1;
    public const uint CLSCTX_LOCAL_SERVER = 0x4;

    public const ushort VT_I4 = 3;
    public const ushort VT_UI4 = 19;
    public const ushort VT_BSTR = 8;
    public const ushort VT_LPWSTR = 31;
    public const ushort VT_CLSID = 72;

    // Device Information Properties (wiadef.h)
    public const uint WIA_DIP_DEV_ID = 2;
    public const uint WIA_DIP_VEND_DESC = 3;
    public const uint WIA_DIP_DEV_DESC = 4;
    public const uint WIA_DIP_DEV_TYPE = 5;
    public const uint WIA_DIP_DEV_NAME = 7;

    // Scanner Device & Item Properties (wiadef.h)
    public const uint WIA_DPS_HORIZONTAL_BED_SIZE = 3074;
    public const uint WIA_DPS_VERTICAL_BED_SIZE = 3075;
    public const uint WIA_DPS_DOCUMENT_HANDLING_CAPABILITIES = 3086;
    public const uint WIA_DPS_DOCUMENT_HANDLING_STATUS = 3087;
    public const uint WIA_DPS_DOCUMENT_HANDLING_SELECT = 3088;
    public const uint WIA_DPS_PAGES = 3096;

    public const uint WIA_IPA_ITEM_NAME = 4098;
    public const uint WIA_IPA_FULL_ITEM_NAME = 4099;
    public const uint WIA_IPA_ITEM_FLAGS = 4101;
    public const uint WIA_IPA_DATATYPE = 4103;
    public const uint WIA_IPA_DEPTH = 4104;
    public const uint WIA_IPA_FORMAT = 4106;
    public const uint WIA_IPA_PIXELS_PER_LINE = 4112;
    public const uint WIA_IPA_BYTES_PER_LINE = 4113;
    public const uint WIA_IPA_NUMBER_OF_LINES = 4114;
    public const uint WIA_IPA_ITEM_CATEGORY = 4125;

    public const uint WIA_IPS_XRES = 6147;
    public const uint WIA_IPS_YRES = 6148;
    public const uint WIA_IPS_XPOS = 6149;
    public const uint WIA_IPS_YPOS = 6150;
    public const uint WIA_IPS_XEXTENT = 6151;
    public const uint WIA_IPS_YEXTENT = 6152;
    public const uint WIA_IPS_BRIGHTNESS = 6154;
    public const uint WIA_IPS_CONTRAST = 6155;

    // Document Handling Flags
    public const int FEEDER = 0x001;
    public const int FLATBED = 0x002;
    public const int DUPLEX = 0x004;
    public const int AUTO_SOURCE = 0x8000;

    // Data Types (WIA_IPA_DATATYPE)
    public const int WIA_DATA_THRESHOLD = 0;
    public const int WIA_DATA_GRAYSCALE = 2;
    public const int WIA_DATA_COLOR = 3;

    // Transfer callback messages
    public const int WIA_TRANSFER_MSG_STATUS = 0x00001;
    public const int WIA_TRANSFER_MSG_END_OF_STREAM = 0x00002;
    public const int WIA_TRANSFER_MSG_END_OF_TRANSFER = 0x00003;
    public const int WIA_TRANSFER_MSG_DEVICE_STATUS = 0x00005;
    public const int WIA_TRANSFER_MSG_NEW_PAGE = 0x00006;

    // WIA HRESULTs (Facility 33 = 0x80210000)
    public const int WIA_ERROR_GENERAL_ERROR = unchecked((int)0x80210001);
    public const int WIA_ERROR_PAPER_JAM = unchecked((int)0x80210002);
    public const int WIA_ERROR_PAPER_EMPTY = unchecked((int)0x80210003);
    public const int WIA_ERROR_PAPER_PROBLEM = unchecked((int)0x80210004);
    public const int WIA_ERROR_OFFLINE = unchecked((int)0x80210005);
    public const int WIA_ERROR_BUSY = unchecked((int)0x80210006);
    public const int WIA_ERROR_WARMING_UP = unchecked((int)0x80210007);
    public const int WIA_ERROR_USER_INTERVENTION = unchecked((int)0x80210008);
    public const int WIA_ERROR_ITEM_DELETED = unchecked((int)0x80210009);
    public const int WIA_ERROR_DEVICE_COMMUNICATION = unchecked((int)0x8021000A);
    public const int WIA_ERROR_INVALID_COMMAND = unchecked((int)0x8021000B);
    public const int WIA_ERROR_INCORRECT_HARDWARE_SETTING = unchecked((int)0x8021000C);
    public const int WIA_ERROR_DEVICE_LOCKED = unchecked((int)0x8021000D);
    public const int WIA_ERROR_EXCEPTION_IN_DRIVER = unchecked((int)0x8021000E);
    public const int WIA_ERROR_INVALID_DRIVER_RESPONSE = unchecked((int)0x8021000F);
    public const int WIA_ERROR_COVER_OPEN = unchecked((int)0x80210010);
    public const int WIA_ERROR_LAMP_OFF = unchecked((int)0x80210011);
    public const int WIA_ERROR_MULTI_FEED = unchecked((int)0x80210014);
    public const int WIA_S_NO_DEVICE_AVAILABLE = unchecked((int)0x80210015);
    public const int REGDB_E_CLASSNOTREG = unchecked((int)0x80040154);

    public static readonly Guid ClsidWiaDevMgr2 = new("B6C292BC-7C88-41EE-8B54-8EC92617E599");
    public static readonly Guid IidIWiaDevMgr2 = new("79C07CF1-CBDD-41EE-8EC3-F00080CADA7A");
    public static readonly Guid IidIWiaPropertyStorage = new("98B5E8A0-29CC-491A-AAC0-E6DB4FDCCEB6");
    public static readonly Guid IidIWiaTransfer = new("C39D6942-2F4E-4D04-92FE-4EF4D3A1DE5A");

    public static readonly Guid WiaImgFmt_RAW = new("B96B3CA9-0728-11D3-9D7B-0000F81EF32E");
    public static readonly Guid WiaImgFmt_MEMORYBMP = new("B96B3CAA-0728-11D3-9D7B-0000F81EF32E");
    public static readonly Guid WiaImgFmt_BMP = new("B96B3CAB-0728-11D3-9D7B-0000F81EF32E");

    public static readonly Guid WiaItemCategoryFlatbed = new("FB607B1F-43F3-488B-855B-FB703EC342A6");
    public static readonly Guid WiaItemCategoryFeeder = new("FE131934-F84C-42AD-8DA4-6129CDDD7288");
}

[StructLayout(LayoutKind.Sequential)]
internal struct WiaPropSpec
{
    public uint Kind;
    public IntPtr Value;

    public static WiaPropSpec FromPropertyId(uint propertyId) => new()
    {
        Kind = WindowsWiaConstants.PRSPEC_PROPID,
        Value = (IntPtr)propertyId,
    };
}

[StructLayout(LayoutKind.Sequential)]
internal struct WiaTransferParams
{
    public int Message;
    public int PercentComplete;
    public ulong TransferredBytes;
    public int ErrorStatus;
}

[GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16)]
[Guid("79C07CF1-CBDD-41EE-8EC3-F00080CADA7A")]
internal partial interface IWiaDevMgr2
{
    [PreserveSig]
    int EnumDeviceInfo(int flags, out IEnumWIA_DEV_INFO enumDevInfo);

    [PreserveSig]
    int CreateDevice(int flags, [MarshalAs(UnmanagedType.BStr)] string deviceId, out IWiaItem2 rootItem);
}

[GeneratedComInterface]
[Guid("5E38B83C-8CF1-11D1-BF92-0060081ED811")]
internal partial interface IEnumWIA_DEV_INFO
{
    [PreserveSig]
    int Next(uint count, out IntPtr propertyStorage, out uint fetched);

    [PreserveSig]
    int Skip(uint count);

    [PreserveSig]
    int Reset();

    [PreserveSig]
    int Clone(out IEnumWIA_DEV_INFO cloned);

    [PreserveSig]
    int GetCount(out uint count);
}

[GeneratedComInterface]
[Guid("98B5E8A0-29CC-491A-AAC0-E6DB4FDCCEB6")]
internal partial interface IWiaPropertyStorage
{
    [PreserveSig]
    int ReadMultiple(uint count, IntPtr specs, IntPtr values);

    [PreserveSig]
    int WriteMultiple(uint count, IntPtr specs, IntPtr values, uint propidNameFirst);
}

[GeneratedComInterface]
[Guid("6CBA0075-1287-407D-9B77-CF0E030435CC")]
internal partial interface IWiaItem2
{
    [PreserveSig]
    int CreateChildItem(int itemFlags, int creationFlags, [MarshalAs(UnmanagedType.BStr)] string itemName, out IWiaItem2 childItem);

    [PreserveSig]
    int DeleteItem(int flags);

    [PreserveSig]
    int EnumChildItems(IntPtr categoryGuid, out IEnumWiaItem2 enumItems);

    [PreserveSig]
    int FindItemByName(int flags, [MarshalAs(UnmanagedType.BStr)] string fullItemName, out IWiaItem2 item);

    [PreserveSig]
    int GetItemCategory(out Guid itemCategoryGuid);

    [PreserveSig]
    int GetItemType(out int itemType);
}

[GeneratedComInterface]
[Guid("59970AF4-CD0D-44D9-AB24-52295630E582")]
internal partial interface IEnumWiaItem2
{
    [PreserveSig]
    int Next(uint count, out IntPtr item, out uint fetched);

    [PreserveSig]
    int Skip(uint count);

    [PreserveSig]
    int Reset();

    [PreserveSig]
    int Clone(out IEnumWiaItem2 cloned);

    [PreserveSig]
    int GetCount(out uint count);
}

[GeneratedComInterface]
[Guid("C39D6942-2F4E-4D04-92FE-4EF4D3A1DE5A")]
internal partial interface IWiaTransfer
{
    [PreserveSig]
    int Download(int flags, IWiaTransferCallback callback);

    [PreserveSig]
    int Upload(int flags, IStream source, IWiaTransferCallback callback);

    [PreserveSig]
    int Cancel();
}

[GeneratedComInterface]
[Guid("27D4EA4F-5D06-4095-8E0F-ECE2E6E2D24E")]
internal partial interface IWiaTransferCallback
{
    [PreserveSig]
    int TransferCallback(int flags, ref WiaTransferParams transferParams);

    [PreserveSig]
    int GetNextStream(int flags, [MarshalAs(UnmanagedType.BStr)] string itemName,
        [MarshalAs(UnmanagedType.BStr)] string fullItemName, out IStream destination);
}

[SupportedOSPlatform("windows")]
internal static class WindowsWiaPropertyHelper
{
    public static unsafe string? TryReadString(IWiaPropertyStorage storage, uint propertyId)
    {
        WiaPropSpec spec = WiaPropSpec.FromPropertyId(propertyId);
        PropVariant value = default;

        try
        {
            int result = storage.ReadMultiple(1, (IntPtr)(&spec), (IntPtr)(&value));
            if (result != ComNative.S_OK || value.PointerValue == IntPtr.Zero)
                return null;

            return value.ValueType switch
            {
                WindowsWiaConstants.VT_BSTR => Marshal.PtrToStringBSTR(value.PointerValue),
                WindowsWiaConstants.VT_LPWSTR => Marshal.PtrToStringUni(value.PointerValue),
                _ => null,
            };
        }
        finally
        {
            if (value.ValueType != 0)
                _ = WindowsWasapiNative.PropVariantClear(ref value);
        }
    }

    public static unsafe bool TryReadInt32(IWiaPropertyStorage storage, uint propertyId, out int intValue)
    {
        intValue = 0;
        WiaPropSpec spec = WiaPropSpec.FromPropertyId(propertyId);
        PropVariant value = default;

        try
        {
            int result = storage.ReadMultiple(1, (IntPtr)(&spec), (IntPtr)(&value));
            if (result != ComNative.S_OK)
                return false;

            if (value.ValueType is WindowsWiaConstants.VT_I4 or WindowsWiaConstants.VT_UI4)
            {
                intValue = unchecked((int)value.PointerValue.ToInt64());
                return true;
            }

            return false;
        }
        finally
        {
            if (value.ValueType != 0)
                _ = WindowsWasapiNative.PropVariantClear(ref value);
        }
    }

    public static unsafe bool TryWriteInt32(IWiaPropertyStorage storage, uint propertyId, int intValue)
    {
        WiaPropSpec spec = WiaPropSpec.FromPropertyId(propertyId);
        PropVariant value = new()
        {
            ValueType = WindowsWiaConstants.VT_I4,
            PointerValue = new IntPtr(intValue),
        };

        int result = storage.WriteMultiple(1, (IntPtr)(&spec), (IntPtr)(&value), 2);
        return result >= 0;
    }

    public static unsafe bool TryWriteGuid(IWiaPropertyStorage storage, uint propertyId, Guid guidValue)
    {
        WiaPropSpec spec = WiaPropSpec.FromPropertyId(propertyId);
        PropVariant value = new()
        {
            ValueType = WindowsWiaConstants.VT_CLSID,
            PointerValue = (IntPtr)(&guidValue),
        };

        int result = storage.WriteMultiple(1, (IntPtr)(&spec), (IntPtr)(&value), 2);
        return result >= 0;
    }
}

internal static class WindowsScannerFaults
{
    public static InputScannerException CreateException(int hresult, string message, string nativeFacility = "WIA")
    {
        (InputFault fault, ScannerFaultCode faultCode) = CreateFault(hresult, message, nativeFacility);
        return new InputScannerException(fault, faultCode);
    }

    public static (InputFault Fault, ScannerFaultCode FaultCode) CreateFault(int hresult, string message, string nativeFacility = "WIA")
    {
        (InputErrorCategory category, ScannerFaultCode faultCode) = hresult switch
        {
            ComNative.E_ACCESSDENIED => (InputErrorCategory.PermissionDenied, ScannerFaultCode.None),
            ComNative.E_NOTFOUND or
                WindowsWiaConstants.WIA_S_NO_DEVICE_AVAILABLE => (InputErrorCategory.DeviceNotFound, ScannerFaultCode.None),
            WindowsWiaConstants.WIA_ERROR_BUSY or
                WindowsWiaConstants.WIA_ERROR_DEVICE_LOCKED or
                WindowsWiaConstants.WIA_ERROR_WARMING_UP => (InputErrorCategory.DeviceBusy, ScannerFaultCode.None),
            WindowsWiaConstants.WIA_ERROR_OFFLINE or
                WindowsWiaConstants.WIA_ERROR_ITEM_DELETED or
                WindowsWiaConstants.WIA_ERROR_DEVICE_COMMUNICATION => (InputErrorCategory.DeviceRemoved, ScannerFaultCode.None),
            WindowsWiaConstants.WIA_ERROR_INVALID_COMMAND or
                WindowsWiaConstants.WIA_ERROR_INCORRECT_HARDWARE_SETTING => (InputErrorCategory.UnsupportedCapability, ScannerFaultCode.None),
            WindowsWiaConstants.WIA_ERROR_PAPER_JAM or
                WindowsWiaConstants.WIA_ERROR_PAPER_PROBLEM => (InputErrorCategory.CaptureDiscontinuity, ScannerFaultCode.PaperJam),
            WindowsWiaConstants.WIA_ERROR_PAPER_EMPTY => (InputErrorCategory.CaptureDiscontinuity, ScannerFaultCode.FeederEmpty),
            WindowsWiaConstants.WIA_ERROR_COVER_OPEN => (InputErrorCategory.CaptureDiscontinuity, ScannerFaultCode.CoverOpen),
            WindowsWiaConstants.WIA_ERROR_LAMP_OFF => (InputErrorCategory.NativeFailure, ScannerFaultCode.LampFailure),
            WindowsWiaConstants.WIA_ERROR_MULTI_FEED => (InputErrorCategory.CaptureDiscontinuity, ScannerFaultCode.MultiFeedDetected),
            WindowsWiaConstants.REGDB_E_CLASSNOTREG => (InputErrorCategory.HostUnavailable, ScannerFaultCode.None),
            _ => (InputErrorCategory.NativeFailure, ScannerFaultCode.None),
        };

        InputFault fault = new(category, FormatNativeFailureMessage(message, hresult, nativeFacility),
            nativeErrorCode: hresult, nativeFacility: nativeFacility);
        return (fault, faultCode);
    }

    public static void ThrowIfFailed(int hresult, string message, string nativeFacility = "WIA")
    {
        if (hresult < 0)
            throw CreateException(hresult, message, nativeFacility);
    }

    private static string FormatNativeFailureMessage(string message, int hresult, string nativeFacility)
    {
        string formattedCode = "0x" + unchecked((uint)hresult).ToString("X8");
        string? name = GetNativeErrorName(hresult);
        string suffix = name is null
            ? nativeFacility + " HRESULT " + formattedCode
            : nativeFacility + " HRESULT " + formattedCode + " (" + name + ")";

        return message + " Native error: " + suffix + ".";
    }

    private static string? GetNativeErrorName(int hresult) => hresult switch
    {
        ComNative.E_ACCESSDENIED => "E_ACCESSDENIED",
        ComNative.E_NOTFOUND => "E_NOTFOUND",
        ComNative.E_NOINTERFACE => "E_NOINTERFACE",
        ComNative.RPC_E_CHANGED_MODE => "RPC_E_CHANGED_MODE",
        WindowsWiaConstants.REGDB_E_CLASSNOTREG => "REGDB_E_CLASSNOTREG",
        WindowsWiaConstants.WIA_ERROR_GENERAL_ERROR => "WIA_ERROR_GENERAL_ERROR",
        WindowsWiaConstants.WIA_ERROR_PAPER_JAM => "WIA_ERROR_PAPER_JAM",
        WindowsWiaConstants.WIA_ERROR_PAPER_EMPTY => "WIA_ERROR_PAPER_EMPTY",
        WindowsWiaConstants.WIA_ERROR_PAPER_PROBLEM => "WIA_ERROR_PAPER_PROBLEM",
        WindowsWiaConstants.WIA_ERROR_OFFLINE => "WIA_ERROR_OFFLINE",
        WindowsWiaConstants.WIA_ERROR_BUSY => "WIA_ERROR_BUSY",
        WindowsWiaConstants.WIA_ERROR_WARMING_UP => "WIA_ERROR_WARMING_UP",
        WindowsWiaConstants.WIA_ERROR_USER_INTERVENTION => "WIA_ERROR_USER_INTERVENTION",
        WindowsWiaConstants.WIA_ERROR_ITEM_DELETED => "WIA_ERROR_ITEM_DELETED",
        WindowsWiaConstants.WIA_ERROR_DEVICE_COMMUNICATION => "WIA_ERROR_DEVICE_COMMUNICATION",
        WindowsWiaConstants.WIA_ERROR_INVALID_COMMAND => "WIA_ERROR_INVALID_COMMAND",
        WindowsWiaConstants.WIA_ERROR_INCORRECT_HARDWARE_SETTING => "WIA_ERROR_INCORRECT_HARDWARE_SETTING",
        WindowsWiaConstants.WIA_ERROR_DEVICE_LOCKED => "WIA_ERROR_DEVICE_LOCKED",
        WindowsWiaConstants.WIA_ERROR_EXCEPTION_IN_DRIVER => "WIA_ERROR_EXCEPTION_IN_DRIVER",
        WindowsWiaConstants.WIA_ERROR_INVALID_DRIVER_RESPONSE => "WIA_ERROR_INVALID_DRIVER_RESPONSE",
        WindowsWiaConstants.WIA_ERROR_COVER_OPEN => "WIA_ERROR_COVER_OPEN",
        WindowsWiaConstants.WIA_ERROR_LAMP_OFF => "WIA_ERROR_LAMP_OFF",
        WindowsWiaConstants.WIA_ERROR_MULTI_FEED => "WIA_ERROR_MULTI_FEED",
        WindowsWiaConstants.WIA_S_NO_DEVICE_AVAILABLE => "WIA_S_NO_DEVICE_AVAILABLE",
        _ => null,
    };
}
