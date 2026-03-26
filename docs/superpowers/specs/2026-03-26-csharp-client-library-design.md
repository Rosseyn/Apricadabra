# C# Client Library Design

**Date:** 2026-03-26
**Sub-project:** 2 of 3 (Protocol → C# Client Library → Trackpad Plugin)
**Status:** Draft

## Overview

Extract the Apricadabra connection/lifecycle logic from the Loupedeck plugin into a reusable C# NuGet package (`Apricadabra.Client`). This library handles named pipe handshake, UDP commands, heartbeat, auto-launch, reconnection, v2 API negotiation, and `coreStartTimeout` suppression — so that any C# plugin developer can connect to the core with a single class.

## Context

The Loupedeck plugin's `CoreConnection.cs` contains all the connection logic but is embedded in the Loupedeck plugin namespace. The trackpad plugin (sub-project 3) needs the same logic. Duplicating it would be the third copy (Loupedeck, Stream Deck TypeScript, trackpad). Extracting it into a library eliminates duplication for C# plugins and establishes the pattern for future client libraries.

---

## 1. Project Location and Target

**Path:** `core/sdk/csharp/Apricadabra.Client/`

**Target framework:** .NET Standard 2.0

**Dependencies:**
- `System.Text.Json` (NuGet package — not included natively in .NET Standard 2.0)

**Package metadata:**
- Package ID: `Apricadabra.Client`
- Description: C# client library for connecting to the Apricadabra core
- License: Same as project

---

## 2. Public API

### ApricadabraClient

Main class. Implements `IDisposable`.

**Constructor:**
```csharp
public ApricadabraClient(
    string pluginName,
    int broadcastPort = 19872,
    string[] commands = null,    // defaults to ["axis", "button", "reset"]
    string[] corePaths = null    // additional paths to search for core binary
)
```

**Connection:**
```csharp
public async Task ConnectAsync(CancellationToken ct = default);
public void Dispose();
public bool IsConnected { get; }
```

**Sending commands (typed helpers):**
```csharp
// Axis
public void SendAxis(int axis, AxisMode mode, int diff, float sensitivity = 0.02f, float decayRate = 0.95f, int steps = 5);

// Button
public void SendButton(int button, ButtonMode mode, ButtonState? state = null, int delay = 50, int rate = 100, int? shortButton = null, int? longButton = null, int threshold = 500);

// Reset
public void SendReset(int axis, float position);
```

All `Send*` methods build the JSON message internally and send via UDP. They are fire-and-forget (no async needed — UDP send is fast). They no-op if not connected.

**Events:**
```csharp
public event Action<Dictionary<int, float>, Dictionary<int, bool>> OnStateUpdate;
public event Action<string, Dictionary<string, ApiStatus>> OnConnected;  // coreVersion, apiStatus
public event Action OnDisconnected;
public event Action<string, string> OnError;  // code, message
public event Action<string, ApiStatus> OnWarning;  // command, status (deprecated/undefined)
```

### Enums

```csharp
public enum AxisMode { Hold, Spring, Detent }
public enum ButtonMode { Momentary, Toggle, Pulse, Double, Rapid, LongShort }
public enum ButtonState { Down, Up }
public enum ApiStatus { Exists, Deprecated, Undefined }
```

---

## 3. File Structure

```
core/sdk/csharp/Apricadabra.Client/
├── Apricadabra.Client.csproj    # .NET Standard 2.0, System.Text.Json dependency
├── ApricadabraClient.cs          # Main class: connect, send, events, lifecycle
├── Enums.cs                      # AxisMode, ButtonMode, ButtonState, ApiStatus
└── CoreLauncher.cs               # Auto-launch logic: find and start core binary
```

### ApricadabraClient.cs

Responsibilities:
- Named pipe connection with exponential backoff retry (100ms → 5s)
- v2 handshake: send hello with `commands`, parse welcome with `apiStatus`/`coreVersion`
- Fire `OnConnected` with parsed handshake results
- Fire `OnWarning` for deprecated/undefined commands
- Heartbeat ack loop on pipe
- Handle `core_restarting` message: set suppression timer
- Handle `error` and `shutdown` messages
- UDP command sending via typed helper methods (build JSON internally)
- UDP state broadcast listening: parse `state` messages, fire `OnStateUpdate` with typed dictionaries
- Reconnection on disconnect (1s delay, then retry)
- `IDisposable`: cancel tasks, close sockets/pipes

### Enums.cs

Four enums with string conversion helpers for JSON serialization. Each enum value maps to its snake_case wire format string.

### CoreLauncher.cs

Responsibilities:
- Find `apricadabra-core.exe` by checking paths in order:
  1. `%APPDATA%/Apricadabra/apricadabra-core.exe`
  2. Any additional paths from constructor's `corePaths` parameter
- Launch with `CreateNoWindow = true`, `UseShellExecute = false`
- Respect `coreStartTimeout` suppression (don't launch during core restart)
- Exposed as internal class, called by `ApricadabraClient` during connection retry

---

## 4. Loupedeck Plugin Migration

After extracting the library:

1. **Delete** `loupedeck-plugin/ApricadabraPlugin/src/CoreConnection.cs`
2. **Add NuGet package reference** to `Apricadabra.Client` in the Loupedeck plugin's `.csproj`:
   ```xml
   <PackageReference Include="Apricadabra.Client" Version="0.1.0" />
   ```
   During local development, use a local NuGet source pointing to the library's build output. For distribution, publish to nuget.org.
3. **Update namespace import** in all action files: `using Apricadabra.Client;`
4. **Update `ApricadabraApplication.cs`** (or equivalent plugin entry point):
   - Replace `new CoreConnection()` with `new ApricadabraClient("loupedeck")`
   - Wire events to the new typed signatures
5. **Update action classes** (`DialAction.cs`, `ButtonCommand.cs`, `ResetAxisCommand.cs`):
   - Replace `SendAsync(new JsonObject { ... })` calls with typed helpers:
     - `client.SendAxis(axis, mode, diff, sensitivity)`
     - `client.SendButton(button, mode)`
     - `client.SendReset(axis, position)`
6. **Update `StateDisplay.cs`** if it reads from `OnStateUpdate` — adapt to typed dictionaries instead of `JsonObject`

### Behavioral Parity

The library must produce identical wire-format messages to the current `CoreConnection.cs`. The Loupedeck plugin should behave identically after migration — same messages sent, same events received, same auto-launch behavior.

---

## 5. Testing Strategy

Unit tests for the library in `core/sdk/csharp/Apricadabra.Client.Tests/`:

- **Enum serialization:** Verify each enum value serializes to correct snake_case string
- **Message building:** Verify `SendAxis`, `SendButton`, `SendReset` produce correct JSON
- **Welcome parsing:** Verify `apiStatus` and `coreVersion` are correctly extracted
- **State parsing:** Verify state broadcast JSON is correctly parsed to typed dictionaries
- **CoreLauncher path resolution:** Verify search order and `coreStartTimeout` suppression

Integration testing (manual, Windows only): connect to real core, verify handshake, send commands, receive state updates.

---

## Implementation Order

1. Create `Apricadabra.Client` project with `.csproj`
2. Implement `Enums.cs`
3. Implement `CoreLauncher.cs`
4. Implement `ApricadabraClient.cs` (extract from `CoreConnection.cs`)
5. Write unit tests
6. Migrate Loupedeck plugin to use the library
7. Verify Loupedeck plugin builds and action classes compile

---

## Sub-project Dependencies

This spec (sub-project 2) depends on:
- **Sub-project 1** (Protocol v2): Completed. The library implements the v2 protocol.

This spec must be complete before:
- **Sub-project 3** (Trackpad Plugin): Consumes this library.
