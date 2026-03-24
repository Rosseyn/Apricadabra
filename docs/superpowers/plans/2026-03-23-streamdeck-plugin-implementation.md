# Stream Deck Plugin Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a TypeScript Stream Deck plugin to Apricadabra, fix core bugs (rapid fire, disconnect decay, broadcast port sharing), and fix Loupedeck slider bugs.

**Architecture:** The Stream Deck plugin is a new IPC client speaking the same JSON/UDP protocol as the Loupedeck plugin. Core changes add per-client broadcast registration (so both plugins can run simultaneously), wire up rapid fire with configurable rate, and enable disconnect decay. The plugin uses the Elgato SDK v2 with Node.js 20.

**Tech Stack:** TypeScript/Node.js (Stream Deck SDK), Rust (core fixes), C# (Loupedeck slider fixes)

**Build notes:** Rust builds require `CARGO_INCREMENTAL=0` on WSL. Plugin deployment requires killing LogiPluginService.exe before copying DLLs. Stream Deck plugin uses `@elgato/cli` for scaffolding. See `docs/superpowers/specs/2026-03-23-streamdeck-plugin-design.md` for full spec.

---

## Task 1: Core — Add `broadcast_port` to Hello protocol

**Files:**
- Modify: `core/src/protocol.rs:8-11`
- Modify: `core/tests/protocol_test.rs`

- [ ] **Step 1: Write test for Hello with broadcast_port**

Add to `core/tests/protocol_test.rs`:

```rust
#[test]
fn test_hello_with_broadcast_port() {
    let json = r#"{"type":"hello","version":1,"name":"streamdeck","broadcastPort":19873}"#;
    let msg: ClientMessage = serde_json::from_str(json).unwrap();
    match msg {
        ClientMessage::Hello { version, name, broadcast_port } => {
            assert_eq!(version, 1);
            assert_eq!(name, "streamdeck");
            assert_eq!(broadcast_port, Some(19873));
        }
        _ => panic!("Expected Hello"),
    }
}

#[test]
fn test_hello_without_broadcast_port() {
    let json = r#"{"type":"hello","version":1,"name":"loupedeck"}"#;
    let msg: ClientMessage = serde_json::from_str(json).unwrap();
    match msg {
        ClientMessage::Hello { version, name, broadcast_port } => {
            assert_eq!(version, 1);
            assert_eq!(name, "loupedeck");
            assert_eq!(broadcast_port, None);
        }
        _ => panic!("Expected Hello"),
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `cd core && CARGO_INCREMENTAL=0 cargo test --test protocol_test test_hello_with_broadcast_port 2>&1 | tail -5`

Expected: FAIL — `Hello` doesn't have `broadcast_port` field.

- [ ] **Step 3: Add broadcast_port field to ClientMessage::Hello**

In `core/src/protocol.rs`, modify the `Hello` variant (lines 8-11):

```rust
    Hello {
        version: u32,
        name: String,
        #[serde(default, rename = "broadcastPort")]
        broadcast_port: Option<u16>,
    },
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `cd core && CARGO_INCREMENTAL=0 cargo test --test protocol_test 2>&1 | tail -5`

Expected: All protocol tests pass (backward compatible — existing Hello tests still work since field is optional).

- [ ] **Step 5: Commit**

```bash
git add core/src/protocol.rs core/tests/protocol_test.rs
git commit -m "feat(core): add optional broadcastPort to Hello message"
```

---

## Task 2: Core — Per-client broadcast registration

**Files:**
- Modify: `core/src/server.rs`
- Modify: `core/tests/integration_test.rs`

- [ ] **Step 1: Update server to track broadcast targets**

In `core/src/server.rs`, replace the hardcoded broadcast destination with a per-client registration system:

1. Add a shared broadcast targets map alongside existing state (after line 48):
```rust
let broadcast_targets: Arc<Mutex<HashMap<u64, std::net::SocketAddr>>> = Arc::new(Mutex::new(HashMap::new()));
```

2. Remove the hardcoded `broadcast_dest` variable. Keep the `broadcast_socket`.

3. In the pipe accept loop, pass `broadcast_targets.clone()` to `handle_client`.

4. In `handle_client`, after parsing the Welcome, register the client's broadcast port:
```rust
// Register broadcast target
let port = match hello {
    ClientMessage::Hello { broadcast_port, .. } => broadcast_port.unwrap_or(19872),
    _ => 19872,
};
let addr: std::net::SocketAddr = format!("127.0.0.1:{port}").parse().unwrap();
broadcast_targets.lock().await.insert(client_id, addr);
```

5. In the cleanup path (disconnect), remove the client's registration:
```rust
broadcast_targets.lock().await.remove(&client_id);
```

6. In the broadcast tick, iterate all targets:
```rust
let targets = broadcast_targets.lock().await;
for (_id, addr) in targets.iter() {
    let _ = broadcast_socket.send_to(json.as_bytes(), addr).await;
}
```

- [ ] **Step 2: Update handle_client signature**

Add `broadcast_targets: Arc<Mutex<HashMap<u64, std::net::SocketAddr>>>` parameter to `handle_client`. Update the spawn call in the accept loop to pass it.

- [ ] **Step 3: Verify it compiles**

Run: `cd core && CARGO_INCREMENTAL=0 cargo check 2>&1 | tail -5`

Expected: Compiles with warnings only.

- [ ] **Step 4: Update integration test for broadcast port**

In `core/tests/integration_test.rs`, update the axis broadcast test to verify broadcasts still arrive on the default port (19872) when no broadcastPort is specified in Hello.

- [ ] **Step 5: Run all tests**

Run: `cd core && CARGO_INCREMENTAL=0 cargo test 2>&1 | tail -10`

Expected: All tests pass.

- [ ] **Step 6: Commit**

```bash
git add core/src/server.rs core/tests/integration_test.rs
git commit -m "feat(core): per-client UDP broadcast registration via Hello.broadcastPort"
```

---

## Task 3: Core — Wire up rapid fire with configurable rate

**Files:**
- Modify: `core/src/button.rs`
- Modify: `core/src/server.rs`
- Modify: `core/tests/button_test.rs`

- [ ] **Step 1: Write test for rapid fire with rate**

Add to `core/tests/button_test.rs`:

```rust
#[test]
fn test_rapid_fire_with_rate() {
    let mut mgr = ButtonManager::new();
    mgr.rapid_start(1, 100); // 100ms rate

    // Simulate ticks — rapid_tick should toggle based on elapsed time
    // First call should toggle
    std::thread::sleep(std::time::Duration::from_millis(110));
    mgr.process_rapid_ticks();
    let changed = mgr.take_changed();
    assert!(changed.contains_key(&1));
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `cd core && CARGO_INCREMENTAL=0 cargo test --test button_test test_rapid_fire_with_rate 2>&1 | tail -5`

Expected: FAIL — `process_rapid_ticks` doesn't exist.

- [ ] **Step 3: Implement rapid fire with rate tracking**

In `core/src/button.rs`:

1. Change `rapid_active` field (line 17):
```rust
rapid_active: HashMap<u8, (u64, Instant)>, // (rate_ms, last_fire_time)
```

2. Add `use std::time::Instant;` import if not present (it's already there).

3. Update `rapid_start` (line 95):
```rust
pub fn rapid_start(&mut self, button: u8, rate_ms: u64) {
    self.set(button, true);
    self.rapid_active.insert(button, (rate_ms, Instant::now()));
}
```

4. Update `rapid_stop` (line 100):
```rust
pub fn rapid_stop(&mut self, button: u8) {
    self.rapid_active.remove(&button);
    self.set(button, false);
}
```

5. Replace `rapid_tick` (line 105) with `process_rapid_ticks`:
```rust
pub fn process_rapid_ticks(&mut self) {
    let now = Instant::now();
    let mut to_toggle = Vec::new();
    for (&button, (rate_ms, last_fire)) in self.rapid_active.iter() {
        if now.duration_since(*last_fire).as_millis() as u64 >= rate_ms {
            to_toggle.push(button);
        }
    }
    for button in to_toggle {
        if let Some(i) = self.idx(button) {
            self.states[i] = !self.states[i];
            self.changed.insert(button);
        }
        if let Some(entry) = self.rapid_active.get_mut(&button) {
            entry.1 = now;
        }
    }
}
```

6. Update `new()` to use `HashMap::new()` for `rapid_active`.

- [ ] **Step 4: Call process_rapid_ticks in server tick loop**

In `core/src/server.rs`, in the tick loop (after `buttons.process_pending()`), add:

```rust
buttons.process_rapid_ticks();
```

- [ ] **Step 5: Run all tests**

Run: `cd core && CARGO_INCREMENTAL=0 cargo test 2>&1 | tail -10`

Expected: All tests pass.

- [ ] **Step 6: Commit**

```bash
git add core/src/button.rs core/src/server.rs core/tests/button_test.rs
git commit -m "fix(core): wire up rapid fire with configurable rate"
```

---

## Task 4: Core — Wire up disconnect decay

**Files:**
- Modify: `core/src/server.rs`

- [ ] **Step 1: Add start_disconnect_decay call**

In `core/src/server.rs`, find the disconnect handler (the `Some(client_id) = disconnect_rx.recv()` arm). Currently it logs "All clients disconnected" but doesn't trigger decay. Add the call:

```rust
Some(client_id) = disconnect_rx.recv() => {
    info!("Client {client_id} cleanup");
    broadcast_targets.lock().await.remove(&client_id);
    if connected_clients.load(std::sync::atomic::Ordering::Relaxed) == 0 {
        info!("All clients disconnected, starting decay");
        axis_mgr.lock().await.start_disconnect_decay();
    }
}
```

- [ ] **Step 2: Re-enable disconnect decay in tick loop**

Verify that `axes.tick_disconnect_decay()` is called in the tick loop. It was removed earlier — add it back after `axes.tick_spring_decay()`:

```rust
axes.tick_spring_decay();
axes.tick_disconnect_decay();
```

- [ ] **Step 3: Run all tests**

Run: `cd core && CARGO_INCREMENTAL=0 cargo test 2>&1 | tail -10`

Expected: All tests pass.

- [ ] **Step 4: Commit**

```bash
git add core/src/server.rs
git commit -m "fix(core): wire up disconnect decay when all clients disconnect"
```

---

## Task 5: Loupedeck — Fix slider bugs

**Files:**
- Modify: `loupedeck-plugin/ApricadabraPlugin/src/Actions/DialAction.cs`
- Modify: `loupedeck-plugin/ApricadabraPlugin/src/Actions/ResetAxisCommand.cs`

- [ ] **Step 1: Fix DialAction sensitivity slider**

In `DialAction.cs`, find the sensitivity slider `SetValues` call. It should be:
```csharp
new ActionEditorSlider(name: SensitivityControl, labelText: "Sensitivity")
    .SetValues(1, 100, 1, 20)
    .SetFormatString("{0}%")
```

Verify the parameters are `(min=1, max=100, step=1, default=20)`. If the step or default are wrong, fix them.

- [ ] **Step 2: Fix ResetAxisCommand position slider**

In `ResetAxisCommand.cs`, find the position slider `SetValues` call. It should be:
```csharp
new ActionEditorSlider(name: PositionControl, labelText: "Reset Position")
    .SetValues(0, 100, 1, 50)
    .SetFormatString("{0}%")
```

Verify the parameters are `(min=0, max=100, step=1, default=50)`.

- [ ] **Step 3: Clean up ResetAxisCommand debug logging**

Remove the debug logging lines in `RunCommand` that log raw position values. Simplify to:

```csharp
actionParameters.TryGetInt32(PositionControl, out var posInt);
var position = posInt / 100f;
```

- [ ] **Step 4: Verify it builds**

Run: `cd loupedeck-plugin/ApricadabraPlugin/src && dotnet build 2>&1 | tail -5`

Expected: Build succeeded, 0 errors.

- [ ] **Step 5: Commit**

```bash
git add loupedeck-plugin/ApricadabraPlugin/src/Actions/DialAction.cs loupedeck-plugin/ApricadabraPlugin/src/Actions/ResetAxisCommand.cs
git commit -m "fix(loupedeck): fix slider ranges for sensitivity and reset position"
```

---

## Task 6: Core — Rebuild and install with all fixes

**Files:**
- No code changes — build and deploy only

- [ ] **Step 1: Build core release**

Run: `cd core && CARGO_INCREMENTAL=0 cargo build --release 2>&1 | tail -5`

Expected: Compiles successfully.

- [ ] **Step 2: Run all tests**

Run: `cd core && CARGO_INCREMENTAL=0 cargo test 2>&1 | tail -5`

Expected: All tests pass.

- [ ] **Step 3: Install core**

```bash
taskkill //F //IM apricadabra-core.exe 2>/dev/null
taskkill //F //IM LogiPluginService.exe 2>/dev/null
taskkill //F //IM Loupedeck.exe 2>/dev/null
sleep 3
cp core/target/release/apricadabra-core.exe "C:/Users/unsab/AppData/Roaming/Apricadabra/apricadabra-core.exe"
```

- [ ] **Step 4: Deploy Loupedeck plugin and verify**

Build and restart Loupedeck. Verify sliders work correctly.

- [ ] **Step 5: Commit tag**

```bash
git tag -a v0.1.1 -m "v0.1.1 — core fixes and slider bug fixes"
```

---

## Task 7: Stream Deck — Scaffold plugin project

**Files:**
- Create: `streamdeck-plugin/` (entire directory via `streamdeck create`)

- [ ] **Step 1: Install Elgato CLI**

Run: `npm install -g @elgato/cli`

- [ ] **Step 2: Scaffold the project**

Run from the repo root:
```bash
cd streamdeck-plugin
streamdeck create
```

Use these values when prompted:
- Name: `Apricadabra`
- UUID: `com.apricadabra.streamdeck`
- Author: `apricadabra`
- Description: `Control vJoy virtual joystick axes and buttons`

- [ ] **Step 3: Verify scaffold builds**

Run: `cd streamdeck-plugin && npm install && npm run build 2>&1 | tail -5`

Expected: Build succeeds.

- [ ] **Step 4: Remove example actions**

Delete any generated example action files — we'll create our own.

- [ ] **Step 5: Update manifest.json**

Replace the generated manifest with the one from the spec. Key fields:
- `SDKVersion: 2`
- `Nodejs.Version: "20"`
- Three actions with proper UUIDs, Controllers, States, Encoder configs
- `CodePath: "bin/plugin.js"`

- [ ] **Step 6: Commit**

```bash
git add streamdeck-plugin/
git commit -m "feat(streamdeck): scaffold plugin project with Elgato SDK"
```

---

## Task 8: Stream Deck — CoreConnection

**Files:**
- Create: `streamdeck-plugin/src/core-connection.ts`

- [ ] **Step 1: Implement CoreConnection class**

```typescript
// core-connection.ts
import * as net from "net";
import * as dgram from "dgram";
import { spawn } from "child_process";
import { join } from "path";
import { existsSync } from "fs";

const PIPE_NAME = "\\\\.\\pipe\\apricadabra";
const UDP_COMMAND_PORT = 19871;
const UDP_BROADCAST_PORT = 19873; // Unique port for Stream Deck
const PROTOCOL_VERSION = 1;
const CORE_EXE_NAME = "apricadabra-core.exe";

type StateCallback = (axes: Record<string, number>, buttons: Record<string, boolean>) => void;
type StatusCallback = (status: string) => void;

export class CoreConnection {
    private pipe: net.Socket | null = null;
    private udpSender: dgram.Socket | null = null;
    private udpListener: dgram.Socket | null = null;
    private connected = false;
    private buffer = "";

    public onStateUpdate: StateCallback | null = null;
    public onStatusChange: StatusCallback | null = null;

    async connect(): Promise<void> {
        let delay = 100;
        while (true) {
            try {
                await this.tryConnect();
                return;
            } catch {
                this.tryLaunchCore();
                await this.sleep(delay);
                delay = Math.min(delay * 2, 5000);
            }
        }
    }

    private tryConnect(): Promise<void> {
        return new Promise((resolve, reject) => {
            const pipe = net.createConnection(PIPE_NAME);
            let resolved = false;

            pipe.on("connect", () => {
                this.pipe = pipe;
                // Send hello
                const hello = JSON.stringify({
                    type: "hello",
                    version: PROTOCOL_VERSION,
                    name: "streamdeck",
                    broadcastPort: UDP_BROADCAST_PORT,
                });
                pipe.write(hello + "\n");
            });

            pipe.on("data", (data) => {
                this.buffer += data.toString();
                const lines = this.buffer.split("\n");
                this.buffer = lines.pop() || "";

                for (const line of lines) {
                    if (!line.trim()) continue;
                    const msg = JSON.parse(line);

                    if (!resolved && msg.type === "welcome") {
                        resolved = true;
                        this.connected = true;
                        this.setupUdp();
                        this.onStatusChange?.("Connected");
                        if (msg.axes || msg.buttons) {
                            this.onStateUpdate?.(msg.axes || {}, msg.buttons || {});
                        }
                        resolve();
                    } else if (msg.type === "heartbeat") {
                        pipe.write(JSON.stringify({ type: "heartbeat_ack" }) + "\n");
                    } else if (msg.type === "error") {
                        this.onStatusChange?.(`Error: ${msg.message}`);
                    } else if (msg.type === "shutdown") {
                        this.onStatusChange?.("Core shutting down");
                        this.disconnect();
                    }
                }
            });

            pipe.on("error", (err) => {
                if (!resolved) reject(err);
                else this.handleDisconnect();
            });

            pipe.on("close", () => {
                if (!resolved) reject(new Error("Pipe closed"));
                else this.handleDisconnect();
            });

            setTimeout(() => {
                if (!resolved) {
                    pipe.destroy();
                    reject(new Error("Timeout"));
                }
            }, 2000);
        });
    }

    private setupUdp(): void {
        // Command sender
        this.udpSender = dgram.createSocket("udp4");

        // Broadcast listener
        this.udpListener = dgram.createSocket({ type: "udp4", reuseAddr: true });
        this.udpListener.bind(UDP_BROADCAST_PORT, "127.0.0.1");
        this.udpListener.on("message", (data) => {
            try {
                const msg = JSON.parse(data.toString());
                if (msg.type === "state") {
                    this.onStateUpdate?.(msg.axes || {}, msg.buttons || {});
                }
            } catch {}
        });
    }

    send(message: Record<string, unknown>): void {
        if (!this.connected || !this.udpSender) return;
        const buf = Buffer.from(JSON.stringify(message));
        this.udpSender.send(buf, UDP_COMMAND_PORT, "127.0.0.1");
    }

    private handleDisconnect(): void {
        this.connected = false;
        this.onStatusChange?.("Disconnected");
        this.cleanup();
        setTimeout(() => this.connect(), 1000);
    }

    private disconnect(): void {
        this.connected = false;
        this.cleanup();
    }

    private cleanup(): void {
        this.pipe?.destroy();
        this.pipe = null;
        this.udpSender?.close();
        this.udpSender = null;
        this.udpListener?.close();
        this.udpListener = null;
    }

    private tryLaunchCore(): void {
        const appData = process.env.APPDATA || "";
        const corePath = join(appData, "Apricadabra", CORE_EXE_NAME);
        if (existsSync(corePath)) {
            spawn(corePath, [], { detached: true, stdio: "ignore" }).unref();
        }
    }

    private sleep(ms: number): Promise<void> {
        return new Promise((r) => setTimeout(r, ms));
    }
}
```

- [ ] **Step 2: Verify it compiles**

Run: `cd streamdeck-plugin && npm run build 2>&1 | tail -5`

Expected: Compiles.

- [ ] **Step 3: Commit**

```bash
git add streamdeck-plugin/src/core-connection.ts
git commit -m "feat(streamdeck): add CoreConnection with pipe handshake and UDP"
```

---

## Task 9: Stream Deck — StateDisplay

**Files:**
- Create: `streamdeck-plugin/src/state-display.ts`

- [ ] **Step 1: Implement StateDisplay class**

```typescript
// state-display.ts
export class StateDisplay {
    private axes: Map<number, number> = new Map();
    private buttons: Map<number, boolean> = new Map();

    getAxis(id: number): number {
        return this.axes.get(id) ?? 0.5;
    }

    getButton(id: number): boolean {
        return this.buttons.get(id) ?? false;
    }

    getAxisPercent(id: number): number {
        return Math.round(this.getAxis(id) * 100);
    }

    getAxisDisplayString(id: number): string {
        return `${this.getAxisPercent(id)}%`;
    }

    update(axes: Record<string, number>, buttons: Record<string, boolean>): void {
        for (const [key, value] of Object.entries(axes)) {
            this.axes.set(Number(key), value);
        }
        for (const [key, value] of Object.entries(buttons)) {
            this.buttons.set(Number(key), value);
        }
    }

    getChangedAxes(axes: Record<string, number>): number[] {
        const changed: number[] = [];
        for (const [key, value] of Object.entries(axes)) {
            const id = Number(key);
            const old = this.axes.get(id);
            if (old === undefined || Math.abs(old - value) > 0.001) {
                changed.push(id);
            }
        }
        return changed;
    }
}
```

- [ ] **Step 2: Verify it compiles**

Run: `cd streamdeck-plugin && npm run build 2>&1 | tail -5`

- [ ] **Step 3: Commit**

```bash
git add streamdeck-plugin/src/state-display.ts
git commit -m "feat(streamdeck): add StateDisplay for axis/button state tracking"
```

---

## Task 10: Stream Deck — Dial Action

**Files:**
- Create: `streamdeck-plugin/src/actions/dial-action.ts`

- [ ] **Step 1: Implement DialAction**

```typescript
// actions/dial-action.ts
import streamDeck, { action, SingletonAction, DialRotateEvent, DialDownEvent, WillAppearEvent, DidReceiveSettingsEvent } from "@elgato/streamdeck";
import { CoreConnection } from "../core-connection";
import { StateDisplay } from "../state-display";

interface DialSettings {
    axis: string;
    mode: string;
    sensitivity: number;
    invert: boolean;
    decayRate: number;
    steps: number;
    encoderButton: string;
}

@action({ UUID: "com.apricadabra.dial" })
export class DialAction extends SingletonAction<DialSettings> {
    private connection: CoreConnection;
    private stateDisplay: StateDisplay;

    constructor(connection: CoreConnection, stateDisplay: StateDisplay) {
        super();
        this.connection = connection;
        this.stateDisplay = stateDisplay;
    }

    override onDialRotate(ev: DialRotateEvent<DialSettings>): void {
        const { axis, mode, sensitivity, invert, decayRate, steps } = ev.payload.settings;
        if (!axis || !mode) return;

        let diff = ev.payload.ticks;
        if (invert) diff = -diff;

        // Clamp detent to +/-1 for single-step movement
        if (mode === "detent") {
            diff = Math.sign(diff);
        }

        const msg: Record<string, unknown> = {
            type: "axis",
            axis: Number(axis),
            mode,
            diff,
            sensitivity: (sensitivity || 20) / 1000,
        };

        if (mode === "spring") {
            msg.decayRate = (decayRate || 95) / 100;
        }
        if (mode === "detent") {
            msg.steps = steps || 5;
        }

        this.connection.send(msg);
    }

    override onDialDown(ev: DialDownEvent<DialSettings>): void {
        const { encoderButton } = ev.payload.settings;
        if (!encoderButton || encoderButton === "none") return;

        this.connection.send({
            type: "button",
            button: Number(encoderButton),
            mode: "pulse",
        });
    }

    override onWillAppear(ev: WillAppearEvent<DialSettings>): void {
        this.updateFeedback(ev.action, ev.payload.settings);
    }

    override onDidReceiveSettings(ev: DidReceiveSettingsEvent<DialSettings>): void {
        this.updateFeedback(ev.action, ev.payload.settings);
    }

    updateFeedback(action: any, settings: DialSettings): void {
        if (!settings.axis) return;
        const axisId = Number(settings.axis);
        const percent = this.stateDisplay.getAxisPercent(axisId);
        action.setFeedback({
            value: `${percent}%`,
            indicator: percent,
        });
    }

    updateAllFeedback(): void {
        // Called from plugin.ts when state updates arrive
        // Iterate all visible actions and update their feedback
    }
}
```

Note: The `updateAllFeedback` method will be wired up in plugin.ts once we have access to the action contexts.

- [ ] **Step 2: Verify it compiles**

Run: `cd streamdeck-plugin && npm run build 2>&1 | tail -5`

- [ ] **Step 3: Commit**

```bash
git add streamdeck-plugin/src/actions/dial-action.ts
git commit -m "feat(streamdeck): add DialAction with rotation, encoder press, and LCD feedback"
```

---

## Task 11: Stream Deck — Button Action

**Files:**
- Create: `streamdeck-plugin/src/actions/button-action.ts`

- [ ] **Step 1: Implement ButtonAction**

```typescript
// actions/button-action.ts
import { action, SingletonAction, KeyDownEvent, KeyUpEvent } from "@elgato/streamdeck";
import { CoreConnection } from "../core-connection";

interface ButtonSettings {
    button: string;
    mode: string;
    delay: number;
    rate: number;
    shortButton: string;
    longButton: string;
    threshold: number;
}

@action({ UUID: "com.apricadabra.button" })
export class ButtonAction extends SingletonAction<ButtonSettings> {
    private connection: CoreConnection;

    constructor(connection: CoreConnection) {
        super();
        this.connection = connection;
    }

    override onKeyDown(ev: KeyDownEvent<ButtonSettings>): void {
        const s = ev.payload.settings;
        if (!s.button || !s.mode) return;
        const button = Number(s.button);

        switch (s.mode) {
            case "momentary":
                this.connection.send({ type: "button", button, mode: "momentary", state: "down" });
                break;
            case "toggle":
                this.connection.send({ type: "button", button, mode: "toggle", state: "down" });
                break;
            case "pulse":
                this.connection.send({ type: "button", button, mode: "pulse" });
                break;
            case "double":
                this.connection.send({ type: "button", button, mode: "double", delay: s.delay || 50 });
                break;
            case "rapid":
                this.connection.send({ type: "button", button, mode: "rapid", state: "down", rate: s.rate || 100 });
                break;
            case "longshort":
                this.connection.send({
                    type: "button",
                    button,
                    mode: "longshort",
                    state: "down",
                    shortButton: Number(s.shortButton || button),
                    longButton: Number(s.longButton || button),
                    threshold: s.threshold || 500,
                });
                break;
        }
    }

    override onKeyUp(ev: KeyUpEvent<ButtonSettings>): void {
        const s = ev.payload.settings;
        if (!s.button || !s.mode) return;
        const button = Number(s.button);

        switch (s.mode) {
            case "momentary":
                this.connection.send({ type: "button", button, mode: "momentary", state: "up" });
                break;
            case "rapid":
                this.connection.send({ type: "button", button, mode: "rapid", state: "up" });
                break;
            case "longshort":
                this.connection.send({
                    type: "button",
                    button,
                    mode: "longshort",
                    state: "up",
                    shortButton: Number(s.shortButton || button),
                    longButton: Number(s.longButton || button),
                    threshold: s.threshold || 500,
                });
                break;
        }
    }
}
```

- [ ] **Step 2: Verify it compiles**

Run: `cd streamdeck-plugin && npm run build 2>&1 | tail -5`

- [ ] **Step 3: Commit**

```bash
git add streamdeck-plugin/src/actions/button-action.ts
git commit -m "feat(streamdeck): add ButtonAction with all 6 modes including key-up"
```

---

## Task 12: Stream Deck — Reset Axis Action

**Files:**
- Create: `streamdeck-plugin/src/actions/reset-axis-action.ts`

- [ ] **Step 1: Implement ResetAxisAction**

```typescript
// actions/reset-axis-action.ts
import { action, SingletonAction, KeyDownEvent, DialDownEvent } from "@elgato/streamdeck";
import { CoreConnection } from "../core-connection";

interface ResetSettings {
    axis: string;
    position: number;
}

@action({ UUID: "com.apricadabra.reset" })
export class ResetAxisAction extends SingletonAction<ResetSettings> {
    private connection: CoreConnection;

    constructor(connection: CoreConnection) {
        super();
        this.connection = connection;
    }

    override onKeyDown(ev: KeyDownEvent<ResetSettings>): void {
        this.doReset(ev.payload.settings);
    }

    override onDialDown(ev: DialDownEvent<ResetSettings>): void {
        this.doReset(ev.payload.settings);
    }

    private doReset(settings: ResetSettings): void {
        if (!settings.axis) return;
        const position = (settings.position ?? 50) / 100;
        this.connection.send({
            type: "reset",
            axis: Number(settings.axis),
            position,
        });
    }
}
```

- [ ] **Step 2: Verify it compiles**

Run: `cd streamdeck-plugin && npm run build 2>&1 | tail -5`

- [ ] **Step 3: Commit**

```bash
git add streamdeck-plugin/src/actions/reset-axis-action.ts
git commit -m "feat(streamdeck): add ResetAxisAction for button and encoder press"
```

---

## Task 13: Stream Deck — Plugin entry point

**Files:**
- Modify: `streamdeck-plugin/src/plugin.ts`

- [ ] **Step 1: Wire up plugin.ts**

```typescript
// plugin.ts
import streamDeck from "@elgato/streamdeck";
import { CoreConnection } from "./core-connection";
import { StateDisplay } from "./state-display";
import { DialAction } from "./actions/dial-action";
import { ButtonAction } from "./actions/button-action";
import { ResetAxisAction } from "./actions/reset-axis-action";

const connection = new CoreConnection();
const stateDisplay = new StateDisplay();
const dialAction = new DialAction(connection, stateDisplay);

connection.onStateUpdate = (axes, buttons) => {
    stateDisplay.update(axes, buttons);
    // Update all active dial LCD feedback
    // Note: action context iteration depends on SDK version
    // May need to track active actions manually
};

connection.onStatusChange = (status) => {
    streamDeck.logger.info(`Core connection: ${status}`);
};

streamDeck.actions.registerAction(dialAction);
streamDeck.actions.registerAction(new ButtonAction(connection));
streamDeck.actions.registerAction(new ResetAxisAction(connection));

connection.connect().catch((err) => {
    streamDeck.logger.error(`Failed to connect to core: ${err}`);
});

streamDeck.connect();
```

- [ ] **Step 2: Verify it compiles**

Run: `cd streamdeck-plugin && npm run build 2>&1 | tail -5`

- [ ] **Step 3: Commit**

```bash
git add streamdeck-plugin/src/plugin.ts
git commit -m "feat(streamdeck): wire up plugin entry point with all actions"
```

---

## Task 14: Stream Deck — Property Inspector (Dial)

**Files:**
- Create: `streamdeck-plugin/property-inspector/dial.html`

- [ ] **Step 1: Create dial Property Inspector**

Create an HTML page with the Elgato PI SDK that shows:
- Axis dropdown (X, Y, Z, Rx, Ry, Rz, Slider 1, Slider 2)
- Mode dropdown (Hold, Spring, Detent)
- Sensitivity range (1-100, step 1, default 20)
- Invert checkbox
- Decay Rate range (1-99, step 1, default 95) — visible only when mode = Spring
- Steps range (2-20, step 1, default 5) — visible only when mode = Detent
- Encoder Press Button dropdown (None, 1-128)

Use the Stream Deck PI SDK patterns: `sdpi-wrapper`, `sdpi-item`, `onDidConnect`, `setSettings`.

JavaScript at the bottom handles:
- Loading saved settings on connect
- Saving settings on change
- Showing/hiding conditional fields based on mode

- [ ] **Step 2: Verify it renders**

Link the plugin to Stream Deck and verify the PI opens without errors. Check browser devtools console.

- [ ] **Step 3: Commit**

```bash
git add streamdeck-plugin/property-inspector/dial.html
git commit -m "feat(streamdeck): add dial Property Inspector with conditional fields"
```

---

## Task 15: Stream Deck — Property Inspector (Button)

**Files:**
- Create: `streamdeck-plugin/property-inspector/button.html`

- [ ] **Step 1: Create button Property Inspector**

HTML page with:
- Button dropdown (1-128)
- Mode dropdown (Momentary, Toggle, Pulse, Double Press, Rapid Fire, Long/Short)
- Delay range (10-200, step 5, default 50) — visible when mode = Double Press
- Rate range (20-500, step 10, default 100) — visible when mode = Rapid Fire
- Short Press Button dropdown (1-128) — visible when mode = Long/Short
- Long Press Button dropdown (1-128) — visible when mode = Long/Short
- Threshold range (100-2000, step 50, default 500) — visible when mode = Long/Short

JavaScript handles conditional visibility.

- [ ] **Step 2: Commit**

```bash
git add streamdeck-plugin/property-inspector/button.html
git commit -m "feat(streamdeck): add button Property Inspector with all 6 modes"
```

---

## Task 16: Stream Deck — Property Inspector (Reset Axis)

**Files:**
- Create: `streamdeck-plugin/property-inspector/reset-axis.html`

- [ ] **Step 1: Create reset axis Property Inspector**

HTML page with:
- Axis dropdown (X, Y, Z, Rx, Ry, Rz, Slider 1, Slider 2)
- Position range (0-100, step 1, default 50)

- [ ] **Step 2: Commit**

```bash
git add streamdeck-plugin/property-inspector/reset-axis.html
git commit -m "feat(streamdeck): add reset axis Property Inspector"
```

---

## Task 17: Stream Deck — Build, link, and smoke test

**Files:**
- No code changes — build and test

- [ ] **Step 1: Build the plugin**

Run: `cd streamdeck-plugin && npm run build`

- [ ] **Step 2: Link to Stream Deck**

Run: `streamdeck link` or manually create a symlink from the Stream Deck plugins directory to the build output.

- [ ] **Step 3: Start the core**

```bash
powershell.exe -Command "Start-Process 'C:\Users\unsab\AppData\Roaming\Apricadabra\apricadabra-core.exe' -WindowStyle Hidden"
```

- [ ] **Step 4: Open Stream Deck software and verify actions appear**

Check that "Apricadabra" category shows with:
- vJoy Dial (encoder slots)
- vJoy Button (button slots)
- vJoy Reset Axis (both)

- [ ] **Step 5: Test vJoy Dial**

Assign to an encoder on SD+:
- Set Axis: X, Mode: Hold, Sensitivity: 20%
- Rotate dial — verify X axis moves in vJoy Monitor
- LCD should show current percentage

- [ ] **Step 6: Test vJoy Button**

Assign to a button:
- Set Button: 1, Mode: Pulse
- Press — verify button 1 flashes in vJoy Monitor
- Test Toggle, Momentary (hold), Rapid Fire modes

- [ ] **Step 7: Test vJoy Reset Axis**

Assign to a button:
- Set Axis: X, Position: 50%
- Press — verify X axis jumps to 50% in vJoy Monitor

- [ ] **Step 8: Test Spring mode with LCD**

Assign dial: Mode: Spring, Sensitivity: 50%
- Rotate — axis should move, then decay to center after 500ms
- LCD should update in real-time showing the decay

- [ ] **Step 9: Commit and tag**

```bash
git add -A
git commit -m "feat(streamdeck): complete Stream Deck plugin v0.1.0"
git tag -a v0.2.0 -m "v0.2.0 — Stream Deck plugin, core fixes"
```
