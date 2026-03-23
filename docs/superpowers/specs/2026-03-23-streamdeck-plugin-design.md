# Stream Deck Plugin for Apricadabra — Design Spec

## Overview

Add a Stream Deck plugin to the Apricadabra project, enabling Stream Deck+ (and other encoder-equipped models) to control vJoy virtual joystick axes and buttons. The plugin communicates with the existing Rust core via the same IPC protocol used by the Loupedeck plugin.

## Architecture

The Stream Deck plugin is a new client to the existing Apricadabra core. Both plugins can run simultaneously.

```
┌─────────────────┐     ┌─────────────────┐
│  Loupedeck App  │     │ Stream Deck App │
│  (Logi Plugin   │     │  (WebSocket)    │
│   Service)      │     │                 │
└───────┬─────────┘     └───────┬─────────┘
        │ C# Plugin             │ TypeScript Plugin
        │                       │
        ▼                       ▼
┌─────────────────────────────────────────┐
│          Apricadabra Core (Rust)        │
│  Named Pipe: handshake + heartbeat      │
│  UDP 19871: commands (plugin → core)    │
│  UDP 19872: broadcasts (core → plugin)  │
│  vJoy FFI: axis/button → virtual HID   │
└─────────────────────────────────────────┘
```

### Project Structure

```
apricadabra/
├── core/                    (existing, Rust)
├── loupedeck-plugin/        (existing, C#)
├── streamdeck-plugin/       (new, TypeScript)
│   ├── src/
│   │   ├── actions/
│   │   │   ├── dial-action.ts
│   │   │   ├── button-action.ts
│   │   │   └── reset-axis-action.ts
│   │   ├── core-connection.ts
│   │   ├── state-display.ts
│   │   └── plugin.ts
│   ├── property-inspector/
│   │   ├── dial.html
│   │   ├── button.html
│   │   └── reset-axis.html
│   ├── manifest.json
│   └── package.json
├── scripts/
└── docs/
```

### Technology

- **Language**: TypeScript (Node.js 20+)
- **SDK**: Elgato Stream Deck SDK v3 (`@elgato/sdk`)
- **Scaffolding**: `@elgato/cli` (`streamdeck create`)

## Actions

### 1. vJoy Dial (`com.apricadabra.dial`)

**Controllers**: `["Encoder"]` — appears only on dial slots (SD+, SD+ XL, Studio, Galleon).

**Behavior**:
- **Rotation**: Sends axis command via UDP. Mode determines behavior (hold/spring/detent).
- **Dial press**: Fires configurable vJoy button pulse.
- **LCD feedback**: `$B1` layout — axis name at top, percentage value, progress bar. Updated in real-time from UDP state broadcasts via `setFeedback()`.

**Settings** (Property Inspector):
| Field | Type | Range | Default | Condition |
|-------|------|-------|---------|-----------|
| Axis | dropdown | X, Y, Z, Rx, Ry, Rz, Slider 1, Slider 2 | — | required |
| Mode | dropdown | Hold, Spring, Detent | Hold | required |
| Sensitivity | range | 1-100% (step 1) | 20 | always |
| Invert | checkbox | — | false | always |
| Decay Rate | range | 1-99% (step 1) | 95 | mode = Spring |
| Steps | range | 2-20 (step 1) | 5 | mode = Detent |
| Encoder Press Button | dropdown | None, Button 1-128 | None | always |

**Protocol messages sent**:
```json
{"type":"axis","axis":1,"mode":"hold","diff":3,"sensitivity":0.02}
{"type":"axis","axis":1,"mode":"spring","diff":3,"sensitivity":0.02,"decayRate":0.95}
{"type":"axis","axis":1,"mode":"detent","diff":1,"steps":5}
{"type":"button","button":1,"mode":"pulse"}
```

### 2. vJoy Button (`com.apricadabra.button`)

**Controllers**: `["Keypad"]` — appears only on button slots.

All 6 button modes exposed (Stream Deck has key-up events, unlike Loupedeck):

| Mode | Behavior | Key-up needed |
|------|----------|---------------|
| Momentary | Held while pressed, released on key-up | yes |
| Toggle | Flip state on each press | no |
| Pulse | Brief 50ms press/release | no |
| Double Press | Two rapid pulses | no |
| Rapid Fire | Auto-repeat while held | yes |
| Long/Short | Tap fires short button, hold fires long button | yes |

**Settings** (Property Inspector):
| Field | Type | Range | Default | Condition |
|-------|------|-------|---------|-----------|
| Button | dropdown | 1-128 | — | required |
| Mode | dropdown | all 6 modes | Pulse | required |
| Delay | range | 10-200ms (step 5) | 50 | mode = Double Press |
| Rate | range | 20-500ms (step 10) | 100 | mode = Rapid Fire |
| Short Press Button | dropdown | 1-128 | — | mode = Long/Short |
| Long Press Button | dropdown | 1-128 | — | mode = Long/Short |
| Threshold | range | 100-2000ms (step 50) | 500 | mode = Long/Short |

**Protocol messages sent**:
```json
{"type":"button","button":1,"mode":"momentary","state":"down"}
{"type":"button","button":1,"mode":"momentary","state":"up"}
{"type":"button","button":1,"mode":"toggle","state":"down"}
{"type":"button","button":1,"mode":"pulse"}
{"type":"button","button":1,"mode":"double","delay":50}
{"type":"button","button":1,"mode":"rapid","state":"down","rate":100}
{"type":"button","button":1,"mode":"rapid","state":"up"}
{"type":"button","button":1,"mode":"longshort","state":"down","shortButton":1,"longButton":2,"threshold":500}
{"type":"button","button":1,"mode":"longshort","state":"up","shortButton":1,"longButton":2,"threshold":500}
```

### 3. vJoy Reset Axis (`com.apricadabra.reset`)

**Controllers**: `["Keypad", "Encoder"]` — works on both button slots and dial press.

**Settings** (Property Inspector):
| Field | Type | Range | Default |
|-------|------|-------|---------|
| Axis | dropdown | X, Y, Z, Rx, Ry, Rz, Slider 1, Slider 2 | — |
| Position | range | 0-100% (step 1) | 50 |

**Protocol message sent**:
```json
{"type":"reset","axis":1,"position":0.5}
```

## Core Connection (TypeScript)

### Named Pipe
- Node.js `net` module connecting to `\\.\pipe\apricadabra`
- Sends Hello `{"type":"hello","version":1,"name":"streamdeck"}`
- Receives Welcome with current state
- Reads heartbeats, sends acks
- Line-delimited JSON

### UDP
- `dgram` module
- Commands: send datagrams to `127.0.0.1:19871`
- Broadcasts: bind to `127.0.0.1:19872`, parse incoming state

### Auto-launch
- On connect failure, spawn `apricadabra-core.exe` from `%APPDATA%\Apricadabra\`
- Exponential backoff: 100ms → 200ms → 400ms → ... → 5s cap

### State Display
- In-memory Map of axis values (number) and button states (boolean)
- Updated from UDP broadcasts
- Actions query for LCD updates via `setFeedback()`

### Real-time LCD Updates
- On each UDP state broadcast, update all active dial actions
- Call `setFeedback({ value: "73%", indicator: 73 })` with current axis percentage
- Only update dials whose axis value actually changed

## Core Fixes (Rust)

These fixes apply to the shared core before building the Stream Deck plugin:

### 1. Wire up rapid fire
- In the server tick loop, iterate `rapid_active` buttons and call `rapid_tick()` based on each button's configured rate
- Store rate per button in a HashMap alongside the active set

### 2. Wire up disconnect decay
- Call `axis_mgr.start_disconnect_decay()` when connected client count reaches 0
- Currently logs "All clients disconnected" but doesn't trigger the decay

### 3. Fix Loupedeck slider bugs
- **Reset Axis position slider**: Fix `SetValues` parameters so it allows 0-100 at 1% increments (currently only 0/50/100)
- **Dial sensitivity slider**: Fix default to 20% and allow 1% increments (currently defaults to 1%, jumps in ~20% steps)
- Apply same fixes to ensure Stream Deck Property Inspector uses correct ranges

## Manifest

```json
{
  "SDKVersion": 3,
  "UUID": "com.apricadabra.streamdeck",
  "Name": "Apricadabra",
  "Version": "0.1.0.0",
  "Description": "Control vJoy virtual joystick axes and buttons",
  "Author": "apricadabra",
  "Category": "Apricadabra",
  "Icon": "assets/plugin-icon",
  "CodePath": "dist/plugin.js",
  "OS": [{ "Platform": "windows", "MinimumVersion": "10" }],
  "Software": { "MinimumVersion": "6.9" },
  "Actions": [
    {
      "UUID": "com.apricadabra.dial",
      "Name": "vJoy Dial",
      "Icon": "assets/dial-icon",
      "Controllers": ["Encoder"],
      "Encoder": {
        "layout": "$B1",
        "TriggerDescription": {
          "Rotate": "Adjust axis",
          "Push": "Fire button",
          "Touch": "Settings"
        }
      },
      "PropertyInspectorPath": "property-inspector/dial.html"
    },
    {
      "UUID": "com.apricadabra.button",
      "Name": "vJoy Button",
      "Icon": "assets/button-icon",
      "Controllers": ["Keypad"],
      "PropertyInspectorPath": "property-inspector/button.html"
    },
    {
      "UUID": "com.apricadabra.reset",
      "Name": "vJoy Reset Axis",
      "Icon": "assets/reset-icon",
      "Controllers": ["Keypad", "Encoder"],
      "Encoder": {
        "layout": "$A0",
        "TriggerDescription": {
          "Push": "Reset axis"
        }
      },
      "PropertyInspectorPath": "property-inspector/reset-axis.html"
    }
  ]
}
```

## What Doesn't Change

- Rust core (except the 3 fixes above)
- Loupedeck plugin (except slider fixes)
- UDP ports 19871/19872
- Named pipe `\\.\pipe\apricadabra`
- Protocol message format
- vJoy integration
- Config file location and format

## Supported Devices

| Device | Buttons | Dials | LCD Strip | vJoy Dial | vJoy Button | vJoy Reset |
|--------|---------|-------|-----------|-----------|-------------|------------|
| Stream Deck (base models) | 6-32 | 0 | No | — | yes | yes |
| Stream Deck+ | 8 | 4 | Yes | yes | yes | yes |
| Stream Deck+ XL | 8 | 6 | Yes | yes | yes | yes |
| Stream Deck Studio | varies | 2 | Yes | yes | yes | yes |
| Stream Deck Neo | 8 | 0 | No | — | yes | yes |
