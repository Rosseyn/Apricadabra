# Protocol & Ecosystem Changes Design

**Date:** 2026-03-25
**Sub-project:** 1 of 3 (Protocol → C# Client Library → Trackpad Plugin)
**Status:** Draft

## Overview

Apricadabra's protocol needs to evolve to support a plugin ecosystem. This spec covers API negotiation during handshake, a core upgrade flow for version management, a standardized bindings schema for standalone plugins, and a formal protocol spec document. These changes lay the foundation for the C# client library (sub-project 2) and the trackpad plugin (sub-project 3), and enable third-party plugin development.

## Context

Currently, two plugins exist (Loupedeck in C#, Stream Deck in TypeScript). Each duplicates the connection/lifecycle logic and bundles the core binary. The protocol is version 1 with no capability negotiation. A third plugin (trackpad) is planned, along with a template repo for third-party developers. The protocol must support:

- Plugins discovering whether the running core supports their commands
- Graceful core version upgrades without manual intervention
- A documented spec that any language can implement against
- Standardized config storage for standalone plugins

## Distribution Model

**Hybrid distribution:** Core is available as a standalone install (e.g., winget, MSI) and also bundled with each plugin as a fallback. First plugin to launch starts the core; others connect to the running instance.

**Repo structure:** Monorepo for first-party code (core, Loupedeck, Stream Deck, trackpad). Separate template repo (`apricadabra-plugin-template`) for third-party developers. Core binary published via GitHub Releases; both first-party and third-party plugins consume it the same way.

**Client libraries:** Maintained NuGet package for C# and npm package for TypeScript. Protocol spec document covers all other languages.

---

## 1. API Negotiation in Handshake

### Protocol Version Semantics

The `version` field in `hello`/`welcome` represents the wire format version, not the core binary version. It bumps only for structural changes to the protocol (e.g., switching from newline-delimited JSON to a different encoding). Adding new commands does not bump the protocol version — that's handled by API negotiation.

Core accepts connections where the plugin's `version` is less than or equal to the core's protocol version (minimum version check, not strict equality). This allows older plugins to connect to newer cores without issue.

### Updated Hello (Plugin → Core, Named Pipe)

```json
{
  "type": "hello",
  "version": 2,
  "name": "trackpad",
  "broadcastPort": 19874,
  "commands": ["axis", "button", "reset", "shutdown"]
}
```

**New fields:**
- `commands` (string array): Every command type this plugin intends to send.

### Updated Welcome (Core → Plugin, Named Pipe)

```json
{
  "type": "welcome",
  "version": 2,
  "axes": { "1": 0.73, "2": 0.50 },
  "buttons": { "1": true, "5": false },
  "apiStatus": {
    "axis": "exists",
    "button": "exists",
    "reset": "exists",
    "shutdown": "deprecated"
  },
  "coreVersion": "1.2.0"
}
```

**New fields:**
- `apiStatus` (object): Maps each requested command to one of three statuses:
  - `exists` — Fully supported, works as intended
  - `deprecated` — Core acknowledges the command. May still work fully, may be a no-op. Core will never reject it. Plugin should warn the user where possible.
  - `undefined` — Core has never heard of this command. The plugin is newer than the core. Triggers the upgrade flow.
- `coreVersion` (string): Semver string for logging and diagnostics.

### Deprecation Semantics

Deprecated commands are **permanent**. They never transition to `undefined`. Over time they may degrade from functional to no-op, but they remain in core's vocabulary forever. This prevents old plugins from seeing `undefined` and incorrectly triggering a core downgrade.

Lifecycle: `exists` → `deprecated` (permanent, may degrade to no-op). The `deprecated` → `undefined` transition never happens.

### Plugin Response to `apiStatus`

- All `exists`: proceed normally.
- Any `deprecated`: warn the user where the plugin SDK supports it. Fallback: rename the affected action to include `[deprecated]` in its display name. Continue operating — deprecated commands are best-effort.
- Any `undefined`: initiate the core upgrade flow (Section 2).

---

## 2. Core Upgrade Flow

When a plugin receives `undefined` in the `apiStatus` response, it initiates a core upgrade.

### Core Version Discovery

Each plugin bundles a core binary alongside an `apricadabra-core.version` file — a plain text file containing the semver string (e.g., `1.3.0`). The plugin reads this file to determine its bundled core version. No shell-out or binary introspection required.

### Step 1: Plugin Sends Upgrade Request

```json
{
  "type": "core_upgrade",
  "newVersion": "1.3.0",
  "estimatedStartupMs": 15000
}
```

Sent over the named pipe to core. The `newVersion` value comes from the plugin's bundled `apricadabra-core.version` file.

### Version Guard

Core compares `newVersion` against its own `coreVersion`. If `newVersion` is not strictly greater than `coreVersion`, core rejects the upgrade:

```json
{
  "type": "error",
  "code": "upgrade_rejected",
  "message": "Bundled version 1.1.0 is not newer than running version 1.3.0"
}
```

The plugin proceeds with the running core as-is. This prevents accidental downgrades — e.g., a plugin with an older bundled core incorrectly triggering the upgrade flow.

### Step 2: Core Broadcasts Restart Notice

```json
{
  "type": "core_restarting",
  "coreStartTimeout": 15000,
  "reason": "upgrade",
  "requestedBy": "trackpad"
}
```

Sent over the named pipe to all connected plugins. The `coreStartTimeout` value is taken from the requesting plugin's `estimatedStartupMs`, defaulting to 15000ms.

### Step 3: Core Performs Graceful Shutdown

- Closes all named pipe connections
- Closes UDP listeners
- Exits cleanly

### Step 4: Requesting Plugin Launches New Core

- Starts its bundled `apricadabra-core.exe`
- Waits for named pipe to become available
- Reconnects with a fresh `hello` handshake
- State is reset (axes at center, buttons off) — this is a clean start

### Step 5: Other Plugins Reconnect

- Each plugin's existing reconnection logic detects the pipe is available
- They perform fresh `hello` handshakes with the new core
- Full state reset

### Plugin Behavior During `coreStartTimeout`

On receiving `core_restarting`:
1. Set an internal timer for `coreStartTimeout` ms
2. Stop sending commands
3. Named pipe will close — enter reconnection mode
4. During timeout: attempt to reconnect to named pipe, but do NOT launch `apricadabra-core.exe`
5. After timeout expires with no connection: fall back to normal auto-launch behavior

### Edge Cases

- **Requesting plugin crashes before launching new core:** Other plugins' `coreStartTimeout` expires, they fall back to auto-launch. Whoever gets there first launches their bundled core.
- **New core fails to start within timeout:** Same fallback — timeout expires, plugins auto-launch normally.
- **Plugin wasn't connected when broadcast went out:** Plugin connects, pipe isn't available, normal auto-launch kicks in. Named pipe acts as a natural mutex (first to bind wins).

---

## 3. `coreStartTimeout` Broadcast Mechanism

Used for any scenario where plugins should back off from launching core.

### Triggers

1. **Core upgrade** — as described in Section 2
2. **Clean shutdown** — core is shutting down intentionally; plugins shouldn't immediately relaunch it

### Message Format

```json
{
  "type": "core_restarting",
  "coreStartTimeout": 15000,
  "reason": "upgrade | shutdown",
  "requestedBy": "trackpad"
}
```

Sent over named pipe to all connected plugins. Default timeout is 15000ms (15 seconds). Core startup involves vJoy acquisition which can be slow. The `requestedBy` field is present when `reason` is `"upgrade"` and omitted for `"shutdown"`.

---

## 4. Unrecognized Action Handling & Debug Mode

### Core Behavior on Unrecognized Actions

1. **Unknown mode** (e.g., `"mode": "turbo"` on a button action): Core defaults to `momentary` for buttons and `hold` for axes. Action fires with default behavior.
2. **Malformed action** (e.g., missing required fields like `button` on a button action): No-op. Core drops the message silently.
3. **Unknown action type** (e.g., `"type": "haptic"`): No-op. Core drops the message silently.

### `--debug-messages` Flag

When core is launched with `--debug-messages`, it sends warnings back to plugins over the named pipe for any unrecognized content:

```json
{
  "type": "warning",
  "code": "unknown_mode",
  "message": "Unknown mode 'turbo' for button action, defaulting to 'momentary'",
  "context": { "actionType": "button", "mode": "turbo" }
}
```

Warning codes:
- `unknown_mode` — Recognized action type, unrecognized mode. Core defaulted to `momentary` (buttons) or `hold` (axes).
- `malformed_action` — Missing required fields. Message dropped.
- `unknown_action` — Unrecognized action type. Message dropped.

Without `--debug-messages`, these warnings are not sent. No throttling is needed — developers opt in explicitly.

The `--debug-messages` flag is documented in the protocol spec.

---

## 5. Protocol Spec Document

A formal protocol specification at `core/docs/protocol.md`. This is the single source of truth for all plugin developers.

### Contents

1. **Connection model** — Named pipes (`\\.\pipe\apricadabra`) for handshake/heartbeat, UDP port 19871 for commands, per-plugin UDP broadcast ports for state updates
2. **Handshake flow** — `hello`/`welcome` exchange with API negotiation
3. **Message reference** — Every message type with JSON schema, field descriptions, examples, and defaults
4. **API negotiation** — `exists`/`deprecated`/`undefined` semantics, upgrade flow, `coreStartTimeout`
5. **Lifecycle** — Auto-launch strategy, reconnection behavior, heartbeat/ack protocol, graceful shutdown
6. **Unrecognized action handling** — Default behavior and `--debug-messages` flag
7. **Core binary** — Where to find it (`%APPDATA%/Apricadabra/` primary, plugin bundle fallback), launch args, expected behavior
8. **Bindings schema** — Standard config file format for standalone plugins (Section 6)

### Maintenance

- Version number tracks protocol version
- Changes go through `CHANGELOG.md` with deprecation notices and migration guidance
- Breaking changes are never introduced silently — they go through the deprecation lifecycle

---

## 6. Standardized Plugin Bindings Schema

For standalone plugins that own their own configuration. Host-managed plugins (Loupedeck, Stream Deck) continue using their platform's built-in settings storage.

### File Location

`%APPDATA%/Apricadabra/<plugin-name>/bindings.json`

### Schema

```json
{
  "schema": 1,
  "plugin": "trackpad",
  "bindings": [
    {
      "id": "swipe-3-left-btn12",
      "gesture": {
        "type": "swipe",
        "fingers": 3,
        "direction": "left"
      },
      "action": {
        "type": "button",
        "button": 12,
        "mode": "pulse"
      }
    },
    {
      "id": "pinch-axis3",
      "gesture": {
        "type": "pinch"
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

### What's Standardized (in the protocol spec)

- **File location convention:** `%APPDATA%/Apricadabra/<plugin-name>/bindings.json`
- **Top-level structure:** `schema` (integer version), `plugin` (string name), `bindings` (array)
- **The `action` object:** Same fields as the core protocol messages — button/axis/reset with their modes and parameters. Defined in the protocol spec message reference.
- **The `id` field:** Plugin-generated string, unique within the bindings array. Used for UI state management.

### What's Plugin-Specific

- **The `gesture`/input object:** Each plugin defines its own input vocabulary. The trackpad plugin has swipes, pinch, rotate, scroll, and taps. A future MIDI plugin would have note-on, CC, etc. The protocol spec does not standardize input types across plugins.

---

## 7. Audit Existing Plugins

Before building on the updated protocol, validate that the existing Loupedeck and Stream Deck plugin code matches the protocol spec as written. Fix any drift. This happens after the spec is written and before any new implementation begins.

### Scope

- Verify handshake message formats match spec
- Verify command message formats match spec
- Verify heartbeat/ack behavior matches spec
- Verify auto-launch and reconnection behavior is consistent
- Document any intentional differences (e.g., Loupedeck's 3-mode limitation due to SDK constraints)

---

## Implementation Order

1. Write `core/docs/protocol.md` (the formal spec, documenting current + new behavior)
2. Audit existing LD and SD plugins against the spec; fix drift
3. Implement protocol v2 changes in core (API negotiation, `core_restarting` broadcast, `--debug-messages`, unrecognized action handling)
4. Update Loupedeck plugin to v2 handshake
5. Update Stream Deck plugin to v2 handshake
6. Add standardized bindings schema to the spec

---

## Sub-project Dependencies

This spec (sub-project 1) must be complete before:
- **Sub-project 2** (C# Client Library): Extracts the connection/lifecycle logic including v2 negotiation into a reusable NuGet package
- **Sub-project 3** (Trackpad Plugin): Consumes the C# client library and bindings schema

---

## Open Questions

None at this time. All decisions resolved during brainstorming.
