using System;
using System.Runtime.InteropServices;
using System.Text;

namespace Apricadabra.Trackpad.Core.Input
{
    #region Constants

    internal static class RawInputConstants
    {
        // Window messages
        public const uint WM_INPUT = 0x00FF;
        public const uint WM_INPUT_DEVICE_CHANGE = 0x00FE;

        // RAWINPUTDEVICE flags
        public const uint RIDEV_INPUTSINK = 0x00000100;
        public const uint RIDEV_DEVNOTIFY = 0x00002000;

        // RIM_TYPE
        public const uint RIM_TYPEHID = 2;

        // GetRawInputData uiCommand
        public const uint RID_INPUT = 0x10000003;

        // GetRawInputDeviceInfo uiCommand
        public const uint RIDI_PREPARSEDDATA = 0x20000005;
        public const uint RIDI_DEVICENAME = 0x20000007;
        public const uint RIDI_DEVICEINFO = 0x2000000b;

        // HID Usage Pages
        public const ushort HID_USAGE_PAGE_GENERIC = 0x01;
        public const ushort HID_USAGE_PAGE_DIGITIZER = 0x0D;

        // HID Usages - Digitizer page
        public const ushort HID_USAGE_DIGITIZER_TOUCH_PAD = 0x05;
        public const ushort HID_USAGE_DIGITIZER_FINGER = 0x22;
        public const ushort HID_USAGE_DIGITIZER_TIP_SWITCH = 0x42;
        public const ushort HID_USAGE_DIGITIZER_CONTACT_ID = 0x51;
        public const ushort HID_USAGE_DIGITIZER_CONTACT_COUNT = 0x54;
        public const ushort HID_USAGE_DIGITIZER_CONTACT_COUNT_MAX = 0x55;

        // HID Usages - Generic Desktop page
        public const ushort HID_USAGE_GENERIC_X = 0x30;
        public const ushort HID_USAGE_GENERIC_Y = 0x31;

        // HidP report type
        public const ushort HidP_Input = 0;

        // HIDP_STATUS
        public const uint HIDP_STATUS_SUCCESS = 0x00110000;
        public const uint HIDP_STATUS_USAGE_NOT_FOUND = 0xC0110004;

        // Window creation
        public static readonly IntPtr HWND_MESSAGE = (IntPtr)(-3);
    }

    #endregion

    #region Structs

    [StructLayout(LayoutKind.Sequential)]
    internal struct RAWINPUTDEVICE
    {
        public ushort UsagePage;
        public ushort Usage;
        public uint Flags;
        public IntPtr WindowHandle;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct RAWINPUTDEVICELIST
    {
        public IntPtr Device;
        public uint Type;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct RAWINPUTHEADER
    {
        public uint Type;
        public uint Size;
        public IntPtr Device;
        public IntPtr WParam;
    }

    [StructLayout(LayoutKind.Explicit)]
    internal struct RID_DEVICE_INFO
    {
        [FieldOffset(0)]
        public uint cbSize;

        [FieldOffset(4)]
        public uint dwType;

        [FieldOffset(8)]
        public RID_DEVICE_INFO_MOUSE mouse;

        [FieldOffset(8)]
        public RID_DEVICE_INFO_KEYBOARD keyboard;

        [FieldOffset(8)]
        public RID_DEVICE_INFO_HID hid;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct RID_DEVICE_INFO_HID
    {
        public uint VendorId;
        public uint ProductId;
        public uint VersionNumber;
        public ushort UsagePage;
        public ushort Usage;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct RID_DEVICE_INFO_MOUSE
    {
        public uint Id;
        public uint NumberOfButtons;
        public uint SampleRate;
        public int HasHorizontalWheel;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct RID_DEVICE_INFO_KEYBOARD
    {
        public uint Type;
        public uint SubType;
        public uint KeyboardMode;
        public uint NumberOfFunctionKeys;
        public uint NumberOfIndicators;
        public uint NumberOfKeysTotal;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct HIDP_CAPS
    {
        public ushort Usage;
        public ushort UsagePage;
        public ushort InputReportByteLength;
        public ushort OutputReportByteLength;
        public ushort FeatureReportByteLength;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 17)]
        public ushort[] Reserved;

        public ushort NumberLinkCollectionNodes;
        public ushort NumberInputButtonCaps;
        public ushort NumberInputValueCaps;
        public ushort NumberInputDataIndices;
        public ushort NumberOutputButtonCaps;
        public ushort NumberOutputValueCaps;
        public ushort NumberOutputDataIndices;
        public ushort NumberFeatureButtonCaps;
        public ushort NumberFeatureValueCaps;
        public ushort NumberFeatureDataIndices;
    }

    [StructLayout(LayoutKind.Explicit)]
    internal struct HIDP_VALUE_CAPS
    {
        [FieldOffset(0)]
        public ushort UsagePage;

        [FieldOffset(2)]
        public byte ReportID;

        [FieldOffset(3)]
        [MarshalAs(UnmanagedType.U1)]
        public bool IsAlias;

        [FieldOffset(4)]
        public ushort BitField;

        [FieldOffset(6)]
        public ushort LinkCollection;

        [FieldOffset(8)]
        public ushort LinkUsage;

        [FieldOffset(10)]
        public ushort LinkUsagePage;

        [FieldOffset(12)]
        [MarshalAs(UnmanagedType.U1)]
        public bool IsRange;

        [FieldOffset(13)]
        [MarshalAs(UnmanagedType.U1)]
        public bool IsStringRange;

        [FieldOffset(14)]
        [MarshalAs(UnmanagedType.U1)]
        public bool IsDesignatorRange;

        [FieldOffset(15)]
        [MarshalAs(UnmanagedType.U1)]
        public bool IsAbsolute;

        [FieldOffset(16)]
        [MarshalAs(UnmanagedType.U1)]
        public bool HasNull;

        [FieldOffset(18)]
        public ushort BitSize;

        [FieldOffset(20)]
        public ushort ReportCount;

        [FieldOffset(32)]
        public uint UnitsExp;

        [FieldOffset(36)]
        public uint Units;

        [FieldOffset(40)]
        public int LogicalMin;

        [FieldOffset(44)]
        public int LogicalMax;

        [FieldOffset(48)]
        public int PhysicalMin;

        [FieldOffset(52)]
        public int PhysicalMax;

        [FieldOffset(56)]
        public HIDP_RANGE Range;

        [FieldOffset(56)]
        public HIDP_NOT_RANGE NotRange;
    }

    [StructLayout(LayoutKind.Explicit)]
    internal struct HIDP_BUTTON_CAPS
    {
        [FieldOffset(0)]
        public ushort UsagePage;

        [FieldOffset(2)]
        public byte ReportID;

        [FieldOffset(3)]
        [MarshalAs(UnmanagedType.U1)]
        public bool IsAlias;

        [FieldOffset(4)]
        public ushort BitField;

        [FieldOffset(6)]
        public ushort LinkCollection;

        [FieldOffset(8)]
        public ushort LinkUsage;

        [FieldOffset(10)]
        public ushort LinkUsagePage;

        [FieldOffset(12)]
        [MarshalAs(UnmanagedType.U1)]
        public bool IsRange;

        [FieldOffset(13)]
        [MarshalAs(UnmanagedType.U1)]
        public bool IsStringRange;

        [FieldOffset(14)]
        [MarshalAs(UnmanagedType.U1)]
        public bool IsDesignatorRange;

        [FieldOffset(15)]
        [MarshalAs(UnmanagedType.U1)]
        public bool IsAbsolute;

        [FieldOffset(16)]
        public ushort ReportCount;

        [FieldOffset(18)]
        public ushort Reserved2;

        // Reserved[9] — 36 bytes padding (offsets 20-55)

        [FieldOffset(56)]
        public HIDP_RANGE Range;

        [FieldOffset(56)]
        public HIDP_NOT_RANGE NotRange;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct HIDP_RANGE
    {
        public ushort UsageMin;
        public ushort UsageMax;
        public ushort StringMin;
        public ushort StringMax;
        public ushort DesignatorMin;
        public ushort DesignatorMax;
        public ushort DataIndexMin;
        public ushort DataIndexMax;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct HIDP_NOT_RANGE
    {
        public ushort Usage;
        public ushort Reserved1;
        public ushort StringIndex;
        public ushort Reserved2;
        public ushort DesignatorIndex;
        public ushort Reserved3;
        public ushort DataIndex;
        public ushort Reserved4;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    internal struct WNDCLASSEX
    {
        public uint cbSize;
        public uint style;
        public IntPtr lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public IntPtr hInstance;
        public IntPtr hIcon;
        public IntPtr hCursor;
        public IntPtr hbrBackground;
        public string lpszMenuName;
        public string lpszClassName;
        public IntPtr hIconSm;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct MSG
    {
        public IntPtr hwnd;
        public uint message;
        public IntPtr wParam;
        public IntPtr lParam;
        public uint time;
        public POINT pt;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct POINT
    {
        public int X;
        public int Y;
    }

    #endregion

    #region Delegates

    internal delegate IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    #endregion

    #region P/Invoke

    internal static class NativeMethods
    {
        // ---- user32.dll ----

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool RegisterRawInputDevices(
            RAWINPUTDEVICE[] pRawInputDevices,
            uint uiNumDevices,
            uint cbSize);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern uint GetRawInputData(
            IntPtr hRawInput,
            uint uiCommand,
            IntPtr pData,
            ref uint pcbSize,
            uint cbSizeHeader);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern uint GetRawInputData(
            IntPtr hRawInput,
            uint uiCommand,
            [Out] byte[] pData,
            ref uint pcbSize,
            uint cbSizeHeader);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern uint GetRawInputDeviceList(
            [Out] RAWINPUTDEVICELIST[] pRawInputDeviceList,
            ref uint puiNumDevices,
            uint cbSize);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern uint GetRawInputDeviceInfo(
            IntPtr hDevice,
            uint uiCommand,
            IntPtr pData,
            ref uint pcbSize);

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern uint GetRawInputDeviceInfo(
            IntPtr hDevice,
            uint uiCommand,
            StringBuilder pData,
            ref uint pcbSize);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern uint GetRawInputDeviceInfo(
            IntPtr hDevice,
            uint uiCommand,
            ref RID_DEVICE_INFO pData,
            ref uint pcbSize);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern uint GetRawInputDeviceInfo(
            IntPtr hDevice,
            uint uiCommand,
            [Out] byte[] pData,
            ref uint pcbSize);

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        public static extern ushort RegisterClassEx(ref WNDCLASSEX lpwcx);

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        public static extern IntPtr CreateWindowEx(
            uint dwExStyle,
            string lpClassName,
            string lpWindowName,
            uint dwStyle,
            int x,
            int y,
            int nWidth,
            int nHeight,
            IntPtr hWndParent,
            IntPtr hMenu,
            IntPtr hInstance,
            IntPtr lpParam);

        [DllImport("user32.dll")]
        public static extern IntPtr DefWindowProc(
            IntPtr hWnd,
            uint msg,
            IntPtr wParam,
            IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern int GetMessage(
            out MSG lpMsg,
            IntPtr hWnd,
            uint wMsgFilterMin,
            uint wMsgFilterMax);

        [DllImport("user32.dll")]
        public static extern bool TranslateMessage(ref MSG lpMsg);

        [DllImport("user32.dll")]
        public static extern IntPtr DispatchMessage(ref MSG lpMsg);

        [DllImport("user32.dll")]
        public static extern void PostQuitMessage(int nExitCode);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool DestroyWindow(IntPtr hWnd);

        // ---- hid.dll ----

        [DllImport("hid.dll", SetLastError = true)]
        public static extern uint HidP_GetCaps(
            IntPtr preparsedData,
            out HIDP_CAPS capabilities);

        [DllImport("hid.dll", SetLastError = true)]
        public static extern uint HidP_GetValueCaps(
            ushort reportType,
            [Out] HIDP_VALUE_CAPS[] valueCaps,
            ref ushort valueCapsLength,
            IntPtr preparsedData);

        [DllImport("hid.dll", SetLastError = true)]
        public static extern uint HidP_GetButtonCaps(
            ushort reportType,
            [Out] HIDP_BUTTON_CAPS[] buttonCaps,
            ref ushort buttonCapsLength,
            IntPtr preparsedData);

        [DllImport("hid.dll", SetLastError = true)]
        public static extern uint HidP_GetUsageValue(
            ushort reportType,
            ushort usagePage,
            ushort linkCollection,
            ushort usage,
            out int usageValue,
            IntPtr preparsedData,
            [In] byte[] report,
            uint reportLength);

        [DllImport("hid.dll", SetLastError = true)]
        public static extern uint HidP_GetUsages(
            ushort reportType,
            ushort usagePage,
            ushort linkCollection,
            [Out] ushort[] usageList,
            ref uint usageLength,
            IntPtr preparsedData,
            [In] byte[] report,
            uint reportLength);

        // ---- kernel32.dll ----

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        public static extern IntPtr GetModuleHandle(string lpModuleName);
    }

    #endregion
}
