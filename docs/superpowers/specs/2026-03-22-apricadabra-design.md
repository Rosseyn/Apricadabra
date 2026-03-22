# Apricadabra Design Spec

A system that turns Loupedeck (and future Stream Deck+) dials and buttons into a virtual joystick for sim games via vJoy and DirectInput.

## System Architecture

Three independent processes communicating over a single named pipe:

```
+------------------+     +------------------+
|  Loupedeck       |     |  Stream Deck+    |
|  Plugin (C#)     |     |  Plugin (TS)     |     ... future devices
|                  |     |                  |
|  Action Editor   |     |  Same IPC        |
|  UI for config   |     |  protocol        |
+--------+---------+     +--------+---------+
         |  named pipe            |  named pipe
         |  (bidirectional)       |
         v                        v
+------------------------------------------+
|           Apricadabra Core (Rust)        |
|                                          |
|  +----------+  +----------+  +--------+  |
|  |  Axis    |  |  Button  |  |  vJoy  |  |
|  |  State   |  |  State   |  |  FFI   |  |
|  |  Manager |  |  Manager |  | Bridge |  |
|  +----------+  +----------+  +--------+  |
|  +--------------------------------------+ |
|  |  State Broadcast (-> plugins)        | |
|  +--------------------------------------+ |
|  +--------------------------------------+ |
|  |  Heartbeat / Health Check            | |
|  +--------------------------------------+ |
+------------------------------------------+
         |
         v
+------------------+
|  vJoy Driver     |
|  (kernel mode)   |
|  Virtual         |
|  DirectInput     |
|  Joystick        |
+------------------+
         |
         v
       Game
```

### Key Decisions

- **Named pipes** for IPC -- low latency, no network dependency, natural on Windows
- **Bidirectional** -- plugins send input events, core sends state updates back for LCD display
- **Core owns all vJoy interaction** -- single point of truth for axis/button state
- **Each plugin is a thin adapter** -- translates device SDK events into the common protocol
- **vJoy behind a trait** -- `VirtualJoystick` trait allows swapping in `uinput` (Linux) or macOS backends in the future

### Platform

Windows only. vJoy is a Windows kernel driver. Loupedeck and Stream Deck official software only run on Windows and macOS (no Linux). The `VirtualJoystick` trait is designed so a future backend could target other platforms, but the initial build is Windows-only.

## Language Split

| Component | Language | Reason |
|---|---|---|
| Core feeder | Rust | Single exe (no runtime), clean FFI to vJoy DLL, low memory footprint, ideal for long-running background process |
| Loupedeck plugin | C# (.NET 8) | Stable SDK, full Action Editor support for parameterized actions with dropdowns/sliders |
| Stream Deck+ plugin (future) | TypeScript (Node.js) | Elgato SDK is Node.js/TypeScript |

## IPC Protocol

Bidirectional newline-delimited JSON over `\\.\pipe\apricadabra`.

### Plugin -> Core (input events)

```json
// Dial rotation
{ "type": "axis", "axis": 1, "mode": "hold", "diff": 3, "sensitivity": 0.5 }
{ "type": "axis", "axis": 2, "mode": "spring", "diff": -1, "sensitivity": 0.5, "decayRate": 0.3 }
{ "type": "axis", "axis": 3, "mode": "detent", "diff": 1, "steps": 5 }
{ "type": "axis", "axis": 4, "mode": "relative", "diff": 2, "sensitivity": 0.5 }

// Buttons
{ "type": "button", "button": 1, "mode": "momentary", "state": "down" }
{ "type": "button", "button": 1, "mode": "momentary", "state": "up" }
{ "type": "button", "button": 2, "mode": "toggle", "state": "down" }
{ "type": "button", "button": 3, "mode": "pulse" }
{ "type": "button", "button": 4, "mode": "double", "delay": 50 }
{ "type": "button", "button": 5, "mode": "rapid", "state": "down", "rate": 100 }
{ "type": "button", "button": 5, "mode": "rapid", "state": "up" }
{ "type": "button", "button": 6, "mode": "longshort", "state": "down", "shortButton": 6, "longButton": 7, "threshold": 500 }
{ "type": "button", "button": 6, "mode": "longshort", "state": "up", "shortButton": 6, "longButton": 7, "threshold": 500 }

// Reset
{ "type": "reset", "axis": 1 }

// Heartbeat response
{ "type": "heartbeat_ack" }
```

Inverted axis modes negate the `diff` value in the plugin before sending -- the core does not need to know about inversion.

### Core -> Plugin (state updates and health)

```json
// State broadcast (~60Hz, only on change)
{ "type": "state", "axes": { "1": 0.73, "2": 0.50, "3": 0.20 }, "buttons": { "1": true, "5": false } }

// Heartbeat ping (every 2-3 seconds)
{ "type": "heartbeat" }
```

### Axis + Button Combo

The `axis_button` action type is handled entirely in the plugin. The plugin registers as both an adjustment and a command, forwarding dial turns as axis events and encoder presses as button events. The core receives them as separate axis and button messages.

## Action Library

15 actions total, each with Action Editor UI for user configuration via dropdowns and sliders.

### Axis Adjustments (8 actions)

| Action | Editor Controls | Encoder Press Behavior |
|---|---|---|
| vJoy Axis - Hold | Axis dropdown, Sensitivity slider | Reset to center |
| vJoy Axis - Hold (Inverted) | Axis dropdown, Sensitivity slider | Reset to center |
| vJoy Axis - Spring | Axis dropdown, Sensitivity slider, Decay rate slider | Reset to center |
| vJoy Axis - Spring (Inverted) | Axis dropdown, Sensitivity slider, Decay rate slider | Reset to center |
| vJoy Axis - Relative | Axis dropdown, Sensitivity slider | Reset to center |
| vJoy Axis - Relative (Inverted) | Axis dropdown, Sensitivity slider | Reset to center |
| vJoy Axis - Detent Step | Axis dropdown, Step count slider | Reset to center |
| vJoy Axis + Button | Axis dropdown, Button dropdown, Sensitivity slider | Fires selected button |

Axis dropdown options: X, Y, Z, Rx, Ry, Rz, Slider1, Slider2 (vJoy axes 1-8).

### Button Commands (6 actions)

| Action | Editor Controls |
|---|---|
| vJoy Button - Momentary | Button dropdown (1-128) |
| vJoy Button - Toggle | Button dropdown (1-128) |
| vJoy Button - Pulse | Button dropdown (1-128) |
| vJoy Button - Double Press | Button dropdown (1-128), Delay slider (ms between presses) |
| vJoy Button - Rapid Fire | Button dropdown (1-128), Rate slider (ms interval) |
| vJoy Button - Long/Short | Short button dropdown, Long button dropdown, Threshold slider (ms) |

### Utility (1 action)

| Action | Editor Controls |
|---|---|
| vJoy Reset Axis | Axis dropdown |

## Core Feeder App (Rust)

### Responsibilities

1. **Named pipe server** -- listens on `\\.\pipe\apricadabra`, accepts multiple simultaneous plugin connections
2. **Axis state manager** -- maintains current value (0.0-1.0) for each of 8 axes, applies mode-specific behavior:
   - Hold: accumulate diffs scaled by sensitivity, clamp to 0.0-1.0
   - Spring: accumulate diffs, tick a decay timer back toward 0.5
   - Relative: pass-through increments (game interprets as relative)
   - Detent: snap to nearest step position (e.g. 0%, 25%, 50%, 75%, 100% for 5 steps)
3. **Button state manager** -- tracks on/off state, handles timing logic:
   - Momentary: direct press/release passthrough
   - Toggle: flip state on each press
   - Pulse: brief press/release (~50ms)
   - Double press: two pulses with configurable delay
   - Rapid fire: repeated pulses at configurable rate while held
   - Long/short: start timer on press, fire short button if released before threshold, long button if held past threshold
4. **vJoy FFI bridge** -- loads `vJoyInterface.dll`, calls `AcquireVJD`, `SetAxis`, `SetBtn`, `RelinquishVJD` through the `VirtualJoystick` trait
5. **State broadcaster** -- on every state change, pushes current axis/button values to all connected plugins, throttled to ~60Hz
6. **Heartbeat** -- pings each client every 2-3 seconds, drops clients that miss 2 consecutive acks

### VirtualJoystick Trait

```rust
trait VirtualJoystick {
    fn acquire(&mut self, device_id: u8) -> Result<()>;
    fn set_axis(&mut self, axis: Axis, value: f32) -> Result<()>;
    fn set_button(&mut self, button: u8, pressed: bool) -> Result<()>;
    fn release(&mut self) -> Result<()>;
}
```

Initial implementation: `VJoyBackend` (Windows, vJoyInterface.dll FFI).
Future: `UInputBackend` (Linux), `MacHidBackend` (macOS DriverKit).

### Spring Decay Loop

A single timer at ~60Hz checks all spring-mode axes and nudges them toward center at their configured decay rate. This is the only time-based behavior besides button timing -- everything else is event-driven.

### Configuration

A `config.json` in the core's directory for:
- vJoy device ID (default: 1)
- Global default sensitivity (overridden by per-action settings from Action Editor)
- Global default decay rate for spring mode

Per-action settings (sensitivity, decay rate, step count, button timing) arrive in the IPC messages from the plugin's Action Editor configuration.

## Loupedeck Plugin (C# / .NET 8)

### Structure

- `ApricadabraPlugin : Plugin` -- entry point
- `ApricadabraApplication : ClientApplication` -- app registration
- 8 `ActionEditorAdjustment` subclasses (one per axis mode)
- 6 `ActionEditorCommand` subclasses (one per button mode)
- 1 `ActionEditorCommand` for Reset Axis
- `CoreConnection` -- named pipe client, auto-launch, reconnection

### Lifecycle

1. Plugin loads in Logi Plugin Service
2. `CoreConnection` attempts to connect to `\\.\pipe\apricadabra`
3. If core not running -> spawns `apricadabra-core.exe` -> retries with backoff (100ms, 200ms, 400ms... up to 5s)
4. On dial turn -> reads Action Editor params (axis, sensitivity, mode) -> sends JSON to core
5. On button press -> reads params (button number, mode, timing) -> sends JSON to core
6. On state update from core -> updates LCD display via `GetAdjustmentValue()` (e.g. "73%")
7. On disconnect -> shows "Disconnected" on action icons, attempts respawn + reconnect with backoff

### Display Feedback

Each axis adjustment overrides `GetAdjustmentValue()` to return the current axis value received from the core's state broadcasts. Displayed as a percentage on the dial's LCD area on supported devices.

## Error Handling & Edge Cases

### Connection Lifecycle

- **Core not running**: plugin spawns it, retries connection with exponential backoff (100ms to 5s)
- **Core crashes**: plugin detects broken pipe, shows "Disconnected" on LCD, attempts respawn + reconnect
- **Plugin disconnects**: core cleans up that client, continues running for other connected plugins
- **All plugins disconnect**: core holds axis state for ~30 seconds with gradual decay to center, then resets. If a plugin reconnects during decay, it receives current (mid-decay) state.
- **LPS crashes and restarts**: plugin reconnects to existing core (if it survived), receives full state dump, LCD displays are immediately accurate

### Heartbeat

- Core pings each client every 2-3 seconds with `{"type": "heartbeat"}`
- Plugin responds with `{"type": "heartbeat_ack"}`
- 2 missed acks -> core considers client dead, cleans up connection

### vJoy Errors

- **vJoy not installed**: core logs clear error message, exits
- **vJoy device not configured**: core reports which device ID it tried, suggests running vJoy config utility
- **Device acquired by another app**: core retries briefly, then reports failure to plugins for display
- **Axis value out of range**: clamp to 0.0-1.0, never pass invalid values to FFI

### Input Conflicts

- Two actions mapped to same axis: last write wins (same behavior as two physical sticks on one axis)
- Rapid fire active when button released: "up" event cancels timer immediately
- Spring decay vs new input: new input resets decay timer

### Disconnect Decay

When all plugins disconnect, instead of snapping axes to center or holding indefinitely:
- All axes gradually decay toward center (0.5) over ~30 seconds
- Decay rate matches the spring mode decay behavior
- If a plugin reconnects mid-decay, it receives current axis positions via state dump
- Prevents phantom stuck inputs in games while avoiding jarring jumps on brief disconnects

## Project Structure

```
apricadabra/
├── core/                              # Rust binary
│   ├── Cargo.toml
│   └── src/
│       ├── main.rs                    # Entry, pipe server, event loop
│       ├── ipc.rs                     # Named pipe server, JSON protocol
│       ├── axis.rs                    # Axis state manager (hold/spring/detent/relative)
│       ├── button.rs                  # Button state manager (momentary/toggle/pulse/etc)
│       ├── vjoy.rs                    # VirtualJoystick trait + vJoy FFI backend
│       └── broadcast.rs              # State broadcaster to connected plugins
│
├── loupedeck-plugin/                  # C# .NET 8
│   ├── src/
│   │   ├── ApricadabraPlugin.cs
│   │   ├── ApricadabraApplication.cs
│   │   ├── CoreConnection.cs          # Named pipe client + auto-launch
│   │   ├── Actions/
│   │   │   ├── AxisHoldAdjustment.cs
│   │   │   ├── AxisSpringAdjustment.cs
│   │   │   ├── AxisRelativeAdjustment.cs
│   │   │   ├── AxisDetentAdjustment.cs
│   │   │   ├── AxisButtonAdjustment.cs
│   │   │   ├── ButtonMomentaryCommand.cs
│   │   │   ├── ButtonToggleCommand.cs
│   │   │   ├── ButtonPulseCommand.cs
│   │   │   ├── ButtonDoublePressCommand.cs
│   │   │   ├── ButtonRapidFireCommand.cs
│   │   │   ├── ButtonLongShortCommand.cs
│   │   │   └── ResetAxisCommand.cs
│   │   └── Display/
│   │       └── StateDisplay.cs        # LCD feedback rendering
│   └── metadata/
│       └── LoupedeckPackage.yaml
│
├── streamdeck-plugin/                 # Future - TypeScript (Node.js)
│
└── docs/
    └── superpowers/
        └── specs/
            └── 2026-03-22-apricadabra-design.md
```

### Build

- **Core**: `cargo build --release` -> `apricadabra-core.exe`
- **Loupedeck plugin**: `dotnet build` + `logiplugintool` -> `.lplug4` package
- **Dev workflow**: `cargo watch` for core, `dotnet watch build` for plugin hot reload

### Deployment

The core exe needs to be accessible to the Loupedeck plugin for auto-launch. Options to resolve during implementation:
- Bundle inside `.lplug4` if the SDK allows spawning executables from the plugin directory
- Install to `%APPDATA%/Apricadabra/` with plugin configured to look there
- Separate installer that places both components

## Supported Devices

| Device | Dials | Buttons | LCD Feedback |
|---|---|---|---|
| Loupedeck CT | 6 rotary encoders + wheel | 12 buttons | Yes (touch screen) |
| Loupedeck Live | 6 rotary dials | LCD touch buttons | Yes |
| Loupedeck Live S | 2 dials | LCD touch buttons | Yes |
| Loupedeck+ | Analog dials | Buttons | Limited |
| Stream Deck+ (future) | 4 dials | 8 LCD buttons | Yes |

All devices use the same IPC protocol. Device-specific behavior is handled entirely in the plugin layer via device profiles.

## Dependencies

### Core (Rust)
- `serde` / `serde_json` -- JSON serialization
- `windows-named-pipes` or `tokio` with named pipe support -- IPC
- `libloading` or raw FFI -- vJoyInterface.dll loading

### Loupedeck Plugin (C#)
- Logi Actions SDK (NuGet or SDK reference)
- `System.IO.Pipes` -- named pipe client (built into .NET)
- `System.Text.Json` -- JSON serialization (built into .NET)
