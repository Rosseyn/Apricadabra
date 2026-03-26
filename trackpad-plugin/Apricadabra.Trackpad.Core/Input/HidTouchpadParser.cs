using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using Apricadabra.Trackpad.Core.Models;

namespace Apricadabra.Trackpad.Core.Input
{
    public class HidTouchpadParser : IDisposable
    {
        #region Internal types

        internal class DeviceContext
        {
            public IntPtr PreparsedData;
            public GCHandle PreparsedHandle;
            public ushort InputReportByteLength;
            public Dictionary<ushort, LinkCollectionInfo> LinkCollections;
            public int MaxContacts;
            public string DevicePath;
            public string DeviceName;
        }

        internal class LinkCollectionInfo
        {
            public bool HasX, HasY, HasContactId, HasTipSwitch;
            public int PhysicalMinX, PhysicalMaxX, PhysicalMinY, PhysicalMaxY;
            public int LogicalMinX, LogicalMaxX, LogicalMinY, LogicalMaxY;
        }

        #endregion

        private readonly List<TouchpadDevice> _devices = new List<TouchpadDevice>();
        private readonly Dictionary<IntPtr, DeviceContext> _deviceContexts = new Dictionary<IntPtr, DeviceContext>();

        // Buffered contacts for building frames
        private readonly List<ContactPoint> _contactBuffer = new List<ContactPoint>();
        private int _expectedContactCount;
        private IntPtr _currentFrameDevice;

        public List<TouchpadDevice> Devices => _devices;
        public Dictionary<IntPtr, DeviceContext> DeviceContexts => _deviceContexts;
        public event Action<ContactFrame> OnContactFrame;

        #region Device discovery

        public void EnumerateDevices()
        {
            // Clean up previous state
            foreach (var ctx in _deviceContexts.Values)
            {
                if (ctx.PreparsedHandle.IsAllocated)
                    ctx.PreparsedHandle.Free();
            }
            _deviceContexts.Clear();
            _devices.Clear();

            // Step 1: Get device count
            uint deviceCount = 0;
            uint deviceListSize = (uint)Marshal.SizeOf<RAWINPUTDEVICELIST>();
            uint result = NativeMethods.GetRawInputDeviceList(null, ref deviceCount, deviceListSize);
            if (result == unchecked((uint)-1) || deviceCount == 0)
                return;

            // Step 2: Fill device list
            var deviceList = new RAWINPUTDEVICELIST[deviceCount];
            result = NativeMethods.GetRawInputDeviceList(deviceList, ref deviceCount, deviceListSize);
            if (result == unchecked((uint)-1))
                return;

            // Step 3: Inspect each HID device
            for (int i = 0; i < deviceCount; i++)
            {
                if (deviceList[i].Type != RawInputConstants.RIM_TYPEHID)
                    continue;

                IntPtr deviceHandle = deviceList[i].Device;

                try
                {
                    var ctx = TryBuildDeviceContext(deviceHandle);
                    if (ctx == null)
                        continue;

                    _deviceContexts[deviceHandle] = ctx;
                    _devices.Add(new TouchpadDevice(ctx.DevicePath, ctx.DeviceName, ctx.MaxContacts));
                }
                catch
                {
                    // Skip devices that fail to enumerate
                }
            }
        }

        private DeviceContext TryBuildDeviceContext(IntPtr deviceHandle)
        {
            // 3a: Check UsagePage/Usage via RIDI_DEVICEINFO
            var deviceInfo = new RID_DEVICE_INFO();
            deviceInfo.cbSize = (uint)Marshal.SizeOf<RID_DEVICE_INFO>();
            uint infoSize = deviceInfo.cbSize;

            uint infoResult = NativeMethods.GetRawInputDeviceInfo(
                deviceHandle, RawInputConstants.RIDI_DEVICEINFO, ref deviceInfo, ref infoSize);
            if (infoResult == unchecked((uint)-1))
                return null;

            if (deviceInfo.hid.UsagePage != RawInputConstants.HID_USAGE_PAGE_DIGITIZER ||
                deviceInfo.hid.Usage != RawInputConstants.HID_USAGE_DIGITIZER_TOUCH_PAD)
                return null;

            // 3b: Get device path
            uint nameSize = 0;
            NativeMethods.GetRawInputDeviceInfo(deviceHandle, RawInputConstants.RIDI_DEVICENAME, IntPtr.Zero, ref nameSize);
            if (nameSize == 0)
                return null;

            var nameBuilder = new StringBuilder((int)nameSize);
            NativeMethods.GetRawInputDeviceInfo(deviceHandle, RawInputConstants.RIDI_DEVICENAME, nameBuilder, ref nameSize);
            string devicePath = nameBuilder.ToString();

            // 3c: Get preparsed data
            uint preparsedSize = 0;
            NativeMethods.GetRawInputDeviceInfo(deviceHandle, RawInputConstants.RIDI_PREPARSEDDATA, IntPtr.Zero, ref preparsedSize);
            if (preparsedSize == 0)
                return null;

            byte[] preparsedBytes = new byte[preparsedSize];
            uint ppResult = NativeMethods.GetRawInputDeviceInfo(
                deviceHandle, RawInputConstants.RIDI_PREPARSEDDATA, preparsedBytes, ref preparsedSize);
            if (ppResult == unchecked((uint)-1))
                return null;

            GCHandle preparsedHandle = GCHandle.Alloc(preparsedBytes, GCHandleType.Pinned);
            IntPtr preparsedPtr = preparsedHandle.AddrOfPinnedObject();

            // 3d: Get capabilities
            uint capsResult = NativeMethods.HidP_GetCaps(preparsedPtr, out HIDP_CAPS caps);
            if (capsResult != RawInputConstants.HIDP_STATUS_SUCCESS)
            {
                preparsedHandle.Free();
                return null;
            }

            // 3e: Get value caps
            var linkCollections = new Dictionary<ushort, LinkCollectionInfo>();
            int maxContacts = 0;

            if (caps.NumberInputValueCaps > 0)
            {
                ushort valueCapsCount = caps.NumberInputValueCaps;
                var valueCaps = new HIDP_VALUE_CAPS[valueCapsCount];
                uint vcResult = NativeMethods.HidP_GetValueCaps(
                    RawInputConstants.HidP_Input, valueCaps, ref valueCapsCount, preparsedPtr);

                if (vcResult == RawInputConstants.HIDP_STATUS_SUCCESS)
                {
                    for (int v = 0; v < valueCapsCount; v++)
                    {
                        var vc = valueCaps[v];
                        ushort linkCol = vc.LinkCollection;

                        if (!linkCollections.TryGetValue(linkCol, out var info))
                        {
                            info = new LinkCollectionInfo();
                            linkCollections[linkCol] = info;
                        }

                        ushort usage = vc.IsRange ? vc.Range.UsageMin : vc.NotRange.Usage;

                        if (vc.UsagePage == RawInputConstants.HID_USAGE_PAGE_GENERIC)
                        {
                            if (usage == RawInputConstants.HID_USAGE_GENERIC_X)
                            {
                                info.HasX = true;
                                info.LogicalMinX = vc.LogicalMin;
                                info.LogicalMaxX = vc.LogicalMax;
                                info.PhysicalMinX = vc.PhysicalMin;
                                info.PhysicalMaxX = vc.PhysicalMax;
                            }
                            else if (usage == RawInputConstants.HID_USAGE_GENERIC_Y)
                            {
                                info.HasY = true;
                                info.LogicalMinY = vc.LogicalMin;
                                info.LogicalMaxY = vc.LogicalMax;
                                info.PhysicalMinY = vc.PhysicalMin;
                                info.PhysicalMaxY = vc.PhysicalMax;
                            }
                        }
                        else if (vc.UsagePage == RawInputConstants.HID_USAGE_PAGE_DIGITIZER)
                        {
                            if (usage == RawInputConstants.HID_USAGE_DIGITIZER_CONTACT_ID)
                            {
                                info.HasContactId = true;
                            }
                            else if (usage == RawInputConstants.HID_USAGE_DIGITIZER_CONTACT_COUNT_MAX)
                            {
                                maxContacts = vc.LogicalMax;
                            }
                        }
                    }
                }
            }

            // 3f: Get button caps
            if (caps.NumberInputButtonCaps > 0)
            {
                ushort buttonCapsCount = caps.NumberInputButtonCaps;
                var buttonCaps = new HIDP_BUTTON_CAPS[buttonCapsCount];
                uint bcResult = NativeMethods.HidP_GetButtonCaps(
                    RawInputConstants.HidP_Input, buttonCaps, ref buttonCapsCount, preparsedPtr);

                if (bcResult == RawInputConstants.HIDP_STATUS_SUCCESS)
                {
                    for (int b = 0; b < buttonCapsCount; b++)
                    {
                        var bc = buttonCaps[b];
                        if (bc.UsagePage != RawInputConstants.HID_USAGE_PAGE_DIGITIZER)
                            continue;

                        ushort usageMin = bc.IsRange ? bc.Range.UsageMin : bc.NotRange.Usage;
                        ushort usageMax = bc.IsRange ? bc.Range.UsageMax : bc.NotRange.Usage;

                        if (usageMin <= RawInputConstants.HID_USAGE_DIGITIZER_TIP_SWITCH &&
                            usageMax >= RawInputConstants.HID_USAGE_DIGITIZER_TIP_SWITCH)
                        {
                            ushort linkCol = bc.LinkCollection;
                            if (!linkCollections.TryGetValue(linkCol, out var info))
                            {
                                info = new LinkCollectionInfo();
                                linkCollections[linkCol] = info;
                            }
                            info.HasTipSwitch = true;
                        }
                    }
                }
            }

            // Fallback: at least 1 contact if we found finger link collections
            if (maxContacts == 0)
            {
                foreach (var kvp in linkCollections)
                {
                    if (kvp.Value.HasX && kvp.Value.HasY && kvp.Value.HasContactId)
                        maxContacts++;
                }
            }

            // Derive a friendly name from the device path
            string deviceName = $"Touchpad (VID:{deviceInfo.hid.VendorId:X4} PID:{deviceInfo.hid.ProductId:X4})";

            return new DeviceContext
            {
                PreparsedData = preparsedPtr,
                PreparsedHandle = preparsedHandle,
                InputReportByteLength = caps.InputReportByteLength,
                LinkCollections = linkCollections,
                MaxContacts = maxContacts,
                DevicePath = devicePath,
                DeviceName = deviceName,
            };
        }

        #endregion

        #region Report parsing

        public void ProcessRawInput(IntPtr deviceHandle, byte[] rawHidData, int dataLength)
        {
            if (!_deviceContexts.TryGetValue(deviceHandle, out var ctx))
                return;

            if (dataLength < 8)
                return;

            // RAWHID structure: dwSizeHid (4 bytes) + dwCount (4 bytes) + bRawData
            int dwSizeHid = BitConverter.ToInt32(rawHidData, 0);
            int dwCount = BitConverter.ToInt32(rawHidData, 4);

            if (dwSizeHid <= 0 || dwCount <= 0)
                return;

            int dataOffset = 8;

            for (int r = 0; r < dwCount; r++)
            {
                int reportOffset = dataOffset + (r * dwSizeHid);
                if (reportOffset + dwSizeHid > dataLength)
                    break;

                // Extract single report into its own buffer
                byte[] report = new byte[dwSizeHid];
                Array.Copy(rawHidData, reportOffset, report, 0, dwSizeHid);
                uint reportLength = (uint)dwSizeHid;

                // Read ContactCount from link collection 0
                int contactCount = 0;
                uint ccResult = NativeMethods.HidP_GetUsageValue(
                    RawInputConstants.HidP_Input,
                    RawInputConstants.HID_USAGE_PAGE_DIGITIZER,
                    0, // link collection 0
                    RawInputConstants.HID_USAGE_DIGITIZER_CONTACT_COUNT,
                    out int ccValue,
                    ctx.PreparsedData,
                    report,
                    reportLength);

                if (ccResult == RawInputConstants.HIDP_STATUS_SUCCESS)
                {
                    contactCount = ccValue;
                    // New frame starting
                    _expectedContactCount = contactCount;
                    _contactBuffer.Clear();
                    _currentFrameDevice = deviceHandle;
                }

                // Parse each link collection that has contact data
                foreach (var kvp in ctx.LinkCollections)
                {
                    ushort linkCol = kvp.Key;
                    var info = kvp.Value;

                    // Skip link collection 0 if it doesn't have finger data
                    if (!info.HasX || !info.HasY)
                        continue;

                    // Extract contact ID
                    int contactId = 0;
                    if (info.HasContactId)
                    {
                        NativeMethods.HidP_GetUsageValue(
                            RawInputConstants.HidP_Input,
                            RawInputConstants.HID_USAGE_PAGE_DIGITIZER,
                            linkCol,
                            RawInputConstants.HID_USAGE_DIGITIZER_CONTACT_ID,
                            out contactId,
                            ctx.PreparsedData,
                            report,
                            reportLength);
                    }

                    // Extract X
                    int xValue = 0;
                    NativeMethods.HidP_GetUsageValue(
                        RawInputConstants.HidP_Input,
                        RawInputConstants.HID_USAGE_PAGE_GENERIC,
                        linkCol,
                        RawInputConstants.HID_USAGE_GENERIC_X,
                        out xValue,
                        ctx.PreparsedData,
                        report,
                        reportLength);

                    // Extract Y
                    int yValue = 0;
                    NativeMethods.HidP_GetUsageValue(
                        RawInputConstants.HidP_Input,
                        RawInputConstants.HID_USAGE_PAGE_GENERIC,
                        linkCol,
                        RawInputConstants.HID_USAGE_GENERIC_Y,
                        out yValue,
                        ctx.PreparsedData,
                        report,
                        reportLength);

                    // Extract TipSwitch (on-surface)
                    bool onSurface = false;
                    if (info.HasTipSwitch)
                    {
                        ushort[] usages = new ushort[16];
                        uint usageCount = (uint)usages.Length;
                        uint tsResult = NativeMethods.HidP_GetUsages(
                            RawInputConstants.HidP_Input,
                            RawInputConstants.HID_USAGE_PAGE_DIGITIZER,
                            linkCol,
                            usages,
                            ref usageCount,
                            ctx.PreparsedData,
                            report,
                            reportLength);

                        if (tsResult == RawInputConstants.HIDP_STATUS_SUCCESS)
                        {
                            for (uint u = 0; u < usageCount; u++)
                            {
                                if (usages[u] == RawInputConstants.HID_USAGE_DIGITIZER_TIP_SWITCH)
                                {
                                    onSurface = true;
                                    break;
                                }
                            }
                        }
                    }

                    // Normalize X/Y to 0.0-1.0
                    float normX = 0f;
                    float normY = 0f;
                    int logicalRangeX = info.LogicalMaxX - info.LogicalMinX;
                    int logicalRangeY = info.LogicalMaxY - info.LogicalMinY;

                    if (logicalRangeX > 0)
                        normX = (float)(xValue - info.LogicalMinX) / logicalRangeX;
                    if (logicalRangeY > 0)
                        normY = (float)(yValue - info.LogicalMinY) / logicalRangeY;

                    // Clamp to [0,1]
                    normX = Math.Max(0f, Math.Min(1f, normX));
                    normY = Math.Max(0f, Math.Min(1f, normY));

                    _contactBuffer.Add(new ContactPoint(contactId, normX, normY, onSurface));
                }

                // Emit frame when we have all expected contacts
                if (_expectedContactCount > 0 &&
                    _contactBuffer.Count >= _expectedContactCount &&
                    _currentFrameDevice == deviceHandle)
                {
                    var frame = new ContactFrame(
                        _contactBuffer.ToArray(),
                        DateTime.UtcNow);
                    _contactBuffer.Clear();
                    _expectedContactCount = 0;

                    OnContactFrame?.Invoke(frame);
                }
            }
        }

        #endregion

        #region IDisposable

        public void Dispose()
        {
            foreach (var ctx in _deviceContexts.Values)
            {
                if (ctx.PreparsedHandle.IsAllocated)
                    ctx.PreparsedHandle.Free();
            }
            _deviceContexts.Clear();
            _devices.Clear();
        }

        #endregion
    }
}
