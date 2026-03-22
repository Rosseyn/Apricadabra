# Apricadabra Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build a Rust core that translates Loupedeck dial/button events into vJoy virtual joystick input, plus a C# Loupedeck plugin that captures device events and forwards them to the core.

**Architecture:** A Rust async binary (tokio) serves a named pipe, receives JSON messages from device plugins, manages axis/button state, and writes to vJoy via FFI. A C# Loupedeck plugin uses the Logi Actions SDK's Action Editor to present 3 configurable actions, forwards events over the pipe, and displays axis state on the device's LCD.

**Tech Stack:** Rust (tokio, serde, tracing, bindgen), C# .NET 8 (Logi Actions SDK, System.IO.Pipes, System.Text.Json), vJoy SDK (C header + DLL)

**Spec:** `docs/superpowers/specs/2026-03-22-apricadabra-design.md`

---

## Development Environment Notes

- **Target platform:** Windows only. vJoy and Loupedeck software are Windows-only.
- **Named pipes:** `\\.\pipe\apricadabra` is Windows-specific. Use `tokio::net::windows::named_pipe`.
- **Unit tests** for pure logic (axis math, button state machines, protocol parsing) can run on any platform.
- **Integration tests** (pipe server, vJoy FFI) require Windows.
- **Loupedeck plugin testing** requires Windows + Loupedeck software + a Loupedeck device.
- **vJoy SDK:** Download from https://github.com/njz3/vJoy/ — need the C header (`vjoyinterface.h`) and DLL (`vJoyInterface.dll`).

---

## File Map

### Core (Rust) — `core/`

| File | Responsibility |
|---|---|
| `Cargo.toml` | Dependencies: tokio, serde, serde_json, tracing, tracing-subscriber, tracing-appender |
| `build.rs` | bindgen setup for vJoy C header |
| `src/main.rs` | Entry point, config loading, tokio runtime, pipe server accept loop, graceful shutdown |
| `src/protocol.rs` | All IPC message types as serde structs/enums, serialization/deserialization |
| `src/axis.rs` | `AxisManager` — maintains 8 axis values, implements hold/spring/detent modes, reset, disconnect decay |
| `src/button.rs` | `ButtonManager` — maintains 128 button states, implements momentary/toggle/pulse/double/rapid/longshort timing |
| `src/vjoy.rs` | `VirtualJoystick` trait, `VJoyBackend` (FFI to vJoyInterface.dll), `MockJoystick` (for tests) |
| `src/server.rs` | Named pipe server — accept connections, per-client read/write tasks, hello/welcome handshake, heartbeat, state broadcasting (merged from spec's `broadcast.rs` since broadcasting is tightly coupled with client management) |
| `src/config.rs` | Load `config.json` from `%APPDATA%/Apricadabra/`, CLI args (`--debug`), defaults |
| `src/lib.rs` | Re-exports for test access |
| `tests/protocol_test.rs` | Protocol serialization round-trip tests |
| `tests/axis_test.rs` | Axis state manager unit tests |
| `tests/button_test.rs` | Button state manager unit tests |
| `tests/integration_test.rs` | End-to-end pipe server test with mock joystick (Windows only) |

### Loupedeck Plugin (C#) — `loupedeck-plugin/`

| File | Responsibility |
|---|---|
| `src/ApricadabraPlugin.cs` | Plugin entry point, inherits `Plugin` |
| `src/ApricadabraApplication.cs` | ClientApplication, registers the plugin with LPS |
| `src/CoreConnection.cs` | Named pipe client, auto-launch core exe, reconnection with backoff, hello/welcome handshake, message dispatch |
| `src/Actions/AxisAdjustment.cs` | ActionEditorAdjustment — mode dropdown (Hold/Spring/Detent), axis, sensitivity, reset position, decay rate, step count |
| `src/Actions/AxisButtonAdjustment.cs` | ActionEditorAdjustment — axis + button combo |
| `src/Actions/ButtonCommand.cs` | ActionEditorCommand — mode dropdown (Momentary/Toggle/Pulse/Double/Rapid/LongShort/ResetAxis) |
| `src/Display/StateDisplay.cs` | Shared state cache updated from core broadcasts, queried by actions for LCD display |
| `metadata/LoupedeckPackage.yaml` | Plugin manifest — name, version, author, supported devices |

---

## Task 1: Core — Project scaffolding

**Files:**
- Create: `core/Cargo.toml`
- Create: `core/src/main.rs`
- Create: `core/src/lib.rs`

- [ ] **Step 1: Create Cargo.toml with dependencies**

```toml
[package]
name = "apricadabra-core"
version = "0.1.0"
edition = "2021"

[dependencies]
tokio = { version = "1", features = ["full"] }
serde = { version = "1", features = ["derive"] }
serde_json = "1"
tracing = "0.1"
tracing-subscriber = { version = "0.3", features = ["env-filter", "json"] }
tracing-appender = "0.2"

[dev-dependencies]
tokio-test = "0.4"
```

- [ ] **Step 2: Create minimal main.rs**

```rust
use tracing::info;

#[tokio::main]
async fn main() {
    tracing_subscriber::fmt::init();
    info!("Apricadabra Core starting");
}
```

- [ ] **Step 3: Create lib.rs**

```rust
pub mod protocol;
pub mod axis;
pub mod button;
pub mod vjoy;
pub mod server;
pub mod config;
```

This will not compile yet (modules don't exist). That's fine — we'll add them in subsequent tasks.

- [ ] **Step 4: Verify Cargo.toml is valid**

Run: `cd core && cargo check 2>&1 | head -5`

Expected: Errors about missing modules (not dependency errors). If dependencies fail to resolve, fix Cargo.toml.

- [ ] **Step 5: Commit**

```bash
git add core/Cargo.toml core/src/main.rs core/src/lib.rs
git commit -m "feat(core): scaffold Rust project with tokio dependencies"
```

---

## Task 2: Core — Protocol types

**Files:**
- Create: `core/src/protocol.rs`
- Create: `core/tests/protocol_test.rs`

- [ ] **Step 1: Write protocol round-trip tests**

```rust
// core/tests/protocol_test.rs
use apricadabra_core::protocol::*;

#[test]
fn test_parse_hello() {
    let json = r#"{"type":"hello","version":1,"name":"loupedeck"}"#;
    let msg: ClientMessage = serde_json::from_str(json).unwrap();
    assert!(matches!(msg, ClientMessage::Hello { version: 1, name } if name == "loupedeck"));
}

#[test]
fn test_parse_axis_hold() {
    let json = r#"{"type":"axis","axis":1,"mode":"hold","diff":3,"sensitivity":0.5}"#;
    let msg: ClientMessage = serde_json::from_str(json).unwrap();
    match msg {
        ClientMessage::Axis { axis, mode: AxisMode::Hold, diff, sensitivity, .. } => {
            assert_eq!(axis, 1);
            assert_eq!(diff, 3);
            assert!((sensitivity.unwrap() - 0.5).abs() < f32::EPSILON);
        }
        _ => panic!("Expected Axis Hold"),
    }
}

#[test]
fn test_parse_axis_spring() {
    let json = r#"{"type":"axis","axis":2,"mode":"spring","diff":-1,"sensitivity":0.5,"decayRate":0.3}"#;
    let msg: ClientMessage = serde_json::from_str(json).unwrap();
    match msg {
        ClientMessage::Axis { axis, mode: AxisMode::Spring, diff, decay_rate, .. } => {
            assert_eq!(axis, 2);
            assert_eq!(diff, -1);
            assert!((decay_rate.unwrap() - 0.3).abs() < f32::EPSILON);
        }
        _ => panic!("Expected Axis Spring"),
    }
}

#[test]
fn test_parse_axis_detent() {
    let json = r#"{"type":"axis","axis":3,"mode":"detent","diff":1,"steps":5}"#;
    let msg: ClientMessage = serde_json::from_str(json).unwrap();
    match msg {
        ClientMessage::Axis { axis, mode: AxisMode::Detent, diff, steps, .. } => {
            assert_eq!(axis, 3);
            assert_eq!(diff, 1);
            assert_eq!(steps.unwrap(), 5);
        }
        _ => panic!("Expected Axis Detent"),
    }
}

#[test]
fn test_parse_button_momentary() {
    let json = r#"{"type":"button","button":1,"mode":"momentary","state":"down"}"#;
    let msg: ClientMessage = serde_json::from_str(json).unwrap();
    match msg {
        ClientMessage::Button { button, mode: ButtonMode::Momentary, state, .. } => {
            assert_eq!(button, 1);
            assert_eq!(state.unwrap(), ButtonState::Down);
        }
        _ => panic!("Expected Button Momentary"),
    }
}

#[test]
fn test_parse_button_longshort() {
    let json = r#"{"type":"button","button":6,"mode":"longshort","state":"down","shortButton":6,"longButton":7,"threshold":500}"#;
    let msg: ClientMessage = serde_json::from_str(json).unwrap();
    match msg {
        ClientMessage::Button { mode: ButtonMode::LongShort, short_button, long_button, threshold, .. } => {
            assert_eq!(short_button.unwrap(), 6);
            assert_eq!(long_button.unwrap(), 7);
            assert_eq!(threshold.unwrap(), 500);
        }
        _ => panic!("Expected Button LongShort"),
    }
}

#[test]
fn test_parse_reset() {
    let json = r#"{"type":"reset","axis":1,"position":0.5}"#;
    let msg: ClientMessage = serde_json::from_str(json).unwrap();
    match msg {
        ClientMessage::Reset { axis, position } => {
            assert_eq!(axis, 1);
            assert!((position - 0.5).abs() < f32::EPSILON);
        }
        _ => panic!("Expected Reset"),
    }
}

#[test]
fn test_parse_heartbeat_ack() {
    let json = r#"{"type":"heartbeat_ack"}"#;
    let msg: ClientMessage = serde_json::from_str(json).unwrap();
    assert!(matches!(msg, ClientMessage::HeartbeatAck));
}

#[test]
fn test_serialize_welcome() {
    let msg = ServerMessage::Welcome {
        version: 1,
        axes: vec![(1, 0.5), (2, 0.73)].into_iter().collect(),
        buttons: vec![(1, true)].into_iter().collect(),
    };
    let json = serde_json::to_string(&msg).unwrap();
    assert!(json.contains("\"type\":\"welcome\""));
    assert!(json.contains("\"version\":1"));
}

#[test]
fn test_serialize_state() {
    let msg = ServerMessage::State {
        axes: vec![(1, 0.73)].into_iter().collect(),
        buttons: vec![(1, true)].into_iter().collect(),
    };
    let json = serde_json::to_string(&msg).unwrap();
    assert!(json.contains("\"type\":\"state\""));
}

#[test]
fn test_serialize_heartbeat() {
    let msg = ServerMessage::Heartbeat;
    let json = serde_json::to_string(&msg).unwrap();
    assert_eq!(json, r#"{"type":"heartbeat"}"#);
}

#[test]
fn test_serialize_error() {
    let msg = ServerMessage::Error {
        code: "vjoy_not_installed".to_string(),
        message: "vJoy driver not found.".to_string(),
    };
    let json = serde_json::to_string(&msg).unwrap();
    assert!(json.contains("\"type\":\"error\""));
    assert!(json.contains("vjoy_not_installed"));
}

#[test]
fn test_serialize_shutdown() {
    let msg = ServerMessage::Shutdown;
    let json = serde_json::to_string(&msg).unwrap();
    assert_eq!(json, r#"{"type":"shutdown"}"#);
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `cd core && cargo test --test protocol_test 2>&1 | tail -5`

Expected: Compilation error — `protocol` module doesn't exist yet.

- [ ] **Step 3: Implement protocol.rs**

```rust
// core/src/protocol.rs
use serde::{Deserialize, Serialize};
use std::collections::HashMap;

/// Messages sent from plugins to the core.
#[derive(Debug, Deserialize, PartialEq)]
#[serde(tag = "type", rename_all = "snake_case")]
pub enum ClientMessage {
    Hello {
        version: u32,
        name: String,
    },
    Axis {
        axis: u8,
        mode: AxisMode,
        diff: i32,
        #[serde(default)]
        sensitivity: Option<f32>,
        #[serde(default, rename = "decayRate")]
        decay_rate: Option<f32>,
        #[serde(default)]
        steps: Option<u32>,
    },
    Button {
        button: u8,
        mode: ButtonMode,
        #[serde(default)]
        state: Option<ButtonState>,
        #[serde(default)]
        delay: Option<u64>,
        #[serde(default)]
        rate: Option<u64>,
        #[serde(default, rename = "shortButton")]
        short_button: Option<u8>,
        #[serde(default, rename = "longButton")]
        long_button: Option<u8>,
        #[serde(default)]
        threshold: Option<u64>,
    },
    Reset {
        axis: u8,
        position: f32,
    },
    HeartbeatAck,
}

#[derive(Debug, Deserialize, PartialEq)]
#[serde(rename_all = "snake_case")]
pub enum AxisMode {
    Hold,
    Spring,
    Detent,
}

#[derive(Debug, Deserialize, PartialEq)]
#[serde(rename_all = "snake_case")]
pub enum ButtonMode {
    Momentary,
    Toggle,
    Pulse,
    Double,
    Rapid,
    #[serde(rename = "longshort")]
    LongShort,
}

#[derive(Debug, Deserialize, PartialEq)]
#[serde(rename_all = "snake_case")]
pub enum ButtonState {
    Down,
    Up,
}

/// Messages sent from the core to plugins.
#[derive(Debug, Serialize, PartialEq)]
#[serde(tag = "type", rename_all = "snake_case")]
pub enum ServerMessage {
    Welcome {
        version: u32,
        axes: HashMap<u8, f32>,
        buttons: HashMap<u8, bool>,
    },
    State {
        axes: HashMap<u8, f32>,
        buttons: HashMap<u8, bool>,
    },
    Heartbeat,
    Error {
        code: String,
        message: String,
    },
    Shutdown,
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `cd core && cargo test --test protocol_test 2>&1 | tail -5`

Expected: All tests pass.

- [ ] **Step 5: Commit**

```bash
git add core/src/protocol.rs core/tests/protocol_test.rs
git commit -m "feat(core): add IPC protocol types with serde serialization"
```

---

## Task 3: Core — VirtualJoystick trait and mock

**Files:**
- Create: `core/src/vjoy.rs`

- [ ] **Step 1: Implement trait and mock**

```rust
// core/src/vjoy.rs
use std::collections::HashMap;
use std::sync::{Arc, Mutex};

/// Axis identifiers matching vJoy axes 1-8.
#[derive(Debug, Clone, Copy, PartialEq, Eq, Hash)]
pub enum Axis {
    X = 1,
    Y = 2,
    Z = 3,
    Rx = 4,
    Ry = 5,
    Rz = 6,
    Slider1 = 7,
    Slider2 = 8,
}

impl Axis {
    pub fn from_id(id: u8) -> Option<Axis> {
        match id {
            1 => Some(Axis::X),
            2 => Some(Axis::Y),
            3 => Some(Axis::Z),
            4 => Some(Axis::Rx),
            5 => Some(Axis::Ry),
            6 => Some(Axis::Rz),
            7 => Some(Axis::Slider1),
            8 => Some(Axis::Slider2),
            _ => None,
        }
    }
}

pub trait VirtualJoystick: Send {
    fn acquire(&mut self, device_id: u8) -> anyhow::Result<()>;
    fn set_axis(&mut self, axis: Axis, value: f32) -> anyhow::Result<()>;
    fn set_button(&mut self, button: u8, pressed: bool) -> anyhow::Result<()>;
    fn release(&mut self) -> anyhow::Result<()>;
}

/// Mock implementation for testing. Records all calls for assertion.
#[derive(Debug, Clone)]
pub struct MockJoystick {
    pub acquired: bool,
    pub device_id: Option<u8>,
    pub axes: HashMap<Axis, f32>,
    pub buttons: HashMap<u8, bool>,
}

impl MockJoystick {
    pub fn new() -> Self {
        Self {
            acquired: false,
            device_id: None,
            axes: HashMap::new(),
            buttons: HashMap::new(),
        }
    }
}

impl VirtualJoystick for MockJoystick {
    fn acquire(&mut self, device_id: u8) -> anyhow::Result<()> {
        self.acquired = true;
        self.device_id = Some(device_id);
        Ok(())
    }

    fn set_axis(&mut self, axis: Axis, value: f32) -> anyhow::Result<()> {
        let clamped = value.clamp(0.0, 1.0);
        self.axes.insert(axis, clamped);
        Ok(())
    }

    fn set_button(&mut self, button: u8, pressed: bool) -> anyhow::Result<()> {
        self.buttons.insert(button, pressed);
        Ok(())
    }

    fn release(&mut self) -> anyhow::Result<()> {
        self.acquired = false;
        Ok(())
    }
}
```

Note: Add `anyhow = "1"` to `[dependencies]` in Cargo.toml.

- [ ] **Step 2: Verify it compiles**

Run: `cd core && cargo check 2>&1 | tail -5`

Expected: May still have errors for other missing modules. Comment out missing modules in lib.rs temporarily if needed, verify vjoy.rs compiles.

- [ ] **Step 3: Commit**

```bash
git add core/src/vjoy.rs core/Cargo.toml
git commit -m "feat(core): add VirtualJoystick trait and MockJoystick for testing"
```

---

## Task 4: Core — Axis state manager

**Files:**
- Create: `core/src/axis.rs`
- Create: `core/tests/axis_test.rs`

- [ ] **Step 1: Write axis manager tests**

```rust
// core/tests/axis_test.rs
use apricadabra_core::axis::AxisManager;

#[test]
fn test_initial_state_all_centered() {
    let mgr = AxisManager::new();
    for id in 1..=8 {
        assert!((mgr.get(id) - 0.5).abs() < f32::EPSILON, "Axis {id} should start at 0.5");
    }
}

#[test]
fn test_hold_positive_diff() {
    let mut mgr = AxisManager::new();
    mgr.apply_hold(1, 10, 0.01); // diff=10, sensitivity=0.01 -> +0.1
    assert!((mgr.get(1) - 0.6).abs() < 0.001);
}

#[test]
fn test_hold_negative_diff() {
    let mut mgr = AxisManager::new();
    mgr.apply_hold(1, -5, 0.02); // diff=-5, sensitivity=0.02 -> -0.1
    assert!((mgr.get(1) - 0.4).abs() < 0.001);
}

#[test]
fn test_hold_clamps_high() {
    let mut mgr = AxisManager::new();
    mgr.apply_hold(1, 1000, 0.1); // Would go way past 1.0
    assert!((mgr.get(1) - 1.0).abs() < f32::EPSILON);
}

#[test]
fn test_hold_clamps_low() {
    let mut mgr = AxisManager::new();
    mgr.apply_hold(1, -1000, 0.1); // Would go way below 0.0
    assert!((mgr.get(1) - 0.0).abs() < f32::EPSILON);
}

#[test]
fn test_reset_to_position() {
    let mut mgr = AxisManager::new();
    mgr.apply_hold(1, 100, 0.01); // Move to 1.0
    mgr.reset(1, 0.25);
    assert!((mgr.get(1) - 0.25).abs() < f32::EPSILON);
}

#[test]
fn test_detent_5_steps_forward() {
    let mut mgr = AxisManager::new();
    // Start at 0.5. 5 steps = positions 0.0, 0.25, 0.5, 0.75, 1.0
    mgr.apply_detent(1, 1, 5); // diff=+1 -> next step = 0.75
    assert!((mgr.get(1) - 0.75).abs() < 0.001);
}

#[test]
fn test_detent_5_steps_backward() {
    let mut mgr = AxisManager::new();
    mgr.apply_detent(1, -1, 5); // diff=-1 -> prev step = 0.25
    assert!((mgr.get(1) - 0.25).abs() < 0.001);
}

#[test]
fn test_detent_clamps_at_max() {
    let mut mgr = AxisManager::new();
    mgr.apply_detent(1, 10, 5); // Try to go way past max
    assert!((mgr.get(1) - 1.0).abs() < 0.001);
}

#[test]
fn test_detent_clamps_at_min() {
    let mut mgr = AxisManager::new();
    mgr.apply_detent(1, -10, 5); // Try to go way below min
    assert!((mgr.get(1) - 0.0).abs() < 0.001);
}

#[test]
fn test_spring_moves_then_decays() {
    let mut mgr = AxisManager::new();
    mgr.apply_spring(1, 10, 0.01, 0.9); // Move to ~0.6, decay_factor=0.9
    let after_move = mgr.get(1);
    assert!(after_move > 0.55);

    // Tick decay 100 times — should approach 0.5
    for _ in 0..100 {
        mgr.tick_spring_decay();
    }
    let after_decay = mgr.get(1);
    assert!((after_decay - 0.5).abs() < 0.01, "Should have decayed close to center, got {after_decay}");
}

#[test]
fn test_spring_new_input_resets_decay() {
    let mut mgr = AxisManager::new();
    mgr.apply_spring(1, 10, 0.01, 0.9);
    // Decay a bit
    for _ in 0..10 {
        mgr.tick_spring_decay();
    }
    let mid = mgr.get(1);
    // New input pushes further
    mgr.apply_spring(1, 10, 0.01, 0.9);
    assert!(mgr.get(1) > mid);
}

#[test]
fn test_decay_all_on_disconnect() {
    let mut mgr = AxisManager::new();
    mgr.apply_hold(1, 100, 0.01); // Axis 1 at 1.0
    mgr.apply_hold(2, -100, 0.01); // Axis 2 at 0.0
    mgr.start_disconnect_decay();

    // Tick many times
    for _ in 0..1000 {
        mgr.tick_disconnect_decay();
    }

    assert!((mgr.get(1) - 0.5).abs() < 0.01, "Axis 1 should decay to center");
    assert!((mgr.get(2) - 0.5).abs() < 0.01, "Axis 2 should decay to center");
}

#[test]
fn test_get_invalid_axis_returns_default() {
    let mgr = AxisManager::new();
    assert!((mgr.get(0) - 0.5).abs() < f32::EPSILON);
    assert!((mgr.get(9) - 0.5).abs() < f32::EPSILON);
}

#[test]
fn test_changed_axes_returns_only_changed() {
    let mut mgr = AxisManager::new();
    mgr.apply_hold(1, 10, 0.01);
    let changed = mgr.take_changed();
    assert!(changed.contains_key(&1));
    assert!(!changed.contains_key(&2));

    // Second call returns empty
    let changed2 = mgr.take_changed();
    assert!(changed2.is_empty());
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `cd core && cargo test --test axis_test 2>&1 | tail -5`

Expected: Compilation error — `axis` module doesn't exist.

- [ ] **Step 3: Implement axis.rs**

```rust
// core/src/axis.rs
use std::collections::{HashMap, HashSet};

const NUM_AXES: usize = 8;
const CENTER: f32 = 0.5;
const DISCONNECT_DECAY_FACTOR: f32 = 0.995; // ~30 seconds to center at 60Hz

pub struct AxisManager {
    values: [f32; NUM_AXES],
    spring_decay_factors: [Option<f32>; NUM_AXES],
    disconnect_decaying: bool,
    changed: HashSet<u8>,
}

impl AxisManager {
    pub fn new() -> Self {
        Self {
            values: [CENTER; NUM_AXES],
            spring_decay_factors: [None; NUM_AXES],
            disconnect_decaying: false,
            changed: HashSet::new(),
        }
    }

    /// Get current axis value. Returns CENTER for invalid axis IDs.
    pub fn get(&self, axis_id: u8) -> f32 {
        self.idx(axis_id)
            .map(|i| self.values[i])
            .unwrap_or(CENTER)
    }

    /// Get all current axis values as a map.
    pub fn get_all(&self) -> HashMap<u8, f32> {
        (1..=NUM_AXES as u8)
            .map(|id| (id, self.values[id as usize - 1]))
            .collect()
    }

    /// Hold mode: accumulate diff * sensitivity, clamp to 0.0-1.0.
    pub fn apply_hold(&mut self, axis_id: u8, diff: i32, sensitivity: f32) {
        if let Some(i) = self.idx(axis_id) {
            self.values[i] = (self.values[i] + diff as f32 * sensitivity).clamp(0.0, 1.0);
            self.changed.insert(axis_id);
            self.disconnect_decaying = false;
        }
    }

    /// Spring mode: accumulate diff, register decay factor for this axis.
    pub fn apply_spring(&mut self, axis_id: u8, diff: i32, sensitivity: f32, decay_rate: f32) {
        if let Some(i) = self.idx(axis_id) {
            self.values[i] = (self.values[i] + diff as f32 * sensitivity).clamp(0.0, 1.0);
            self.spring_decay_factors[i] = Some(decay_rate);
            self.changed.insert(axis_id);
            self.disconnect_decaying = false;
        }
    }

    /// Detent mode: move by `diff` steps within `steps` total positions.
    pub fn apply_detent(&mut self, axis_id: u8, diff: i32, steps: u32) {
        if let Some(i) = self.idx(axis_id) {
            if steps < 2 {
                return;
            }
            let step_size = 1.0 / (steps - 1) as f32;
            let current_step = (self.values[i] / step_size).round() as i32;
            let new_step = (current_step + diff).clamp(0, (steps - 1) as i32);
            self.values[i] = (new_step as f32 * step_size).clamp(0.0, 1.0);
            self.changed.insert(axis_id);
            self.disconnect_decaying = false;
        }
    }

    /// Reset axis to a specific position.
    pub fn reset(&mut self, axis_id: u8, position: f32) {
        if let Some(i) = self.idx(axis_id) {
            self.values[i] = position.clamp(0.0, 1.0);
            self.spring_decay_factors[i] = None;
            self.changed.insert(axis_id);
        }
    }

    /// Tick spring decay for all spring-mode axes. Call at ~60Hz.
    pub fn tick_spring_decay(&mut self) {
        for i in 0..NUM_AXES {
            if let Some(factor) = self.spring_decay_factors[i] {
                let old = self.values[i];
                self.values[i] = CENTER + (self.values[i] - CENTER) * factor;
                if (self.values[i] - old).abs() > 0.0001 {
                    self.changed.insert((i + 1) as u8);
                }
                // Stop tracking when close enough to center
                if (self.values[i] - CENTER).abs() < 0.001 {
                    self.values[i] = CENTER;
                    self.spring_decay_factors[i] = None;
                }
            }
        }
    }

    /// Begin gradual decay of all axes toward center (on disconnect).
    pub fn start_disconnect_decay(&mut self) {
        self.disconnect_decaying = true;
    }

    /// Tick disconnect decay for all axes. Call at ~60Hz when decaying.
    pub fn tick_disconnect_decay(&mut self) {
        if !self.disconnect_decaying {
            return;
        }
        let mut any_moving = false;
        for i in 0..NUM_AXES {
            let old = self.values[i];
            self.values[i] = CENTER + (self.values[i] - CENTER) * DISCONNECT_DECAY_FACTOR;
            if (self.values[i] - old).abs() > 0.0001 {
                self.changed.insert((i + 1) as u8);
                any_moving = true;
            }
            if (self.values[i] - CENTER).abs() < 0.001 {
                self.values[i] = CENTER;
            }
        }
        if !any_moving {
            self.disconnect_decaying = false;
        }
    }

    /// Returns changed axis IDs and their values since last call, then clears.
    pub fn take_changed(&mut self) -> HashMap<u8, f32> {
        let result: HashMap<u8, f32> = self
            .changed
            .iter()
            .filter_map(|&id| self.idx(id).map(|i| (id, self.values[i])))
            .collect();
        self.changed.clear();
        result
    }

    fn idx(&self, axis_id: u8) -> Option<usize> {
        if axis_id >= 1 && axis_id <= NUM_AXES as u8 {
            Some(axis_id as usize - 1)
        } else {
            None
        }
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `cd core && cargo test --test axis_test 2>&1 | tail -10`

Expected: All tests pass.

- [ ] **Step 5: Commit**

```bash
git add core/src/axis.rs core/tests/axis_test.rs
git commit -m "feat(core): add AxisManager with hold/spring/detent modes and disconnect decay"
```

---

## Task 5: Core — Button state manager

**Files:**
- Create: `core/src/button.rs`
- Create: `core/tests/button_test.rs`

- [ ] **Step 1: Write button manager tests**

```rust
// core/tests/button_test.rs
use apricadabra_core::button::{ButtonManager, ButtonEvent};
use std::time::Duration;
use tokio::time;

#[test]
fn test_momentary_down_up() {
    let mut mgr = ButtonManager::new();
    mgr.momentary_down(1);
    assert_eq!(mgr.get(1), true);
    mgr.momentary_up(1);
    assert_eq!(mgr.get(1), false);
}

#[test]
fn test_toggle() {
    let mut mgr = ButtonManager::new();
    assert_eq!(mgr.get(5), false);
    mgr.toggle(5);
    assert_eq!(mgr.get(5), true);
    mgr.toggle(5);
    assert_eq!(mgr.get(5), false);
}

#[tokio::test]
async fn test_pulse_fires_and_releases() {
    let mut mgr = ButtonManager::new();
    let events = mgr.pulse(3);
    // Should get a press event immediately
    assert_eq!(mgr.get(3), true);
    // After processing pending releases
    time::sleep(Duration::from_millis(60)).await;
    mgr.process_pending();
    assert_eq!(mgr.get(3), false);
}

#[tokio::test]
async fn test_double_press() {
    let mut mgr = ButtonManager::new();
    mgr.double_press(4, 50);
    // First pulse fires
    assert_eq!(mgr.get(4), true);
    time::sleep(Duration::from_millis(60)).await;
    mgr.process_pending();
    // Should release, then press again after delay
    time::sleep(Duration::from_millis(60)).await;
    mgr.process_pending();
    // Eventually both pulses complete and button is released
    time::sleep(Duration::from_millis(120)).await;
    mgr.process_pending();
    assert_eq!(mgr.get(4), false);
}

#[test]
fn test_rapid_fire_start_stop() {
    let mut mgr = ButtonManager::new();
    mgr.rapid_start(7, 100);
    assert_eq!(mgr.get(7), true);
    mgr.rapid_stop(7);
    mgr.process_pending();
    assert_eq!(mgr.get(7), false);
}

#[tokio::test]
async fn test_longshort_short_press() {
    let mut mgr = ButtonManager::new();
    mgr.longshort_down(6, 7, 500);
    time::sleep(Duration::from_millis(100)).await; // Well under 500ms threshold
    let fired = mgr.longshort_up(6, 7, 500);
    // Should fire the short button (6)
    assert_eq!(fired, Some(6));
}

#[tokio::test]
async fn test_longshort_long_press() {
    let mut mgr = ButtonManager::new();
    mgr.longshort_down(6, 7, 200);
    time::sleep(Duration::from_millis(300)).await; // Over 200ms threshold
    let fired = mgr.longshort_up(6, 7, 200);
    // Should fire the long button (7)
    assert_eq!(fired, Some(7));
}

#[test]
fn test_changed_buttons() {
    let mut mgr = ButtonManager::new();
    mgr.momentary_down(1);
    let changed = mgr.take_changed();
    assert_eq!(changed.get(&1), Some(&true));
    assert!(mgr.take_changed().is_empty());
}

#[test]
fn test_get_invalid_button_returns_false() {
    let mgr = ButtonManager::new();
    assert_eq!(mgr.get(0), false);
    assert_eq!(mgr.get(129), false);
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `cd core && cargo test --test button_test 2>&1 | tail -5`

Expected: Compilation error — `button` module doesn't exist.

- [ ] **Step 3: Implement button.rs**

```rust
// core/src/button.rs
use std::collections::{HashMap, HashSet};
use std::time::Instant;

const MAX_BUTTONS: usize = 128;
const PULSE_DURATION_MS: u64 = 50;

/// Scheduled future button state change.
struct PendingAction {
    button: u8,
    pressed: bool,
    at: Instant,
}

pub struct ButtonManager {
    states: [bool; MAX_BUTTONS],
    changed: HashSet<u8>,
    pending: Vec<PendingAction>,
    rapid_active: HashSet<u8>,
    longshort_start: HashMap<u8, Instant>, // short_button -> press start time
}

/// Which button was actually fired by a longshort release.
pub enum ButtonEvent {
    Short(u8),
    Long(u8),
}

impl ButtonManager {
    pub fn new() -> Self {
        Self {
            states: [false; MAX_BUTTONS],
            changed: HashSet::new(),
            pending: Vec::new(),
            rapid_active: HashSet::new(),
            longshort_start: HashMap::new(),
        }
    }

    pub fn get(&self, button: u8) -> bool {
        self.idx(button).map(|i| self.states[i]).unwrap_or(false)
    }

    pub fn get_all(&self) -> HashMap<u8, bool> {
        (1..=MAX_BUTTONS as u8)
            .filter(|&id| self.states[id as usize - 1])
            .map(|id| (id, true))
            .collect()
    }

    pub fn momentary_down(&mut self, button: u8) {
        self.set(button, true);
    }

    pub fn momentary_up(&mut self, button: u8) {
        self.set(button, false);
    }

    pub fn toggle(&mut self, button: u8) {
        if let Some(i) = self.idx(button) {
            self.states[i] = !self.states[i];
            self.changed.insert(button);
        }
    }

    pub fn pulse(&mut self, button: u8) {
        self.set(button, true);
        self.pending.push(PendingAction {
            button,
            pressed: false,
            at: Instant::now() + std::time::Duration::from_millis(PULSE_DURATION_MS),
        });
    }

    pub fn double_press(&mut self, button: u8, delay_ms: u64) {
        let now = Instant::now();
        let pulse = std::time::Duration::from_millis(PULSE_DURATION_MS);
        let delay = std::time::Duration::from_millis(delay_ms);

        // First pulse: on now, off after PULSE_DURATION
        self.set(button, true);
        self.pending.push(PendingAction {
            button,
            pressed: false,
            at: now + pulse,
        });
        // Second pulse: on after pulse+delay, off after pulse+delay+pulse
        self.pending.push(PendingAction {
            button,
            pressed: true,
            at: now + pulse + delay,
        });
        self.pending.push(PendingAction {
            button,
            pressed: false,
            at: now + pulse + delay + pulse,
        });
    }

    pub fn rapid_start(&mut self, button: u8, _rate_ms: u64) {
        self.set(button, true);
        self.rapid_active.insert(button);
        // Rapid fire toggling is handled by process_pending with a timer.
        // For simplicity, the caller (server) manages the rapid fire interval timer
        // and calls rapid_tick() at the configured rate.
    }

    pub fn rapid_stop(&mut self, button: u8) {
        self.rapid_active.remove(&button);
        self.set(button, false);
    }

    /// Called by the rapid fire timer. Toggles the button state.
    pub fn rapid_tick(&mut self, button: u8) {
        if self.rapid_active.contains(&button) {
            if let Some(i) = self.idx(button) {
                self.states[i] = !self.states[i];
                self.changed.insert(button);
            }
        }
    }

    pub fn longshort_down(&mut self, short_button: u8, _long_button: u8, _threshold_ms: u64) {
        self.longshort_start.insert(short_button, Instant::now());
    }

    /// Returns which button should fire: short_button or long_button.
    pub fn longshort_up(&mut self, short_button: u8, long_button: u8, threshold_ms: u64) -> Option<u8> {
        if let Some(start) = self.longshort_start.remove(&short_button) {
            let held_ms = start.elapsed().as_millis() as u64;
            let fire_button = if held_ms >= threshold_ms {
                long_button
            } else {
                short_button
            };
            // Fire a pulse on the chosen button
            self.pulse(fire_button);
            Some(fire_button)
        } else {
            None
        }
    }

    /// Process pending scheduled actions. Call frequently (~60Hz or faster).
    pub fn process_pending(&mut self) {
        let now = Instant::now();
        let mut i = 0;
        while i < self.pending.len() {
            if now >= self.pending[i].at {
                let action = self.pending.remove(i);
                self.set(action.button, action.pressed);
            } else {
                i += 1;
            }
        }
    }

    /// Returns changed button IDs and their states since last call, then clears.
    pub fn take_changed(&mut self) -> HashMap<u8, bool> {
        let result: HashMap<u8, bool> = self
            .changed
            .iter()
            .filter_map(|&id| self.idx(id).map(|i| (id, self.states[i])))
            .collect();
        self.changed.clear();
        result
    }

    fn set(&mut self, button: u8, pressed: bool) {
        if let Some(i) = self.idx(button) {
            self.states[i] = pressed;
            self.changed.insert(button);
        }
    }

    fn idx(&self, button: u8) -> Option<usize> {
        if button >= 1 && button <= MAX_BUTTONS as u8 {
            Some(button as usize - 1)
        } else {
            None
        }
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `cd core && cargo test --test button_test 2>&1 | tail -10`

Expected: All tests pass. Some timing-sensitive tests may need tolerance adjustments.

- [ ] **Step 5: Commit**

```bash
git add core/src/button.rs core/tests/button_test.rs
git commit -m "feat(core): add ButtonManager with momentary/toggle/pulse/double/rapid/longshort modes"
```

---

## Task 6: Core — Configuration

**Files:**
- Create: `core/src/config.rs`

- [ ] **Step 1: Implement config loading**

```rust
// core/src/config.rs
use serde::Deserialize;
use std::path::PathBuf;
use tracing::info;

#[derive(Debug, Deserialize)]
pub struct Config {
    #[serde(default = "default_device_id")]
    pub vjoy_device_id: u8,
    #[serde(default = "default_sensitivity")]
    pub default_sensitivity: f32,
    #[serde(default = "default_decay_rate")]
    pub default_decay_rate: f32,
    #[serde(default = "default_log_level")]
    pub log_level: String,
    #[serde(default = "default_pipe_name")]
    pub pipe_name: String,
}

fn default_device_id() -> u8 { 1 }
fn default_sensitivity() -> f32 { 0.02 }
fn default_decay_rate() -> f32 { 0.95 }
fn default_log_level() -> String { "info".to_string() }
fn default_pipe_name() -> String { r"\\.\pipe\apricadabra".to_string() }

impl Default for Config {
    fn default() -> Self {
        Self {
            vjoy_device_id: default_device_id(),
            default_sensitivity: default_sensitivity(),
            default_decay_rate: default_decay_rate(),
            log_level: default_log_level(),
            pipe_name: default_pipe_name(),
        }
    }
}

impl Config {
    pub fn load() -> Self {
        let path = Self::config_path();
        match std::fs::read_to_string(&path) {
            Ok(contents) => {
                match serde_json::from_str(&contents) {
                    Ok(config) => {
                        info!("Loaded config from {}", path.display());
                        config
                    }
                    Err(e) => {
                        tracing::warn!("Failed to parse config: {e}, using defaults");
                        Self::default()
                    }
                }
            }
            Err(_) => {
                info!("No config file found at {}, using defaults", path.display());
                Self::default()
            }
        }
    }

    pub fn config_dir() -> PathBuf {
        dirs::config_dir()
            .unwrap_or_else(|| PathBuf::from("."))
            .join("Apricadabra")
    }

    fn config_path() -> PathBuf {
        Self::config_dir().join("config.json")
    }
}
```

Note: Add `dirs = "5"` to `[dependencies]` in Cargo.toml.

- [ ] **Step 2: Verify it compiles**

Run: `cd core && cargo check 2>&1 | tail -5`

Expected: Compiles (may still have errors for missing server/broadcast modules).

- [ ] **Step 3: Commit**

```bash
git add core/src/config.rs core/Cargo.toml
git commit -m "feat(core): add config loading from %APPDATA%/Apricadabra/config.json"
```

---

## Task 7: Core — Named pipe server with handshake and heartbeat

**Files:**
- Create: `core/src/server.rs`

This is the largest core module. It ties together the pipe server, client management, handshake, heartbeat, message dispatch, and state broadcasting.

- [ ] **Step 1: Implement server.rs**

```rust
// core/src/server.rs
use crate::axis::AxisManager;
use crate::button::ButtonManager;
use crate::config::Config;
use crate::protocol::*;
use crate::vjoy::{Axis, VirtualJoystick};

use std::collections::HashMap;
use std::sync::Arc;
use tokio::io::{AsyncBufReadExt, AsyncWriteExt, BufReader};
use tokio::net::windows::named_pipe::{NamedPipeServer, ServerOptions};
use tokio::sync::{broadcast, mpsc, Mutex};
use tokio::time::{self, Duration, Instant};
use tracing::{error, info, warn};

// Pipe name is configurable via Config.pipe_name (default: \\.\pipe\apricadabra)
// This allows integration tests to use a unique pipe name.
const PROTOCOL_VERSION: u32 = 1;
const HEARTBEAT_INTERVAL: Duration = Duration::from_secs(3);
const HEARTBEAT_TIMEOUT: Duration = Duration::from_secs(9); // 3 missed beats
const TICK_INTERVAL: Duration = Duration::from_millis(16); // ~60Hz

/// Inbound message from a client with client ID.
struct ClientInput {
    client_id: u64,
    message: ClientMessage,
}

/// Core server state.
pub struct Server {
    config: Config,
    joystick: Box<dyn VirtualJoystick>,
}

impl Server {
    pub fn new(config: Config, joystick: Box<dyn VirtualJoystick>) -> Self {
        Self { config, joystick }
    }

    pub async fn run(mut self, mut shutdown_rx: tokio::sync::watch::Receiver<bool>) -> anyhow::Result<()> {
        // Acquire vJoy device
        if let Err(e) = self.joystick.acquire(self.config.vjoy_device_id) {
            error!("Failed to acquire vJoy device {}: {e}", self.config.vjoy_device_id);
            return Err(e);
        }
        info!("Acquired vJoy device {}", self.config.vjoy_device_id);

        let axis_mgr = Arc::new(Mutex::new(AxisManager::new()));
        let button_mgr = Arc::new(Mutex::new(ButtonManager::new()));
        let joystick = Arc::new(Mutex::new(self.joystick));

        // Channel for client messages -> main loop
        let (input_tx, mut input_rx) = mpsc::channel::<ClientInput>(256);

        // Broadcast channel for server -> all clients
        let (broadcast_tx, _) = broadcast::channel::<String>(256);

        let mut client_counter: u64 = 0;
        let connected_clients = Arc::new(std::sync::atomic::AtomicU64::new(0));

        // Channel for client disconnect notifications
        let (disconnect_tx, mut disconnect_rx) = mpsc::channel::<u64>(32);

        // Spawn pipe accept loop
        let accept_input_tx = input_tx.clone();
        let accept_broadcast_tx = broadcast_tx.clone();
        let accept_axis = axis_mgr.clone();
        let accept_button = button_mgr.clone();
        let accept_clients = connected_clients.clone();
        let accept_disconnect_tx = disconnect_tx.clone();
        let pipe_name = self.config.pipe_name.clone();

        tokio::spawn(async move {
            loop {
                let pipe = match ServerOptions::new()
                    .first_pipe_instance(client_counter == 0)
                    .create(&pipe_name)
                {
                    Ok(pipe) => pipe,
                    Err(e) => {
                        error!("Failed to create named pipe: {e}");
                        time::sleep(Duration::from_secs(1)).await;
                        continue;
                    }
                };

                if let Err(e) = pipe.connect().await {
                    error!("Pipe connect error: {e}");
                    continue;
                }

                client_counter += 1;
                let client_id = client_counter;
                info!("Client {client_id} connected");

                accept_clients.fetch_add(1, std::sync::atomic::Ordering::Relaxed);

                let tx = accept_input_tx.clone();
                let mut broadcast_rx = accept_broadcast_tx.subscribe();
                let axis = accept_axis.clone();
                let button = accept_button.clone();
                let clients = accept_clients.clone();
                let disc_tx = accept_disconnect_tx.clone();

                tokio::spawn(async move {
                    Self::handle_client(client_id, pipe, tx, broadcast_rx, axis, button).await;
                    clients.fetch_sub(1, std::sync::atomic::Ordering::Relaxed);
                    let _ = disc_tx.send(client_id).await;
                    info!("Client {client_id} disconnected");
                });
            }
        });

        // Main tick loop: process inputs, update state, broadcast, decay
        let mut tick_interval = time::interval(TICK_INTERVAL);

        loop {
            tokio::select! {
                _ = tick_interval.tick() => {
                    let mut axes = axis_mgr.lock().await;
                    let mut buttons = button_mgr.lock().await;

                    // Process spring decay
                    axes.tick_spring_decay();

                    // Process disconnect decay if active
                    axes.tick_disconnect_decay();

                    // Process pending button actions (pulse releases, double press timing)
                    buttons.process_pending();

                    // Collect changes and broadcast
                    let axis_changes = axes.take_changed();
                    let button_changes = buttons.take_changed();

                    if !axis_changes.is_empty() || !button_changes.is_empty() {
                        // Update vJoy
                        let mut joy = joystick.lock().await;
                        for (&id, &value) in &axis_changes {
                            if let Some(axis) = Axis::from_id(id) {
                                let _ = joy.set_axis(axis, value);
                            }
                        }
                        for (&id, &pressed) in &button_changes {
                            let _ = joy.set_button(id, pressed);
                        }

                        // Broadcast state to plugins
                        let msg = ServerMessage::State {
                            axes: axis_changes,
                            buttons: button_changes,
                        };
                        if let Ok(json) = serde_json::to_string(&msg) {
                            let _ = broadcast_tx.send(json);
                        }
                    }
                }

                Some(input) = input_rx.recv() => {
                    match input.message {
                        ClientMessage::Axis { axis, mode, diff, sensitivity, decay_rate, steps } => {
                            let sens = sensitivity.unwrap_or(self.config.default_sensitivity);
                            let mut axes = axis_mgr.lock().await;
                            match mode {
                                AxisMode::Hold => axes.apply_hold(axis, diff, sens),
                                AxisMode::Spring => {
                                    let dr = decay_rate.unwrap_or(self.config.default_decay_rate);
                                    axes.apply_spring(axis, diff, sens, dr);
                                }
                                AxisMode::Detent => {
                                    axes.apply_detent(axis, diff, steps.unwrap_or(5));
                                }
                            }
                        }
                        ClientMessage::Button { button, mode, state, delay, rate, short_button, long_button, threshold } => {
                            let mut buttons = button_mgr.lock().await;
                            match mode {
                                ButtonMode::Momentary => match state {
                                    Some(ButtonState::Down) => buttons.momentary_down(button),
                                    Some(ButtonState::Up) => buttons.momentary_up(button),
                                    None => {}
                                },
                                ButtonMode::Toggle => {
                                    if matches!(state, Some(ButtonState::Down)) {
                                        buttons.toggle(button);
                                    }
                                }
                                ButtonMode::Pulse => buttons.pulse(button),
                                ButtonMode::Double => buttons.double_press(button, delay.unwrap_or(50)),
                                ButtonMode::Rapid => match state {
                                    Some(ButtonState::Down) => buttons.rapid_start(button, rate.unwrap_or(100)),
                                    Some(ButtonState::Up) => buttons.rapid_stop(button),
                                    None => {}
                                },
                                ButtonMode::LongShort => {
                                    let sb = short_button.unwrap_or(button);
                                    let lb = long_button.unwrap_or(button);
                                    let th = threshold.unwrap_or(500);
                                    match state {
                                        Some(ButtonState::Down) => buttons.longshort_down(sb, lb, th),
                                        Some(ButtonState::Up) => { buttons.longshort_up(sb, lb, th); }
                                        None => {}
                                    }
                                }
                            }
                        }
                        ClientMessage::Reset { axis, position } => {
                            axis_mgr.lock().await.reset(axis, position);
                        }
                        ClientMessage::Hello { .. } | ClientMessage::HeartbeatAck => {
                            // Handled in client handler
                        }
                    }
                }

                Some(client_id) = disconnect_rx.recv() => {
                    info!("Client {client_id} cleanup");
                    if connected_clients.load(std::sync::atomic::Ordering::Relaxed) == 0 {
                        info!("All clients disconnected, starting decay");
                        axis_mgr.lock().await.start_disconnect_decay();
                    }
                }

                _ = shutdown_rx.changed() => {
                    if *shutdown_rx.borrow() {
                        info!("Broadcasting shutdown to all clients");
                        if let Ok(json) = serde_json::to_string(&ServerMessage::Shutdown) {
                            let _ = broadcast_tx.send(json);
                        }
                        // Give clients a moment to receive the message
                        time::sleep(Duration::from_millis(100)).await;
                        // Release vJoy
                        let _ = joystick.lock().await.release();
                        break;
                    }
                }
            }
        }

        Ok(())
    }

    async fn handle_client(
        client_id: u64,
        pipe: NamedPipeServer,
        input_tx: mpsc::Sender<ClientInput>,
        mut broadcast_rx: broadcast::Receiver<String>,
        axis_mgr: Arc<Mutex<AxisManager>>,
        button_mgr: Arc<Mutex<ButtonManager>>,
    ) {
        let (reader, mut writer) = tokio::io::split(pipe);
        let mut reader = BufReader::new(reader);
        let mut line = String::new();

        // Wait for hello
        line.clear();
        match reader.read_line(&mut line).await {
            Ok(0) | Err(_) => return,
            Ok(_) => {}
        }

        let hello: ClientMessage = match serde_json::from_str(line.trim()) {
            Ok(msg) => msg,
            Err(_) => return,
        };

        match hello {
            ClientMessage::Hello { version, name } => {
                info!("Client {client_id} hello: {name} v{version}");
                if version != PROTOCOL_VERSION {
                    let err = ServerMessage::Error {
                        code: "unsupported_version".to_string(),
                        message: format!("Server supports protocol v{PROTOCOL_VERSION}, client sent v{version}"),
                    };
                    let _ = Self::send_message(&mut writer, &err).await;
                    return;
                }

                // Send welcome with current state
                let axes = axis_mgr.lock().await.get_all();
                let buttons = button_mgr.lock().await.get_all();
                let welcome = ServerMessage::Welcome { version: PROTOCOL_VERSION, axes, buttons };
                if Self::send_message(&mut writer, &welcome).await.is_err() {
                    return;
                }
            }
            _ => return, // First message must be hello
        }

        line.clear(); // Clear hello message before entering read loop

        let mut heartbeat_interval = time::interval(HEARTBEAT_INTERVAL);
        let mut last_ack = Instant::now();

        loop {
            tokio::select! {
                result = reader.read_line(&mut line) => {
                    match result {
                        Ok(0) | Err(_) => break, // Client disconnected
                        Ok(_) => {
                            if let Ok(msg) = serde_json::from_str::<ClientMessage>(line.trim()) {
                                match msg {
                                    ClientMessage::HeartbeatAck => {
                                        last_ack = Instant::now();
                                    }
                                    _ => {
                                        let _ = input_tx.send(ClientInput { client_id, message: msg }).await;
                                    }
                                }
                            }
                            line.clear();
                        }
                    }
                }

                Ok(broadcast_msg) = broadcast_rx.recv() => {
                    let msg = format!("{broadcast_msg}\n");
                    if writer.write_all(msg.as_bytes()).await.is_err() {
                        break;
                    }
                }

                _ = heartbeat_interval.tick() => {
                    if last_ack.elapsed() > HEARTBEAT_TIMEOUT {
                        warn!("Client {client_id} heartbeat timeout");
                        break;
                    }
                    if Self::send_message(&mut writer, &ServerMessage::Heartbeat).await.is_err() {
                        break;
                    }
                }
            }
        }
    }

    async fn send_message(
        writer: &mut (impl AsyncWriteExt + Unpin),
        msg: &ServerMessage,
    ) -> anyhow::Result<()> {
        let mut json = serde_json::to_string(msg)?;
        json.push('\n');
        writer.write_all(json.as_bytes()).await?;
        writer.flush().await?;
        Ok(())
    }
}
```

This is a large module. The key design: per-client tasks handle reading/writing to their pipe, the main loop runs at ~60Hz processing state changes and broadcasting.

Note: The `first_pipe_instance` flag on the first call and subsequent calls need care. The actual implementation may need adjustment for how `tokio::net::windows::named_pipe` handles multiple instances. This should be verified during implementation.

- [ ] **Step 2: Verify it compiles**

Run: `cd core && cargo check 2>&1 | tail -10`

Expected: Compiles on Windows. On non-Windows, `tokio::net::windows` won't exist — use `#[cfg(windows)]` guards if cross-platform compilation is needed for CI.

- [ ] **Step 3: Commit**

```bash
git add core/src/server.rs
git commit -m "feat(core): add named pipe server with handshake, heartbeat, and message dispatch"
```

---

## Task 8: Core — Wire up main.rs with logging and graceful shutdown

**Files:**
- Modify: `core/src/main.rs`
- Modify: `core/src/lib.rs`

- [ ] **Step 1: Update lib.rs to export all modules**

```rust
// core/src/lib.rs
pub mod protocol;
pub mod axis;
pub mod button;
pub mod vjoy;
pub mod server;
pub mod config;
```

- [ ] **Step 2: Update main.rs**

```rust
// core/src/main.rs
use apricadabra_core::config::Config;
use apricadabra_core::server::Server;
use apricadabra_core::vjoy::MockJoystick;

use tracing::info;
use tracing_appender::rolling;
use tracing_subscriber::{fmt, layer::SubscriberExt, util::SubscriberInitExt, EnvFilter};

#[tokio::main]
async fn main() -> anyhow::Result<()> {
    let config = Config::load();

    // Set up logging
    let log_dir = Config::config_dir().join("logs");
    std::fs::create_dir_all(&log_dir)?;
    // Note: tracing-appender rotates daily but does not enforce file count/size limits.
    // Log retention (spec: 5 files, 10MB max) is deferred — add cleanup logic later.
    let file_appender = rolling::daily(&log_dir, "apricadabra-core.log");

    let env_filter = EnvFilter::try_from_default_env()
        .unwrap_or_else(|_| EnvFilter::new(&config.log_level));

    tracing_subscriber::registry()
        .with(env_filter)
        .with(fmt::layer().with_writer(std::io::stderr))
        .with(fmt::layer().with_writer(file_appender).with_ansi(false))
        .init();

    info!("Apricadabra Core v{} starting", env!("CARGO_PKG_VERSION"));
    info!("vJoy device ID: {}", config.vjoy_device_id);

    // Parse --debug flag
    let debug = std::env::args().any(|a| a == "--debug");
    if debug {
        // Override log level if --debug flag is present
        // (env_filter already set above — reconstruct if needed)
        info!("Debug mode enabled via --debug flag");
    }

    // TODO: Replace MockJoystick with VJoyBackend when FFI is implemented
    let joystick = Box::new(MockJoystick::new());
    let server = Server::new(config, joystick);

    // Handle Ctrl+C for graceful shutdown
    // Server.run() accepts a shutdown signal receiver so it can broadcast
    // ServerMessage::Shutdown to all clients before exiting.
    let (shutdown_tx, shutdown_rx) = tokio::sync::watch::channel(false);

    let server_handle = tokio::spawn(async move {
        server.run(shutdown_rx).await
    });

    tokio::signal::ctrl_c().await?;
    info!("Shutting down...");
    let _ = shutdown_tx.send(true);

    // Give server time to broadcast shutdown and clean up
    let _ = tokio::time::timeout(
        std::time::Duration::from_secs(2),
        server_handle,
    ).await;

    Ok(())
}
```

- [ ] **Step 3: Verify it compiles**

Run: `cd core && cargo build 2>&1 | tail -5`

Expected: Compiles successfully (on Windows). On Linux, the `windows::named_pipe` import in server.rs will fail — that's expected and acceptable for now.

- [ ] **Step 4: Commit**

```bash
git add core/src/main.rs core/src/lib.rs
git commit -m "feat(core): wire up main with logging, config, and graceful shutdown"
```

---

## Task 9: Core — vJoy FFI backend (Windows only)

**Files:**
- Create: `core/build.rs`
- Modify: `core/src/vjoy.rs`
- Create: `core/vjoy-sdk/` (vendored header)

This task depends on having the vJoy SDK header. The vJoy SDK provides `vJoyInterface.h` and `vJoyInterface.dll`.

- [ ] **Step 1: Vendor the vJoy header**

Download `vjoyinterface.h` from the vJoy SDK (https://github.com/njz3/vJoy/). Place it at `core/vjoy-sdk/vjoyinterface.h`.

- [ ] **Step 2: Create build.rs for bindgen**

```rust
// core/build.rs
fn main() {
    #[cfg(windows)]
    {
        let bindings = bindgen::Builder::default()
            .header("vjoy-sdk/vjoyinterface.h")
            .allowlist_function("AcquireVJD")
            .allowlist_function("RelinquishVJD")
            .allowlist_function("SetAxis")
            .allowlist_function("SetBtn")
            .allowlist_function("GetVJDStatus")
            .allowlist_function("vJoyEnabled")
            .allowlist_type("VjdStat")
            .allowlist_var("HID_USAGE_X")
            .allowlist_var("HID_USAGE_Y")
            .allowlist_var("HID_USAGE_Z")
            .allowlist_var("HID_USAGE_RX")
            .allowlist_var("HID_USAGE_RY")
            .allowlist_var("HID_USAGE_RZ")
            .allowlist_var("HID_USAGE_SL0")
            .allowlist_var("HID_USAGE_SL1")
            .generate()
            .expect("Unable to generate vJoy bindings");

        let out_path = std::path::PathBuf::from(std::env::var("OUT_DIR").unwrap());
        bindings
            .write_to_file(out_path.join("vjoy_bindings.rs"))
            .expect("Couldn't write bindings");
    }
}
```

Note: Add `bindgen = "0.69"` to `[build-dependencies]` in Cargo.toml.

- [ ] **Step 3: Add VJoyBackend to vjoy.rs**

Append to `core/src/vjoy.rs`:

```rust
#[cfg(windows)]
mod vjoy_ffi {
    include!(concat!(env!("OUT_DIR"), "/vjoy_bindings.rs"));
}

#[cfg(windows)]
pub struct VJoyBackend {
    device_id: u32,
}

#[cfg(windows)]
impl VJoyBackend {
    pub fn new() -> anyhow::Result<Self> {
        unsafe {
            if vjoy_ffi::vJoyEnabled() == 0 {
                anyhow::bail!("vJoy driver is not installed or not enabled");
            }
        }
        Ok(Self { device_id: 0 })
    }

    fn axis_to_usage(axis: Axis) -> u32 {
        match axis {
            Axis::X => vjoy_ffi::HID_USAGE_X,
            Axis::Y => vjoy_ffi::HID_USAGE_Y,
            Axis::Z => vjoy_ffi::HID_USAGE_Z,
            Axis::Rx => vjoy_ffi::HID_USAGE_RX,
            Axis::Ry => vjoy_ffi::HID_USAGE_RY,
            Axis::Rz => vjoy_ffi::HID_USAGE_RZ,
            Axis::Slider1 => vjoy_ffi::HID_USAGE_SL0,
            Axis::Slider2 => vjoy_ffi::HID_USAGE_SL1,
        }
    }
}

#[cfg(windows)]
impl VirtualJoystick for VJoyBackend {
    fn acquire(&mut self, device_id: u8) -> anyhow::Result<()> {
        self.device_id = device_id as u32;
        unsafe {
            let status = vjoy_ffi::GetVJDStatus(self.device_id);
            // VJD_STAT_FREE = 0
            if status != 0 {
                anyhow::bail!("vJoy device {} is not free (status: {})", device_id, status);
            }
            if vjoy_ffi::AcquireVJD(self.device_id) == 0 {
                anyhow::bail!("Failed to acquire vJoy device {}", device_id);
            }
        }
        Ok(())
    }

    fn set_axis(&mut self, axis: Axis, value: f32) -> anyhow::Result<()> {
        let vjoy_value = (value.clamp(0.0, 1.0) * 32767.0) as i64;
        let usage = Self::axis_to_usage(axis);
        unsafe {
            vjoy_ffi::SetAxis(vjoy_value as i32, self.device_id, usage);
        }
        Ok(())
    }

    fn set_button(&mut self, button: u8, pressed: bool) -> anyhow::Result<()> {
        unsafe {
            vjoy_ffi::SetBtn(pressed as i32, self.device_id, button as u8);
        }
        Ok(())
    }

    fn release(&mut self) -> anyhow::Result<()> {
        unsafe {
            vjoy_ffi::RelinquishVJD(self.device_id);
        }
        Ok(())
    }
}
```

The exact FFI signatures will depend on what bindgen generates from the header. The above is the intended mapping — adjust types as needed when bindgen output is available.

**Fallback if bindgen proves difficult:** The vJoy header includes `windows.h` which can cause bindgen to pull in thousands of Windows types. If bindgen + LLVM toolchain setup is too painful, manually write the ~6 FFI function signatures and use `libloading` for runtime dynamic loading instead. This is common practice for small FFI surfaces:

```rust
// Alternative: manual FFI without bindgen
type AcquireVJD = unsafe extern "C" fn(rID: u32) -> i32;
type SetAxis = unsafe extern "C" fn(value: i32, rID: u32, axis: u32) -> i32;
// ... load with libloading::Library::new("vJoyInterface.dll")
```

- [ ] **Step 4: Update main.rs to use VJoyBackend on Windows**

Replace the `MockJoystick` line in main.rs:

```rust
#[cfg(windows)]
let joystick: Box<dyn apricadabra_core::vjoy::VirtualJoystick> = {
    match apricadabra_core::vjoy::VJoyBackend::new() {
        Ok(backend) => Box::new(backend),
        Err(e) => {
            tracing::error!("vJoy initialization failed: {e}");
            tracing::warn!("Falling back to mock joystick (no game output)");
            Box::new(MockJoystick::new())
        }
    }
};

#[cfg(not(windows))]
let joystick: Box<dyn apricadabra_core::vjoy::VirtualJoystick> = {
    tracing::warn!("Not on Windows — using mock joystick");
    Box::new(MockJoystick::new())
};
```

- [ ] **Step 5: Verify it compiles**

Run: `cd core && cargo build 2>&1 | tail -10`

Expected: Compiles. On Windows with vJoy SDK header present, bindgen generates bindings. On non-Windows, the `#[cfg(windows)]` blocks are skipped.

- [ ] **Step 6: Commit**

```bash
git add core/build.rs core/vjoy-sdk/ core/src/vjoy.rs core/src/main.rs core/Cargo.toml
git commit -m "feat(core): add vJoy FFI backend via bindgen with Windows-only compilation"
```

---

## Task 10: Core — Integration test with mock joystick

**Files:**
- Create: `core/tests/integration_test.rs`

This test exercises the full pipeline: connect to the named pipe, send hello, receive welcome, send axis/button events, and verify state broadcasts come back. Uses MockJoystick. Windows only (named pipes).

- [ ] **Step 1: Write integration test**

```rust
// core/tests/integration_test.rs
#![cfg(windows)]

use apricadabra_core::config::Config;
use apricadabra_core::server::Server;
use apricadabra_core::vjoy::MockJoystick;

use tokio::io::{AsyncBufReadExt, AsyncWriteExt, BufReader};
use tokio::net::windows::named_pipe::ClientOptions;
use tokio::time::{self, Duration};

const TEST_PIPE: &str = r"\\.\pipe\apricadabra_test";

async fn send_line(writer: &mut impl AsyncWriteExt + Unpin, msg: &str) {
    writer.write_all(format!("{msg}\n").as_bytes()).await.unwrap();
    writer.flush().await.unwrap();
}

async fn read_line(reader: &mut (impl AsyncBufReadExt + Unpin)) -> String {
    let mut line = String::new();
    reader.read_line(&mut line).await.unwrap();
    line
}

#[tokio::test]
async fn test_hello_welcome_handshake() {
    let config = Config::default();
    let joystick = Box::new(MockJoystick::new());
    let server = Server::new(config, joystick);

    // Start server in background
    let server_handle = tokio::spawn(async move {
        let _ = server.run().await;
    });

    // Give server time to start
    time::sleep(Duration::from_millis(100)).await;

    // Connect client
    let pipe = ClientOptions::new().open(TEST_PIPE).unwrap();
    let (reader, mut writer) = tokio::io::split(pipe);
    let mut reader = BufReader::new(reader);

    // Send hello
    send_line(&mut writer, r#"{"type":"hello","version":1,"name":"test"}"#).await;

    // Expect welcome
    let welcome = read_line(&mut reader).await;
    assert!(welcome.contains("\"type\":\"welcome\""));
    assert!(welcome.contains("\"version\":1"));

    server_handle.abort();
}

#[tokio::test]
async fn test_axis_hold_state_broadcast() {
    let config = Config::default();
    let joystick = Box::new(MockJoystick::new());
    let server = Server::new(config, joystick);

    let server_handle = tokio::spawn(async move {
        let _ = server.run().await;
    });

    time::sleep(Duration::from_millis(100)).await;

    let pipe = ClientOptions::new().open(TEST_PIPE).unwrap();
    let (reader, mut writer) = tokio::io::split(pipe);
    let mut reader = BufReader::new(reader);

    // Handshake
    send_line(&mut writer, r#"{"type":"hello","version":1,"name":"test"}"#).await;
    let _ = read_line(&mut reader).await; // welcome

    // Send axis event
    send_line(&mut writer, r#"{"type":"axis","axis":1,"mode":"hold","diff":10,"sensitivity":0.01}"#).await;

    // Wait for state broadcast
    time::sleep(Duration::from_millis(50)).await;
    let state = read_line(&mut reader).await;
    assert!(state.contains("\"type\":\"state\""));

    server_handle.abort();
}
```

Note: The integration test needs the server to use a test-specific pipe name. The `Server` struct should accept the pipe name as a parameter (add a `pipe_name: String` field to `Config` or `Server::new`). Update server.rs to use `config.pipe_name` instead of the hardcoded `PIPE_NAME`. This is a small refactor — default the pipe name to `\\.\pipe\apricadabra` in Config.

- [ ] **Step 2: Run on Windows to verify**

Run: `cd core && cargo test --test integration_test 2>&1 | tail -10`

Expected: Tests pass on Windows. Tests are skipped on other platforms due to `#![cfg(windows)]`.

- [ ] **Step 3: Commit**

```bash
git add core/tests/integration_test.rs core/src/server.rs core/src/config.rs
git commit -m "test(core): add integration tests for pipe server handshake and state broadcast"
```

---

## Task 11: Loupedeck Plugin — Project scaffolding

**Pre-requisite:** Before writing any plugin code, examine the actual Logi Actions SDK types. The Action Editor API (`ActionEditorListbox`, `ActionEditorSlider`, `AddControlEx`, etc.) is based on SDK documentation that may differ from the actual API. After generating the project (Step 1), inspect the SDK NuGet package or decompile the SDK DLL to verify the class names and method signatures used in Tasks 14-16.

**Files:**
- Create: `loupedeck-plugin/src/ApricadabraPlugin.cs`
- Create: `loupedeck-plugin/src/ApricadabraApplication.cs`
- Create: `loupedeck-plugin/metadata/LoupedeckPackage.yaml`

This requires the Logi Actions SDK installed and `logiplugintool` available. Generate the project skeleton, then replace the boilerplate.

- [ ] **Step 1: Generate the plugin project**

Run (on Windows with .NET 8 SDK and LogiPluginTool installed):

```bash
cd loupedeck-plugin
dotnet tool install --global LogiPluginTool
logiplugintool generate Apricadabra
```

If `logiplugintool` doesn't work or the SDK structure doesn't match, create the project manually:

```bash
cd loupedeck-plugin
dotnet new classlib -n Apricadabra -f net8.0
```

- [ ] **Step 2: Create LoupedeckPackage.yaml**

```yaml
# loupedeck-plugin/metadata/LoupedeckPackage.yaml
type: plugin4
name: Apricadabra
displayName: Apricadabra - vJoy Controller
version: 0.1.0
author: apricadabra
supportPageUrl: https://github.com/your-repo/apricadabra
license: MIT
supportedDevices:
  - LoupedeckCt
  - LoupedeckLive
  - LoupedeckLiveS
  - LoupedeckPlus
```

- [ ] **Step 3: Create ApricadabraPlugin.cs**

```csharp
// loupedeck-plugin/src/ApricadabraPlugin.cs
namespace Loupedeck.ApricadabraPlugin
{
    public class ApricadabraPlugin : Plugin
    {
        public override bool UsesApplicationApiOnly => true;
        public override bool HasNoApplication => true;

        public override void Load()
        {
            this.Info.Icon16x16 = EmbeddedResources.ReadImage("icon-16.png");
            this.Info.Icon32x32 = EmbeddedResources.ReadImage("icon-32.png");
            this.Info.Icon48x48 = EmbeddedResources.ReadImage("icon-48.png");
            this.Info.Icon256x256 = EmbeddedResources.ReadImage("icon-256.png");
        }

        public override void Unload()
        {
        }
    }
}
```

- [ ] **Step 4: Create ApricadabraApplication.cs**

```csharp
// loupedeck-plugin/src/ApricadabraApplication.cs
namespace Loupedeck.ApricadabraPlugin
{
    public class ApricadabraApplication : ClientApplication
    {
        public ApricadabraApplication()
        {
        }

        protected override string GetProcessName() => "";

        protected override string GetBundleName() => "";
    }
}
```

- [ ] **Step 5: Verify it builds**

Run: `cd loupedeck-plugin && dotnet build 2>&1 | tail -5`

Expected: Build succeeds (may need SDK NuGet package reference — adjust .csproj as needed based on Logi Actions SDK installation method).

- [ ] **Step 6: Commit**

```bash
git add loupedeck-plugin/
git commit -m "feat(plugin): scaffold Loupedeck plugin project with manifest"
```

---

## Task 12: Loupedeck Plugin — CoreConnection

**Files:**
- Create: `loupedeck-plugin/src/CoreConnection.cs`

- [ ] **Step 1: Implement CoreConnection**

```csharp
// loupedeck-plugin/src/CoreConnection.cs
using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace Loupedeck.ApricadabraPlugin
{
    public class CoreConnection : IDisposable
    {
        private const string PipeName = "apricadabra";
        private const string CoreExeName = "apricadabra-core.exe";
        private const int ProtocolVersion = 1;

        private NamedPipeClientStream _pipe;
        private StreamReader _reader;
        private StreamWriter _writer;
        private CancellationTokenSource _cts;
        private Task _readTask;
        private bool _connected;

        public event Action<JsonObject> OnStateUpdate;
        public event Action<string, string> OnError; // code, message
        public event Action OnDisconnected;
        public event Action OnShutdown;

        public bool IsConnected => _connected;

        public async Task ConnectAsync()
        {
            _cts = new CancellationTokenSource();
            int delay = 100;

            while (!_cts.Token.IsCancellationRequested)
            {
                try
                {
                    _pipe = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
                    await _pipe.ConnectAsync(1000, _cts.Token);

                    _reader = new StreamReader(_pipe);
                    _writer = new StreamWriter(_pipe) { AutoFlush = true };

                    // Send hello
                    var hello = new JsonObject
                    {
                        ["type"] = "hello",
                        ["version"] = ProtocolVersion,
                        ["name"] = "loupedeck"
                    };
                    await _writer.WriteLineAsync(hello.ToJsonString());

                    // Read welcome
                    var welcomeLine = await _reader.ReadLineAsync();
                    if (welcomeLine == null) throw new IOException("No welcome received");

                    var welcome = JsonNode.Parse(welcomeLine)?.AsObject();
                    if (welcome?["type"]?.GetValue<string>() != "welcome")
                        throw new IOException("Expected welcome message");

                    _connected = true;
                    delay = 100;

                    // Start reading loop
                    _readTask = Task.Run(() => ReadLoopAsync(_cts.Token));

                    // Dispatch initial state
                    OnStateUpdate?.Invoke(welcome);
                    return;
                }
                catch (Exception) when (!_cts.Token.IsCancellationRequested)
                {
                    TryLaunchCore();
                    await Task.Delay(delay, _cts.Token);
                    delay = Math.Min(delay * 2, 5000);
                }
            }
        }

        public async Task SendAsync(JsonObject message)
        {
            if (!_connected || _writer == null) return;
            try
            {
                await _writer.WriteLineAsync(message.ToJsonString());
            }
            catch
            {
                HandleDisconnect();
            }
        }

        private async Task ReadLoopAsync(CancellationToken ct)
        {
            try
            {
                while (!ct.IsCancellationRequested)
                {
                    var line = await _reader.ReadLineAsync();
                    if (line == null) break;

                    var msg = JsonNode.Parse(line)?.AsObject();
                    if (msg == null) continue;

                    var msgType = msg["type"]?.GetValue<string>();
                    switch (msgType)
                    {
                        case "state":
                            OnStateUpdate?.Invoke(msg);
                            break;
                        case "heartbeat":
                            await SendAsync(new JsonObject { ["type"] = "heartbeat_ack" });
                            break;
                        case "error":
                            OnError?.Invoke(
                                msg["code"]?.GetValue<string>() ?? "unknown",
                                msg["message"]?.GetValue<string>() ?? "Unknown error"
                            );
                            break;
                        case "shutdown":
                            OnShutdown?.Invoke();
                            return;
                    }
                }
            }
            catch { }

            HandleDisconnect();
        }

        private void HandleDisconnect()
        {
            _connected = false;
            OnDisconnected?.Invoke();
            // Auto-reconnect
            _ = Task.Run(async () =>
            {
                await Task.Delay(1000);
                await ConnectAsync();
            });
        }

        private void TryLaunchCore()
        {
            try
            {
                // Look for core exe in known locations
                var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                var corePath = Path.Combine(appData, "Apricadabra", CoreExeName);

                if (!File.Exists(corePath))
                {
                    // Try relative to plugin directory
                    corePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, CoreExeName);
                }

                if (File.Exists(corePath))
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = corePath,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    });
                }
            }
            catch { }
        }

        public void Dispose()
        {
            _cts?.Cancel();
            _pipe?.Dispose();
            _reader?.Dispose();
            _writer?.Dispose();
        }
    }
}
```

- [ ] **Step 2: Verify it compiles**

Run: `cd loupedeck-plugin && dotnet build 2>&1 | tail -5`

Expected: Compiles.

- [ ] **Step 3: Commit**

```bash
git add loupedeck-plugin/src/CoreConnection.cs
git commit -m "feat(plugin): add CoreConnection with auto-launch, reconnection, and handshake"
```

---

## Task 13: Loupedeck Plugin — StateDisplay

**Files:**
- Create: `loupedeck-plugin/src/Display/StateDisplay.cs`

- [ ] **Step 1: Implement StateDisplay**

```csharp
// loupedeck-plugin/src/Display/StateDisplay.cs
using System;
using System.Collections.Concurrent;
using System.Text.Json.Nodes;

namespace Loupedeck.ApricadabraPlugin
{
    /// <summary>
    /// Shared state cache updated from core broadcasts.
    /// Actions query this for LCD display values.
    /// </summary>
    public class StateDisplay
    {
        private readonly ConcurrentDictionary<int, float> _axes = new();
        private readonly ConcurrentDictionary<int, bool> _buttons = new();

        public string ConnectionStatus { get; set; } = "Connecting...";
        public string ErrorMessage { get; set; }

        public float GetAxis(int axisId) =>
            _axes.TryGetValue(axisId, out var value) ? value : 0.5f;

        public bool GetButton(int buttonId) =>
            _buttons.TryGetValue(buttonId, out var value) && value;

        public string GetAxisDisplayString(int axisId)
        {
            if (ErrorMessage != null) return ErrorMessage;
            if (ConnectionStatus != null) return ConnectionStatus;
            var value = GetAxis(axisId);
            return $"{(int)(value * 100)}%";
        }

        public void UpdateFromState(JsonObject msg)
        {
            ConnectionStatus = null;
            ErrorMessage = null;

            if (msg["axes"] is JsonObject axes)
            {
                foreach (var kvp in axes)
                {
                    if (int.TryParse(kvp.Key, out var id) && kvp.Value != null)
                    {
                        _axes[id] = kvp.Value.GetValue<float>();
                    }
                }
            }

            if (msg["buttons"] is JsonObject buttons)
            {
                foreach (var kvp in buttons)
                {
                    if (int.TryParse(kvp.Key, out var id) && kvp.Value != null)
                    {
                        _buttons[id] = kvp.Value.GetValue<bool>();
                    }
                }
            }
        }
    }
}
```

- [ ] **Step 2: Verify it compiles**

Run: `cd loupedeck-plugin && dotnet build 2>&1 | tail -5`

Expected: Compiles.

- [ ] **Step 3: Commit**

```bash
git add loupedeck-plugin/src/Display/StateDisplay.cs
git commit -m "feat(plugin): add StateDisplay cache for LCD feedback"
```

---

## Task 14: Loupedeck Plugin — AxisAdjustment action

**Files:**
- Create: `loupedeck-plugin/src/Actions/AxisAdjustment.cs`

This is the most complex action — it uses the Action Editor API with dynamic control visibility based on mode selection.

- [ ] **Step 1: Implement AxisAdjustment**

```csharp
// loupedeck-plugin/src/Actions/AxisAdjustment.cs
using System;
using System.Text.Json.Nodes;

namespace Loupedeck.ApricadabraPlugin
{
    public class AxisAdjustment : ActionEditorAdjustment
    {
        private CoreConnection _connection;
        private StateDisplay _stateDisplay;

        // Control names
        private const string ModeControl = "mode";
        private const string AxisControl = "axis";
        private const string InvertControl = "invert";
        private const string SensitivityControl = "sensitivity";
        private const string ResetPositionControl = "resetPosition";
        private const string DecayRateControl = "decayRate";
        private const string StepCountControl = "stepCount";

        public AxisAdjustment()
            : base(hasReset: true)
        {
            this.DisplayName = "vJoy Axis";
            this.Description = "Map a dial to a vJoy axis";
            this.GroupName = "Apricadabra";

            // Mode dropdown
            this.ActionEditor.AddControlEx(
                new ActionEditorListbox(name: ModeControl, labelText: "Mode")
                    .SetRequired()
            );

            // Axis dropdown
            this.ActionEditor.AddControlEx(
                new ActionEditorListbox(name: AxisControl, labelText: "Axis")
                    .SetRequired()
            );

            // Invert checkbox
            this.ActionEditor.AddControlEx(
                new ActionEditorCheckbox(name: InvertControl, labelText: "Invert")
                    .SetDefaultValue(false)
            );

            // Sensitivity slider (Hold, Spring)
            this.ActionEditor.AddControlEx(
                new ActionEditorSlider(name: SensitivityControl, labelText: "Sensitivity")
                    .SetValues(1, 100, 1)
                    .SetDefaultValue(20)
                    .SetFormatString("{0}%")
            );

            // Reset position slider
            this.ActionEditor.AddControlEx(
                new ActionEditorSlider(name: ResetPositionControl, labelText: "Reset Position")
                    .SetValues(0, 100, 1)
                    .SetDefaultValue(50)
                    .SetFormatString("{0}%")
            );

            // Decay rate slider (Spring only)
            this.ActionEditor.AddControlEx(
                new ActionEditorSlider(name: DecayRateControl, labelText: "Decay Rate")
                    .SetValues(0, 100, 1)
                    .SetDefaultValue(95)
                    .SetFormatString("{0}%")
            );

            // Step count slider (Detent only)
            this.ActionEditor.AddControlEx(
                new ActionEditorSlider(name: StepCountControl, labelText: "Steps")
                    .SetValues(2, 20, 1)
                    .SetDefaultValue(5)
            );

            this.ActionEditor.ListboxItemsRequested += OnListboxItemsRequested;
            this.ActionEditor.ControlValueChanged += OnControlValueChanged;
        }

        private void OnListboxItemsRequested(object sender, ActionEditorListboxItemsRequestedEventArgs e)
        {
            if (e.ControlName == ModeControl)
            {
                e.AddItem("hold", "Hold", "Maintains position");
                e.AddItem("spring", "Spring", "Returns to center");
                e.AddItem("detent", "Detent", "Discrete steps");
            }
            else if (e.ControlName == AxisControl)
            {
                e.AddItem("1", "X", null);
                e.AddItem("2", "Y", null);
                e.AddItem("3", "Z", null);
                e.AddItem("4", "Rx", null);
                e.AddItem("5", "Ry", null);
                e.AddItem("6", "Rz", null);
                e.AddItem("7", "Slider 1", null);
                e.AddItem("8", "Slider 2", null);
            }
        }

        private void OnControlValueChanged(object sender, ActionEditorControlValueChangedEventArgs e)
        {
            // Dynamic visibility based on mode
            // Note: The actual API for showing/hiding controls may differ.
            // This is the intended behavior — verify against SDK docs during implementation.
        }

        protected override void ApplyAdjustment(ActionEditorActionParameters actionParameters, int diff)
        {
            if (!actionParameters.TryGetString(ModeControl, out var mode)) return;
            if (!actionParameters.TryGetString(AxisControl, out var axisStr)) return;
            if (!int.TryParse(axisStr, out var axis)) return;

            var invert = false;
            actionParameters.TryGetBoolean(InvertControl, out invert);
            var adjustedDiff = invert ? -diff : diff;

            var msg = new JsonObject
            {
                ["type"] = "axis",
                ["axis"] = axis,
                ["mode"] = mode,
                ["diff"] = adjustedDiff,
            };

            // Add mode-specific params
            if (mode == "hold" || mode == "spring")
            {
                actionParameters.TryGetString(SensitivityControl, out var sensStr);
                var sensitivity = int.TryParse(sensStr, out var sensInt) ? sensInt / 1000f : 0.02f;
                msg["sensitivity"] = sensitivity;
            }

            if (mode == "spring")
            {
                actionParameters.TryGetString(DecayRateControl, out var decayStr);
                var decay = int.TryParse(decayStr, out var decayInt) ? decayInt / 100f : 0.95f;
                msg["decayRate"] = decay;
            }

            if (mode == "detent")
            {
                actionParameters.TryGetString(StepCountControl, out var stepsStr);
                var steps = int.TryParse(stepsStr, out var stepsInt) ? stepsInt : 5;
                msg["steps"] = steps;
            }

            _ = _connection?.SendAsync(msg);

            this.AdjustmentValueChanged();
        }

        protected override void RunCommand(ActionEditorActionParameters actionParameters)
        {
            // Encoder press = reset to configured position
            if (!actionParameters.TryGetString(AxisControl, out var axisStr)) return;
            if (!int.TryParse(axisStr, out var axis)) return;

            actionParameters.TryGetString(ResetPositionControl, out var resetStr);
            var resetPos = int.TryParse(resetStr, out var resetInt) ? resetInt / 100f : 0.5f;

            var msg = new JsonObject
            {
                ["type"] = "reset",
                ["axis"] = axis,
                ["position"] = resetPos,
            };

            _ = _connection?.SendAsync(msg);

            this.AdjustmentValueChanged();
        }

        protected override string GetAdjustmentValue(ActionEditorActionParameters actionParameters)
        {
            if (_stateDisplay == null) return "---";
            if (!actionParameters.TryGetString(AxisControl, out var axisStr)) return "---";
            if (!int.TryParse(axisStr, out var axis)) return "---";
            return _stateDisplay.GetAxisDisplayString(axis);
        }
    }
}
```

Note: The exact Action Editor API (`ActionEditorListbox`, `AddControlEx`, `ActionEditorSlider`, etc.) may differ from the documented C# SDK. The names and patterns above are based on the SDK documentation fetched earlier. Verify method signatures against the actual SDK during implementation. The `_connection` and `_stateDisplay` fields need to be injected — either via the Plugin class or by accessing the plugin instance. This wiring is done in ApricadabraPlugin.Load().

- [ ] **Step 2: Verify it compiles**

Run: `cd loupedeck-plugin && dotnet build 2>&1 | tail -10`

Expected: May have compilation errors due to SDK API differences. Adjust method names and signatures to match the actual Logi Actions SDK.

- [ ] **Step 3: Commit**

```bash
git add loupedeck-plugin/src/Actions/AxisAdjustment.cs
git commit -m "feat(plugin): add AxisAdjustment with Action Editor for Hold/Spring/Detent modes"
```

---

## Task 15: Loupedeck Plugin — AxisButtonAdjustment action

**Files:**
- Create: `loupedeck-plugin/src/Actions/AxisButtonAdjustment.cs`

- [ ] **Step 1: Implement AxisButtonAdjustment**

```csharp
// loupedeck-plugin/src/Actions/AxisButtonAdjustment.cs
using System;
using System.Text.Json.Nodes;

namespace Loupedeck.ApricadabraPlugin
{
    public class AxisButtonAdjustment : ActionEditorAdjustment
    {
        private CoreConnection _connection;
        private StateDisplay _stateDisplay;

        private const string AxisControl = "axis";
        private const string InvertControl = "invert";
        private const string SensitivityControl = "sensitivity";
        private const string ButtonControl = "button";

        public AxisButtonAdjustment()
            : base(hasReset: true)
        {
            this.DisplayName = "vJoy Axis + Button";
            this.Description = "Dial controls axis, encoder press fires button";
            this.GroupName = "Apricadabra";

            this.ActionEditor.AddControlEx(
                new ActionEditorListbox(name: AxisControl, labelText: "Axis").SetRequired()
            );
            this.ActionEditor.AddControlEx(
                new ActionEditorCheckbox(name: InvertControl, labelText: "Invert").SetDefaultValue(false)
            );
            this.ActionEditor.AddControlEx(
                new ActionEditorSlider(name: SensitivityControl, labelText: "Sensitivity")
                    .SetValues(1, 100, 1).SetDefaultValue(20).SetFormatString("{0}%")
            );
            this.ActionEditor.AddControlEx(
                new ActionEditorListbox(name: ButtonControl, labelText: "Button").SetRequired()
            );

            this.ActionEditor.ListboxItemsRequested += OnListboxItemsRequested;
        }

        private void OnListboxItemsRequested(object sender, ActionEditorListboxItemsRequestedEventArgs e)
        {
            if (e.ControlName == AxisControl)
            {
                e.AddItem("1", "X", null);
                e.AddItem("2", "Y", null);
                e.AddItem("3", "Z", null);
                e.AddItem("4", "Rx", null);
                e.AddItem("5", "Ry", null);
                e.AddItem("6", "Rz", null);
                e.AddItem("7", "Slider 1", null);
                e.AddItem("8", "Slider 2", null);
            }
            else if (e.ControlName == ButtonControl)
            {
                for (int i = 1; i <= 128; i++)
                    e.AddItem(i.ToString(), $"Button {i}", null);
            }
        }

        protected override void ApplyAdjustment(ActionEditorActionParameters actionParameters, int diff)
        {
            if (!actionParameters.TryGetString(AxisControl, out var axisStr)) return;
            if (!int.TryParse(axisStr, out var axis)) return;

            actionParameters.TryGetBoolean(InvertControl, out var invert);
            actionParameters.TryGetString(SensitivityControl, out var sensStr);
            var sensitivity = int.TryParse(sensStr, out var sensInt) ? sensInt / 1000f : 0.02f;

            var msg = new JsonObject
            {
                ["type"] = "axis",
                ["axis"] = axis,
                ["mode"] = "hold",
                ["diff"] = invert ? -diff : diff,
                ["sensitivity"] = sensitivity,
            };

            _ = _connection?.SendAsync(msg);
            this.AdjustmentValueChanged();
        }

        protected override void RunCommand(ActionEditorActionParameters actionParameters)
        {
            if (!actionParameters.TryGetString(ButtonControl, out var btnStr)) return;
            if (!int.TryParse(btnStr, out var button)) return;

            // Fire momentary press
            var downMsg = new JsonObject
            {
                ["type"] = "button",
                ["button"] = button,
                ["mode"] = "momentary",
                ["state"] = "down",
            };
            _ = _connection?.SendAsync(downMsg);

            // Schedule release after 50ms
            System.Threading.Tasks.Task.Delay(50).ContinueWith(_ =>
            {
                var upMsg = new JsonObject
                {
                    ["type"] = "button",
                    ["button"] = button,
                    ["mode"] = "momentary",
                    ["state"] = "up",
                };
                _ = _connection?.SendAsync(upMsg);
            });
        }

        protected override string GetAdjustmentValue(ActionEditorActionParameters actionParameters)
        {
            if (_stateDisplay == null) return "---";
            if (!actionParameters.TryGetString(AxisControl, out var axisStr)) return "---";
            if (!int.TryParse(axisStr, out var axis)) return "---";
            return _stateDisplay.GetAxisDisplayString(axis);
        }
    }
}
```

- [ ] **Step 2: Verify it compiles**

Run: `cd loupedeck-plugin && dotnet build 2>&1 | tail -5`

- [ ] **Step 3: Commit**

```bash
git add loupedeck-plugin/src/Actions/AxisButtonAdjustment.cs
git commit -m "feat(plugin): add AxisButtonAdjustment combo action"
```

---

## Task 16: Loupedeck Plugin — ButtonCommand action

**Files:**
- Create: `loupedeck-plugin/src/Actions/ButtonCommand.cs`

- [ ] **Step 1: Implement ButtonCommand**

```csharp
// loupedeck-plugin/src/Actions/ButtonCommand.cs
using System;
using System.Text.Json.Nodes;

namespace Loupedeck.ApricadabraPlugin
{
    public class ButtonCommand : ActionEditorCommand
    {
        private CoreConnection _connection;

        private const string ModeControl = "mode";
        private const string ButtonControl = "button";
        private const string DelayControl = "delay";
        private const string RateControl = "rate";
        private const string ShortButtonControl = "shortButton";
        private const string LongButtonControl = "longButton";
        private const string ThresholdControl = "threshold";
        private const string AxisControl = "axis";
        private const string ResetPositionControl = "resetPosition";

        public ButtonCommand()
        {
            this.DisplayName = "vJoy Button";
            this.Description = "Map a button to a vJoy button or axis reset";
            this.GroupName = "Apricadabra";

            this.ActionEditor.AddControlEx(
                new ActionEditorListbox(name: ModeControl, labelText: "Mode").SetRequired()
            );

            // Button (most modes)
            this.ActionEditor.AddControlEx(
                new ActionEditorListbox(name: ButtonControl, labelText: "Button")
            );

            // Double press delay
            this.ActionEditor.AddControlEx(
                new ActionEditorSlider(name: DelayControl, labelText: "Delay (ms)")
                    .SetValues(10, 200, 5).SetDefaultValue(50)
            );

            // Rapid fire rate
            this.ActionEditor.AddControlEx(
                new ActionEditorSlider(name: RateControl, labelText: "Rate (ms)")
                    .SetValues(20, 500, 10).SetDefaultValue(100)
            );

            // Long/Short buttons
            this.ActionEditor.AddControlEx(
                new ActionEditorListbox(name: ShortButtonControl, labelText: "Short Press Button")
            );
            this.ActionEditor.AddControlEx(
                new ActionEditorListbox(name: LongButtonControl, labelText: "Long Press Button")
            );
            this.ActionEditor.AddControlEx(
                new ActionEditorSlider(name: ThresholdControl, labelText: "Hold Threshold (ms)")
                    .SetValues(100, 2000, 50).SetDefaultValue(500)
            );

            // Reset Axis controls
            this.ActionEditor.AddControlEx(
                new ActionEditorListbox(name: AxisControl, labelText: "Axis")
            );
            this.ActionEditor.AddControlEx(
                new ActionEditorSlider(name: ResetPositionControl, labelText: "Reset Position")
                    .SetValues(0, 100, 1).SetDefaultValue(50).SetFormatString("{0}%")
            );

            this.ActionEditor.ListboxItemsRequested += OnListboxItemsRequested;
            this.ActionEditor.ControlValueChanged += OnControlValueChanged;
        }

        private void OnListboxItemsRequested(object sender, ActionEditorListboxItemsRequestedEventArgs e)
        {
            if (e.ControlName == ModeControl)
            {
                e.AddItem("momentary", "Momentary", "Held while pressed");
                e.AddItem("toggle", "Toggle", "On/off on each press");
                e.AddItem("pulse", "Pulse", "Brief press/release");
                e.AddItem("double", "Double Press", "Two rapid pulses");
                e.AddItem("rapid", "Rapid Fire", "Auto-repeats while held");
                e.AddItem("longshort", "Long/Short", "Different buttons for tap vs hold");
                e.AddItem("resetaxis", "Reset Axis", "Reset an axis to a position");
            }
            else if (e.ControlName == ButtonControl || e.ControlName == ShortButtonControl || e.ControlName == LongButtonControl)
            {
                for (int i = 1; i <= 128; i++)
                    e.AddItem(i.ToString(), $"Button {i}", null);
            }
            else if (e.ControlName == AxisControl)
            {
                e.AddItem("1", "X", null);
                e.AddItem("2", "Y", null);
                e.AddItem("3", "Z", null);
                e.AddItem("4", "Rx", null);
                e.AddItem("5", "Ry", null);
                e.AddItem("6", "Rz", null);
                e.AddItem("7", "Slider 1", null);
                e.AddItem("8", "Slider 2", null);
            }
        }

        private void OnControlValueChanged(object sender, ActionEditorControlValueChangedEventArgs e)
        {
            // Dynamic visibility based on mode selection
            // Show/hide controls based on which mode is active
        }

        protected override void RunCommand(ActionEditorActionParameters actionParameters)
        {
            if (!actionParameters.TryGetString(ModeControl, out var mode)) return;

            if (mode == "resetaxis")
            {
                HandleResetAxis(actionParameters);
                return;
            }

            if (!actionParameters.TryGetString(ButtonControl, out var btnStr)) return;
            if (!int.TryParse(btnStr, out var button)) return;

            var msg = new JsonObject
            {
                ["type"] = "button",
                ["button"] = button,
                ["mode"] = mode,
            };

            switch (mode)
            {
                case "momentary":
                    msg["state"] = "down";
                    _ = _connection?.SendAsync(msg);
                    // Release handled by KeyUp if available, or pulse-style
                    break;

                case "toggle":
                    msg["state"] = "down";
                    _ = _connection?.SendAsync(msg);
                    break;

                case "pulse":
                    _ = _connection?.SendAsync(msg);
                    break;

                case "double":
                    actionParameters.TryGetString(DelayControl, out var delayStr);
                    msg["delay"] = int.TryParse(delayStr, out var delay) ? delay : 50;
                    _ = _connection?.SendAsync(msg);
                    break;

                case "rapid":
                    actionParameters.TryGetString(RateControl, out var rateStr);
                    msg["rate"] = int.TryParse(rateStr, out var rate) ? rate : 100;
                    msg["state"] = "down";
                    _ = _connection?.SendAsync(msg);
                    break;

                case "longshort":
                    HandleLongShortDown(actionParameters);
                    break;
            }
        }

        private void HandleLongShortDown(ActionEditorActionParameters actionParameters)
        {
            actionParameters.TryGetString(ShortButtonControl, out var shortStr);
            actionParameters.TryGetString(LongButtonControl, out var longStr);
            actionParameters.TryGetString(ThresholdControl, out var threshStr);

            var msg = new JsonObject
            {
                ["type"] = "button",
                ["mode"] = "longshort",
                ["state"] = "down",
                ["shortButton"] = int.TryParse(shortStr, out var sb) ? sb : 1,
                ["longButton"] = int.TryParse(longStr, out var lb) ? lb : 2,
                ["threshold"] = int.TryParse(threshStr, out var th) ? th : 500,
            };
            _ = _connection?.SendAsync(msg);
        }

        private void HandleResetAxis(ActionEditorActionParameters actionParameters)
        {
            if (!actionParameters.TryGetString(AxisControl, out var axisStr)) return;
            if (!int.TryParse(axisStr, out var axis)) return;

            actionParameters.TryGetString(ResetPositionControl, out var resetStr);
            var resetPos = int.TryParse(resetStr, out var resetInt) ? resetInt / 100f : 0.5f;

            var msg = new JsonObject
            {
                ["type"] = "reset",
                ["axis"] = axis,
                ["position"] = resetPos,
            };
            _ = _connection?.SendAsync(msg);
        }
    }
}
```

**IMPORTANT — Press/Release concern:** `ActionEditorCommand.RunCommand` fires once on button press. Modes like Momentary and Rapid Fire require detecting the button release. The Loupedeck SDK's `ActionEditorCommand` may not support key-up events. **Investigate this before implementing Task 16:**

1. Check if the SDK provides an `OnKeyUp` override or `KeyUpCommand` method on `ActionEditorCommand`.
2. If not, Momentary and Rapid modes would need to use `ActionEditorAdjustment` (which has both press and release via `RunCommand` and `ApplyAdjustment`) or a `PluginDynamicCommand` with key-up support.
3. Worst case, Momentary becomes Pulse (brief press/release) and Rapid becomes a toggle that starts/stops rapid fire on each press. Document whichever approach works.

- [ ] **Step 2: Verify it compiles**

Run: `cd loupedeck-plugin && dotnet build 2>&1 | tail -5`

- [ ] **Step 3: Commit**

```bash
git add loupedeck-plugin/src/Actions/ButtonCommand.cs
git commit -m "feat(plugin): add ButtonCommand with all mode dropdowns via Action Editor"
```

---

## Task 17: Loupedeck Plugin — Wire up Plugin.Load with connection and state

**Files:**
- Modify: `loupedeck-plugin/src/ApricadabraPlugin.cs`

- [ ] **Step 1: Update ApricadabraPlugin to initialize CoreConnection and StateDisplay**

```csharp
// loupedeck-plugin/src/ApricadabraPlugin.cs
using System;

namespace Loupedeck.ApricadabraPlugin
{
    public class ApricadabraPlugin : Plugin
    {
        public CoreConnection Connection { get; private set; }
        public StateDisplay State { get; private set; }

        public override bool UsesApplicationApiOnly => true;
        public override bool HasNoApplication => true;

        public override void Load()
        {
            this.State = new StateDisplay();
            this.Connection = new CoreConnection();

            this.Connection.OnStateUpdate += msg =>
            {
                this.State.UpdateFromState(msg);
                this.OnPluginStatusChanged(Loupedeck.PluginStatus.Normal, "Connected");
            };

            this.Connection.OnError += (code, message) =>
            {
                this.State.ErrorMessage = message;
                this.OnPluginStatusChanged(Loupedeck.PluginStatus.Error, message);
            };

            this.Connection.OnDisconnected += () =>
            {
                this.State.ConnectionStatus = "Disconnected";
                this.OnPluginStatusChanged(Loupedeck.PluginStatus.Warning, "Disconnected from core");
            };

            this.Connection.OnShutdown += () =>
            {
                this.State.ConnectionStatus = "Core shutting down";
                this.OnPluginStatusChanged(Loupedeck.PluginStatus.Warning, "Core shutting down");
            };

            _ = this.Connection.ConnectAsync();
        }

        public override void Unload()
        {
            this.Connection?.Dispose();
        }
    }
}
```

Note: The action classes (AxisAdjustment, AxisButtonAdjustment, ButtonCommand) need references to `Connection` and `State`. In the Logi Actions SDK, actions can access their parent plugin via `this.Plugin`. Cast to `ApricadabraPlugin` to get the connection:

```csharp
var plugin = (ApricadabraPlugin)this.Plugin;
var connection = plugin.Connection;
var stateDisplay = plugin.State;
```

Update the action classes to use this pattern instead of the `_connection` and `_stateDisplay` fields. Replace field references with property lookups:

```csharp
private CoreConnection Connection => ((ApricadabraPlugin)this.Plugin).Connection;
private StateDisplay StateDisplay => ((ApricadabraPlugin)this.Plugin).State;
```

- [ ] **Step 2: Update action classes to use plugin references**

In each action class (AxisAdjustment, AxisButtonAdjustment, ButtonCommand), replace:
- `private CoreConnection _connection;` with `private CoreConnection Connection => ((ApricadabraPlugin)this.Plugin).Connection;`
- `private StateDisplay _stateDisplay;` with `private StateDisplay StateDisplay => ((ApricadabraPlugin)this.Plugin).State;`
- Update all references from `_connection` to `Connection` and `_stateDisplay` to `StateDisplay`.

- [ ] **Step 3: Verify it compiles**

Run: `cd loupedeck-plugin && dotnet build 2>&1 | tail -5`

Expected: Compiles. The exact Plugin/Action API may need adjustments based on the SDK version.

- [ ] **Step 4: Commit**

```bash
git add loupedeck-plugin/src/ApricadabraPlugin.cs loupedeck-plugin/src/Actions/
git commit -m "feat(plugin): wire up CoreConnection and StateDisplay in plugin lifecycle"
```

---

## Task 18: End-to-end manual testing

This task is manual — no automated test. It verifies the full pipeline works with real hardware.

**Prerequisites:**
- Windows machine with vJoy installed and configured (1 device, 8 axes, 128 buttons)
- Loupedeck software installed with a Loupedeck device connected
- Core built: `cd core && cargo build --release`
- Plugin built and linked to Logi Plugin Service

- [ ] **Step 1: Start the core manually**

Run: `core/target/release/apricadabra-core.exe`

Expected: Logs "Apricadabra Core starting", "Acquired vJoy device 1".

- [ ] **Step 2: Verify vJoy device appears**

Open vJoy Monitor or "Set up USB game controllers" in Windows. The vJoy device should show as connected.

- [ ] **Step 3: Install and load the Loupedeck plugin**

Build and link the plugin:
```bash
cd loupedeck-plugin
dotnet build
# Use logiplugintool or manual copy to install
```

Open the Loupedeck software. Apricadabra should appear in the plugin list.

- [ ] **Step 4: Assign a vJoy Axis action to a dial**

In the Loupedeck software, drag "vJoy Axis" onto a dial. Configure:
- Mode: Hold
- Axis: X
- Sensitivity: 50%

- [ ] **Step 5: Turn the dial and verify**

Turn the dial. In vJoy Monitor, axis X should move. The dial's LCD should show the current percentage.

- [ ] **Step 6: Assign a vJoy Button action to a button**

Drag "vJoy Button" onto a button. Configure:
- Mode: Momentary
- Button: 1

- [ ] **Step 7: Press the button and verify**

Press the Loupedeck button. In vJoy Monitor, button 1 should light up while held.

- [ ] **Step 8: Test in a game**

Launch a sim game (e.g. Elite Dangerous, DCS, Star Citizen). Go to controller settings. The vJoy device should appear. Map the axis/button and verify they respond to Loupedeck input.

- [ ] **Step 9: Test disconnect/reconnect**

Kill the core process. Plugin LCD should show "Disconnected". Core should auto-restart (or restart manually). Plugin should reconnect and LCD should show values again.

---

## Task 19: Commit and tag release

- [ ] **Step 1: Final commit**

```bash
git add -A
git status # Review all changes
git commit -m "feat: complete Apricadabra v0.1.0 - Loupedeck to vJoy bridge"
```

- [ ] **Step 2: Tag**

```bash
git tag v0.1.0
```
