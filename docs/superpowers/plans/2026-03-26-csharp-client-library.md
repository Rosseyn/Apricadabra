# C# Client Library Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Extract the Apricadabra connection logic into a reusable C# NuGet package (`Apricadabra.Client`) and migrate the Loupedeck plugin to consume it.

**Architecture:** The library provides a semi-typed `ApricadabraClient` class with helper methods for axis/button/reset commands, typed enums, and typed events. It handles named pipe handshake, UDP transport, heartbeat, auto-launch, reconnection, and v2 API negotiation. The Loupedeck plugin deletes its `CoreConnection.cs` and references the library via NuGet.

**Tech Stack:** C# / .NET Standard 2.0, System.Text.Json, NUnit (tests)

**Spec:** `docs/superpowers/specs/2026-03-26-csharp-client-library-design.md`

---

## File Structure

### New Files (Library)
- `core/sdk/csharp/Apricadabra.Client/Apricadabra.Client.csproj` — Project targeting .NET Standard 2.0
- `core/sdk/csharp/Apricadabra.Client/Enums.cs` — AxisMode, ButtonMode, ButtonState, ApiStatus with snake_case helpers
- `core/sdk/csharp/Apricadabra.Client/CoreLauncher.cs` — Find and launch core binary
- `core/sdk/csharp/Apricadabra.Client/ApricadabraClient.cs` — Main client class

### New Files (Tests)
- `core/sdk/csharp/Apricadabra.Client.Tests/Apricadabra.Client.Tests.csproj` — Test project targeting .NET 8.0
- `core/sdk/csharp/Apricadabra.Client.Tests/EnumTests.cs` — Enum serialization tests
- `core/sdk/csharp/Apricadabra.Client.Tests/MessageBuildingTests.cs` — Send* method JSON output tests
- `core/sdk/csharp/Apricadabra.Client.Tests/ParsingTests.cs` — Welcome and state parsing tests

### Modified Files (Loupedeck Migration)
- `loupedeck-plugin/ApricadabraPlugin/src/ApricadabraPlugin.csproj` — Add NuGet reference
- `loupedeck-plugin/ApricadabraPlugin/src/ApricadabraPlugin.cs` — Switch to ApricadabraClient
- `loupedeck-plugin/ApricadabraPlugin/src/Actions/DialAction.cs` — Use typed helpers
- `loupedeck-plugin/ApricadabraPlugin/src/Actions/ButtonCommand.cs` — Use typed helpers
- `loupedeck-plugin/ApricadabraPlugin/src/Actions/ResetAxisCommand.cs` — Use typed helpers
- `loupedeck-plugin/ApricadabraPlugin/src/StateDisplay.cs` — Accept typed dictionaries

### Deleted Files
- `loupedeck-plugin/ApricadabraPlugin/src/CoreConnection.cs` — Replaced by library

---

## Task 1: Create Project and Enums

**Files:**
- Create: `core/sdk/csharp/Apricadabra.Client/Apricadabra.Client.csproj`
- Create: `core/sdk/csharp/Apricadabra.Client/Enums.cs`
- Create: `core/sdk/csharp/Apricadabra.Client.Tests/Apricadabra.Client.Tests.csproj`
- Create: `core/sdk/csharp/Apricadabra.Client.Tests/EnumTests.cs`

- [ ] **Step 1: Create the library .csproj**

Create `core/sdk/csharp/Apricadabra.Client/Apricadabra.Client.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>netstandard2.0</TargetFramework>
    <PackageId>Apricadabra.Client</PackageId>
    <Version>0.1.0</Version>
    <Description>C# client library for connecting to the Apricadabra core</Description>
    <LangVersion>latest</LangVersion>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="System.Text.Json" Version="8.0.5" />
  </ItemGroup>
</Project>
```

- [ ] **Step 2: Create the test project .csproj**

Create `core/sdk/csharp/Apricadabra.Client.Tests/Apricadabra.Client.Tests.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <IsPackable>false</IsPackable>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="NUnit" Version="4.3.2" />
    <PackageReference Include="NUnit3TestAdapter" Version="4.6.0" />
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.12.0" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\Apricadabra.Client\Apricadabra.Client.csproj" />
  </ItemGroup>
</Project>
```

- [ ] **Step 3: Write enum tests**

Create `core/sdk/csharp/Apricadabra.Client.Tests/EnumTests.cs`:

```csharp
using NUnit.Framework;
using Apricadabra.Client;

namespace Apricadabra.Client.Tests;

[TestFixture]
public class EnumTests
{
    [TestCase(AxisMode.Hold, "hold")]
    [TestCase(AxisMode.Spring, "spring")]
    [TestCase(AxisMode.Detent, "detent")]
    public void AxisMode_ToWireString_ReturnsSnakeCase(AxisMode mode, string expected)
    {
        Assert.That(mode.ToWireString(), Is.EqualTo(expected));
    }

    [TestCase(ButtonMode.Momentary, "momentary")]
    [TestCase(ButtonMode.Toggle, "toggle")]
    [TestCase(ButtonMode.Pulse, "pulse")]
    [TestCase(ButtonMode.Double, "double")]
    [TestCase(ButtonMode.Rapid, "rapid")]
    [TestCase(ButtonMode.LongShort, "longshort")]
    public void ButtonMode_ToWireString_ReturnsSnakeCase(ButtonMode mode, string expected)
    {
        Assert.That(mode.ToWireString(), Is.EqualTo(expected));
    }

    [TestCase(ButtonState.Down, "down")]
    [TestCase(ButtonState.Up, "up")]
    public void ButtonState_ToWireString_ReturnsSnakeCase(ButtonState state, string expected)
    {
        Assert.That(state.ToWireString(), Is.EqualTo(expected));
    }

    [TestCase("exists", ApiStatus.Exists)]
    [TestCase("deprecated", ApiStatus.Deprecated)]
    [TestCase("undefined", ApiStatus.Undefined)]
    public void ApiStatus_Parse_FromWireString(string wire, ApiStatus expected)
    {
        Assert.That(EnumExtensions.ParseApiStatus(wire), Is.EqualTo(expected));
    }
}
```

- [ ] **Step 4: Run tests to verify they fail**

Run: `cd core/sdk/csharp && dotnet test Apricadabra.Client.Tests/`
Expected: Compilation error — `Apricadabra.Client` namespace and types don't exist

- [ ] **Step 5: Implement Enums.cs**

Create `core/sdk/csharp/Apricadabra.Client/Enums.cs`:

```csharp
namespace Apricadabra.Client
{
    public enum AxisMode { Hold, Spring, Detent }
    public enum ButtonMode { Momentary, Toggle, Pulse, Double, Rapid, LongShort }
    public enum ButtonState { Down, Up }
    public enum ApiStatus { Exists, Deprecated, Undefined }

    public static class EnumExtensions
    {
        public static string ToWireString(this AxisMode mode) => mode switch
        {
            AxisMode.Hold => "hold",
            AxisMode.Spring => "spring",
            AxisMode.Detent => "detent",
            _ => "hold"
        };

        public static string ToWireString(this ButtonMode mode) => mode switch
        {
            ButtonMode.Momentary => "momentary",
            ButtonMode.Toggle => "toggle",
            ButtonMode.Pulse => "pulse",
            ButtonMode.Double => "double",
            ButtonMode.Rapid => "rapid",
            ButtonMode.LongShort => "longshort",
            _ => "momentary"
        };

        public static string ToWireString(this ButtonState state) => state switch
        {
            ButtonState.Down => "down",
            ButtonState.Up => "up",
            _ => "down"
        };

        public static ApiStatus ParseApiStatus(string wire) => wire switch
        {
            "exists" => ApiStatus.Exists,
            "deprecated" => ApiStatus.Deprecated,
            "undefined" => ApiStatus.Undefined,
            _ => ApiStatus.Undefined
        };
    }
}
```

- [ ] **Step 6: Run tests to verify they pass**

Run: `cd core/sdk/csharp && dotnet test Apricadabra.Client.Tests/`
Expected: All PASS

- [ ] **Step 7: Commit**

```bash
git add core/sdk/csharp/
git commit -m "feat(sdk): create Apricadabra.Client project with enums and tests"
```

---

## Task 2: CoreLauncher

**Files:**
- Create: `core/sdk/csharp/Apricadabra.Client/CoreLauncher.cs`

- [ ] **Step 1: Implement CoreLauncher**

Create `core/sdk/csharp/Apricadabra.Client/CoreLauncher.cs`:

```csharp
using System;
using System.Diagnostics;
using System.IO;

namespace Apricadabra.Client
{
    internal class CoreLauncher
    {
        private const string CoreExeName = "apricadabra-core.exe";
        private readonly string[] _additionalPaths;
        private DateTime _suppressUntil = DateTime.MinValue;

        public CoreLauncher(string[] additionalPaths)
        {
            _additionalPaths = additionalPaths ?? Array.Empty<string>();
        }

        public void SuppressLaunchUntil(DateTime until)
        {
            _suppressUntil = until;
        }

        public void TryLaunch()
        {
            if (DateTime.UtcNow < _suppressUntil)
                return;

            try
            {
                var corePath = FindCore();
                if (corePath == null) return;

                Process.Start(new ProcessStartInfo
                {
                    FileName = corePath,
                    UseShellExecute = false,
                    CreateNoWindow = true
                });
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"[Apricadabra] Failed to launch core: {ex.Message}");
            }
        }

        private string FindCore()
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var primaryPath = Path.Combine(appData, "Apricadabra", CoreExeName);
            if (File.Exists(primaryPath))
                return primaryPath;

            foreach (var dir in _additionalPaths)
            {
                var path = Path.Combine(dir, CoreExeName);
                if (File.Exists(path))
                    return path;
            }

            return null;
        }
    }
}
```

- [ ] **Step 2: Commit**

```bash
git add core/sdk/csharp/Apricadabra.Client/CoreLauncher.cs
git commit -m "feat(sdk): add CoreLauncher for auto-launching core binary"
```

---

## Task 3: ApricadabraClient — Message Building and Sending

**Files:**
- Create: `core/sdk/csharp/Apricadabra.Client/ApricadabraClient.cs` (partial — send methods only)
- Create: `core/sdk/csharp/Apricadabra.Client.Tests/MessageBuildingTests.cs`

- [ ] **Step 1: Write message building tests**

Create `core/sdk/csharp/Apricadabra.Client.Tests/MessageBuildingTests.cs`:

```csharp
using System.Text.Json.Nodes;
using NUnit.Framework;
using Apricadabra.Client;

namespace Apricadabra.Client.Tests;

[TestFixture]
public class MessageBuildingTests
{
    [Test]
    public void BuildAxisMessage_Hold_CorrectJson()
    {
        var json = ApricadabraClient.BuildAxisMessage(1, AxisMode.Hold, 3, 0.02f);
        var obj = JsonNode.Parse(json).AsObject();
        Assert.That(obj["type"].GetValue<string>(), Is.EqualTo("axis"));
        Assert.That(obj["axis"].GetValue<int>(), Is.EqualTo(1));
        Assert.That(obj["mode"].GetValue<string>(), Is.EqualTo("hold"));
        Assert.That(obj["diff"].GetValue<int>(), Is.EqualTo(3));
        Assert.That(obj["sensitivity"].GetValue<float>(), Is.EqualTo(0.02f).Within(0.001f));
    }

    [Test]
    public void BuildAxisMessage_Spring_IncludesDecayRate()
    {
        var json = ApricadabraClient.BuildAxisMessage(2, AxisMode.Spring, -1, 0.02f, decayRate: 0.95f);
        var obj = JsonNode.Parse(json).AsObject();
        Assert.That(obj["mode"].GetValue<string>(), Is.EqualTo("spring"));
        Assert.That(obj["decayRate"].GetValue<float>(), Is.EqualTo(0.95f).Within(0.001f));
    }

    [Test]
    public void BuildAxisMessage_Detent_IncludesSteps()
    {
        var json = ApricadabraClient.BuildAxisMessage(3, AxisMode.Detent, 1, 0.02f, steps: 5);
        var obj = JsonNode.Parse(json).AsObject();
        Assert.That(obj["mode"].GetValue<string>(), Is.EqualTo("detent"));
        Assert.That(obj["steps"].GetValue<int>(), Is.EqualTo(5));
    }

    [Test]
    public void BuildButtonMessage_Pulse_NoState()
    {
        var json = ApricadabraClient.BuildButtonMessage(5, ButtonMode.Pulse);
        var obj = JsonNode.Parse(json).AsObject();
        Assert.That(obj["type"].GetValue<string>(), Is.EqualTo("button"));
        Assert.That(obj["button"].GetValue<int>(), Is.EqualTo(5));
        Assert.That(obj["mode"].GetValue<string>(), Is.EqualTo("pulse"));
        Assert.That(obj["state"], Is.Null);
    }

    [Test]
    public void BuildButtonMessage_Momentary_IncludesState()
    {
        var json = ApricadabraClient.BuildButtonMessage(1, ButtonMode.Momentary, state: ButtonState.Down);
        var obj = JsonNode.Parse(json).AsObject();
        Assert.That(obj["state"].GetValue<string>(), Is.EqualTo("down"));
    }

    [Test]
    public void BuildButtonMessage_Double_IncludesDelay()
    {
        var json = ApricadabraClient.BuildButtonMessage(4, ButtonMode.Double, delay: 80);
        var obj = JsonNode.Parse(json).AsObject();
        Assert.That(obj["mode"].GetValue<string>(), Is.EqualTo("double"));
        Assert.That(obj["delay"].GetValue<int>(), Is.EqualTo(80));
    }

    [Test]
    public void BuildButtonMessage_Rapid_IncludesRate()
    {
        var json = ApricadabraClient.BuildButtonMessage(5, ButtonMode.Rapid, state: ButtonState.Down, rate: 100);
        var obj = JsonNode.Parse(json).AsObject();
        Assert.That(obj["rate"].GetValue<int>(), Is.EqualTo(100));
    }

    [Test]
    public void BuildButtonMessage_LongShort_IncludesAllFields()
    {
        var json = ApricadabraClient.BuildButtonMessage(6, ButtonMode.LongShort,
            state: ButtonState.Down, shortButton: 6, longButton: 7, threshold: 500);
        var obj = JsonNode.Parse(json).AsObject();
        Assert.That(obj["mode"].GetValue<string>(), Is.EqualTo("longshort"));
        Assert.That(obj["shortButton"].GetValue<int>(), Is.EqualTo(6));
        Assert.That(obj["longButton"].GetValue<int>(), Is.EqualTo(7));
        Assert.That(obj["threshold"].GetValue<int>(), Is.EqualTo(500));
    }

    [Test]
    public void BuildResetMessage_CorrectJson()
    {
        var json = ApricadabraClient.BuildResetMessage(1, 0.5f);
        var obj = JsonNode.Parse(json).AsObject();
        Assert.That(obj["type"].GetValue<string>(), Is.EqualTo("reset"));
        Assert.That(obj["axis"].GetValue<int>(), Is.EqualTo(1));
        Assert.That(obj["position"].GetValue<float>(), Is.EqualTo(0.5f).Within(0.001f));
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `cd core/sdk/csharp && dotnet test Apricadabra.Client.Tests/`
Expected: Compilation error — `BuildAxisMessage` etc. don't exist

- [ ] **Step 3: Implement message building methods in ApricadabraClient.cs**

Create `core/sdk/csharp/Apricadabra.Client/ApricadabraClient.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace Apricadabra.Client
{
    public class ApricadabraClient : IDisposable
    {
        private const string PipeName = "apricadabra";
        private const int UdpCommandPort = 19871;
        private const int ProtocolVersion = 2;

        private readonly string _pluginName;
        private readonly int _broadcastPort;
        private readonly string[] _commands;
        private readonly CoreLauncher _launcher;

        private NamedPipeClientStream _pipe;
        private StreamReader _reader;
        private StreamWriter _writer;
        private UdpClient _udpSender;
        private CancellationTokenSource _cts;
        private bool _connected;

        public event Action<Dictionary<int, float>, Dictionary<int, bool>> OnStateUpdate;
        public event Action<string, Dictionary<string, ApiStatus>> OnConnected;
        public event Action OnDisconnected;
        public event Action<string, string> OnError;
        public event Action<string, ApiStatus> OnWarning;

        public bool IsConnected => _connected;

        public ApricadabraClient(
            string pluginName,
            int broadcastPort = 19872,
            string[] commands = null,
            string[] corePaths = null)
        {
            _pluginName = pluginName;
            _broadcastPort = broadcastPort;
            _commands = commands ?? new[] { "axis", "button", "reset" };
            _launcher = new CoreLauncher(corePaths);
        }

        // --- Message Building (internal static for testability) ---

        internal static string BuildAxisMessage(int axis, AxisMode mode, int diff, float sensitivity,
            float decayRate = 0.95f, int steps = 5)
        {
            var obj = new JsonObject
            {
                ["type"] = "axis",
                ["axis"] = axis,
                ["mode"] = mode.ToWireString(),
                ["diff"] = diff,
                ["sensitivity"] = sensitivity
            };
            if (mode == AxisMode.Spring)
                obj["decayRate"] = decayRate;
            if (mode == AxisMode.Detent)
                obj["steps"] = steps;
            return obj.ToJsonString();
        }

        internal static string BuildButtonMessage(int button, ButtonMode mode,
            ButtonState? state = null, int delay = 50, int rate = 100,
            int? shortButton = null, int? longButton = null, int threshold = 500)
        {
            var obj = new JsonObject
            {
                ["type"] = "button",
                ["button"] = button,
                ["mode"] = mode.ToWireString()
            };
            if (state.HasValue)
                obj["state"] = state.Value.ToWireString();
            if (mode == ButtonMode.Double)
                obj["delay"] = delay;
            if (mode == ButtonMode.Rapid)
                obj["rate"] = rate;
            if (mode == ButtonMode.LongShort)
            {
                obj["shortButton"] = shortButton ?? button;
                obj["longButton"] = longButton ?? button;
                obj["threshold"] = threshold;
            }
            return obj.ToJsonString();
        }

        internal static string BuildResetMessage(int axis, float position)
        {
            var obj = new JsonObject
            {
                ["type"] = "reset",
                ["axis"] = axis,
                ["position"] = position
            };
            return obj.ToJsonString();
        }

        // --- Public Send Methods ---

        public void SendAxis(int axis, AxisMode mode, int diff,
            float sensitivity = 0.02f, float decayRate = 0.95f, int steps = 5)
        {
            SendUdp(BuildAxisMessage(axis, mode, diff, sensitivity, decayRate, steps));
        }

        public void SendButton(int button, ButtonMode mode, ButtonState? state = null,
            int delay = 50, int rate = 100, int? shortButton = null,
            int? longButton = null, int threshold = 500)
        {
            SendUdp(BuildButtonMessage(button, mode, state, delay, rate, shortButton, longButton, threshold));
        }

        public void SendReset(int axis, float position)
        {
            SendUdp(BuildResetMessage(axis, position));
        }

        private void SendUdp(string json)
        {
            if (!_connected || _udpSender == null) return;
            try
            {
                var bytes = Encoding.UTF8.GetBytes(json);
                _udpSender.Send(bytes, bytes.Length);
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"[Apricadabra] SendUdp failed: {ex.Message}");
            }
        }

        // --- Connection (implemented in Task 4) ---

        public async Task ConnectAsync(CancellationToken ct = default)
        {
            throw new NotImplementedException("Implemented in Task 4");
        }

        public void Dispose()
        {
            _cts?.Cancel();
            _udpSender?.Dispose();
            _pipe?.Dispose();
            _reader?.Dispose();
            _writer?.Dispose();
        }
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `cd core/sdk/csharp && dotnet test Apricadabra.Client.Tests/`
Expected: All PASS

- [ ] **Step 5: Commit**

```bash
git add core/sdk/csharp/
git commit -m "feat(sdk): add ApricadabraClient with message building and send methods"
```

---

## Task 4: ApricadabraClient — Connection and Lifecycle

**Files:**
- Modify: `core/sdk/csharp/Apricadabra.Client/ApricadabraClient.cs`
- Create: `core/sdk/csharp/Apricadabra.Client.Tests/ParsingTests.cs`

- [ ] **Step 1: Write parsing tests**

Create `core/sdk/csharp/Apricadabra.Client.Tests/ParsingTests.cs`:

```csharp
using System.Collections.Generic;
using System.Text.Json.Nodes;
using NUnit.Framework;
using Apricadabra.Client;

namespace Apricadabra.Client.Tests;

[TestFixture]
public class ParsingTests
{
    [Test]
    public void ParseWelcome_ExtractsApiStatus()
    {
        var json = @"{""type"":""welcome"",""version"":2,""axes"":{""1"":0.5},""buttons"":{""1"":false},""apiStatus"":{""axis"":""exists"",""button"":""deprecated"",""reset"":""undefined""},""coreVersion"":""0.1.0""}";
        var welcome = JsonNode.Parse(json).AsObject();

        var (coreVersion, apiStatus) = ApricadabraClient.ParseWelcome(welcome);

        Assert.That(coreVersion, Is.EqualTo("0.1.0"));
        Assert.That(apiStatus["axis"], Is.EqualTo(ApiStatus.Exists));
        Assert.That(apiStatus["button"], Is.EqualTo(ApiStatus.Deprecated));
        Assert.That(apiStatus["reset"], Is.EqualTo(ApiStatus.Undefined));
    }

    [Test]
    public void ParseWelcome_NoApiStatus_ReturnsNulls()
    {
        var json = @"{""type"":""welcome"",""version"":1,""axes"":{""1"":0.5},""buttons"":{""1"":false}}";
        var welcome = JsonNode.Parse(json).AsObject();

        var (coreVersion, apiStatus) = ApricadabraClient.ParseWelcome(welcome);

        Assert.That(coreVersion, Is.Null);
        Assert.That(apiStatus, Is.Null);
    }

    [Test]
    public void ParseState_ExtractsTypedDictionaries()
    {
        var json = @"{""type"":""state"",""axes"":{""1"":0.75,""3"":0.25},""buttons"":{""1"":true,""5"":false}}";
        var msg = JsonNode.Parse(json).AsObject();

        var (axes, buttons) = ApricadabraClient.ParseState(msg);

        Assert.That(axes[1], Is.EqualTo(0.75f).Within(0.001f));
        Assert.That(axes[3], Is.EqualTo(0.25f).Within(0.001f));
        Assert.That(buttons[1], Is.True);
        Assert.That(buttons[5], Is.False);
    }

    [Test]
    public void ParseState_EmptyAxesAndButtons()
    {
        var json = @"{""type"":""state"",""axes"":{},""buttons"":{}}";
        var msg = JsonNode.Parse(json).AsObject();

        var (axes, buttons) = ApricadabraClient.ParseState(msg);

        Assert.That(axes, Is.Empty);
        Assert.That(buttons, Is.Empty);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `cd core/sdk/csharp && dotnet test Apricadabra.Client.Tests/`
Expected: Compilation error — `ParseWelcome` and `ParseState` don't exist

- [ ] **Step 3: Add parsing methods and full connection logic to ApricadabraClient**

Replace the `ConnectAsync` placeholder and add parsing methods in `ApricadabraClient.cs`. The connection logic is extracted from the Loupedeck `CoreConnection.cs`:

Add these `internal static` methods for testability:

```csharp
internal static (string coreVersion, Dictionary<string, ApiStatus> apiStatus) ParseWelcome(JsonObject welcome)
{
    var apiStatusNode = welcome["apiStatus"]?.AsObject();
    Dictionary<string, ApiStatus> apiStatus = null;
    if (apiStatusNode != null)
    {
        apiStatus = new Dictionary<string, ApiStatus>();
        foreach (var kvp in apiStatusNode)
        {
            var statusStr = kvp.Value?.GetValue<string>() ?? "undefined";
            apiStatus[kvp.Key] = EnumExtensions.ParseApiStatus(statusStr);
        }
    }
    var coreVersion = welcome["coreVersion"]?.GetValue<string>();
    return (coreVersion, apiStatus);
}

internal static (Dictionary<int, float> axes, Dictionary<int, bool> buttons) ParseState(JsonObject msg)
{
    var axes = new Dictionary<int, float>();
    var buttons = new Dictionary<int, bool>();

    var axesNode = msg["axes"]?.AsObject();
    if (axesNode != null)
    {
        foreach (var kvp in axesNode)
        {
            if (int.TryParse(kvp.Key, out var id))
                axes[id] = kvp.Value.GetValue<float>();
        }
    }

    var buttonsNode = msg["buttons"]?.AsObject();
    if (buttonsNode != null)
    {
        foreach (var kvp in buttonsNode)
        {
            if (int.TryParse(kvp.Key, out var id))
                buttons[id] = kvp.Value.GetValue<bool>();
        }
    }

    return (axes, buttons);
}
```

Replace the `ConnectAsync` method:

```csharp
public async Task ConnectAsync(CancellationToken ct = default)
{
    _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
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
                ["name"] = _pluginName,
                ["broadcastPort"] = _broadcastPort,
                ["commands"] = new JsonArray(_commands)
            };
            await _writer.WriteLineAsync(hello.ToJsonString());

            // Read welcome
            var welcomeLine = await _reader.ReadLineAsync();
            if (welcomeLine == null) throw new IOException("No welcome received");

            var welcome = JsonNode.Parse(welcomeLine)?.AsObject();
            if (welcome?["type"]?.GetValue<string>() != "welcome")
                throw new IOException("Expected welcome message");

            var (coreVersion, apiStatus) = ParseWelcome(welcome);

            // Fire warnings for deprecated/undefined
            if (apiStatus != null)
            {
                foreach (var kvp in apiStatus)
                {
                    if (kvp.Value == ApiStatus.Deprecated || kvp.Value == ApiStatus.Undefined)
                        OnWarning?.Invoke(kvp.Key, kvp.Value);
                }
            }

            _connected = true;
            delay = 100;

            // UDP sender
            _udpSender = new UdpClient();
            _udpSender.Connect(IPAddress.Loopback, UdpCommandPort);

            // Start background loops
            _ = Task.Run(() => PipeReadLoopAsync(_cts.Token));
            _ = Task.Run(() => UdpListenLoopAsync(_cts.Token));

            // Fire connected event
            OnConnected?.Invoke(coreVersion, apiStatus);

            // Fire initial state from welcome
            var (axes, buttons) = ParseState(welcome);
            if (axes.Count > 0 || buttons.Count > 0)
                OnStateUpdate?.Invoke(axes, buttons);

            return;
        }
        catch (Exception) when (!_cts.Token.IsCancellationRequested)
        {
            _launcher.TryLaunch();
            await Task.Delay(delay, _cts.Token);
            delay = Math.Min(delay * 2, 5000);
        }
    }
}
```

Add the pipe read loop:

```csharp
private async Task PipeReadLoopAsync(CancellationToken ct)
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
                case "heartbeat":
                    try { await _writer.WriteLineAsync(new JsonObject { ["type"] = "heartbeat_ack" }.ToJsonString()); }
                    catch (Exception ex) { Trace.WriteLine($"[Apricadabra] Heartbeat ack failed: {ex.Message}"); }
                    break;
                case "error":
                    OnError?.Invoke(
                        msg["code"]?.GetValue<string>() ?? "unknown",
                        msg["message"]?.GetValue<string>() ?? "Unknown error");
                    break;
                case "core_restarting":
                    var timeout = msg["coreStartTimeout"]?.GetValue<int>() ?? 15000;
                    Trace.WriteLine($"[Apricadabra] Core restarting, suppressing auto-launch for {timeout}ms");
                    _launcher.SuppressLaunchUntil(DateTime.UtcNow.AddMilliseconds(timeout));
                    break;
                case "shutdown":
                    return;
            }
        }
    }
    catch (OperationCanceledException) { }
    catch (Exception ex)
    {
        Trace.WriteLine($"[Apricadabra] Pipe read error: {ex.Message}");
    }

    HandleDisconnect();
}
```

Add the UDP listen loop:

```csharp
private async Task UdpListenLoopAsync(CancellationToken ct)
{
    try
    {
        using var udp = new UdpClient(_broadcastPort);
        udp.Client.ReceiveTimeout = 5000;

        while (!ct.IsCancellationRequested)
        {
            try
            {
                var result = await udp.ReceiveAsync();
                var json = Encoding.UTF8.GetString(result.Buffer);
                var msg = JsonNode.Parse(json)?.AsObject();
                if (msg != null && msg["type"]?.GetValue<string>() == "state")
                {
                    var (axes, buttons) = ParseState(msg);
                    OnStateUpdate?.Invoke(axes, buttons);
                }
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                Trace.WriteLine($"[Apricadabra] UDP receive error: {ex.Message}");
            }
        }
    }
    catch (OperationCanceledException) { }
    catch (Exception ex)
    {
        Trace.WriteLine($"[Apricadabra] UDP listener error: {ex.Message}");
    }
}
```

Add the disconnect handler:

```csharp
private void HandleDisconnect()
{
    _connected = false;
    OnDisconnected?.Invoke();
    _ = Task.Run(async () =>
    {
        await Task.Delay(1000);
        await ConnectAsync();
    });
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `cd core/sdk/csharp && dotnet test Apricadabra.Client.Tests/`
Expected: All PASS

- [ ] **Step 5: Commit**

```bash
git add core/sdk/csharp/
git commit -m "feat(sdk): add connection lifecycle, parsing, and pipe/UDP loops to ApricadabraClient"
```

---

## Task 5: Migrate Loupedeck Plugin

**Files:**
- Delete: `loupedeck-plugin/ApricadabraPlugin/src/CoreConnection.cs`
- Modify: `loupedeck-plugin/ApricadabraPlugin/src/ApricadabraPlugin.csproj`
- Modify: `loupedeck-plugin/ApricadabraPlugin/src/ApricadabraPlugin.cs`
- Modify: `loupedeck-plugin/ApricadabraPlugin/src/StateDisplay.cs`
- Modify: `loupedeck-plugin/ApricadabraPlugin/src/Actions/DialAction.cs`
- Modify: `loupedeck-plugin/ApricadabraPlugin/src/Actions/ButtonCommand.cs`
- Modify: `loupedeck-plugin/ApricadabraPlugin/src/Actions/ResetAxisCommand.cs`

- [ ] **Step 1: Add NuGet reference and local source config**

Add to `loupedeck-plugin/ApricadabraPlugin/src/ApricadabraPlugin.csproj`:

```xml
<PackageReference Include="Apricadabra.Client" Version="0.1.0" />
```

Create `loupedeck-plugin/ApricadabraPlugin/nuget.config` for local development:

```xml
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
    <add key="local" value="../../../core/sdk/csharp/Apricadabra.Client/bin/Release" />
  </packageSources>
</configuration>
```

Pack the library locally:

```bash
cd core/sdk/csharp/Apricadabra.Client && dotnet pack -c Release
```

- [ ] **Step 2: Delete CoreConnection.cs**

```bash
git rm loupedeck-plugin/ApricadabraPlugin/src/CoreConnection.cs
```

- [ ] **Step 3: Update ApricadabraPlugin.cs**

Replace the plugin's main class to use `ApricadabraClient` instead of `CoreConnection`. The key changes:
- Property type changes from `CoreConnection` to `ApricadabraClient`
- Constructor uses `new ApricadabraClient("loupedeck")`
- `OnStateUpdate` now receives typed dictionaries instead of `JsonObject`
- `OnConnected` event replaces manual welcome parsing
- Remove `using System.Text.Json.Nodes` if no longer needed

Read the current file, then update:
- The `Connection` property type
- The `Load()` method to create `ApricadabraClient` and subscribe to new event signatures
- The `Unload()` method (Dispose call stays the same)

The `OnStateUpdate` handler changes from:
```csharp
this.Connection.OnStateUpdate += msg => {
    this.State.UpdateFromState(msg);
};
```
to:
```csharp
this.Connection.OnStateUpdate += (axes, buttons) => {
    this.State.UpdateFromState(axes, buttons);
};
```

- [ ] **Step 4: Update StateDisplay.cs**

Change `UpdateFromState(JsonObject msg)` to `UpdateFromState(Dictionary<int, float> axes, Dictionary<int, bool> buttons)`. Remove the JSON parsing — the library already did it.

Current implementation parses `msg["axes"]` and `msg["buttons"]` from `JsonObject`. Replace with direct dictionary access:

```csharp
public void UpdateFromState(Dictionary<int, float> axes, Dictionary<int, bool> buttons)
{
    foreach (var kvp in axes)
        Axes[kvp.Key] = kvp.Value;
    foreach (var kvp in buttons)
        Buttons[kvp.Key] = kvp.Value;
}
```

- [ ] **Step 5: Update DialAction.cs**

Replace `SendAsync` calls with typed helpers. The current code builds a `JsonObject` with axis fields:

```csharp
// Before:
var msg = new JsonObject { ["type"] = "axis", ["axis"] = axisId, ["mode"] = modeStr, ... };
_ = Connection?.SendAsync(msg);

// After:
Connection?.SendAxis(axisId, axisMode, diff, sensitivity, decayRate, steps);
```

The `Connection` property accessor changes from:
```csharp
private CoreConnection Connection => ((ApricadabraPlugin)this.Plugin).Connection;
```
to:
```csharp
private ApricadabraClient Connection => ((ApricadabraPlugin)this.Plugin).Connection;
```

Read the current DialAction.cs, map the string mode values to enum values, and replace all `SendAsync` calls. Also update the encoder press button send to use `SendButton`.

- [ ] **Step 6: Update ButtonCommand.cs**

Same pattern as DialAction:

```csharp
// Before:
var msg = new JsonObject { ["type"] = "button", ["button"] = btnId, ["mode"] = modeStr };
_ = Connection?.SendAsync(msg);

// After:
Connection?.SendButton(btnId, buttonMode);
// or for toggle:
Connection?.SendButton(btnId, ButtonMode.Toggle, ButtonState.Down);
```

Update the `Connection` property type. Map string modes to enum values.

- [ ] **Step 7: Update ResetAxisCommand.cs**

```csharp
// Before:
var msg = new JsonObject { ["type"] = "reset", ["axis"] = axisId, ["position"] = pos };
_ = Connection?.SendAsync(msg);

// After:
Connection?.SendReset(axisId, pos);
```

Update the `Connection` property type.

- [ ] **Step 8: Add `using Apricadabra.Client;`**

Add the import to all modified files that reference library types:
- `ApricadabraPlugin.cs`
- `DialAction.cs`
- `ButtonCommand.cs`
- `ResetAxisCommand.cs`

`StateDisplay.cs` needs `using System.Collections.Generic;` if not already present.

- [ ] **Step 9: Verify build**

Run: `cd loupedeck-plugin/ApricadabraPlugin && dotnet build src/ApricadabraPlugin.csproj`
Expected: Build succeeds with no errors

- [ ] **Step 10: Commit**

```bash
git add -u
git add loupedeck-plugin/ApricadabraPlugin/nuget.config
git commit -m "feat(loupedeck): migrate to Apricadabra.Client library, delete CoreConnection.cs"
```

---

## Task 6: Final Validation

**Files:** All modified files

- [ ] **Step 1: Run library tests**

Run: `cd core/sdk/csharp && dotnet test Apricadabra.Client.Tests/ -v normal`
Expected: All tests PASS

- [ ] **Step 2: Verify Loupedeck plugin builds**

Run: `cd loupedeck-plugin/ApricadabraPlugin && dotnet build src/ApricadabraPlugin.csproj`
Expected: Build succeeds

- [ ] **Step 3: Check for TODOs/FIXMEs**

Search for unresolved items across the new library and modified plugin files.

- [ ] **Step 4: Verify wire format parity**

Compare `BuildAxisMessage`, `BuildButtonMessage`, `BuildResetMessage` output against the message examples in `core/docs/protocol.md`. Ensure field names and value formats match exactly.

- [ ] **Step 5: Commit any final fixes**

```bash
git add -u
git commit -m "chore: final validation and cleanup for Apricadabra.Client library"
```
