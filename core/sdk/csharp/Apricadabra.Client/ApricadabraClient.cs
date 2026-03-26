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

        // --- Parsing (internal static for testability) ---

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

        // --- Connection ---

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
