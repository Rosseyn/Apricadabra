# Trackpad Listener Design

**Date:** 2026-03-26
**Sub-project:** 3a of 3 (Protocol → C# Client Library → Trackpad Listener → Trackpad UI)
**Status:** Draft

## Overview

A headless C# library (`Apricadabra.Trackpad.Core`) that captures raw touchpad input from Windows Precision Touchpads via the Raw Input API, recognizes gestures, and dispatches actions to the Apricadabra core via the `Apricadabra.Client` SDK. This library is consumed by the WPF UI app (sub-project 3b) but is fully functional and testable without a UI.

## Context

This is the first Apricadabra plugin built as a standalone Windows application (not hosted inside a device manufacturer's SDK). It serves as the reference implementation for the plugin template repo and is the first consumer of both the `Apricadabra.Client` NuGet package and the standardized bindings schema.

---

## 1. Project Structure

```
trackpad-plugin/
├── Apricadabra.Trackpad.Core/              # Headless library (.NET 8, Windows)
│   ├── Apricadabra.Trackpad.Core.csproj
│   ├── TrackpadService.cs                  # Top-level orchestrator
│   ├── Input/
│   │   ├── RawInputCapture.cs              # Raw Input API registration + WM_INPUT handler
│   │   ├── HidTouchpadParser.cs            # HID report parsing → contact points
│   │   ├── TouchpadDevice.cs               # Device info model
│   │   └── NativeMethods.cs                # P/Invoke declarations
│   ├── Gestures/
│   │   ├── ContactTracker.cs               # Tracks contacts across frames
│   │   ├── GestureRecognizer.cs            # Classifies contact patterns into gestures
│   │   └── GestureEvent.cs                 # Gesture types, directions, phases
│   ├── Bindings/
│   │   ├── BindingEngine.cs                # Gesture → action dispatch
│   │   ├── BindingConfig.cs                # Load/save bindings.json
│   │   └── TrackpadSettings.cs             # Global thresholds and device selection
│   └── Models/
│       └── ContactFrame.cs                 # ContactFrame and ContactPoint types
│
└── Apricadabra.Trackpad.Core.Tests/        # Tests (.NET 8)
    ├── GestureRecognizerTests.cs
    ├── BindingEngineTests.cs
    └── ContactTrackerTests.cs
```

**Target framework:** .NET 8.0 (Windows-only — uses Raw Input, HID parser, named pipes)

**Dependencies:**
- `Apricadabra.Client` (NuGet) — core connection and typed command helpers
- No other external dependencies — Raw Input and HID parser via P/Invoke

---

## 2. Input Layer

### RawInputCapture.cs

Manages device registration and message dispatch for background touchpad capture.

- Creates a hidden message-only `HWND` to receive `WM_INPUT` messages
- Registers with `RegisterRawInputDevices` using:
  - `usUsagePage = HID_USAGE_PAGE_DIGITIZER` (0x0D)
  - `usUsage = HID_USAGE_DIGITIZER_TOUCH_PAD` (0x05)
  - `dwFlags = RIDEV_INPUTSINK | RIDEV_DEVNOTIFY` (background capture + device change notifications)
- On `WM_INPUT`: calls `GetRawInputData`, passes raw HID bytes to `HidTouchpadParser`
- On `WM_INPUT_DEVICE_CHANGE`: re-enumerates devices via `HidTouchpadParser`, updates `AvailableDevices`, fires `OnDevicesChanged`
- Filters incoming events by device handle when a specific device is selected in settings. If the selected device is removed, falls back to "all devices" and fires `OnDevicesChanged`.
- Exposes:
  - `event Action<ContactFrame> OnContactFrame` — fires once per complete contact set
  - `event Action OnDevicesChanged` — fires when devices are added or removed (hot-plug)
  - `IReadOnlyList<TouchpadDevice> AvailableDevices` — discovered precision touchpads (updated on hot-plug)

### HidTouchpadParser.cs

Extracts contact points from raw HID reports.

**Device discovery (at startup):**
- Enumerates devices via `GetRawInputDeviceList`
- For each device, retrieves preparsed data via `GetRawInputDeviceInfo` with `RIDI_PREPARSEDDATA`
- Calls `HidP_GetCaps`, `HidP_GetValueCaps`, `HidP_GetButtonCaps` to learn report layout
- Identifies precision touchpads by presence of digitizer value capabilities
- Stores physical X/Y min/max per link collection for normalization

**Per-report parsing:**
- Extracts via `HidP_GetUsageValue`:
  - `HID_USAGE_DIGITIZER_CONTACT_COUNT` (0x54) — how many contacts in this set
  - `HID_USAGE_DIGITIZER_CONTACT_ID` (0x51) — persistent ID per finger
  - `HID_USAGE_GENERIC_X` (0x30) — X position
  - `HID_USAGE_GENERIC_Y` (0x31) — Y position
- Extracts via `HidP_GetUsages`:
  - `HID_USAGE_DIGITIZER_TIP_SWITCH` — finger on/off surface
- Buffers individual contact reports until the full set arrives (contact count tells how many reports to expect), then emits a complete `ContactFrame`
- Normalizes X/Y to 0.0-1.0 using device physical min/max

### TouchpadDevice.cs

```csharp
public class TouchpadDevice
{
    public string DevicePath { get; }     // Raw Input device path (unique ID)
    public string Name { get; }           // Human-readable device name
    public int MaxContacts { get; }       // Max simultaneous contacts supported
}
```

### ContactFrame.cs

```csharp
public class ContactFrame
{
    public ContactPoint[] Contacts { get; }
    public DateTime Timestamp { get; }
}

public struct ContactPoint
{
    public int Id;           // Contact ID (persistent while finger is down)
    public float X, Y;      // Normalized 0.0-1.0
    public bool OnSurface;  // True if finger touching, false if lifted
}
```

### NativeMethods.cs

All P/Invoke signatures in one file:
- Raw Input: `RegisterRawInputDevices`, `GetRawInputData`, `GetRawInputDeviceList`, `GetRawInputDeviceInfo`
- HID Parser: `HidP_GetCaps`, `HidP_GetValueCaps`, `HidP_GetButtonCaps`, `HidP_GetUsageValue`, `HidP_GetUsages`
- Window: `CreateWindowEx`, `DefWindowProc`, `RegisterClassEx`
- Supporting structs: `RAWINPUTDEVICE`, `RAWINPUT`, `RAWINPUTHEADER`, `HIDP_CAPS`, `HIDP_VALUE_CAPS`, `HIDP_BUTTON_CAPS`, etc.

---

## 3. Gesture Recognition

### ContactTracker.cs

Maintains contact state across frames.

- Tracks active contacts by ID (finger down → move → up lifecycle)
- Computes per-contact:
  - Delta (movement since last frame)
  - Cumulative path distance
  - Velocity (distance / time)
- Computes aggregate metrics per frame:
  - Finger count
  - Center-of-mass position and movement
  - Spread (average distance between contacts — for pinch detection)
  - Rotation angle between contact pairs (for rotate detection)
- Detects finger count transitions (contacts added/removed)

### GestureRecognizer.cs

Classifies contact patterns into gesture events using fixed classification rules and configurable thresholds.

**Classification rules:**

| Gesture | Finger count | Detection |
|---------|-------------|-----------|
| Pinch | Always 2 | Spread delta dominates linear delta |
| Rotate | 2+ (any count) | Rotation delta dominates |
| Scroll | `ScrollFingerCount` (default 2) | Linear motion dominates |
| Swipe | > `ScrollFingerCount` | Linear motion, fires on lift |
| Tap | Any (2, 3, 4) | Minimal movement, short duration, momentary (down/up) |

**Recognition priority** (when multiple gestures could match):
1. Pinch — 2 fingers, spread delta dominates
2. Rotate — 2+ fingers, rotation delta dominates
3. Scroll — finger count == `ScrollFingerCount`, linear motion
4. Swipe — finger count > `ScrollFingerCount`, linear motion
5. Tap — no significant movement, fingers lift quickly

**Gesture commitment:** The recognizer commits to a gesture type on the first frame that crosses the movement threshold and sticks with it until all fingers lift. This prevents mid-gesture reclassification.

**Continuous gestures** (scroll, pinch, rotate): Fire `GestureEvent` with `Phase.Update` on each frame with a `Delta` value. Fire `Phase.Begin` on first classified frame, `Phase.End` when fingers lift.

**Brief gestures** (swipe): Fire once with `Phase.End` when fingers lift, if cumulative distance and velocity exceed thresholds. Direction = cardinal direction of cumulative movement.

**Taps:** Fire `Phase.Begin` when finger count stabilizes (all fingers down), `Phase.End` when fingers lift. Classified only if fingers lift within `TapMaxDuration` and cumulative movement is below `TapMaxMovement`.

**Exposes:** `event Action<GestureEvent> OnGestureEvent`

### GestureEvent.cs

```csharp
public class GestureEvent
{
    public GestureType Type { get; }
    public int Fingers { get; }
    public GestureDirection Direction { get; }
    public GesturePhase Phase { get; }
    public float Delta { get; }  // Incremental value for continuous gestures
}

public enum GestureType { Scroll, Pinch, Rotate, Swipe, Tap }
public enum GestureDirection { Up, Down, Left, Right, In, Out, Clockwise, CounterClockwise, None }
public enum GesturePhase { Begin, Update, End }
```

---

## 4. Binding Engine

### BindingEngine.cs

Receives `GestureEvent`s, looks up matching bindings, dispatches actions via `ApricadabraClient`.

**Matching:** A binding matches when `GestureType`, `Fingers`, and `Direction` all match the event.

**Dispatch rules:**

| Gesture category | Action type | Behavior |
|-----------------|-------------|----------|
| Continuous → Axis | `delta * sensitivity` → `SendAxis()` each update |
| Continuous → Button | Accumulate delta, fire `SendButton(pulse)` when accumulated exceeds threshold (derived from sensitivity) |
| Swipe → Button | Fire `SendButton()` on `End` phase |
| Swipe → Axis | `SendAxis()` with `diff = intensity` on `End` phase |
| Tap → Button | `SendButton(down)` on `Begin`, `SendButton(up)` on `End` (momentary) |
| Tap → Axis | `SendAxis()` with `diff = intensity` on `Begin` |

**Per-binding parameters:**
- `sensitivity` (float) — for continuous→axis: scales delta. For continuous→button: inverse threshold (higher = fires more often)
- `intensity` (int) — for brief→axis: how much the axis moves per gesture

### BindingConfig.cs

Loads/saves `%APPDATA%/Apricadabra/trackpad/bindings.json` using the standardized schema.

```json
{
  "schema": 1,
  "plugin": "trackpad",
  "bindings": [
    {
      "id": "scroll-up-axis1",
      "gesture": {
        "type": "scroll",
        "fingers": 2,
        "direction": "up"
      },
      "action": {
        "type": "axis",
        "axis": 1,
        "mode": "hold",
        "sensitivity": 0.02
      }
    },
    {
      "id": "swipe-3-left-btn5",
      "gesture": {
        "type": "swipe",
        "fingers": 3,
        "direction": "left"
      },
      "action": {
        "type": "button",
        "button": 5,
        "mode": "pulse"
      }
    },
    {
      "id": "tap-2-btn10",
      "gesture": {
        "type": "tap",
        "fingers": 2,
        "direction": "none"
      },
      "action": {
        "type": "button",
        "button": 10,
        "mode": "momentary"
      }
    },
    {
      "id": "pinch-in-axis3",
      "gesture": {
        "type": "pinch",
        "fingers": 2,
        "direction": "in"
      },
      "action": {
        "type": "axis",
        "axis": 3,
        "mode": "spring",
        "sensitivity": 0.02,
        "decayRate": 0.95
      }
    }
  ]
}
```

### TrackpadSettings.cs

Global settings at `%APPDATA%/Apricadabra/trackpad/settings.json`:

```json
{
  "selectedDevicePath": null,
  "scrollFingerCount": 2,
  "scrollSensitivity": 1.0,
  "swipeDistanceThreshold": 0.15,
  "swipeSpeedThreshold": 0.3,
  "pinchSensitivity": 1.0,
  "rotateSensitivity": 1.0,
  "tapMaxDuration": 300,
  "tapMaxMovement": 0.03
}
```

| Setting | Type | Default | Description |
|---------|------|---------|-------------|
| `selectedDevicePath` | string/null | null | Device to capture from. Null = all devices. |
| `scrollFingerCount` | int | 2 | Finger count for scroll gesture. Swipes require more fingers. |
| `scrollSensitivity` | float | 1.0 | Multiplier for scroll delta |
| `swipeDistanceThreshold` | float | 0.15 | Min normalized distance for swipe recognition |
| `swipeSpeedThreshold` | float | 0.3 | Min velocity for swipe recognition |
| `pinchSensitivity` | float | 1.0 | Multiplier for pinch delta |
| `rotateSensitivity` | float | 1.0 | Multiplier for rotation delta |
| `tapMaxDuration` | int (ms) | 300 | Max finger-down time for tap classification |
| `tapMaxMovement` | float | 0.03 | Max normalized movement allowed during tap |

---

## 5. TrackpadService (Orchestrator)

Wires all components together. This is what the WPF app (3b) instantiates.

```csharp
public class TrackpadService : IDisposable
{
    public RawInputCapture Input { get; }
    public GestureRecognizer Recognizer { get; }
    public BindingEngine Bindings { get; }
    public ApricadabraClient Client { get; }
    public TrackpadSettings Settings { get; }
    public BindingConfig BindingConfig { get; }

    public void Start();   // Load settings, init capture, connect to core
    public void Stop();    // Stop capture, disconnect, save settings
}
```

**Data flow:**
```
RawInputCapture.OnContactFrame
    → ContactTracker (track state across frames)
        → GestureRecognizer.OnGestureEvent
            → BindingEngine → ApricadabraClient.Send*()
```

**Start:**
1. Load `TrackpadSettings` and `BindingConfig` from disk
2. Initialize `RawInputCapture` with device filter from settings
3. Initialize `ContactTracker` and `GestureRecognizer` with thresholds from settings
4. Initialize `BindingEngine` with bindings and `ApricadabraClient("trackpad", broadcastPort: 19874)`
5. Wire the event chain
6. Connect to core via `ApricadabraClient.ConnectAsync()`

**Stop:**
1. Disconnect from core
2. Stop input capture
3. Save settings to disk

**Public properties** exposed for UI consumption (3b):
- `Input.AvailableDevices` — for device selector dropdown
- `Recognizer.OnGestureEvent` — for live test panel
- `Settings` — for settings editor
- `BindingConfig` — for binding editor
- `Client.IsConnected` — for connection status display

---

## 6. Testing Strategy

Unit tests in `Apricadabra.Trackpad.Core.Tests/`:

- **ContactTrackerTests** — Feed sequences of `ContactFrame`s, verify delta computation, finger count transitions, spread and rotation calculations
- **GestureRecognizerTests** — Feed synthetic `ContactFrame` sequences simulating each gesture type, verify correct `GestureEvent` classification, direction, and phase transitions. Test priority (pinch vs scroll with 2 fingers), gesture commitment (no mid-gesture reclassification), threshold edge cases.
- **BindingEngineTests** — Feed `GestureEvent`s with mock bindings, verify correct `Send*` calls. Test continuous→axis scaling, continuous→button accumulation threshold, swipe→button dispatch, tap momentary down/up.

Integration testing (manual, Windows only): run with a real Precision Touchpad, verify gesture recognition and core command dispatch.

---

## Implementation Order

1. NativeMethods.cs (P/Invoke declarations)
2. ContactFrame.cs, TouchpadDevice.cs (data models)
3. HidTouchpadParser.cs (HID report parsing)
4. RawInputCapture.cs (device registration, message dispatch, device filtering)
5. ContactTracker.cs (state tracking across frames)
6. GestureRecognizer.cs (classification + thresholds)
7. GestureEvent.cs (types/enums)
8. TrackpadSettings.cs + BindingConfig.cs (config persistence)
9. BindingEngine.cs (gesture → action dispatch)
10. TrackpadService.cs (orchestrator)
11. Unit tests

---

## Sub-project Dependencies

This spec depends on:
- **Sub-project 2** (C# Client Library): Completed. Consumed via NuGet.

This spec must be complete before:
- **Sub-project 3b** (Trackpad UI): WPF system tray app consuming this library.
