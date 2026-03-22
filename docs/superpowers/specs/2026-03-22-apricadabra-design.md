# Apricadabra Design Spec

A system that turns Loupedeck (and future Stream Deck+) dials and buttons into a virtual joystick for sim games via vJoy and DirectInput.

## System Architecture

Three independent processes communicating over named pipes (one pipe instance per connected plugin):

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

- **Named pipes** for IPC -- low latency, no network dependency, natural on Windows. The core creates a new pipe instance (`CreateNamedPipe`) for each connected client, all on `\\.\pipe\apricadabra`.
- **Bidirectional** -- plugins send input events, core sends state updates back for LCD display
- **Core owns all vJoy interaction** -- single point of truth for axis/button state
- **Each plugin is a thin adapter** -- translates device SDK events into the common protocol
- **vJoy behind a trait** -- `VirtualJoystick` trait allows swapping in `uinput` (Linux) or macOS backends in the future
- **Async runtime (tokio)** -- the core handles multiple pipe connections, decay timers, button timing, and heartbeats concurrently; an async runtime is required, not optional

### Platform

Windows only. vJoy is a Windows kernel driver. Loupedeck and Stream Deck official software only run on Windows and macOS (no Linux). The `VirtualJoystick` trait is designed so a future backend could target other platforms, but the initial build is Windows-only.

## Language Split

| Component | Language | Reason |
|---|---|---|
| Core feeder | Rust | Single exe (no runtime), clean FFI to vJoy DLL, low memory footprint, ideal for long-running background process |
| Loupedeck plugin | C# (.NET 8) | Stable SDK, full Action Editor support for parameterized actions with dropdowns/sliders |
| Stream Deck+ plugin (future) | TypeScript (Node.js) | Elgato SDK is Node.js/TypeScript |

## IPC Protocol

Bidirectional newline-delimited JSON over `\\.\pipe\apricadabra`. Protocol version: 1.

### Value Ranges

- Axis values are normalized 0.0-1.0 in the protocol. The core maps these to vJoy's native 0-32767 range internally before calling `SetAxis`.
- Button numbers are 1-128 (matching vJoy's supported range).
- Axis identifiers are 1-8 mapping to: X, Y, Z, Rx, Ry, Rz, Slider1, Slider2.

### Connection Handshake

When a plugin connects, it sends a hello message. The core responds with the current full state:

```json
// Plugin -> Core (first message after connect)
{ "type": "hello", "version": 1, "name": "loupedeck" }

// Core -> Plugin (immediate response)
{ "type": "welcome", "version": 1, "axes": { "1": 0.73, "2": 0.50 }, "buttons": { "1": true } }
```

If the core does not support the plugin's protocol version, it responds with an error and closes the connection.

### Plugin -> Core (input events)

```json
// Dial rotation
{ "type": "axis", "axis": 1, "mode": "hold", "diff": 3, "sensitivity": 0.5 }
{ "type": "axis", "axis": 2, "mode": "spring", "diff": -1, "sensitivity": 0.5, "decayRate": 0.3 }
{ "type": "axis", "axis": 3, "mode": "detent", "diff": 1, "steps": 5 }
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

// Reset axis to configured position
{ "type": "reset", "axis": 1, "position": 0.5 }

// Heartbeat response
{ "type": "heartbeat_ack" }
```

Inverted axis modes negate the `diff` value in the plugin before sending -- the core does not need to know about inversion.

The `longshort` mode does not use a top-level `button` field. The core uses `shortButton` and `longButton` exclusively to determine which vJoy buttons to fire.

### Core -> Plugin (state updates, health, errors)

```json
// State broadcast (~60Hz, batched per tick -- all axis/button changes within one tick are sent in a single message)
{ "type": "state", "axes": { "1": 0.73, "2": 0.50, "3": 0.20 }, "buttons": { "1": true, "5": false } }

// Heartbeat ping (every 2-3 seconds)
{ "type": "heartbeat" }

// Error (displayed on plugin LCD/UI)
{ "type": "error", "code": "vjoy_not_installed", "message": "vJoy driver not found. Please install vJoy." }
{ "type": "error", "code": "vjoy_device_busy", "message": "vJoy device 1 is acquired by another application." }
{ "type": "error", "code": "vjoy_device_missing", "message": "vJoy device 1 is not configured. Run vJoy config utility." }

// Shutdown notification (lets plugins show "Core shutting down" instead of "Disconnected")
{ "type": "shutdown" }
```

### Axis + Button Combo

The `Axis + Button` action is handled entirely in the plugin. The plugin registers as both an adjustment and a command, forwarding dial turns as standard axis events and encoder presses as momentary button events. The core receives them as separate `axis` and `button` messages -- it has no knowledge of the combo relationship.

## Action Library

3 actions total, each with Action Editor UI for user configuration. Mode-specific controls are shown/hidden dynamically via `ControlValueChanged` events based on the selected mode.

### vJoy Axis (Adjustment)

For dials/encoders. Mode dropdown selects the axis behavior; additional controls appear based on mode.

| Editor Control | Applies To | Description |
|---|---|---|
| Mode dropdown | All | Hold, Spring, Detent |
| Axis dropdown | All | X, Y, Z, Rx, Ry, Rz, Slider1, Slider2 (vJoy axes 1-8) |
| Invert checkbox | All | Negates diff before sending to core |
| Sensitivity slider | Hold, Spring | Scales diff magnitude |
| Reset position slider | All | 0-100%, default 50%. Encoder press resets axis to this value |
| Decay rate slider | Spring | How quickly axis returns to center (0.0 = instant, 1.0 = never) |
| Step count slider | Detent | Number of discrete positions (e.g. 5 = 0%, 25%, 50%, 75%, 100%) |

Encoder press sends: `{ "type": "reset", "axis": N, "position": P }`

### vJoy Axis + Button (Adjustment)

For dials/encoders where dial controls an axis and pressing the encoder fires a vJoy button.

| Editor Control | Description |
|---|---|
| Axis dropdown | X, Y, Z, Rx, Ry, Rz, Slider1, Slider2 |
| Invert checkbox | Negates diff before sending to core |
| Sensitivity slider | Scales diff magnitude |
| Button dropdown | vJoy button (1-128) fired on encoder press (momentary) |

Dial turn sends axis event (hold mode). Encoder press sends momentary button event.

### vJoy Button (Command)

For buttons/keys. Mode dropdown selects the button behavior; additional controls appear based on mode.

| Editor Control | Applies To | Description |
|---|---|---|
| Mode dropdown | All | Momentary, Toggle, Pulse, Double Press, Rapid Fire, Long/Short, Reset Axis |
| Button dropdown (1-128) | Momentary, Toggle, Pulse, Double, Rapid | Which vJoy button to control |
| Delay slider (ms) | Double Press | Time between the two pulses |
| Rate slider (ms) | Rapid Fire | Interval between repeated pulses |
| Short button dropdown | Long/Short | vJoy button for quick tap |
| Long button dropdown | Long/Short | vJoy button for held press |
| Threshold slider (ms) | Long/Short | Hold duration to distinguish short from long |
| Axis dropdown | Reset Axis | Which axis to reset |
| Reset position slider (0-100%) | Reset Axis | Target value for the reset |

## Core Feeder App (Rust)

### Responsibilities

1. **Named pipe server** -- listens on `\\.\pipe\apricadabra`, creates a new pipe instance for each connected client. This is standard Windows named pipe behavior -- unlike TCP, each connection requires a new `CreateNamedPipe` call.
2. **Axis state manager** -- maintains current value (0.0-1.0) for each of 8 axes, applies mode-specific behavior:
   - Hold: accumulate diffs scaled by sensitivity, clamp to 0.0-1.0
   - Spring: accumulate diffs scaled by sensitivity, decay toward 0.5 using exponential decay (`value = center + (value - center) * decay_factor`) each tick at ~60Hz. `decayRate` (0.0-1.0) maps to the decay factor -- 0.0 is instant snap, 1.0 is no decay
   - Detent: `diff` is the number of steps to advance (positive) or retreat (negative). Axis snaps to the nearest step position. E.g. with 5 steps, positions are 0.0, 0.25, 0.5, 0.75, 1.0
3. **Button state manager** -- tracks on/off state, handles timing logic:
   - Momentary: direct press/release passthrough
   - Toggle: flip state on each press
   - Pulse: brief press/release (~50ms)
   - Double press: two pulses with configurable delay
   - Rapid fire: repeated pulses at configurable rate while held
   - Long/short: start timer on press, fire short button if released before threshold, long button if held past threshold
4. **vJoy FFI bridge** -- loads `vJoyInterface.dll`, calls `AcquireVJD`, `SetAxis`, `SetBtn`, `RelinquishVJD` through the `VirtualJoystick` trait. Maps 0.0-1.0 axis values to vJoy's 0-32767 range.
5. **State broadcaster** -- on every state change, pushes current axis/button values to all connected plugins. Changes are batched per ~60Hz tick -- all changes within one tick are sent in a single `state` message.
6. **Heartbeat** -- pings each client every 2-3 seconds, drops clients that miss 2 consecutive acks
7. **Logging** -- structured logging to `%APPDATA%/Apricadabra/logs/`. Info level by default, debug level enabled via `config.json` or `--debug` flag. Logs rotation (keep last 5 files, max 10MB each).

### VirtualJoystick Trait

```rust
trait VirtualJoystick {
    fn acquire(&mut self, device_id: u8) -> Result<()>;
    fn set_axis(&mut self, axis: Axis, value: f32) -> Result<()>;
    fn set_button(&mut self, button: u8, pressed: bool) -> Result<()>;
    fn release(&mut self) -> Result<()>;
}
```

`button` parameter valid range: 1-128. `value` parameter valid range: 0.0-1.0. Implementations map to backend-specific ranges internally.

Initial implementation: `VJoyBackend` (Windows, vJoyInterface.dll FFI). vJoy provides a C header -- use `bindgen` to generate Rust bindings for compile-time type safety, with dynamic linking to the DLL at runtime.

Future: `UInputBackend` (Linux), `MacHidBackend` (macOS DriverKit).

### Spring Decay Loop

A single timer at ~60Hz checks all spring-mode axes and applies exponential decay toward center. Decay model: `value = center + (value - center) * decay_factor` per tick. This is the only time-based behavior besides button timing -- everything else is event-driven.

### Configuration

A `config.json` in `%APPDATA%/Apricadabra/`:
- vJoy device ID (default: 1)
- Global default sensitivity (overridden by per-action settings from Action Editor)
- Global default decay rate for spring mode
- Log level (info/debug)

Per-action settings (sensitivity, decay rate, step count, button timing) arrive in the IPC messages from the plugin's Action Editor configuration.

## Loupedeck Plugin (C# / .NET 8)

### Structure

- `ApricadabraPlugin : Plugin` -- entry point
- `ApricadabraApplication : ClientApplication` -- app registration
- `AxisAdjustment : ActionEditorAdjustment` -- vJoy Axis action (mode/axis/sensitivity/etc via Action Editor)
- `AxisButtonAdjustment : ActionEditorAdjustment` -- vJoy Axis + Button combo action
- `ButtonCommand : ActionEditorCommand` -- vJoy Button action (mode/button/timing via Action Editor)
- `CoreConnection` -- named pipe client, auto-launch, reconnection, hello/welcome handshake

### Lifecycle

1. Plugin loads in Logi Plugin Service
2. `CoreConnection` attempts to connect to `\\.\pipe\apricadabra`
3. If core not running -> spawns `apricadabra-core.exe` -> retries with backoff (100ms, 200ms, 400ms... up to 5s)
4. On connect -> sends `hello` message, receives `welcome` with current state
5. On dial turn -> reads Action Editor params (axis, sensitivity, mode) -> sends JSON to core
6. On button press -> reads params (button number, mode, timing) -> sends JSON to core
7. On state update from core -> updates LCD display via `GetAdjustmentValue()` (e.g. "73%")
8. On error from core -> displays error message on action icons
9. On shutdown message from core -> shows "Core shutting down", does not attempt respawn
10. On disconnect (broken pipe) -> shows "Disconnected" on action icons, attempts respawn + reconnect with backoff

### Display Feedback

Each axis adjustment overrides `GetAdjustmentValue()` to return the current axis value received from the core's state broadcasts. Displayed as a percentage on the dial's LCD area on supported devices.

## Error Handling & Edge Cases

### Connection Lifecycle

- **Core not running**: plugin spawns it, retries connection with exponential backoff (100ms to 5s)
- **Core crashes**: plugin detects broken pipe, shows "Disconnected" on LCD, attempts respawn + reconnect
- **Plugin disconnects**: core cleans up that client, continues running for other connected plugins
- **All plugins disconnect**: core holds axis state for ~30 seconds with gradual exponential decay to center, then resets. If a plugin reconnects during decay, it receives current (mid-decay) state via `welcome` message.
- **LPS crashes and restarts**: plugin reconnects to existing core (if it survived), sends `hello`, receives `welcome` with full state dump, LCD displays are immediately accurate
- **Core shutting down**: core sends `{"type": "shutdown"}` to all plugins before closing pipes. Plugins display "Core shutting down" and do not attempt respawn.

### Heartbeat

- Core pings each client every 2-3 seconds with `{"type": "heartbeat"}`
- Plugin responds with `{"type": "heartbeat_ack"}`
- 2 missed acks -> core considers client dead, cleans up connection

### vJoy Errors

- **vJoy not installed**: core sends `error` message to all plugins, logs error, exits
- **vJoy device not configured**: core sends `error` message reporting which device ID it tried
- **Device acquired by another app**: core retries briefly, then sends `error` message to plugins for display
- **Axis value out of range**: clamp to 0.0-1.0, never pass invalid values to FFI

### Input Conflicts

- Two actions mapped to same axis: last write wins (same behavior as two physical sticks on one axis)
- Rapid fire active when button released: "up" event cancels timer immediately
- Spring decay vs new input: new input resets decay timer

### Disconnect Decay

When all plugins disconnect, instead of snapping axes to center or holding indefinitely:
- All axes gradually decay toward center (0.5) over ~30 seconds using exponential decay
- If a plugin reconnects mid-decay, it receives current axis positions via `welcome` state dump
- Prevents phantom stuck inputs in games while avoiding jarring jumps on brief disconnects

## Project Structure

```
apricadabra/
├── core/                              # Rust binary
│   ├── Cargo.toml
│   └── src/
│       ├── main.rs                    # Entry, pipe server, event loop
│       ├── ipc.rs                     # Named pipe server, JSON protocol, handshake
│       ├── axis.rs                    # Axis state manager (hold/spring/detent)
│       ├── button.rs                  # Button state manager (momentary/toggle/pulse/etc)
│       ├── vjoy.rs                    # VirtualJoystick trait + vJoy FFI backend (bindgen)
│       └── broadcast.rs              # State broadcaster to connected plugins
│
├── loupedeck-plugin/                  # C# .NET 8
│   ├── src/
│   │   ├── ApricadabraPlugin.cs
│   │   ├── ApricadabraApplication.cs
│   │   ├── CoreConnection.cs          # Named pipe client + auto-launch + handshake
│   │   ├── Actions/
│   │   │   ├── AxisAdjustment.cs              # vJoy Axis (Hold/Spring/Detent via mode dropdown)
│   │   │   ├── AxisButtonAdjustment.cs        # vJoy Axis + Button combo
│   │   │   └── ButtonCommand.cs               # vJoy Button (all modes via mode dropdown)
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

Note: The Logi Plugin Service may sandbox plugins. Whether a plugin can spawn an arbitrary `.exe` needs to be verified early in implementation. If not possible, the core would need to run as a Windows service or be launched via a separate tray app / startup entry.

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
- `tokio` -- async runtime with named pipe support
- `serde` / `serde_json` -- JSON serialization
- `bindgen` (build dependency) -- generate FFI bindings from vJoy C header
- `tracing` / `tracing-subscriber` -- structured logging

### Loupedeck Plugin (C#)
- Logi Actions SDK (NuGet or SDK reference)
- `System.IO.Pipes` -- named pipe client (built into .NET)
- `System.Text.Json` -- JSON serialization (built into .NET)
