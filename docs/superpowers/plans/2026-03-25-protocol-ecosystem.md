# Protocol & Ecosystem Changes Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Evolve the Apricadabra protocol to v2 with API negotiation, core upgrade flow, graceful degradation, and a formal protocol spec — laying the foundation for a plugin ecosystem.

**Architecture:** Protocol v2 adds `commands`/`apiStatus` fields to the handshake, a `core_upgrade`/`core_restarting` message flow for live core upgrades, `--debug-messages` flag for developer warnings, and a standardized bindings schema. Version check changes from strict equality to minimum-version. All existing plugins (Loupedeck, Stream Deck) are updated together.

**Tech Stack:** Rust (core), C# .NET 8 (Loupedeck plugin), TypeScript/Node.js (Stream Deck plugin), serde/serde_json, tokio

**Spec:** `docs/superpowers/specs/2026-03-25-protocol-ecosystem-design.md`

---

## File Structure

### Core (Rust) — Modified Files
- `core/src/protocol.rs` — Add new message variants: `commands` field on Hello, `apiStatus`/`coreVersion` on Welcome, new `CoreUpgrade`/`CoreRestarting`/`Warning` messages, `ApiStatus` enum
- `core/src/server.rs` — Version check change (strict → minimum), API negotiation in handshake, `core_upgrade` handler, `core_restarting` broadcast, `--debug-messages` warning dispatch, graceful unknown command handling
- `core/src/main.rs` — Add `--debug-messages` CLI flag, pass to Server, add `--version` flag
- `core/src/config.rs` — No changes needed

### Core (Rust) — New Files
- `core/src/api_registry.rs` — Central registry of known commands with their status (exists/deprecated), used by handshake negotiation
- `core/apricadabra-core.version` — Plain text version file read by plugins

### Core Tests — Modified/New Files
- `core/tests/protocol_test.rs` — Tests for new message types (v2 hello, v2 welcome, core_upgrade, core_restarting, warning)
- `core/tests/api_registry_test.rs` — Tests for API status resolution
- `core/tests/server_test.rs` — Tests for version negotiation, upgrade flow, unknown command handling

### Plugin Files — Modified
- `loupedeck-plugin/ApricadabraPlugin/src/CoreConnection.cs` — v2 hello (send commands array), parse apiStatus/coreVersion from welcome, handle `core_restarting`, coreStartTimeout logic
- `streamdeck-plugin/src/core-connection.ts` — Same v2 changes as Loupedeck

### Documentation — New Files
- `core/docs/protocol.md` — Formal protocol spec (source of truth)
- `core/CHANGELOG.md` — Deprecation and change tracking

---

## Task 1: Protocol Types — v2 Hello and Welcome

**Files:**
- Modify: `core/src/protocol.rs:1-94`
- Test: `core/tests/protocol_test.rs`

- [ ] **Step 1: Write failing tests for v2 Hello with commands field**

Add to `core/tests/protocol_test.rs`:

```rust
#[test]
fn test_parse_hello_v2_with_commands() {
    let json = r#"{"type":"hello","version":2,"name":"trackpad","broadcastPort":19874,"commands":["axis","button","reset"]}"#;
    let msg: ClientMessage = serde_json::from_str(json).unwrap();
    match msg {
        ClientMessage::Hello { version, name, broadcast_port, commands } => {
            assert_eq!(version, 2);
            assert_eq!(name, "trackpad");
            assert_eq!(broadcast_port, Some(19874));
            assert_eq!(commands, Some(vec!["axis".to_string(), "button".to_string(), "reset".to_string()]));
        }
        _ => panic!("Expected Hello"),
    }
}

#[test]
fn test_parse_hello_v2_without_commands() {
    let json = r#"{"type":"hello","version":2,"name":"loupedeck"}"#;
    let msg: ClientMessage = serde_json::from_str(json).unwrap();
    match msg {
        ClientMessage::Hello { commands, .. } => {
            assert_eq!(commands, None);
        }
        _ => panic!("Expected Hello"),
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `cd /home/bc/projects/apricadabra/core && cargo test test_parse_hello_v2 -- --nocapture`
Expected: Compilation error — `commands` field doesn't exist on Hello variant

- [ ] **Step 3: Add `commands` field to Hello variant**

In `core/src/protocol.rs`, update the `Hello` variant (lines 8-13):

```rust
    Hello {
        version: u32,
        name: String,
        #[serde(default, rename = "broadcastPort")]
        broadcast_port: Option<u16>,
        #[serde(default)]
        commands: Option<Vec<String>>,
    },
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `cd /home/bc/projects/apricadabra/core && cargo test test_parse_hello_v2 -- --nocapture`
Expected: PASS

- [ ] **Step 5: Write failing tests for v2 Welcome with apiStatus and coreVersion**

Add to `core/tests/protocol_test.rs`:

```rust
#[test]
fn test_serialize_welcome_v2() {
    let mut api_status = std::collections::HashMap::new();
    api_status.insert("axis".to_string(), ApiStatus::Exists);
    api_status.insert("button".to_string(), ApiStatus::Deprecated);
    api_status.insert("haptic".to_string(), ApiStatus::Undefined);

    let msg = ServerMessage::Welcome {
        version: 2,
        axes: vec![(1, 0.5)].into_iter().collect(),
        buttons: vec![(1, true)].into_iter().collect(),
        api_status: Some(api_status),
        core_version: Some("1.2.0".to_string()),
    };
    let json = serde_json::to_string(&msg).unwrap();
    assert!(json.contains("\"apiStatus\""));
    assert!(json.contains("\"coreVersion\""));
    assert!(json.contains("\"exists\""));
    assert!(json.contains("\"deprecated\""));
    assert!(json.contains("\"undefined\""));
}

#[test]
fn test_serialize_welcome_v1_compat() {
    let msg = ServerMessage::Welcome {
        version: 1,
        axes: vec![(1, 0.5)].into_iter().collect(),
        buttons: vec![(1, true)].into_iter().collect(),
        api_status: None,
        core_version: None,
    };
    let json = serde_json::to_string(&msg).unwrap();
    assert!(json.contains("\"type\":\"welcome\""));
    assert!(!json.contains("apiStatus"));
    assert!(!json.contains("coreVersion"));
}
```

- [ ] **Step 6: Run tests to verify they fail**

Run: `cd /home/bc/projects/apricadabra/core && cargo test test_serialize_welcome_v -- --nocapture`
Expected: Compilation error — `ApiStatus` type and new Welcome fields don't exist

- [ ] **Step 7: Add ApiStatus enum and update Welcome variant**

In `core/src/protocol.rs`, add the `ApiStatus` enum and update `ServerMessage::Welcome`:

```rust
#[derive(Debug, Serialize, Deserialize, PartialEq, Clone)]
#[serde(rename_all = "snake_case")]
pub enum ApiStatus {
    Exists,
    Deprecated,
    Undefined,
}
```

Update the `Welcome` variant:

```rust
    Welcome {
        version: u32,
        axes: HashMap<u8, f32>,
        buttons: HashMap<u8, bool>,
        #[serde(skip_serializing_if = "Option::is_none", rename = "apiStatus")]
        api_status: Option<HashMap<String, ApiStatus>>,
        #[serde(skip_serializing_if = "Option::is_none", rename = "coreVersion")]
        core_version: Option<String>,
    },
```

- [ ] **Step 8: Fix existing Welcome usages in server.rs and tests**

In `core/src/server.rs` line 330, update the Welcome construction:

```rust
let welcome = ServerMessage::Welcome {
    version: PROTOCOL_VERSION,
    axes,
    buttons,
    api_status: None,
    core_version: None,
};
```

In `core/tests/protocol_test.rs`, update `test_serialize_welcome` (line 128):

```rust
#[test]
fn test_serialize_welcome() {
    let msg = ServerMessage::Welcome {
        version: 1,
        axes: vec![(1, 0.5), (2, 0.73)].into_iter().collect(),
        buttons: vec![(1, true)].into_iter().collect(),
        api_status: None,
        core_version: None,
    };
    let json = serde_json::to_string(&msg).unwrap();
    assert!(json.contains("\"type\":\"welcome\""));
    assert!(json.contains("\"version\":1"));
}
```

Also update the Welcome construction in `core/tests/integration_test.rs` if it constructs Welcome messages directly.

- [ ] **Step 9: Run full test suite to verify everything passes**

Run: `cd /home/bc/projects/apricadabra/core && cargo test -- --nocapture`
Expected: All tests PASS

- [ ] **Step 10: Commit**

```bash
git add core/src/protocol.rs core/tests/protocol_test.rs core/src/server.rs core/tests/integration_test.rs
git commit -m "feat(protocol): add v2 hello commands field, welcome apiStatus/coreVersion, and ApiStatus enum"
```

---

## Task 2: Protocol Types — New Message Variants

**Files:**
- Modify: `core/src/protocol.rs`
- Test: `core/tests/protocol_test.rs`

- [ ] **Step 1: Write failing tests for CoreUpgrade, CoreRestarting, and Warning messages**

Add to `core/tests/protocol_test.rs`:

```rust
#[test]
fn test_parse_core_upgrade() {
    let json = r#"{"type":"core_upgrade","newVersion":"1.3.0","estimatedStartupMs":15000}"#;
    let msg: ClientMessage = serde_json::from_str(json).unwrap();
    match msg {
        ClientMessage::CoreUpgrade { new_version, estimated_startup_ms } => {
            assert_eq!(new_version, "1.3.0");
            assert_eq!(estimated_startup_ms, Some(15000));
        }
        _ => panic!("Expected CoreUpgrade"),
    }
}

#[test]
fn test_serialize_core_restarting() {
    let msg = ServerMessage::CoreRestarting {
        core_start_timeout: 15000,
        reason: "upgrade".to_string(),
        requested_by: Some("trackpad".to_string()),
    };
    let json = serde_json::to_string(&msg).unwrap();
    assert!(json.contains("\"type\":\"core_restarting\""));
    assert!(json.contains("\"coreStartTimeout\":15000"));
    assert!(json.contains("\"reason\":\"upgrade\""));
    assert!(json.contains("\"requestedBy\":\"trackpad\""));
}

#[test]
fn test_serialize_core_restarting_shutdown() {
    let msg = ServerMessage::CoreRestarting {
        core_start_timeout: 15000,
        reason: "shutdown".to_string(),
        requested_by: None,
    };
    let json = serde_json::to_string(&msg).unwrap();
    assert!(json.contains("\"type\":\"core_restarting\""));
    assert!(!json.contains("requestedBy"));
}

#[test]
fn test_serialize_warning() {
    let mut context = HashMap::new();
    context.insert("actionType".to_string(), "button".to_string());
    context.insert("mode".to_string(), "turbo".to_string());

    let msg = ServerMessage::Warning {
        code: "unknown_mode".to_string(),
        message: "Unknown mode 'turbo' for button action".to_string(),
        context,
    };
    let json = serde_json::to_string(&msg).unwrap();
    assert!(json.contains("\"type\":\"warning\""));
    assert!(json.contains("\"code\":\"unknown_mode\""));
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `cd /home/bc/projects/apricadabra/core && cargo test test_parse_core_upgrade test_serialize_core_restarting test_serialize_warning -- --nocapture`
Expected: Compilation error — new variants don't exist

- [ ] **Step 3: Add new variants to ClientMessage and ServerMessage**

In `core/src/protocol.rs`, add to `ClientMessage`:

```rust
    CoreUpgrade {
        #[serde(rename = "newVersion")]
        new_version: String,
        #[serde(default, rename = "estimatedStartupMs")]
        estimated_startup_ms: Option<u64>,
    },
```

Add to `ServerMessage`:

```rust
    CoreRestarting {
        #[serde(rename = "coreStartTimeout")]
        core_start_timeout: u64,
        reason: String,
        #[serde(skip_serializing_if = "Option::is_none", rename = "requestedBy")]
        requested_by: Option<String>,
    },
    Warning {
        code: String,
        message: String,
        context: HashMap<String, String>,
    },
```

- [ ] **Step 4: Update process_command to handle CoreUpgrade (no-op for now)**

In `core/src/server.rs` `process_command()`, update the catch-all at line 289:

```rust
ClientMessage::Hello { .. } | ClientMessage::HeartbeatAck | ClientMessage::CoreUpgrade { .. } => {}
```

- [ ] **Step 5: Run full test suite**

Run: `cd /home/bc/projects/apricadabra/core && cargo test -- --nocapture`
Expected: All tests PASS

- [ ] **Step 6: Commit**

```bash
git add core/src/protocol.rs core/tests/protocol_test.rs core/src/server.rs
git commit -m "feat(protocol): add CoreUpgrade, CoreRestarting, and Warning message types"
```

---

## Task 3: API Registry

**Files:**
- Create: `core/src/api_registry.rs`
- Modify: `core/src/lib.rs`
- Create: `core/tests/api_registry_test.rs`

- [ ] **Step 1: Write failing tests for API registry**

Create `core/tests/api_registry_test.rs`:

```rust
use apricadabra_core::api_registry::ApiRegistry;
use apricadabra_core::protocol::ApiStatus;

#[test]
fn test_known_commands_return_exists() {
    let registry = ApiRegistry::new();
    assert_eq!(registry.status("axis"), ApiStatus::Exists);
    assert_eq!(registry.status("button"), ApiStatus::Exists);
    assert_eq!(registry.status("reset"), ApiStatus::Exists);
}

#[test]
fn test_unknown_commands_return_undefined() {
    let registry = ApiRegistry::new();
    assert_eq!(registry.status("haptic"), ApiStatus::Undefined);
    assert_eq!(registry.status("foobar"), ApiStatus::Undefined);
}

#[test]
fn test_resolve_commands_list() {
    let registry = ApiRegistry::new();
    let result = registry.resolve(&["axis".to_string(), "button".to_string(), "haptic".to_string()]);
    assert_eq!(result.get("axis"), Some(&ApiStatus::Exists));
    assert_eq!(result.get("button"), Some(&ApiStatus::Exists));
    assert_eq!(result.get("haptic"), Some(&ApiStatus::Undefined));
}

#[test]
fn test_has_undefined() {
    let registry = ApiRegistry::new();
    let result = registry.resolve(&["axis".to_string(), "haptic".to_string()]);
    assert!(result.values().any(|s| *s == ApiStatus::Undefined));
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `cd /home/bc/projects/apricadabra/core && cargo test --test api_registry_test -- --nocapture`
Expected: Compilation error — module doesn't exist

- [ ] **Step 3: Implement ApiRegistry**

Create `core/src/api_registry.rs`:

```rust
use crate::protocol::ApiStatus;
use std::collections::HashMap;

pub struct ApiRegistry {
    commands: HashMap<String, ApiStatus>,
}

impl ApiRegistry {
    pub fn new() -> Self {
        let mut commands = HashMap::new();
        commands.insert("axis".to_string(), ApiStatus::Exists);
        commands.insert("button".to_string(), ApiStatus::Exists);
        commands.insert("reset".to_string(), ApiStatus::Exists);
        commands.insert("shutdown".to_string(), ApiStatus::Exists);
        Self { commands }
    }

    pub fn status(&self, command: &str) -> ApiStatus {
        self.commands.get(command).cloned().unwrap_or(ApiStatus::Undefined)
    }

    pub fn resolve(&self, requested: &[String]) -> HashMap<String, ApiStatus> {
        requested.iter().map(|cmd| (cmd.clone(), self.status(cmd))).collect()
    }
}
```

- [ ] **Step 4: Add module to lib.rs**

In `core/src/lib.rs`, add:

```rust
pub mod api_registry;
```

- [ ] **Step 5: Run tests**

Run: `cd /home/bc/projects/apricadabra/core && cargo test --test api_registry_test -- --nocapture`
Expected: All PASS

- [ ] **Step 6: Commit**

```bash
git add core/src/api_registry.rs core/src/lib.rs core/tests/api_registry_test.rs
git commit -m "feat(core): add ApiRegistry for command status resolution"
```

---

## Task 4: Version Check — Strict Equality to Minimum Version

**Files:**
- Modify: `core/src/server.rs:16,319-326`
- Test: `core/tests/integration_test.rs`

- [ ] **Step 1: Write a test for minimum version acceptance**

In `core/tests/protocol_test.rs`, add a test that validates the version check logic. Since the version check lives in server.rs and is hard to unit test in isolation, we'll test at the protocol level that v1 and v2 hellos both parse correctly (already covered), and add an integration test comment noting the behavior change.

Add a comment-test in `core/tests/protocol_test.rs`:

```rust
#[test]
fn test_hello_v1_and_v2_both_parse() {
    // v1 hello (no commands) should still be accepted by v2 core
    let v1 = r#"{"type":"hello","version":1,"name":"loupedeck"}"#;
    let v1_msg: ClientMessage = serde_json::from_str(v1).unwrap();
    assert!(matches!(v1_msg, ClientMessage::Hello { version: 1, .. }));

    // v2 hello should be accepted by v2 core
    let v2 = r#"{"type":"hello","version":2,"name":"trackpad","commands":["axis"]}"#;
    let v2_msg: ClientMessage = serde_json::from_str(v2).unwrap();
    assert!(matches!(v2_msg, ClientMessage::Hello { version: 2, .. }));
}
```

- [ ] **Step 2: Run test to verify it passes (parsing already works)**

Run: `cd /home/bc/projects/apricadabra/core && cargo test test_hello_v1_and_v2 -- --nocapture`
Expected: PASS

- [ ] **Step 3: Update PROTOCOL_VERSION and version check in server.rs**

In `core/src/server.rs`:

Change line 16:
```rust
const PROTOCOL_VERSION: u32 = 2;
```

Change lines 319-326 (the version check in `handle_client`):
```rust
                if version > PROTOCOL_VERSION {
                    let err = ServerMessage::Error {
                        code: "unsupported_version".to_string(),
                        message: format!("Server supports protocol v{PROTOCOL_VERSION}, client sent v{version}"),
                    };
                    let _ = Self::send_message(&mut writer, &err).await;
                    return;
                }
```

This changes from `version != PROTOCOL_VERSION` (strict) to `version > PROTOCOL_VERSION` (minimum). Older plugins (v1) are accepted; plugins claiming a newer version than core supports are rejected.

- [ ] **Step 4: Run full test suite**

Run: `cd /home/bc/projects/apricadabra/core && cargo test -- --nocapture`
Expected: All PASS (integration test sends v1 hello which is now accepted by v2 core)

- [ ] **Step 5: Commit**

```bash
git add core/src/server.rs core/tests/protocol_test.rs
git commit -m "feat(core): bump protocol to v2, change version check from strict equality to minimum version"
```

---

## Task 5: API Negotiation in Handshake

**Files:**
- Modify: `core/src/server.rs:293-341` (handle_client)
- Modify: `core/src/server.rs:26-29` (Server struct)

- [ ] **Step 1: Add ApiRegistry to Server struct**

In `core/src/server.rs`, add import:

```rust
use crate::api_registry::ApiRegistry;
```

Update the `Server` struct and `new()`:

```rust
pub struct Server {
    config: Config,
    joystick: Box<dyn VirtualJoystick>,
    api_registry: ApiRegistry,
}

impl Server {
    pub fn new(config: Config, joystick: Box<dyn VirtualJoystick>) -> Self {
        Self {
            config,
            joystick,
            api_registry: ApiRegistry::new(),
        }
    }
```

- [ ] **Step 2: Thread ApiRegistry into handle_client**

Update `handle_client` signature to accept `Arc<ApiRegistry>`:

```rust
    async fn handle_client(
        client_id: u64,
        pipe: NamedPipeServer,
        axis_mgr: Arc<Mutex<AxisManager>>,
        button_mgr: Arc<Mutex<ButtonManager>>,
        broadcast_targets: Arc<Mutex<HashMap<u64, std::net::SocketAddr>>>,
        api_registry: Arc<ApiRegistry>,
    ) {
```

In `Server::run()`, create and pass the Arc:

```rust
let api_registry = Arc::new(self.api_registry);
```

Update the spawn call in the accept loop to clone and pass `api_registry`.

- [ ] **Step 3: Update Welcome construction to include apiStatus when commands are present**

In `handle_client`, after extracting Hello fields, build Welcome with negotiation:

```rust
ClientMessage::Hello { version, name, broadcast_port, commands } => {
    info!("Client {client_id} hello: {name} v{version}");
    if version > PROTOCOL_VERSION {
        let err = ServerMessage::Error {
            code: "unsupported_version".to_string(),
            message: format!("Server supports protocol v{PROTOCOL_VERSION}, client sent v{version}"),
        };
        let _ = Self::send_message(&mut writer, &err).await;
        return;
    }

    let axes = axis_mgr.lock().await.get_all();
    let buttons = button_mgr.lock().await.get_all();

    let (api_status, core_version) = match &commands {
        Some(cmds) => (
            Some(api_registry.resolve(cmds)),
            Some(env!("CARGO_PKG_VERSION").to_string()),
        ),
        None => (None, None),
    };

    let welcome = ServerMessage::Welcome {
        version: PROTOCOL_VERSION,
        axes,
        buttons,
        api_status,
        core_version,
    };
    if Self::send_message(&mut writer, &welcome).await.is_err() {
        return;
    }

    let port = broadcast_port.unwrap_or(UDP_BROADCAST_PORT);
    let addr: std::net::SocketAddr = format!("127.0.0.1:{port}").parse().unwrap();
    broadcast_targets.lock().await.insert(client_id, addr);
    info!("Client {client_id} ({name}) registered broadcast target: {addr}");
}
```

- [ ] **Step 4: Run full test suite**

Run: `cd /home/bc/projects/apricadabra/core && cargo test -- --nocapture`
Expected: All PASS

- [ ] **Step 5: Commit**

```bash
git add core/src/server.rs
git commit -m "feat(core): add API negotiation to handshake — resolve commands to exists/deprecated/undefined"
```

---

## Task 6: Core Upgrade Handler and CoreRestarting Broadcast

**Files:**
- Modify: `core/src/server.rs` (handle_client, run)

- [ ] **Step 1: Track connected clients' pipe writers for broadcasting**

This is the most complex change. The core needs to broadcast `core_restarting` to all connected plugins over their named pipe connections before shutting down. Currently, each client handler owns its own pipe writer. We need a shared structure for broadcast writes.

Add a broadcast channel for server→client messages:

```rust
use tokio::sync::broadcast;
```

In `Server::run()`, create a broadcast channel:

```rust
let (server_broadcast_tx, _) = broadcast::channel::<String>(16);
```

Pass `server_broadcast_tx.subscribe()` to each `handle_client` call, and pass `server_broadcast_tx.clone()` to the accept loop.

In `handle_client`, add a receiver arm in the heartbeat select! loop:

```rust
msg = server_broadcast_rx.recv() => {
    if let Ok(json) = msg {
        if writer.write_all(json.as_bytes()).await.is_err() {
            break;
        }
        let _ = writer.flush().await;
    }
}
```

- [ ] **Step 2: Handle CoreUpgrade in the pipe read loop**

In `handle_client`, the pipe currently only processes `HeartbeatAck`. Extend it to also handle `CoreUpgrade`:

When a `CoreUpgrade` is received on the pipe:
1. Compare `new_version` against `CARGO_PKG_VERSION` using semver comparison
2. If not strictly greater, send `Error { code: "upgrade_rejected", ... }` back on the pipe
3. If accepted, broadcast `CoreRestarting` via the broadcast channel
4. Signal the main run loop to shut down (via a new shutdown channel or the existing one)

Add `semver` crate to `Cargo.toml`:

```toml
semver = "1"
```

- [ ] **Step 3: Implement the upgrade flow in handle_client**

After parsing the pipe message in the heartbeat loop, add:

```rust
if let Ok(msg) = serde_json::from_str::<ClientMessage>(line.trim()) {
    match msg {
        ClientMessage::HeartbeatAck => {
            last_ack = Instant::now();
        }
        ClientMessage::CoreUpgrade { new_version, estimated_startup_ms } => {
            let current = semver::Version::parse(env!("CARGO_PKG_VERSION")).unwrap();
            let requested = match semver::Version::parse(&new_version) {
                Ok(v) => v,
                Err(_) => {
                    let err = ServerMessage::Error {
                        code: "invalid_version".to_string(),
                        message: format!("Invalid version format: {new_version}"),
                    };
                    let _ = Self::send_message(&mut writer, &err).await;
                    continue;
                }
            };

            if requested <= current {
                let err = ServerMessage::Error {
                    code: "upgrade_rejected".to_string(),
                    message: format!("Bundled version {new_version} is not newer than running version {}", env!("CARGO_PKG_VERSION")),
                };
                let _ = Self::send_message(&mut writer, &err).await;
            } else {
                let timeout = estimated_startup_ms.unwrap_or(15000);
                let restart_msg = ServerMessage::CoreRestarting {
                    core_start_timeout: timeout,
                    reason: "upgrade".to_string(),
                    requested_by: Some(client_name.clone()),
                };
                if let Ok(json) = serde_json::to_string(&restart_msg) {
                    let mut broadcast_json = json;
                    broadcast_json.push('\n');
                    let _ = server_broadcast_tx.send(broadcast_json);
                }
                let _ = upgrade_tx.send(true);
            }
        }
        _ => {}
    }
}
```

Note: `client_name` needs to be captured from the Hello and stored. `upgrade_tx` is a new channel that signals `Server::run()` to perform graceful shutdown (reusing the existing shutdown path).

- [ ] **Step 4: Wire upgrade signal into main run loop**

In `Server::run()`, add an `upgrade_tx`/`upgrade_rx` channel pair. In the main `tokio::select!` loop, add an arm watching `upgrade_rx`. When triggered, broadcast `CoreRestarting` to all plugins via named pipes, release vJoy, and exit cleanly.

- [ ] **Step 5: Run full test suite**

Run: `cd /home/bc/projects/apricadabra/core && cargo test -- --nocapture`
Expected: All PASS

- [ ] **Step 6: Commit**

```bash
git add core/src/server.rs core/Cargo.toml
git commit -m "feat(core): implement core upgrade flow with version guard and core_restarting broadcast"
```

---

## Task 7: `--debug-messages` Flag and Unknown Command Handling

**Files:**
- Modify: `core/src/main.rs`
- Modify: `core/src/server.rs`

- [ ] **Step 1: Add `--debug-messages` CLI flag**

In `core/src/main.rs`, after the existing `--debug` flag parsing (line 41):

```rust
let debug_messages = std::env::args().any(|a| a == "--debug-messages");
if debug_messages {
    info!("Debug messages enabled via --debug-messages flag");
}
```

- [ ] **Step 2: Pass debug_messages to Server**

Update `Server::new()` to accept and store `debug_messages: bool`. Update the call site in `main.rs`.

```rust
let server = Server::new(config, joystick, debug_messages);
```

Update struct:

```rust
pub struct Server {
    config: Config,
    joystick: Box<dyn VirtualJoystick>,
    api_registry: ApiRegistry,
    debug_messages: bool,
}
```

- [ ] **Step 3: Add graceful unknown command handling in process_command**

Currently, `process_command` receives a `ClientMessage` which is already successfully deserialized — unknown types fail at deserialization. To handle truly unknown types, modify the UDP command reception in `Server::run()` (around line 203):

```rust
if let Ok(text) = std::str::from_utf8(&cmd_buf[..len]) {
    let trimmed = text.trim();
    if trimmed.contains("\"shutdown\"") {
        info!("Received shutdown command via UDP");
        let _ = joystick.lock().await.release();
        break;
    }
    match serde_json::from_str::<ClientMessage>(trimmed) {
        Ok(msg) => {
            Self::process_command(&self.config, &axis_mgr, &button_mgr, msg).await;
        }
        Err(e) => {
            if self.debug_messages {
                // Try to extract type field for better error context
                if let Ok(raw) = serde_json::from_str::<serde_json::Value>(trimmed) {
                    let action_type = raw.get("type").and_then(|t| t.as_str()).unwrap_or("unknown");
                    let mode = raw.get("mode").and_then(|m| m.as_str()).unwrap_or("");
                    let warning = if !mode.is_empty() {
                        ServerMessage::Warning {
                            code: "unknown_mode".to_string(),
                            message: format!("Unknown mode '{mode}' for {action_type} action, defaulting to first mode"),
                            context: HashMap::from([
                                ("actionType".to_string(), action_type.to_string()),
                                ("mode".to_string(), mode.to_string()),
                            ]),
                        }
                    } else {
                        ServerMessage::Warning {
                            code: "unknown_action".to_string(),
                            message: format!("Unknown action type: {action_type}"),
                            context: HashMap::from([
                                ("actionType".to_string(), action_type.to_string()),
                            ]),
                        }
                    };
                    if let Ok(json) = serde_json::to_string(&warning) {
                        let mut warn_json = json;
                        warn_json.push('\n');
                        let _ = server_broadcast_tx.send(warn_json);
                    }
                }
            }
            tracing::debug!("Failed to parse command: {e}");
        }
    }
}
```

- [ ] **Step 4: Add `--version` flag for core version discovery**

In `core/src/main.rs`, before the `--stop` check:

```rust
if std::env::args().any(|a| a == "--version") {
    println!("{}", env!("CARGO_PKG_VERSION"));
    return Ok(());
}
```

- [ ] **Step 5: Create version file**

Create `core/apricadabra-core.version` containing the current version from `Cargo.toml`. This file should be updated as part of the release process.

- [ ] **Step 6: Run full test suite**

Run: `cd /home/bc/projects/apricadabra/core && cargo test -- --nocapture`
Expected: All PASS

- [ ] **Step 7: Commit**

```bash
git add core/src/main.rs core/src/server.rs core/apricadabra-core.version
git commit -m "feat(core): add --debug-messages flag, --version flag, and graceful unknown command handling"
```

---

## Task 8: Write Protocol Spec Document

**Files:**
- Create: `core/docs/protocol.md`

- [ ] **Step 1: Create the protocol spec**

Create `core/docs/protocol.md` documenting the complete protocol. Structure:

1. **Overview** — What Apricadabra is, what the protocol does
2. **Connection Model** — Named pipes (`\\.\pipe\apricadabra`) for handshake/heartbeat, UDP 19871 for commands, per-plugin broadcast ports
3. **Handshake Flow** — `hello`/`welcome` exchange, API negotiation with `commands`/`apiStatus`
4. **Message Reference** — Every message type with JSON schema, field descriptions, examples, defaults, required vs optional fields:
   - Client→Core: `hello`, `axis`, `button`, `reset`, `heartbeat_ack`, `core_upgrade`
   - Core→Client: `welcome`, `state`, `heartbeat`, `error`, `shutdown`, `core_restarting`, `warning`
5. **API Negotiation** — `exists`/`deprecated`/`undefined` semantics, deprecation lifecycle (permanent, never becomes undefined), plugin response guidance
6. **Core Upgrade Flow** — Step-by-step: detect undefined → send core_upgrade → version guard → broadcast core_restarting → graceful shutdown → relaunch → reconnect
7. **Lifecycle** — Auto-launch strategy (check `%APPDATA%/Apricadabra/` then plugin bundle), reconnection behavior, heartbeat (5s interval, 30s timeout), `coreStartTimeout` mechanism
8. **Unrecognized Action Handling** — Unknown mode defaults (`momentary`/`hold`), malformed/unknown action no-ops
9. **`--debug-messages` Flag** — Warning message format and codes
10. **Core Binary** — Location (`%APPDATA%/Apricadabra/`), CLI flags (`--stop`, `--debug`, `--debug-messages`, `--version`), version file (`apricadabra-core.version`)
11. **Plugin Bindings Schema** — File location, schema structure, standardized action objects, plugin-specific input objects
12. **Broadcast Port Registry** — 19872 (Loupedeck), 19873 (Stream Deck), 19874 (Trackpad)

Source all details from the spec document, the code, and brainstorming decisions. Cross-reference with actual message types in `protocol.rs` to ensure accuracy.

- [ ] **Step 2: Verify spec against code**

Read through the finished protocol.md and compare every message example against `core/src/protocol.rs` serde attributes. Ensure field names match (camelCase in JSON, snake_case in Rust with serde renames).

- [ ] **Step 3: Commit**

```bash
git add core/docs/protocol.md
git commit -m "docs: add formal protocol specification for plugin developers"
```

---

## Task 9: Create CHANGELOG.md

**Files:**
- Create: `core/CHANGELOG.md`

- [ ] **Step 1: Create CHANGELOG.md**

Create `core/CHANGELOG.md`:

```markdown
# Changelog

All notable changes to the Apricadabra Core protocol will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/).

## [Unreleased]

### Added
- Protocol v2: API negotiation in handshake (`commands` in hello, `apiStatus`/`coreVersion` in welcome)
- Core upgrade flow: `core_upgrade` and `core_restarting` messages for live version management
- `--debug-messages` flag for developer warnings on unknown modes/actions
- `--version` flag for core version discovery
- `apricadabra-core.version` file for plugin version detection
- Formal protocol spec at `core/docs/protocol.md`
- Standardized plugin bindings schema (`%APPDATA%/Apricadabra/<plugin>/bindings.json`)

### Changed
- Protocol version check changed from strict equality to minimum version (older plugins accepted)
- Unknown button modes default to `momentary`, unknown axis modes default to `hold`
- Malformed and unknown action types are silently dropped (no-op)

### Deprecated
- Nothing yet. Deprecations will be listed here with migration guidance.
```

- [ ] **Step 2: Commit**

```bash
git add core/CHANGELOG.md
git commit -m "docs: add CHANGELOG.md for protocol change tracking"
```

---

## Task 10: Audit and Update Loupedeck Plugin

**Files:**
- Modify: `loupedeck-plugin/ApricadabraPlugin/src/CoreConnection.cs`

- [ ] **Step 1: Audit current Loupedeck hello message against spec**

Read `CoreConnection.cs` and verify the hello message. Current state: sends `version: 1`, `name: "loupedeck"`, no `broadcastPort`, no `commands`.

Needed changes:
- Send `version: 2`
- Add `commands` array listing commands this plugin uses: `["axis", "button", "reset"]`
- Continue omitting `broadcastPort` (uses default 19872)

- [ ] **Step 2: Update hello message to v2**

Update the hello message construction in `CoreConnection.cs` to include version 2 and commands:

```csharp
var hello = new
{
    type = "hello",
    version = 2,
    name = "loupedeck",
    commands = new[] { "axis", "button", "reset" }
};
```

- [ ] **Step 3: Parse apiStatus and coreVersion from welcome**

Update the welcome parsing to read the new fields. Since Loupedeck only uses `exists`-level commands, log any `deprecated` status as a warning. If any `undefined` is found, log an error (Loupedeck can't easily trigger core upgrade since it runs inside Logi Plugin Service, but it should log the situation).

- [ ] **Step 4: Handle `core_restarting` message in pipe read loop**

In the pipe read loop (`PipeReadLoopAsync`), add handling for the `core_restarting` message type. When received:
- Set a `_coreStartTimeout` timer
- During timeout: suppress auto-launch attempts in the reconnection logic
- After timeout: resume normal behavior

- [ ] **Step 5: Review for unhandled exceptions**

Audit `CoreConnection.cs` for:
- Unhandled exceptions in async methods (missing try/catch around pipe operations, UDP send/receive)
- Null reference risks when parsing JSON responses
- Race conditions in reconnection (overlapping reconnect attempts)
- Missing disposal of sockets/pipes on error paths

Fix any issues found.

- [ ] **Step 6: Test manually (Windows only)**

Build the Loupedeck plugin and verify:
- Connects to core with v2 hello
- Receives apiStatus in welcome
- Existing button/dial functionality unchanged

- [ ] **Step 7: Commit**

```bash
git add loupedeck-plugin/ApricadabraPlugin/src/CoreConnection.cs
git commit -m "feat(loupedeck): update to protocol v2 handshake with API negotiation and core_restarting handling"
```

---

## Task 11: Audit and Update Stream Deck Plugin

**Files:**
- Modify: `streamdeck-plugin/src/core-connection.ts`

- [ ] **Step 1: Audit current Stream Deck hello message against spec**

Read `core-connection.ts` and verify the hello message. Current state: sends `version: 1`, `name: "streamdeck"`, `broadcastPort: 19873`, no `commands`.

Needed changes:
- Send `version: 2`
- Add `commands` array: `["axis", "button", "reset"]`

- [ ] **Step 2: Update hello message to v2**

Update the hello construction:

```typescript
const hello = JSON.stringify({
    type: "hello",
    version: 2,
    name: "streamdeck",
    broadcastPort: BROADCAST_PORT,
    commands: ["axis", "button", "reset"]
}) + "\n";
```

- [ ] **Step 3: Parse apiStatus and coreVersion from welcome**

Update welcome parsing to read `apiStatus` and `coreVersion`. Log any `deprecated` commands. If `undefined` commands are found, attempt the core upgrade flow:
- Read `apricadabra-core.version` from the bundled core location
- Send `core_upgrade` message on the pipe

- [ ] **Step 4: Handle `core_restarting` message in pipe read loop**

In the pipe message handler, add handling for `core_restarting`:
- Set a timeout timer
- Suppress auto-launch during timeout
- Resume normal behavior after timeout

- [ ] **Step 5: Review for unhandled exceptions**

Audit `core-connection.ts` for:
- Uncaught exceptions/rejections in async functions
- Missing error handling on pipe write/read operations
- Socket cleanup on error paths
- Race conditions in reconnection logic

Fix any issues found.

- [ ] **Step 6: Test manually (Windows only)**

Build the Stream Deck plugin and verify:
- Connects to core with v2 hello
- Receives apiStatus in welcome
- Existing button/dial functionality unchanged

- [ ] **Step 7: Commit**

```bash
git add streamdeck-plugin/src/core-connection.ts
git commit -m "feat(streamdeck): update to protocol v2 handshake with API negotiation and core_restarting handling"
```

---

## Task 12: Add Bindings Schema to Protocol Spec

**Files:**
- Modify: `core/docs/protocol.md`

- [ ] **Step 1: Add bindings schema section to protocol.md**

Add the standardized bindings schema section to the protocol spec document. Include:
- File location convention: `%APPDATA%/Apricadabra/<plugin-name>/bindings.json`
- Top-level structure: `schema`, `plugin`, `bindings`
- Standardized `action` object format (same as protocol command messages)
- Plugin-specific `gesture`/input object (not standardized, documented as plugin's domain)
- Example bindings file
- Schema versioning guidance

- [ ] **Step 2: Commit**

```bash
git add core/docs/protocol.md
git commit -m "docs: add standardized plugin bindings schema to protocol spec"
```

---

## Task 13: Review Core and Existing Plugins for Unhandled Exceptions

**Files:**
- Review: `core/src/server.rs`, `core/src/main.rs`
- Review: `loupedeck-plugin/ApricadabraPlugin/src/CoreConnection.cs`
- Review: `streamdeck-plugin/src/core-connection.ts`

- [ ] **Step 1: Audit Rust core for panics and unhandled errors**

Review `server.rs` and `main.rs` for:
- `unwrap()` calls that could panic on unexpected input
- Error paths that silently drop important errors
- Missing error propagation in spawned tasks
- Panics in async tasks (which abort the runtime)

Key areas to check:
- `server.rs:336`: `.parse().unwrap()` on socket address — could panic on invalid port
- `server.rs:203`: JSON parse in UDP handler — already guarded with `if let Ok`
- Spawned tasks that use `?` without proper error handling

- [ ] **Step 2: Fix any issues found in core**

Apply fixes. Prefer `match`/`if let` over `unwrap()` in runtime code. Log errors rather than panicking.

- [ ] **Step 3: Audit Loupedeck plugin (done partially in Task 10)**

Cross-reference with Task 10 step 5. Focus on:
- JSON deserialization failures
- Named pipe read/write errors
- UDP socket errors
- Process launch failures

- [ ] **Step 4: Audit Stream Deck plugin (done partially in Task 11)**

Cross-reference with Task 11 step 5. Focus on:
- Unhandled promise rejections
- Socket error events
- Pipe stream error handling

- [ ] **Step 5: Fix any remaining issues**

Apply fixes across all three codebases.

- [ ] **Step 6: Run core test suite to verify no regressions**

Run: `cd /home/bc/projects/apricadabra/core && cargo test -- --nocapture`
Expected: All PASS

- [ ] **Step 7: Commit fixes**

```bash
git add -u
git commit -m "fix: address unhandled exceptions and error paths across core and plugins"
```

---

## Task 14: Final Validation

**Files:** All modified files

- [ ] **Step 1: Run full core test suite**

Run: `cd /home/bc/projects/apricadabra/core && cargo test -- --nocapture`
Expected: All PASS

- [ ] **Step 2: Verify protocol.md matches code**

Read `core/docs/protocol.md` and cross-reference every message example against `core/src/protocol.rs`. Verify:
- All field names match (camelCase in spec ↔ serde renames in Rust)
- All message types documented
- Default values documented correctly
- New v2 fields documented

- [ ] **Step 3: Verify no TODO/FIXME left behind**

Run: `grep -r "TODO\|FIXME\|TBD\|HACK" core/src/ loupedeck-plugin/ApricadabraPlugin/src/ streamdeck-plugin/src/`

Address any new items introduced during this work.

- [ ] **Step 4: Commit any final fixes**

```bash
git add -u
git commit -m "chore: final validation and cleanup for protocol v2"
```
