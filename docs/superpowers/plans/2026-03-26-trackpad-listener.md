# Trackpad Listener Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build a headless C# library that captures Windows Precision Touchpad input via Raw Input API, recognizes gestures, and dispatches vJoy actions to the Apricadabra core.

**Architecture:** Three layers — Input (Raw Input + HID parsing → ContactFrames), Gestures (ContactTracker + GestureRecognizer → GestureEvents), Bindings (BindingEngine → ApricadabraClient). TrackpadService orchestrates the pipeline. All components are independently testable.

**Tech Stack:** C# / .NET 8.0 (Windows), Raw Input API + HID Parser via P/Invoke, Apricadabra.Client NuGet, NUnit tests

**Spec:** `docs/superpowers/specs/2026-03-26-trackpad-listener-design.md`

---

## File Structure

### New Files (Library)
- `trackpad-plugin/Apricadabra.Trackpad.Core/Apricadabra.Trackpad.Core.csproj`
- `trackpad-plugin/Apricadabra.Trackpad.Core/Models/ContactFrame.cs`
- `trackpad-plugin/Apricadabra.Trackpad.Core/Input/NativeMethods.cs`
- `trackpad-plugin/Apricadabra.Trackpad.Core/Input/TouchpadDevice.cs`
- `trackpad-plugin/Apricadabra.Trackpad.Core/Input/HidTouchpadParser.cs`
- `trackpad-plugin/Apricadabra.Trackpad.Core/Input/RawInputCapture.cs`
- `trackpad-plugin/Apricadabra.Trackpad.Core/Gestures/GestureEvent.cs`
- `trackpad-plugin/Apricadabra.Trackpad.Core/Gestures/ContactTracker.cs`
- `trackpad-plugin/Apricadabra.Trackpad.Core/Gestures/GestureRecognizer.cs`
- `trackpad-plugin/Apricadabra.Trackpad.Core/Bindings/TrackpadSettings.cs`
- `trackpad-plugin/Apricadabra.Trackpad.Core/Bindings/BindingConfig.cs`
- `trackpad-plugin/Apricadabra.Trackpad.Core/Bindings/BindingEngine.cs`
- `trackpad-plugin/Apricadabra.Trackpad.Core/TrackpadService.cs`

### New Files (Tests)
- `trackpad-plugin/Apricadabra.Trackpad.Core.Tests/Apricadabra.Trackpad.Core.Tests.csproj`
- `trackpad-plugin/Apricadabra.Trackpad.Core.Tests/ContactTrackerTests.cs`
- `trackpad-plugin/Apricadabra.Trackpad.Core.Tests/GestureRecognizerTests.cs`
- `trackpad-plugin/Apricadabra.Trackpad.Core.Tests/BindingEngineTests.cs`

---

## Task 1: Project Scaffold and Data Models

**Files:**
- Create: `trackpad-plugin/Apricadabra.Trackpad.Core/Apricadabra.Trackpad.Core.csproj`
- Create: `trackpad-plugin/Apricadabra.Trackpad.Core/Models/ContactFrame.cs`
- Create: `trackpad-plugin/Apricadabra.Trackpad.Core/Input/TouchpadDevice.cs`
- Create: `trackpad-plugin/Apricadabra.Trackpad.Core/Gestures/GestureEvent.cs`
- Create: `trackpad-plugin/Apricadabra.Trackpad.Core.Tests/Apricadabra.Trackpad.Core.Tests.csproj`

- [ ] **Step 1: Create the library .csproj**

Create `trackpad-plugin/Apricadabra.Trackpad.Core/Apricadabra.Trackpad.Core.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0-windows</TargetFramework>
    <LangVersion>latest</LangVersion>
    <AllowUnsafeBlocks>true</AllowUnsafeBlocks>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Apricadabra.Client" Version="0.1.0" />
    <PackageReference Include="System.Text.Json" Version="8.0.5" />
  </ItemGroup>
  <ItemGroup>
    <InternalsVisibleTo Include="Apricadabra.Trackpad.Core.Tests" />
  </ItemGroup>
</Project>
```

Note: `AllowUnsafeBlocks` needed for pointer operations when parsing raw HID data. Target is `net8.0-windows` since Raw Input is Windows-only.

- [ ] **Step 2: Create the test project .csproj**

Create `trackpad-plugin/Apricadabra.Trackpad.Core.Tests/Apricadabra.Trackpad.Core.Tests.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0-windows</TargetFramework>
    <IsPackable>false</IsPackable>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="NUnit" Version="4.3.2" />
    <PackageReference Include="NUnit3TestAdapter" Version="4.6.0" />
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.12.0" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\Apricadabra.Trackpad.Core\Apricadabra.Trackpad.Core.csproj" />
  </ItemGroup>
</Project>
```

- [ ] **Step 3: Create ContactFrame.cs**

Create `trackpad-plugin/Apricadabra.Trackpad.Core/Models/ContactFrame.cs`:

```csharp
using System;

namespace Apricadabra.Trackpad.Core.Models
{
    public class ContactFrame
    {
        public ContactPoint[] Contacts { get; }
        public DateTime Timestamp { get; }

        public ContactFrame(ContactPoint[] contacts, DateTime timestamp)
        {
            Contacts = contacts;
            Timestamp = timestamp;
        }
    }

    public struct ContactPoint
    {
        public int Id;
        public float X;
        public float Y;
        public bool OnSurface;

        public ContactPoint(int id, float x, float y, bool onSurface)
        {
            Id = id;
            X = x;
            Y = y;
            OnSurface = onSurface;
        }
    }
}
```

- [ ] **Step 4: Create TouchpadDevice.cs**

Create `trackpad-plugin/Apricadabra.Trackpad.Core/Input/TouchpadDevice.cs`:

```csharp
namespace Apricadabra.Trackpad.Core.Input
{
    public class TouchpadDevice
    {
        public string DevicePath { get; }
        public string Name { get; }
        public int MaxContacts { get; }

        public TouchpadDevice(string devicePath, string name, int maxContacts)
        {
            DevicePath = devicePath;
            Name = name;
            MaxContacts = maxContacts;
        }

        public override string ToString() => $"{Name} ({MaxContacts} contacts)";
    }
}
```

- [ ] **Step 5: Create GestureEvent.cs**

Create `trackpad-plugin/Apricadabra.Trackpad.Core/Gestures/GestureEvent.cs`:

```csharp
namespace Apricadabra.Trackpad.Core.Gestures
{
    public enum GestureType { Scroll, Pinch, Rotate, Swipe, Tap }

    public enum GestureDirection
    {
        Up, Down, Left, Right,
        In, Out,
        Clockwise, CounterClockwise,
        None
    }

    public enum GesturePhase { Begin, Update, End }

    public class GestureEvent
    {
        public GestureType Type { get; }
        public int Fingers { get; }
        public GestureDirection Direction { get; }
        public GesturePhase Phase { get; }
        public float Delta { get; }

        public GestureEvent(GestureType type, int fingers, GestureDirection direction,
            GesturePhase phase, float delta = 0f)
        {
            Type = type;
            Fingers = fingers;
            Direction = direction;
            Phase = phase;
            Delta = delta;
        }
    }
}
```

- [ ] **Step 6: Commit**

```bash
git add trackpad-plugin/
git commit -m "feat(trackpad): scaffold project with data models and gesture types"
```

---

## Task 2: P/Invoke Declarations (NativeMethods.cs)

**Files:**
- Create: `trackpad-plugin/Apricadabra.Trackpad.Core/Input/NativeMethods.cs`

This is a large file containing all Win32 and HID P/Invoke signatures. It must be complete and correct — these definitions are the foundation for the entire input layer.

- [ ] **Step 1: Create NativeMethods.cs with constants**

Create `trackpad-plugin/Apricadabra.Trackpad.Core/Input/NativeMethods.cs` with all constants, structs, and function imports. This is a single large file — all P/Invoke in one place.

The file must contain:

**Constants:**
- `WM_INPUT = 0x00FF`, `WM_INPUT_DEVICE_CHANGE = 0x00FE`
- `RIDEV_INPUTSINK = 0x00000100`, `RIDEV_DEVNOTIFY = 0x00002000`
- `RIM_TYPEHID = 2`, `RID_INPUT = 0x10000003`
- `RIDI_PREPARSEDDATA = 0x20000005`, `RIDI_DEVICENAME = 0x20000007`, `RIDI_DEVICEINFO = 0x2000000b`
- `HID_USAGE_PAGE_DIGITIZER = 0x0D`, `HID_USAGE_DIGITIZER_TOUCH_PAD = 0x05`
- `HID_USAGE_DIGITIZER_TIP_SWITCH = 0x42`, `HID_USAGE_DIGITIZER_CONTACT_ID = 0x51`, `HID_USAGE_DIGITIZER_CONTACT_COUNT = 0x54`
- `HID_USAGE_GENERIC_X = 0x30`, `HID_USAGE_GENERIC_Y = 0x31`
- `HidP_Input = 0`, `HIDP_STATUS_SUCCESS = 0x00110000`
- `HWND_MESSAGE = (IntPtr)(-3)`

**Structs (all `[StructLayout]`):**
- `RAWINPUTDEVICE` (UsagePage, Usage, Flags, WindowHandle)
- `RAWINPUTDEVICELIST` (Device, Type)
- `RAWINPUTHEADER` (Type, Size, Device, WParam)
- `RID_DEVICE_INFO` (explicit layout union with mouse/keyboard/hid)
- `RID_DEVICE_INFO_HID` (VendorId, ProductId, VersionNumber, UsagePage, Usage)
- `RID_DEVICE_INFO_MOUSE`, `RID_DEVICE_INFO_KEYBOARD` (for union size padding)
- `HIDP_CAPS` (Usage, UsagePage, InputReportByteLength, Reserved[17], NumberInputButtonCaps, NumberInputValueCaps, etc.)
- `HIDP_VALUE_CAPS` (explicit layout — UsagePage, ReportID, LinkCollection, IsRange, PhysicalMin, PhysicalMax, Range/NotRange union at offset 56)
- `HIDP_BUTTON_CAPS` (explicit layout — similar to value caps, union at offset 56)
- `HIDP_RANGE` (UsageMin/Max, StringMin/Max, DesignatorMin/Max, DataIndexMin/Max)
- `HIDP_NOT_RANGE` (Usage, StringIndex, DesignatorIndex, DataIndex — with reserved padding)
- `WNDCLASSEX`, `MSG`, `POINT`

**Functions (all `[DllImport]`):**

From `user32.dll`:
- `RegisterRawInputDevices(RAWINPUTDEVICE[], uint, uint) → bool`
- `GetRawInputData(IntPtr, uint, IntPtr, ref uint, uint) → uint` (for size query)
- `GetRawInputData(IntPtr, uint, byte[], ref uint, uint) → uint` (for data read)
- `GetRawInputDeviceList(RAWINPUTDEVICELIST[], ref uint, uint) → uint`
- `GetRawInputDeviceInfo` (4 overloads: size query, device name, device info, preparsed data)
- `RegisterClassEx(ref WNDCLASSEX) → ushort`
- `CreateWindowEx(...) → IntPtr`
- `DefWindowProc(IntPtr, uint, IntPtr, IntPtr) → IntPtr`
- `GetMessage(out MSG, IntPtr, uint, uint) → int`
- `TranslateMessage(ref MSG) → bool`
- `DispatchMessage(ref MSG) → IntPtr`
- `PostQuitMessage(int)`
- `DestroyWindow(IntPtr) → bool`

From `hid.dll`:
- `HidP_GetCaps(IntPtr, out HIDP_CAPS) → uint`
- `HidP_GetValueCaps(ushort, HIDP_VALUE_CAPS[], ref ushort, IntPtr) → uint`
- `HidP_GetButtonCaps(ushort, HIDP_BUTTON_CAPS[], ref ushort, IntPtr) → uint`
- `HidP_GetUsageValue(ushort, ushort, ushort, ushort, out int, IntPtr, byte[], uint) → uint`
- `HidP_GetUsages(ushort, ushort, ushort, ushort[], ref uint, IntPtr, byte[], uint) → uint`

From `kernel32.dll`:
- `GetModuleHandle(string) → IntPtr`

**WndProc delegate:**
- `delegate IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)`

Reference the P/Invoke research for exact struct layouts and field offsets. Pay special attention to:
- `HIDP_VALUE_CAPS` and `HIDP_BUTTON_CAPS` use `[StructLayout(LayoutKind.Explicit)]` with specific byte offsets
- `RID_DEVICE_INFO` is a union struct with `[FieldOffset(8)]` for all three variants
- `RAWINPUTHEADER` size varies by platform (IntPtr fields)

- [ ] **Step 2: Commit**

```bash
git add trackpad-plugin/Apricadabra.Trackpad.Core/Input/NativeMethods.cs
git commit -m "feat(trackpad): add P/Invoke declarations for Raw Input and HID Parser APIs"
```

---

## Task 3: HID Touchpad Parser

**Files:**
- Create: `trackpad-plugin/Apricadabra.Trackpad.Core/Input/HidTouchpadParser.cs`

- [ ] **Step 1: Implement HidTouchpadParser**

Create `trackpad-plugin/Apricadabra.Trackpad.Core/Input/HidTouchpadParser.cs`:

The parser has two responsibilities:
1. **Device discovery** (`EnumerateDevices()`) — called at startup and on hot-plug
2. **Report parsing** (`ParseReport(IntPtr deviceHandle, byte[] rawData, int rawDataLength)`) — called per `WM_INPUT`

**Device discovery implementation:**
```
EnumerateDevices():
  1. Call GetRawInputDeviceList(null, ref count, sizeof) to get device count
  2. Allocate RAWINPUTDEVICELIST[count], call again to fill
  3. For each device where Type == RIM_TYPEHID:
     a. GetRawInputDeviceInfo with RIDI_DEVICEINFO → check usUsagePage == 0x0D and usUsage == 0x05
     b. GetRawInputDeviceInfo with RIDI_DEVICENAME → store device path
     c. GetRawInputDeviceInfo with RIDI_PREPARSEDDATA → store preparsed data buffer
     d. HidP_GetCaps → get NumberInputValueCaps, NumberInputButtonCaps, InputReportByteLength
     e. HidP_GetValueCaps → for each value cap, store UsagePage, Usage, LinkCollection, PhysicalMin, PhysicalMax
     f. HidP_GetButtonCaps → for each button cap, store UsagePage, LinkCollection
     g. Build TouchpadDevice with name, path, max contacts (from contact count max capability)
  4. Store per-device parsing context: preparsed data IntPtr, value caps map, button caps map, X/Y physical ranges
```

**Report parsing implementation:**
```
ParseReport(deviceHandle, rawHidData, dataLength):
  1. Look up device context by deviceHandle
  2. Extract dwSizeHid and dwCount from RAWHID header (first 8 bytes)
  3. For each HID report (dwCount reports, each dwSizeHid bytes):
     a. HidP_GetUsageValue for CONTACT_COUNT (page 0x0D, usage 0x54) → sets expectedContacts
     b. For each link collection (finger slot):
        - HidP_GetUsageValue for CONTACT_ID (page 0x0D, usage 0x51, linkCollection)
        - HidP_GetUsageValue for X (page 0x01, usage 0x30, linkCollection)
        - HidP_GetUsageValue for Y (page 0x01, usage 0x31, linkCollection)
        - HidP_GetUsages for TIP_SWITCH (page 0x0D, linkCollection) → onSurface
        - Normalize X/Y to 0.0-1.0 using stored PhysicalMin/PhysicalMax
        - Buffer as ContactPoint
     c. When all expected contacts received, emit complete ContactFrame
```

Key implementation details:
- Store a `Dictionary<IntPtr, DeviceContext>` mapping device handles to their preparsed data and capabilities
- The contact count comes from a top-level value capability (linkCollection 0)
- Individual contact data comes from per-finger link collections (linkCollection 1, 2, 3...)
- Some touchpads report each contact in a separate HID report within one `WM_INPUT`; others pack all contacts into one report. The `dwCount` field in `RAWHID` tells you how many reports are in the message.
- Buffer contacts and only emit a `ContactFrame` when all expected contacts (per `CONTACT_COUNT`) have been received

Expose:
- `List<TouchpadDevice> EnumerateDevices()` — returns discovered devices
- `event Action<ContactFrame> OnContactFrame` — fires when a complete set is ready
- `void ProcessRawInput(IntPtr deviceHandle, byte[] rawHidData, int dataLength)` — called by RawInputCapture

- [ ] **Step 2: Commit**

```bash
git add trackpad-plugin/Apricadabra.Trackpad.Core/Input/HidTouchpadParser.cs
git commit -m "feat(trackpad): add HID touchpad parser with device discovery and report extraction"
```

---

## Task 4: Raw Input Capture

**Files:**
- Create: `trackpad-plugin/Apricadabra.Trackpad.Core/Input/RawInputCapture.cs`

- [ ] **Step 1: Implement RawInputCapture**

Create `trackpad-plugin/Apricadabra.Trackpad.Core/Input/RawInputCapture.cs`:

This class:
1. Creates a message-only window on a dedicated thread
2. Registers for Raw Input with `RIDEV_INPUTSINK | RIDEV_DEVNOTIFY`
3. Processes `WM_INPUT` messages → passes to `HidTouchpadParser`
4. Processes `WM_INPUT_DEVICE_CHANGE` → re-enumerates devices
5. Filters by selected device

**Implementation outline:**

```csharp
public class RawInputCapture : IDisposable
{
    private readonly HidTouchpadParser _parser;
    private IntPtr _hwnd;
    private Thread _messageThread;
    private WndProc _wndProcDelegate; // prevent GC
    private string _selectedDevicePath;

    public event Action<ContactFrame> OnContactFrame;
    public event Action OnDevicesChanged;
    public IReadOnlyList<TouchpadDevice> AvailableDevices => _parser.Devices;

    public string SelectedDevicePath
    {
        get => _selectedDevicePath;
        set => _selectedDevicePath = value;
    }

    public RawInputCapture()
    {
        _parser = new HidTouchpadParser();
        _parser.OnContactFrame += frame =>
        {
            OnContactFrame?.Invoke(frame);
        };
    }

    public void Start()
    {
        _messageThread = new Thread(MessageLoop) { IsBackground = true, Name = "RawInput" };
        _messageThread.Start();
    }

    private void MessageLoop()
    {
        // Register window class
        _wndProcDelegate = WndProcHandler;
        var wc = new WNDCLASSEX { ... };
        RegisterClassEx(ref wc);

        // Create message-only window
        _hwnd = CreateWindowEx(0, className, "", 0, 0, 0, 0, 0, HWND_MESSAGE, ...);

        // Register for raw input
        var rid = new RAWINPUTDEVICE
        {
            UsagePage = HID_USAGE_PAGE_DIGITIZER,
            Usage = HID_USAGE_DIGITIZER_TOUCH_PAD,
            Flags = RIDEV_INPUTSINK | RIDEV_DEVNOTIFY,
            WindowHandle = _hwnd
        };
        RegisterRawInputDevices(new[] { rid }, 1, (uint)Marshal.SizeOf<RAWINPUTDEVICE>());

        // Enumerate initial devices
        _parser.EnumerateDevices();

        // Message pump
        while (GetMessage(out var msg, IntPtr.Zero, 0, 0) > 0)
        {
            TranslateMessage(ref msg);
            DispatchMessage(ref msg);
        }
    }

    private IntPtr WndProcHandler(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        switch (msg)
        {
            case WM_INPUT:
                HandleRawInput(lParam);
                return IntPtr.Zero;
            case WM_INPUT_DEVICE_CHANGE:
                _parser.EnumerateDevices();
                OnDevicesChanged?.Invoke();
                return IntPtr.Zero;
        }
        return DefWindowProc(hwnd, msg, wParam, lParam);
    }

    private void HandleRawInput(IntPtr lParam)
    {
        // 1. GetRawInputData with RID_INPUT to get size
        // 2. Allocate buffer, GetRawInputData to fill
        // 3. Read RAWINPUTHEADER from buffer
        // 4. If header.Type != RIM_TYPEHID, return
        // 5. Check device filter (if _selectedDevicePath set, compare against device handle)
        // 6. Extract RAWHID data (after header) and pass to _parser.ProcessRawInput()
    }

    public void Stop()
    {
        if (_hwnd != IntPtr.Zero)
            PostQuitMessage(0); // ends GetMessage loop
    }

    public void Dispose() => Stop();
}
```

The device filter checks `_selectedDevicePath` against the device handle's path (looked up from the parser's device context). If null, all devices pass through.

- [ ] **Step 2: Commit**

```bash
git add trackpad-plugin/Apricadabra.Trackpad.Core/Input/RawInputCapture.cs
git commit -m "feat(trackpad): add RawInputCapture with message-only window and device filtering"
```

---

## Task 5: Contact Tracker

**Files:**
- Create: `trackpad-plugin/Apricadabra.Trackpad.Core/Gestures/ContactTracker.cs`
- Create: `trackpad-plugin/Apricadabra.Trackpad.Core.Tests/ContactTrackerTests.cs`

- [ ] **Step 1: Write ContactTracker tests**

Create `trackpad-plugin/Apricadabra.Trackpad.Core.Tests/ContactTrackerTests.cs`:

```csharp
using NUnit.Framework;
using Apricadabra.Trackpad.Core.Gestures;
using Apricadabra.Trackpad.Core.Models;
using System;

namespace Apricadabra.Trackpad.Core.Tests;

[TestFixture]
public class ContactTrackerTests
{
    private ContactTracker _tracker;

    [SetUp]
    public void Setup() => _tracker = new ContactTracker();

    [Test]
    public void Update_SingleContact_TracksDelta()
    {
        var t = DateTime.UtcNow;
        _tracker.Update(new ContactFrame(new[] {
            new ContactPoint(1, 0.5f, 0.5f, true)
        }, t));

        _tracker.Update(new ContactFrame(new[] {
            new ContactPoint(1, 0.6f, 0.5f, true)
        }, t.AddMilliseconds(16)));

        var state = _tracker.CurrentState;
        Assert.That(state.FingerCount, Is.EqualTo(1));
        Assert.That(state.CenterDeltaX, Is.EqualTo(0.1f).Within(0.01f));
        Assert.That(state.CenterDeltaY, Is.EqualTo(0f).Within(0.01f));
    }

    [Test]
    public void Update_TwoContacts_ComputesSpread()
    {
        var t = DateTime.UtcNow;
        // Two fingers 0.2 apart
        _tracker.Update(new ContactFrame(new[] {
            new ContactPoint(1, 0.4f, 0.5f, true),
            new ContactPoint(2, 0.6f, 0.5f, true)
        }, t));

        // Spread to 0.4 apart
        _tracker.Update(new ContactFrame(new[] {
            new ContactPoint(1, 0.3f, 0.5f, true),
            new ContactPoint(2, 0.7f, 0.5f, true)
        }, t.AddMilliseconds(16)));

        var state = _tracker.CurrentState;
        Assert.That(state.FingerCount, Is.EqualTo(2));
        Assert.That(state.SpreadDelta, Is.GreaterThan(0)); // fingers moved apart
    }

    [Test]
    public void Update_TwoContacts_ComputesRotation()
    {
        var t = DateTime.UtcNow;
        // Two fingers horizontal
        _tracker.Update(new ContactFrame(new[] {
            new ContactPoint(1, 0.4f, 0.5f, true),
            new ContactPoint(2, 0.6f, 0.5f, true)
        }, t));

        // Rotate: finger 1 goes up, finger 2 goes down
        _tracker.Update(new ContactFrame(new[] {
            new ContactPoint(1, 0.4f, 0.4f, true),
            new ContactPoint(2, 0.6f, 0.6f, true)
        }, t.AddMilliseconds(16)));

        var state = _tracker.CurrentState;
        Assert.That(state.RotationDelta, Is.Not.EqualTo(0).Within(0.001f));
    }

    [Test]
    public void Update_ContactLifted_DetectsFingerUp()
    {
        var t = DateTime.UtcNow;
        _tracker.Update(new ContactFrame(new[] {
            new ContactPoint(1, 0.5f, 0.5f, true)
        }, t));

        _tracker.Update(new ContactFrame(new[] {
            new ContactPoint(1, 0.5f, 0.5f, false)
        }, t.AddMilliseconds(100)));

        var state = _tracker.CurrentState;
        Assert.That(state.FingerCount, Is.EqualTo(0));
        Assert.That(state.AllFingersLifted, Is.True);
    }

    [Test]
    public void Update_NoContacts_ReturnsEmptyState()
    {
        var t = DateTime.UtcNow;
        _tracker.Update(new ContactFrame(Array.Empty<ContactPoint>(), t));

        var state = _tracker.CurrentState;
        Assert.That(state.FingerCount, Is.EqualTo(0));
    }

    [Test]
    public void CumulativeDistance_TracksTotal()
    {
        var t = DateTime.UtcNow;
        _tracker.Update(new ContactFrame(new[] {
            new ContactPoint(1, 0.5f, 0.5f, true)
        }, t));
        _tracker.Update(new ContactFrame(new[] {
            new ContactPoint(1, 0.6f, 0.5f, true)
        }, t.AddMilliseconds(16)));
        _tracker.Update(new ContactFrame(new[] {
            new ContactPoint(1, 0.7f, 0.5f, true)
        }, t.AddMilliseconds(32)));

        Assert.That(_tracker.CurrentState.CumulativeDistance, Is.EqualTo(0.2f).Within(0.01f));
    }
}
```

- [ ] **Step 2: Implement ContactTracker**

Create `trackpad-plugin/Apricadabra.Trackpad.Core/Gestures/ContactTracker.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using Apricadabra.Trackpad.Core.Models;

namespace Apricadabra.Trackpad.Core.Gestures
{
    public class TrackerState
    {
        public int FingerCount { get; set; }
        public float CenterDeltaX { get; set; }
        public float CenterDeltaY { get; set; }
        public float SpreadDelta { get; set; }
        public float RotationDelta { get; set; }
        public float CumulativeDistance { get; set; }
        public float Velocity { get; set; }
        public bool AllFingersLifted { get; set; }
        public DateTime GestureStartTime { get; set; }
        public DateTime Timestamp { get; set; }
    }

    public class ContactTracker
    {
        private Dictionary<int, ContactPoint> _previousContacts = new();
        private float _previousSpread;
        private float _previousAngle;
        private float _cumulativeDistance;
        private DateTime _gestureStartTime;
        private bool _hasGesture;

        public TrackerState CurrentState { get; private set; } = new();

        public void Update(ContactFrame frame)
        {
            var onSurface = frame.Contacts.Where(c => c.OnSurface).ToArray();
            var state = new TrackerState
            {
                FingerCount = onSurface.Length,
                Timestamp = frame.Timestamp
            };

            if (onSurface.Length == 0)
            {
                state.AllFingersLifted = _hasGesture;
                state.CumulativeDistance = _cumulativeDistance;
                state.GestureStartTime = _gestureStartTime;
                CurrentState = state;
                Reset();
                return;
            }

            if (!_hasGesture)
            {
                _gestureStartTime = frame.Timestamp;
                _hasGesture = true;
            }

            // Center of mass
            float cx = onSurface.Average(c => c.X);
            float cy = onSurface.Average(c => c.Y);

            // Center delta (compared to previous frame)
            if (_previousContacts.Count > 0)
            {
                var prevOnSurface = _previousContacts.Values.Where(c => c.OnSurface).ToArray();
                if (prevOnSurface.Length > 0)
                {
                    float pcx = prevOnSurface.Average(c => c.X);
                    float pcy = prevOnSurface.Average(c => c.Y);
                    state.CenterDeltaX = cx - pcx;
                    state.CenterDeltaY = cy - pcy;
                }
            }

            // Cumulative distance
            float frameDist = MathF.Sqrt(state.CenterDeltaX * state.CenterDeltaX + state.CenterDeltaY * state.CenterDeltaY);
            _cumulativeDistance += frameDist;
            state.CumulativeDistance = _cumulativeDistance;
            state.GestureStartTime = _gestureStartTime;

            // Velocity
            var elapsed = (frame.Timestamp - _gestureStartTime).TotalSeconds;
            state.Velocity = elapsed > 0 ? _cumulativeDistance / (float)elapsed : 0;

            // Spread (average distance between contacts) — for pinch
            if (onSurface.Length >= 2)
            {
                float spread = ComputeSpread(onSurface);
                state.SpreadDelta = spread - _previousSpread;
                _previousSpread = spread;

                // Rotation angle between first two contacts
                float angle = MathF.Atan2(
                    onSurface[1].Y - onSurface[0].Y,
                    onSurface[1].X - onSurface[0].X);
                state.RotationDelta = AngleDelta(_previousAngle, angle);
                _previousAngle = angle;
            }
            else
            {
                _previousSpread = 0;
                _previousAngle = 0;
            }

            // Store current contacts for next frame
            _previousContacts.Clear();
            foreach (var c in frame.Contacts)
                _previousContacts[c.Id] = c;

            CurrentState = state;
        }

        public void Reset()
        {
            _previousContacts.Clear();
            _previousSpread = 0;
            _previousAngle = 0;
            _cumulativeDistance = 0;
            _hasGesture = false;
        }

        private static float ComputeSpread(ContactPoint[] contacts)
        {
            if (contacts.Length < 2) return 0;
            float totalDist = 0;
            int pairs = 0;
            for (int i = 0; i < contacts.Length; i++)
            {
                for (int j = i + 1; j < contacts.Length; j++)
                {
                    float dx = contacts[i].X - contacts[j].X;
                    float dy = contacts[i].Y - contacts[j].Y;
                    totalDist += MathF.Sqrt(dx * dx + dy * dy);
                    pairs++;
                }
            }
            return pairs > 0 ? totalDist / pairs : 0;
        }

        private static float AngleDelta(float prev, float current)
        {
            float delta = current - prev;
            if (delta > MathF.PI) delta -= 2 * MathF.PI;
            if (delta < -MathF.PI) delta += 2 * MathF.PI;
            return delta;
        }
    }
}
```

- [ ] **Step 3: Commit**

```bash
git add trackpad-plugin/
git commit -m "feat(trackpad): add ContactTracker with delta, spread, and rotation computation"
```

---

## Task 6: Gesture Recognizer

**Files:**
- Create: `trackpad-plugin/Apricadabra.Trackpad.Core/Gestures/GestureRecognizer.cs`
- Create: `trackpad-plugin/Apricadabra.Trackpad.Core.Tests/GestureRecognizerTests.cs`

- [ ] **Step 1: Write GestureRecognizer tests**

Create `trackpad-plugin/Apricadabra.Trackpad.Core.Tests/GestureRecognizerTests.cs`:

```csharp
using NUnit.Framework;
using Apricadabra.Trackpad.Core.Gestures;
using Apricadabra.Trackpad.Core.Bindings;
using Apricadabra.Trackpad.Core.Models;
using System;
using System.Collections.Generic;

namespace Apricadabra.Trackpad.Core.Tests;

[TestFixture]
public class GestureRecognizerTests
{
    private GestureRecognizer _recognizer;
    private TrackpadSettings _settings;
    private List<GestureEvent> _events;

    [SetUp]
    public void Setup()
    {
        _settings = new TrackpadSettings();
        _recognizer = new GestureRecognizer(_settings);
        _events = new List<GestureEvent>();
        _recognizer.OnGestureEvent += e => _events.Add(e);
    }

    private ContactFrame Frame(DateTime t, params ContactPoint[] contacts)
        => new ContactFrame(contacts, t);

    [Test]
    public void TwoFingerLinearMotion_DefaultSettings_IsScroll()
    {
        var t = DateTime.UtcNow;
        // Two fingers moving up
        _recognizer.ProcessFrame(Frame(t,
            new ContactPoint(1, 0.4f, 0.5f, true),
            new ContactPoint(2, 0.6f, 0.5f, true)));
        _recognizer.ProcessFrame(Frame(t.AddMilliseconds(16),
            new ContactPoint(1, 0.4f, 0.4f, true),
            new ContactPoint(2, 0.6f, 0.4f, true)));
        _recognizer.ProcessFrame(Frame(t.AddMilliseconds(32),
            new ContactPoint(1, 0.4f, 0.3f, true),
            new ContactPoint(2, 0.6f, 0.3f, true)));

        Assert.That(_events, Has.Some.Matches<GestureEvent>(e =>
            e.Type == GestureType.Scroll && e.Direction == GestureDirection.Up));
    }

    [Test]
    public void ThreeFingerLinearMotion_DefaultSettings_IsSwipe()
    {
        var t = DateTime.UtcNow;
        _recognizer.ProcessFrame(Frame(t,
            new ContactPoint(1, 0.3f, 0.5f, true),
            new ContactPoint(2, 0.5f, 0.5f, true),
            new ContactPoint(3, 0.7f, 0.5f, true)));
        // Move left
        for (int i = 1; i <= 10; i++)
        {
            float offset = i * 0.03f;
            _recognizer.ProcessFrame(Frame(t.AddMilliseconds(16 * i),
                new ContactPoint(1, 0.3f - offset, 0.5f, true),
                new ContactPoint(2, 0.5f - offset, 0.5f, true),
                new ContactPoint(3, 0.7f - offset, 0.5f, true)));
        }
        // Lift
        _recognizer.ProcessFrame(Frame(t.AddMilliseconds(200),
            new ContactPoint(1, 0f, 0.5f, false),
            new ContactPoint(2, 0.2f, 0.5f, false),
            new ContactPoint(3, 0.4f, 0.5f, false)));

        Assert.That(_events, Has.Some.Matches<GestureEvent>(e =>
            e.Type == GestureType.Swipe &&
            e.Direction == GestureDirection.Left &&
            e.Fingers == 3 &&
            e.Phase == GesturePhase.End));
    }

    [Test]
    public void TwoFingerSpread_IsPinch()
    {
        var t = DateTime.UtcNow;
        _recognizer.ProcessFrame(Frame(t,
            new ContactPoint(1, 0.45f, 0.5f, true),
            new ContactPoint(2, 0.55f, 0.5f, true)));
        // Spread apart significantly
        _recognizer.ProcessFrame(Frame(t.AddMilliseconds(16),
            new ContactPoint(1, 0.3f, 0.5f, true),
            new ContactPoint(2, 0.7f, 0.5f, true)));
        _recognizer.ProcessFrame(Frame(t.AddMilliseconds(32),
            new ContactPoint(1, 0.2f, 0.5f, true),
            new ContactPoint(2, 0.8f, 0.5f, true)));

        Assert.That(_events, Has.Some.Matches<GestureEvent>(e =>
            e.Type == GestureType.Pinch && e.Direction == GestureDirection.Out));
    }

    [Test]
    public void TwoFingerQuickTapAndRelease_IsTap()
    {
        var t = DateTime.UtcNow;
        _recognizer.ProcessFrame(Frame(t,
            new ContactPoint(1, 0.4f, 0.5f, true),
            new ContactPoint(2, 0.6f, 0.5f, true)));
        // Lift within tap duration, minimal movement
        _recognizer.ProcessFrame(Frame(t.AddMilliseconds(100),
            new ContactPoint(1, 0.4f, 0.5f, false),
            new ContactPoint(2, 0.6f, 0.5f, false)));

        Assert.That(_events, Has.Some.Matches<GestureEvent>(e =>
            e.Type == GestureType.Tap && e.Fingers == 2));
    }

    [Test]
    public void GestureCommitment_NoReclassification()
    {
        var t = DateTime.UtcNow;
        // Start as scroll
        _recognizer.ProcessFrame(Frame(t,
            new ContactPoint(1, 0.4f, 0.5f, true),
            new ContactPoint(2, 0.6f, 0.5f, true)));
        _recognizer.ProcessFrame(Frame(t.AddMilliseconds(16),
            new ContactPoint(1, 0.4f, 0.4f, true),
            new ContactPoint(2, 0.6f, 0.4f, true)));

        // Even if fingers now spread, should stay as scroll (committed)
        _recognizer.ProcessFrame(Frame(t.AddMilliseconds(32),
            new ContactPoint(1, 0.3f, 0.3f, true),
            new ContactPoint(2, 0.7f, 0.3f, true)));

        var scrollEvents = _events.FindAll(e => e.Type == GestureType.Scroll);
        var pinchEvents = _events.FindAll(e => e.Type == GestureType.Pinch);
        Assert.That(scrollEvents.Count, Is.GreaterThan(0));
        Assert.That(pinchEvents.Count, Is.EqualTo(0));
    }

    [Test]
    public void CustomScrollFingerCount_ThreeFingerScroll()
    {
        _settings.ScrollFingerCount = 3;
        var t = DateTime.UtcNow;
        _recognizer.ProcessFrame(Frame(t,
            new ContactPoint(1, 0.3f, 0.5f, true),
            new ContactPoint(2, 0.5f, 0.5f, true),
            new ContactPoint(3, 0.7f, 0.5f, true)));
        _recognizer.ProcessFrame(Frame(t.AddMilliseconds(16),
            new ContactPoint(1, 0.3f, 0.4f, true),
            new ContactPoint(2, 0.5f, 0.4f, true),
            new ContactPoint(3, 0.7f, 0.4f, true)));

        Assert.That(_events, Has.Some.Matches<GestureEvent>(e =>
            e.Type == GestureType.Scroll && e.Fingers == 3));
    }
}
```

- [ ] **Step 2: Implement GestureRecognizer**

Create `trackpad-plugin/Apricadabra.Trackpad.Core/Gestures/GestureRecognizer.cs`:

```csharp
using System;
using Apricadabra.Trackpad.Core.Bindings;
using Apricadabra.Trackpad.Core.Models;

namespace Apricadabra.Trackpad.Core.Gestures
{
    public class GestureRecognizer
    {
        private readonly TrackpadSettings _settings;
        private readonly ContactTracker _tracker = new();
        private GestureType? _committedGesture;
        private int _committedFingers;
        private bool _beginFired;

        public event Action<GestureEvent> OnGestureEvent;

        public GestureRecognizer(TrackpadSettings settings)
        {
            _settings = settings;
        }

        public void ProcessFrame(ContactFrame frame)
        {
            _tracker.Update(frame);
            var state = _tracker.CurrentState;

            if (state.AllFingersLifted)
            {
                HandleFingersLifted(state);
                ResetGesture();
                return;
            }

            if (state.FingerCount < 2) return;

            if (_committedGesture == null)
            {
                TryClassify(state);
            }
            else
            {
                EmitContinuousUpdate(state);
            }
        }

        private void TryClassify(TrackerState state)
        {
            float linearMag = MathF.Sqrt(state.CenterDeltaX * state.CenterDeltaX + state.CenterDeltaY * state.CenterDeltaY);
            float spreadMag = MathF.Abs(state.SpreadDelta);
            float rotationMag = MathF.Abs(state.RotationDelta);

            // Need minimum movement to classify
            float threshold = 0.005f;
            if (linearMag < threshold && spreadMag < threshold && rotationMag < 0.02f)
                return;

            // Priority: pinch > rotate > scroll > swipe
            if (state.FingerCount == 2 && spreadMag > linearMag && spreadMag > rotationMag)
            {
                CommitGesture(GestureType.Pinch, 2);
            }
            else if (state.FingerCount >= 2 && rotationMag > linearMag * 2 && rotationMag > spreadMag)
            {
                CommitGesture(GestureType.Rotate, state.FingerCount);
            }
            else if (state.FingerCount == _settings.ScrollFingerCount)
            {
                CommitGesture(GestureType.Scroll, state.FingerCount);
            }
            else if (state.FingerCount > _settings.ScrollFingerCount)
            {
                // Don't commit swipe yet — wait for lift
                _committedGesture = GestureType.Swipe;
                _committedFingers = state.FingerCount;
            }
        }

        private void CommitGesture(GestureType type, int fingers)
        {
            _committedGesture = type;
            _committedFingers = fingers;
        }

        private void EmitContinuousUpdate(TrackerState state)
        {
            if (_committedGesture == GestureType.Swipe) return; // swipe fires on lift only

            var direction = _committedGesture switch
            {
                GestureType.Scroll => GetLinearDirection(state.CenterDeltaX, state.CenterDeltaY),
                GestureType.Pinch => state.SpreadDelta > 0 ? GestureDirection.Out : GestureDirection.In,
                GestureType.Rotate => state.RotationDelta > 0 ? GestureDirection.Clockwise : GestureDirection.CounterClockwise,
                _ => GestureDirection.None
            };

            float delta = _committedGesture switch
            {
                GestureType.Scroll => MathF.Sqrt(state.CenterDeltaX * state.CenterDeltaX + state.CenterDeltaY * state.CenterDeltaY),
                GestureType.Pinch => state.SpreadDelta,
                GestureType.Rotate => state.RotationDelta,
                _ => 0
            };

            if (direction == GestureDirection.None || MathF.Abs(delta) < 0.001f) return;

            var phase = _beginFired ? GesturePhase.Update : GesturePhase.Begin;
            _beginFired = true;

            OnGestureEvent?.Invoke(new GestureEvent(_committedGesture.Value, _committedFingers, direction, phase, delta));
        }

        private void HandleFingersLifted(TrackerState state)
        {
            if (_committedGesture == GestureType.Swipe)
            {
                // Check swipe thresholds
                if (state.CumulativeDistance >= _settings.SwipeDistanceThreshold &&
                    state.Velocity >= _settings.SwipeSpeedThreshold)
                {
                    var dir = GetCardinalDirection(state);
                    OnGestureEvent?.Invoke(new GestureEvent(GestureType.Swipe, _committedFingers, dir, GesturePhase.End));
                }
            }
            else if (_committedGesture != null && _beginFired)
            {
                // End continuous gesture
                OnGestureEvent?.Invoke(new GestureEvent(_committedGesture.Value, _committedFingers, GestureDirection.None, GesturePhase.End));
            }
            else if (_committedGesture == null)
            {
                // Check for tap: short duration + minimal movement
                var duration = (state.Timestamp - state.GestureStartTime).TotalMilliseconds;
                if (duration > 0 && duration <= _settings.TapMaxDuration &&
                    state.CumulativeDistance <= _settings.TapMaxMovement &&
                    _committedFingers == 0)
                {
                    // Infer finger count from the frames we saw
                    int fingers = state.FingerCount > 0 ? state.FingerCount : _tracker.CurrentState.FingerCount;
                    // Fire begin+end for tap
                    OnGestureEvent?.Invoke(new GestureEvent(GestureType.Tap, fingers, GestureDirection.None, GesturePhase.Begin));
                    OnGestureEvent?.Invoke(new GestureEvent(GestureType.Tap, fingers, GestureDirection.None, GesturePhase.End));
                }
            }
        }

        private void ResetGesture()
        {
            _committedGesture = null;
            _committedFingers = 0;
            _beginFired = false;
        }

        private static GestureDirection GetLinearDirection(float dx, float dy)
        {
            if (MathF.Abs(dy) > MathF.Abs(dx))
                return dy < 0 ? GestureDirection.Up : GestureDirection.Down;
            return dx > 0 ? GestureDirection.Right : GestureDirection.Left;
        }

        private static GestureDirection GetCardinalDirection(TrackerState state)
        {
            // Use cumulative center delta direction
            return GetLinearDirection(state.CenterDeltaX, state.CenterDeltaY);
        }
    }
}
```

Note: The tap detection needs refinement — the `_committedFingers` is 0 when no gesture was committed. We need to track the peak finger count separately. Add a `_peakFingerCount` field that updates on each frame and use it for tap finger count.

- [ ] **Step 3: Commit**

```bash
git add trackpad-plugin/
git commit -m "feat(trackpad): add GestureRecognizer with scroll, pinch, rotate, swipe, and tap classification"
```

---

## Task 7: Settings and Binding Config

**Files:**
- Create: `trackpad-plugin/Apricadabra.Trackpad.Core/Bindings/TrackpadSettings.cs`
- Create: `trackpad-plugin/Apricadabra.Trackpad.Core/Bindings/BindingConfig.cs`

- [ ] **Step 1: Implement TrackpadSettings**

Create `trackpad-plugin/Apricadabra.Trackpad.Core/Bindings/TrackpadSettings.cs`:

```csharp
using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Apricadabra.Trackpad.Core.Bindings
{
    public class TrackpadSettings
    {
        [JsonPropertyName("selectedDevicePath")]
        public string SelectedDevicePath { get; set; }

        [JsonPropertyName("scrollFingerCount")]
        public int ScrollFingerCount { get; set; } = 2;

        [JsonPropertyName("scrollSensitivity")]
        public float ScrollSensitivity { get; set; } = 1.0f;

        [JsonPropertyName("swipeDistanceThreshold")]
        public float SwipeDistanceThreshold { get; set; } = 0.15f;

        [JsonPropertyName("swipeSpeedThreshold")]
        public float SwipeSpeedThreshold { get; set; } = 0.3f;

        [JsonPropertyName("pinchSensitivity")]
        public float PinchSensitivity { get; set; } = 1.0f;

        [JsonPropertyName("rotateSensitivity")]
        public float RotateSensitivity { get; set; } = 1.0f;

        [JsonPropertyName("tapMaxDuration")]
        public int TapMaxDuration { get; set; } = 300;

        [JsonPropertyName("tapMaxMovement")]
        public float TapMaxMovement { get; set; } = 0.03f;

        private static string SettingsPath =>
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Apricadabra", "trackpad", "settings.json");

        public static TrackpadSettings Load()
        {
            try
            {
                if (File.Exists(SettingsPath))
                    return JsonSerializer.Deserialize<TrackpadSettings>(File.ReadAllText(SettingsPath));
            }
            catch { }
            return new TrackpadSettings();
        }

        public void Save()
        {
            var dir = Path.GetDirectoryName(SettingsPath);
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(SettingsPath, json);
        }
    }
}
```

- [ ] **Step 2: Implement BindingConfig**

Create `trackpad-plugin/Apricadabra.Trackpad.Core/Bindings/BindingConfig.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Apricadabra.Trackpad.Core.Bindings
{
    public class BindingEntry
    {
        [JsonPropertyName("id")]
        public string Id { get; set; }

        [JsonPropertyName("gesture")]
        public JsonObject Gesture { get; set; }

        [JsonPropertyName("action")]
        public JsonObject Action { get; set; }

        // Parsed gesture fields for matching
        [JsonIgnore] public string GestureType => Gesture?["type"]?.GetValue<string>();
        [JsonIgnore] public int GestureFingers => Gesture?["fingers"]?.GetValue<int>() ?? 0;
        [JsonIgnore] public string GestureDirection => Gesture?["direction"]?.GetValue<string>() ?? "none";

        // Parsed action fields for dispatch
        [JsonIgnore] public string ActionType => Action?["type"]?.GetValue<string>();
        [JsonIgnore] public int ActionAxis => Action?["axis"]?.GetValue<int>() ?? 1;
        [JsonIgnore] public int ActionButton => Action?["button"]?.GetValue<int>() ?? 1;
        [JsonIgnore] public string ActionMode => Action?["mode"]?.GetValue<string>() ?? "hold";
        [JsonIgnore] public float ActionSensitivity => Action?["sensitivity"]?.GetValue<float>() ?? 0.02f;
        [JsonIgnore] public float ActionDecayRate => Action?["decayRate"]?.GetValue<float>() ?? 0.95f;
        [JsonIgnore] public int ActionSteps => Action?["steps"]?.GetValue<int>() ?? 5;
    }

    public class BindingConfig
    {
        [JsonPropertyName("schema")]
        public int Schema { get; set; } = 1;

        [JsonPropertyName("plugin")]
        public string Plugin { get; set; } = "trackpad";

        [JsonPropertyName("bindings")]
        public List<BindingEntry> Bindings { get; set; } = new();

        private static string ConfigPath =>
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Apricadabra", "trackpad", "bindings.json");

        public static BindingConfig Load()
        {
            try
            {
                if (File.Exists(ConfigPath))
                    return JsonSerializer.Deserialize<BindingConfig>(File.ReadAllText(ConfigPath));
            }
            catch { }
            return new BindingConfig();
        }

        public void Save()
        {
            var dir = Path.GetDirectoryName(ConfigPath);
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(ConfigPath, json);
        }
    }
}
```

- [ ] **Step 3: Commit**

```bash
git add trackpad-plugin/Apricadabra.Trackpad.Core/Bindings/
git commit -m "feat(trackpad): add TrackpadSettings and BindingConfig with JSON persistence"
```

---

## Task 8: Binding Engine

**Files:**
- Create: `trackpad-plugin/Apricadabra.Trackpad.Core/Bindings/BindingEngine.cs`
- Create: `trackpad-plugin/Apricadabra.Trackpad.Core.Tests/BindingEngineTests.cs`

- [ ] **Step 1: Write BindingEngine tests**

Create `trackpad-plugin/Apricadabra.Trackpad.Core.Tests/BindingEngineTests.cs`:

```csharp
using NUnit.Framework;
using Apricadabra.Trackpad.Core.Bindings;
using Apricadabra.Trackpad.Core.Gestures;
using System.Collections.Generic;
using System.Text.Json.Nodes;

namespace Apricadabra.Trackpad.Core.Tests;

[TestFixture]
public class BindingEngineTests
{
    private BindingEngine _engine;
    private List<(string method, object[] args)> _sentCommands;

    [SetUp]
    public void Setup()
    {
        _sentCommands = new List<(string, object[])>();
        var config = new BindingConfig
        {
            Bindings = new List<BindingEntry>
            {
                MakeBinding("scroll-up", "scroll", 2, "up", "axis", axis: 1, mode: "hold"),
                MakeBinding("swipe-3-left", "swipe", 3, "left", "button", button: 5, mode: "pulse"),
                MakeBinding("tap-2", "tap", 2, "none", "button", button: 10, mode: "momentary"),
                MakeBinding("pinch-out", "pinch", 2, "out", "axis", axis: 3, mode: "spring"),
            }
        };
        _engine = new BindingEngine(config);
        _engine.OnSendAxis += (axis, mode, diff, sens, decay, steps) =>
            _sentCommands.Add(("axis", new object[] { axis, mode, diff }));
        _engine.OnSendButton += (button, mode, state) =>
            _sentCommands.Add(("button", new object[] { button, mode, state }));
    }

    [Test]
    public void ScrollUp_MatchesAxisBinding_SendsAxis()
    {
        _engine.ProcessGesture(new GestureEvent(GestureType.Scroll, 2, GestureDirection.Up, GesturePhase.Update, 0.05f));
        Assert.That(_sentCommands, Has.Count.GreaterThan(0));
        Assert.That(_sentCommands[0].method, Is.EqualTo("axis"));
    }

    [Test]
    public void SwipeLeft_MatchesButtonBinding_SendsButtonOnEnd()
    {
        _engine.ProcessGesture(new GestureEvent(GestureType.Swipe, 3, GestureDirection.Left, GesturePhase.End));
        Assert.That(_sentCommands, Has.Count.EqualTo(1));
        Assert.That(_sentCommands[0].method, Is.EqualTo("button"));
    }

    [Test]
    public void TapBegin_MomentaryButton_SendsDown()
    {
        _engine.ProcessGesture(new GestureEvent(GestureType.Tap, 2, GestureDirection.None, GesturePhase.Begin));
        Assert.That(_sentCommands, Has.Count.EqualTo(1));
        Assert.That(_sentCommands[0].args[2], Is.EqualTo("down"));
    }

    [Test]
    public void TapEnd_MomentaryButton_SendsUp()
    {
        _engine.ProcessGesture(new GestureEvent(GestureType.Tap, 2, GestureDirection.None, GesturePhase.End));
        Assert.That(_sentCommands, Has.Count.EqualTo(1));
        Assert.That(_sentCommands[0].args[2], Is.EqualTo("up"));
    }

    [Test]
    public void NoMatchingBinding_DoesNothing()
    {
        _engine.ProcessGesture(new GestureEvent(GestureType.Swipe, 4, GestureDirection.Right, GesturePhase.End));
        Assert.That(_sentCommands, Is.Empty);
    }

    private static BindingEntry MakeBinding(string id, string gestureType, int fingers, string direction,
        string actionType, int axis = 0, int button = 0, string mode = "hold")
    {
        var gesture = new JsonObject
        {
            ["type"] = gestureType,
            ["fingers"] = fingers,
            ["direction"] = direction
        };
        var action = new JsonObject { ["type"] = actionType, ["mode"] = mode };
        if (actionType == "axis") { action["axis"] = axis; action["sensitivity"] = 0.02f; }
        if (actionType == "button") { action["button"] = button; }
        return new BindingEntry { Id = id, Gesture = gesture, Action = action };
    }
}
```

- [ ] **Step 2: Implement BindingEngine**

Create `trackpad-plugin/Apricadabra.Trackpad.Core/Bindings/BindingEngine.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using Apricadabra.Trackpad.Core.Gestures;

namespace Apricadabra.Trackpad.Core.Bindings
{
    public class BindingEngine
    {
        private readonly BindingConfig _config;
        private readonly Dictionary<string, float> _accumulators = new();

        public event Action<int, string, int, float, float, int> OnSendAxis;  // axis, mode, diff, sensitivity, decayRate, steps
        public event Action<int, string, string> OnSendButton;  // button, mode, state

        public BindingEngine(BindingConfig config)
        {
            _config = config;
        }

        public void ProcessGesture(GestureEvent gesture)
        {
            var dirString = DirectionToString(gesture.Direction);
            var typeString = TypeToString(gesture.Type);

            var matches = _config.Bindings.Where(b =>
                b.GestureType == typeString &&
                b.GestureFingers == gesture.Fingers &&
                b.GestureDirection == dirString).ToList();

            foreach (var binding in matches)
            {
                DispatchBinding(binding, gesture);
            }
        }

        private void DispatchBinding(BindingEntry binding, GestureEvent gesture)
        {
            bool isContinuous = gesture.Type == GestureType.Scroll ||
                                gesture.Type == GestureType.Pinch ||
                                gesture.Type == GestureType.Rotate;

            if (isContinuous && gesture.Phase == GesturePhase.Update)
            {
                if (binding.ActionType == "axis")
                {
                    int diff = (int)(gesture.Delta * binding.ActionSensitivity * 1000);
                    if (diff != 0)
                        OnSendAxis?.Invoke(binding.ActionAxis, binding.ActionMode, diff,
                            binding.ActionSensitivity, binding.ActionDecayRate, binding.ActionSteps);
                }
                else if (binding.ActionType == "button")
                {
                    // Accumulate delta, fire when threshold reached
                    var key = binding.Id;
                    if (!_accumulators.ContainsKey(key)) _accumulators[key] = 0;
                    _accumulators[key] += MathF.Abs(gesture.Delta);
                    float threshold = 1.0f / Math.Max(binding.ActionSensitivity * 100, 1);
                    if (_accumulators[key] >= threshold)
                    {
                        _accumulators[key] = 0;
                        OnSendButton?.Invoke(binding.ActionButton, "pulse", null);
                    }
                }
            }
            else if (gesture.Type == GestureType.Swipe && gesture.Phase == GesturePhase.End)
            {
                if (binding.ActionType == "button")
                    OnSendButton?.Invoke(binding.ActionButton, binding.ActionMode, null);
                else if (binding.ActionType == "axis")
                    OnSendAxis?.Invoke(binding.ActionAxis, binding.ActionMode, 1,
                        binding.ActionSensitivity, binding.ActionDecayRate, binding.ActionSteps);
            }
            else if (gesture.Type == GestureType.Tap)
            {
                if (binding.ActionType == "button")
                {
                    if (gesture.Phase == GesturePhase.Begin)
                        OnSendButton?.Invoke(binding.ActionButton, binding.ActionMode, "down");
                    else if (gesture.Phase == GesturePhase.End)
                        OnSendButton?.Invoke(binding.ActionButton, binding.ActionMode, "up");
                }
                else if (binding.ActionType == "axis" && gesture.Phase == GesturePhase.Begin)
                {
                    OnSendAxis?.Invoke(binding.ActionAxis, binding.ActionMode, 1,
                        binding.ActionSensitivity, binding.ActionDecayRate, binding.ActionSteps);
                }
            }
        }

        private static string DirectionToString(GestureDirection dir) => dir switch
        {
            GestureDirection.Up => "up",
            GestureDirection.Down => "down",
            GestureDirection.Left => "left",
            GestureDirection.Right => "right",
            GestureDirection.In => "in",
            GestureDirection.Out => "out",
            GestureDirection.Clockwise => "clockwise",
            GestureDirection.CounterClockwise => "counterclockwise",
            _ => "none"
        };

        private static string TypeToString(GestureType type) => type switch
        {
            GestureType.Scroll => "scroll",
            GestureType.Pinch => "pinch",
            GestureType.Rotate => "rotate",
            GestureType.Swipe => "swipe",
            GestureType.Tap => "tap",
            _ => "unknown"
        };
    }
}
```

- [ ] **Step 3: Commit**

```bash
git add trackpad-plugin/
git commit -m "feat(trackpad): add BindingEngine with gesture-to-action dispatch and accumulator logic"
```

---

## Task 9: TrackpadService Orchestrator

**Files:**
- Create: `trackpad-plugin/Apricadabra.Trackpad.Core/TrackpadService.cs`

- [ ] **Step 1: Implement TrackpadService**

Create `trackpad-plugin/Apricadabra.Trackpad.Core/TrackpadService.cs`:

```csharp
using System;
using System.Threading.Tasks;
using Apricadabra.Client;
using Apricadabra.Trackpad.Core.Bindings;
using Apricadabra.Trackpad.Core.Gestures;
using Apricadabra.Trackpad.Core.Input;

namespace Apricadabra.Trackpad.Core
{
    public class TrackpadService : IDisposable
    {
        public RawInputCapture Input { get; private set; }
        public GestureRecognizer Recognizer { get; private set; }
        public BindingEngine Bindings { get; private set; }
        public ApricadabraClient Client { get; private set; }
        public TrackpadSettings Settings { get; private set; }
        public BindingConfig BindingConfig { get; private set; }

        public async Task Start()
        {
            // Load config
            Settings = TrackpadSettings.Load();
            BindingConfig = BindingConfig.Load();

            // Initialize input capture
            Input = new RawInputCapture();
            Input.SelectedDevicePath = Settings.SelectedDevicePath;

            // Initialize gesture pipeline
            Recognizer = new GestureRecognizer(Settings);
            Input.OnContactFrame += frame => Recognizer.ProcessFrame(frame);

            // Initialize binding engine
            Bindings = new BindingEngine(BindingConfig);

            // Initialize core connection
            Client = new ApricadabraClient("trackpad", broadcastPort: 19874);

            // Wire binding engine → client
            Bindings.OnSendAxis += (axis, mode, diff, sens, decay, steps) =>
            {
                var axisMode = mode switch
                {
                    "spring" => AxisMode.Spring,
                    "detent" => AxisMode.Detent,
                    _ => AxisMode.Hold
                };
                Client.SendAxis(axis, axisMode, diff, sens, decay, steps);
            };
            Bindings.OnSendButton += (button, mode, state) =>
            {
                var btnMode = mode switch
                {
                    "toggle" => ButtonMode.Toggle,
                    "pulse" => ButtonMode.Pulse,
                    "double" => ButtonMode.Double,
                    "rapid" => ButtonMode.Rapid,
                    "longshort" => ButtonMode.LongShort,
                    _ => ButtonMode.Momentary
                };
                ButtonState? btnState = state switch
                {
                    "down" => Apricadabra.Client.ButtonState.Down,
                    "up" => Apricadabra.Client.ButtonState.Up,
                    _ => null
                };
                Client.SendButton(button, btnMode, btnState);
            };

            // Wire gesture events → binding engine
            Recognizer.OnGestureEvent += gesture => Bindings.ProcessGesture(gesture);

            // Start input capture
            Input.Start();

            // Connect to core
            await Client.ConnectAsync();
        }

        public void Stop()
        {
            Input?.Stop();
            Client?.Dispose();
            Settings?.Save();
        }

        public void Dispose()
        {
            Stop();
            Input?.Dispose();
        }
    }
}
```

- [ ] **Step 2: Commit**

```bash
git add trackpad-plugin/Apricadabra.Trackpad.Core/TrackpadService.cs
git commit -m "feat(trackpad): add TrackpadService orchestrator wiring input → gestures → bindings → core"
```

---

## Task 10: Final Validation

**Files:** All created files

- [ ] **Step 1: Verify all files exist and have correct namespaces**

Check that every file in the file structure section exists and uses the correct namespace:
- `Apricadabra.Trackpad.Core.Models` for ContactFrame
- `Apricadabra.Trackpad.Core.Input` for RawInputCapture, HidTouchpadParser, TouchpadDevice, NativeMethods
- `Apricadabra.Trackpad.Core.Gestures` for GestureEvent, ContactTracker, GestureRecognizer
- `Apricadabra.Trackpad.Core.Bindings` for TrackpadSettings, BindingConfig, BindingEngine
- `Apricadabra.Trackpad.Core` for TrackpadService

- [ ] **Step 2: Check for TODOs/placeholders**

Search all `.cs` files for TODO, FIXME, NotImplementedException, TBD.

- [ ] **Step 3: Verify type consistency across files**

Check that:
- `ContactTracker.CurrentState` returns `TrackerState` (used by `GestureRecognizer`)
- `GestureRecognizer` accepts `ContactFrame` via `ProcessFrame()` (called by `TrackpadService`)
- `BindingEngine.ProcessGesture()` accepts `GestureEvent` (wired by `TrackpadService`)
- `BindingEngine` events match `ApricadabraClient` `Send*` signatures (wired by `TrackpadService`)

- [ ] **Step 4: Commit any fixes**

```bash
git add -u
git commit -m "chore: final validation and cleanup for trackpad listener"
```
