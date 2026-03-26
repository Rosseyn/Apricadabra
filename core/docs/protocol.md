# Apricadabra Protocol Specification

**Protocol Version:** 2
**Status:** Living document — canonical source of truth for plugin developers

---

## 1. Overview

Apricadabra is a virtual joystick multiplexer built on vJoy. It exposes a single DirectInput device and lets multiple input plugins (hardware controllers, touchscreens, etc.) send button presses and axis movements to a shared core process.

This protocol defines how plugins communicate with the core. A plugin developer needs only this document and a JSON serializer to build a fully functional client.

---

## 2. Connection Model

The protocol uses two transports:

| Transport | Direction | Purpose |
|---|---|---|
| Named pipe `\\.\pipe\apricadabra` | Bidirectional | Handshake, heartbeat, errors, lifecycle events |
| UDP port **19871** | Plugin -> Core | Commands (axis, button, reset) |
| UDP broadcast (per-plugin port) | Core -> Plugin | State updates |

**Why two transports?** The named pipe is reliable and ordered, suitable for session management. UDP is fire-and-forget, suitable for high-frequency input commands where occasional packet loss is acceptable.

### Known Broadcast Ports

| Port | Plugin |
|---|---|
| 19872 | Loupedeck (default) |
| 19873 | Stream Deck |
| 19874 | Trackpad |

Plugins declare their broadcast port in the hello message. If omitted, the default is **19872**.

---

## 3. Handshake Flow

```
Plugin                          Core
  |                               |
  |--- [pipe] hello ------------->|
  |                               |
  |<-- [pipe] welcome ------------|
  |                               |
  |=== session established ========|
  |                               |
  |--- [UDP 19871] commands ----->|
  |<-- [UDP broadcast] state -----|
  |                               |
  |<-- [pipe] heartbeat ----------|
  |--- [pipe] heartbeat_ack ---->|
```

### Hello (Plugin -> Core, named pipe)

```json
{
  "type": "hello",
  "version": 2,
  "name": "trackpad",
  "broadcastPort": 19874,
  "commands": ["axis", "button", "reset"]
}
```

| Field | Type | Required | Description |
|---|---|---|---|
| `type` | string | yes | Always `"hello"` |
| `version` | integer | yes | Protocol version. Core accepts any version <= its own. |
| `name` | string | yes | Plugin identifier (e.g. `"loupedeck"`, `"streamdeck"`, `"trackpad"`). |
| `broadcastPort` | integer | no | UDP port where this plugin listens for state broadcasts. Default: `19872`. |
| `commands` | string[] | no | Command types this plugin intends to use. Triggers API negotiation in the welcome response. If omitted, no `apiStatus` is returned. |

### Welcome (Core -> Plugin, named pipe)

```json
{
  "type": "welcome",
  "version": 2,
  "axes": {"1": 0.5, "2": 0.5},
  "buttons": {"1": false},
  "apiStatus": {"axis": "exists", "button": "exists", "reset": "exists"},
  "coreVersion": "0.1.0"
}
```

| Field | Type | Required | Description |
|---|---|---|---|
| `type` | string | yes | Always `"welcome"` |
| `version` | integer | yes | Protocol version the core is using. |
| `axes` | object | yes | Current axis positions. Keys are axis IDs (string), values are floats 0.0-1.0. |
| `buttons` | object | yes | Current button states. Keys are button IDs (string), values are booleans. |
| `apiStatus` | object | conditional | Present only when the hello included `commands`. Maps each requested command name to one of: `"exists"`, `"deprecated"`, `"undefined"`. |
| `coreVersion` | string | conditional | Present only when the hello included `commands`. Core binary version as semver (e.g. `"0.1.0"`). |

### Error on Handshake Failure

If the hello is rejected, core sends an error and closes the pipe:

```json
{"type": "error", "code": "unsupported_version", "message": "Protocol version 99 is not supported"}
```

---

## 4. Message Reference

### Client -> Core (UDP port 19871)

#### Axis

Controls a virtual joystick axis.

```json
{"type": "axis", "axis": 1, "mode": "hold", "diff": 3, "sensitivity": 0.02}
{"type": "axis", "axis": 2, "mode": "spring", "diff": -1, "sensitivity": 0.02, "decayRate": 0.95}
{"type": "axis", "axis": 3, "mode": "detent", "diff": 1, "steps": 5}
```

| Field | Type | Required | Description |
|---|---|---|---|
| `type` | string | yes | Always `"axis"` |
| `axis` | integer | yes | Axis number, 1-8. |
| `mode` | string | yes | One of: `"hold"`, `"spring"`, `"detent"`. |
| `diff` | integer | yes | Signed delta (positive = increase, negative = decrease). |
| `sensitivity` | float | no | Multiplier applied to diff. Default: `0.02`. Used by `hold` and `spring` modes. |
| `decayRate` | float | no | Rate at which axis decays toward center (0.5). Range: 0.0-1.0. Default: `0.95`. Only used by `spring` mode. |
| `steps` | integer | no | Number of discrete positions across the axis range. Range: 2-20. Default: `5`. Only used by `detent` mode. |

**Mode details:**

- **hold** — Accumulates `diff * sensitivity` onto the current axis position. The axis stays where it is set.
- **spring** — Same accumulation as hold, but the axis continuously decays toward center (0.5) each tick, multiplying the distance from center by `decayRate`.
- **detent** — Divides the 0.0-1.0 range into `steps` discrete positions. Each `diff` unit moves one detent step.

#### Button

Controls a virtual joystick button.

```json
{"type": "button", "button": 1, "mode": "momentary", "state": "down"}
{"type": "button", "button": 2, "mode": "toggle", "state": "down"}
{"type": "button", "button": 3, "mode": "pulse"}
{"type": "button", "button": 4, "mode": "double", "delay": 50}
{"type": "button", "button": 5, "mode": "rapid", "state": "down", "rate": 100}
{"type": "button", "button": 6, "mode": "longshort", "state": "down", "shortButton": 6, "longButton": 7, "threshold": 500}
```

| Field | Type | Required | Description |
|---|---|---|---|
| `type` | string | yes | Always `"button"` |
| `button` | integer | yes | Button number, 1-128. |
| `mode` | string | yes | One of: `"momentary"`, `"toggle"`, `"pulse"`, `"double"`, `"rapid"`, `"longshort"`. |
| `state` | string | conditional | `"down"` or `"up"`. Required for `momentary`, `rapid`, `longshort`. Used by `toggle` (flips on `"down"`, ignores `"up"`). Not used by `pulse` or `double`. |
| `delay` | integer | no | Milliseconds between the two pulses in `double` mode. Range: 10-200. Default: `50`. |
| `rate` | integer | no | Milliseconds between auto-repeat presses in `rapid` mode. Range: 20-500. Default: `100`. |
| `shortButton` | integer | no | Button to press on short tap in `longshort` mode. |
| `longButton` | integer | no | Button to press on long hold in `longshort` mode. |
| `threshold` | integer | no | Milliseconds to distinguish short from long press in `longshort` mode. Range: 100-2000. Default: `500`. |

**Mode details:**

- **momentary** — Direct press/release. The virtual button mirrors the physical state.
- **toggle** — Flips the button state on each `"down"` event. `"up"` events are ignored.
- **pulse** — Single 50ms press-release cycle. No `state` needed.
- **double** — Two pulses separated by `delay` milliseconds. No `state` needed.
- **rapid** — Auto-repeats the button at `rate` ms intervals while `state` is `"down"`. Stops on `"up"`.
- **longshort** — On `"down"`, starts a timer. If `"up"` arrives before `threshold` ms, presses `shortButton`. If the timer exceeds `threshold`, presses `longButton`.

#### Reset

Resets an axis to a specific position.

```json
{"type": "reset", "axis": 1, "position": 0.5}
```

| Field | Type | Required | Description |
|---|---|---|---|
| `type` | string | yes | Always `"reset"` |
| `axis` | integer | yes | Axis number, 1-8. |
| `position` | float | yes | Target position, 0.0-1.0. |

### Client -> Core (Named Pipe)

#### Heartbeat Ack

Sent in response to a heartbeat from core.

```json
{"type": "heartbeat_ack"}
```

No additional fields.

#### Core Upgrade

Requests the core to shut down so that a newer version can be launched.

```json
{"type": "core_upgrade", "newVersion": "1.3.0", "estimatedStartupMs": 15000}
```

| Field | Type | Required | Description |
|---|---|---|---|
| `type` | string | yes | Always `"core_upgrade"` |
| `newVersion` | string | yes | Semver version of the new core binary. Must be strictly greater than the running version. |
| `estimatedStartupMs` | integer | no | How long (ms) the plugin expects the new core to take to start. Used as the `coreStartTimeout` in the resulting `core_restarting` broadcast. |

### Core -> Client (Named Pipe)

#### Welcome

See [Handshake Flow](#3-handshake-flow).

#### Heartbeat

```json
{"type": "heartbeat"}
```

Sent every **5 seconds**. The plugin must respond with `heartbeat_ack`. If 2 consecutive acks are missed (30 seconds), the core considers the plugin disconnected and may close the pipe.

#### Error

```json
{"type": "error", "code": "unsupported_version", "message": "Protocol version 99 is not supported"}
```

| Field | Type | Required | Description |
|---|---|---|---|
| `type` | string | yes | Always `"error"` |
| `code` | string | yes | Machine-readable error code. |
| `message` | string | yes | Human-readable description. |

**Error codes:**

| Code | When |
|---|---|
| `unsupported_version` | Hello version is higher than core supports. |
| `upgrade_rejected` | Core upgrade request denied (e.g. version not greater). |
| `invalid_version` | The `newVersion` in a core_upgrade is not valid semver. |
| `vjoy_not_installed` | vJoy driver is not detected on the system. |

#### Shutdown

```json
{"type": "shutdown"}
```

Core is shutting down normally. Plugins should stop sending commands and begin reconnection logic.

#### Core Restarting

```json
{"type": "core_restarting", "coreStartTimeout": 15000, "reason": "upgrade", "requestedBy": "trackpad"}
```

| Field | Type | Required | Description |
|---|---|---|---|
| `type` | string | yes | Always `"core_restarting"` |
| `coreStartTimeout` | integer | yes | Milliseconds to wait before attempting auto-launch of the new core. |
| `reason` | string | yes | Why the core is restarting (e.g. `"upgrade"`, `"shutdown"`). |
| `requestedBy` | string | no | Present when `reason` is `"upgrade"`. The `name` of the plugin that requested the upgrade. |

#### Warning (debug mode only)

```json
{"type": "warning", "code": "unknown_mode", "message": "Unknown button mode: turbo", "context": {"actionType": "button", "mode": "turbo"}}
```

Only sent when the core is launched with the `--debug-messages` flag.

| Field | Type | Required | Description |
|---|---|---|---|
| `type` | string | yes | Always `"warning"` |
| `code` | string | yes | Warning code. |
| `message` | string | yes | Human-readable description. |
| `context` | object | yes | Key-value pairs providing additional detail. |

**Warning codes:**

| Code | When |
|---|---|
| `unknown_mode` | A recognized action type was received with an unknown mode. |
| `malformed_action` | A message could not be fully parsed (e.g. missing required fields). |
| `unknown_action` | The `type` field does not match any known action. |

### Core -> Client (UDP Broadcast)

#### State

```json
{"type": "state", "axes": {"1": 0.73}, "buttons": {"1": true}}
```

| Field | Type | Required | Description |
|---|---|---|---|
| `type` | string | yes | Always `"state"` |
| `axes` | object | yes | Current axis positions. Keys are axis IDs (string), values are floats 0.0-1.0. |
| `buttons` | object | yes | Current button states. Keys are button IDs (string), values are booleans. |

**Broadcast timing:** State updates are sent at approximately 60 Hz, debounced: 100ms after the first change, with a forced flush at 250ms maximum latency.

---

## 5. API Negotiation

When a plugin includes a `commands` array in its hello message, the core responds with an `apiStatus` map in the welcome. Each requested command is mapped to one of three statuses:

| Status | Meaning | Plugin action |
|---|---|---|
| `exists` | The command is fully supported. | Proceed normally. |
| `deprecated` | The command still works but may degrade to a no-op in a future version. | Warn the user. Continue operating. |
| `undefined` | The command is not recognized by this core version. | Trigger the upgrade flow (see next section). |

**Invariants:**

- Once a command reaches `deprecated`, it never reverts to `undefined`. It may eventually become a silent no-op, but it will always be acknowledged.
- `undefined` means the core genuinely does not know about this command type. The most common cause is a newer plugin talking to an older core.

---

## 6. Core Upgrade Flow

When a plugin detects that a required command is `undefined`, it can request a core upgrade:

1. **Detect** — Plugin sees `"undefined"` for one or more commands in `apiStatus`.
2. **Locate binary** — Plugin finds the newer core binary (bundled with the plugin or downloaded).
3. **Send core_upgrade** — Plugin sends `core_upgrade` over the named pipe with the `newVersion` and optional `estimatedStartupMs`.
4. **Version guard** — Core validates that `newVersion` is strictly greater than the running version. If not, core responds with an `upgrade_rejected` error.
5. **Broadcast** — Core sends `core_restarting` to all connected plugins (with `reason: "upgrade"` and `requestedBy` set to the requesting plugin's name).
6. **Shutdown** — Core shuts down cleanly.
7. **Relaunch** — The requesting plugin launches the new core binary after a short delay.
8. **Reconnect** — All plugins reconnect via their normal reconnection logic, performing a fresh handshake.

---

## 7. Lifecycle

### Auto-launch

If a plugin cannot connect to the named pipe (core not running), it should attempt to launch the core binary:

1. Check `%APPDATA%\Apricadabra\apricadabra-core.exe` (user-installed / upgraded version).
2. Fall back to the core binary bundled with the plugin.

### Reconnection

When the pipe connection is lost (core crash, shutdown, upgrade), plugins should:

1. Wait **1 second**.
2. Attempt to reconnect (open pipe, send hello).
3. Repeat until successful.

### Heartbeat

- Core sends `heartbeat` every **5 seconds** over the named pipe.
- Plugin must respond with `heartbeat_ack` over the named pipe.
- If 2 consecutive acks are missed (~30 seconds), core considers the plugin disconnected.

### Core Start Timeout

When a plugin receives `core_restarting`, the `coreStartTimeout` value (milliseconds) tells the plugin how long to wait before attempting auto-launch. This prevents multiple plugins from racing to launch the new core simultaneously.

During the timeout window, the plugin should:
- Not attempt to auto-launch the core.
- Continue reconnection attempts (the new core may come up before the timeout).

---

## 8. Core Binary

### Location

| Priority | Path |
|---|---|
| 1 (primary) | `%APPDATA%\Apricadabra\apricadabra-core.exe` |
| 2 (fallback) | Bundled with the plugin |

### Version File

`%APPDATA%\Apricadabra\apricadabra-core.version` — plain text file containing a single semver string (e.g. `0.1.0`). No trailing newline required.

### CLI Flags

| Flag | Description |
|---|---|
| `--stop` | Sends a shutdown signal to the running core instance and exits. |
| `--debug` | Enables verbose debug logging. |
| `--debug-messages` | Enables warning broadcasts to connected plugins for unrecognized actions. |
| `--version` | Prints the core version and exits. |

---

## 9. Plugin Bindings Schema

Each plugin stores its input-to-action mappings in a JSON file.

### Location

`%APPDATA%\Apricadabra\<plugin-name>\bindings.json`

### Structure

```json
{
  "schema": 1,
  "plugin": "loupedeck",
  "bindings": [
    {
      "input": { ... },
      "action": { ... }
    }
  ]
}
```

| Field | Type | Description |
|---|---|---|
| `schema` | integer | Schema version for this bindings format. |
| `plugin` | string | Plugin name, matching the `name` in the hello message. |
| `bindings` | array | Array of binding objects. |

### Binding Object

Each binding maps a plugin-specific **input** (gesture, knob turn, button press) to a protocol **action** (the message that will be sent to the core).

- **`action`** — Matches the wire format of a protocol command. For example, an axis binding's action would contain `type`, `axis`, `mode`, `sensitivity`, etc. exactly as they appear in the UDP message.
- **`input`** — Plugin-specific. The schema is defined by each plugin and is not part of this protocol. For example, the Loupedeck plugin might use `{"knob": "knob1", "gesture": "rotate"}`.

---

## 10. Unrecognized Action Handling

The core is lenient with incoming messages to maintain forward compatibility:

| Situation | Core behavior |
|---|---|
| Known action type, unknown mode | Default to `momentary` (button) or `hold` (axis). |
| Malformed action (missing required fields) | No-op. Message is silently dropped. |
| Unknown action type | No-op. Message is silently dropped. |

When the core is launched with `--debug-messages`, it sends `warning` messages over the named pipe for each of the above cases, enabling plugin developers to diagnose issues during development.
