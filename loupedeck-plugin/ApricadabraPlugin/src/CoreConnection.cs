using System;
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

namespace Loupedeck.ApricadabraPlugin
{
    public class CoreConnection : IDisposable
    {
        private const string PipeName = "apricadabra";
        private const string CoreExeName = "apricadabra-core.exe";
        private const int ProtocolVersion = 1;
        private const int UdpCommandPort = 19871;
        private const int UdpBroadcastPort = 19872;

        private NamedPipeClientStream _pipe;
        private StreamReader _reader;
        private StreamWriter _writer;
        private UdpClient _udpSender;
        private CancellationTokenSource _cts;
        private Task _pipeReadTask;
        private Task _udpListenTask;
        private bool _connected;

        public event Action<JsonObject> OnStateUpdate;
        public event Action<string, string> OnError;
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

                    // UDP sender for commands
                    _udpSender = new UdpClient();
                    _udpSender.Connect(IPAddress.Loopback, UdpCommandPort);

                    // Start pipe read loop (heartbeat only)
                    _pipeReadTask = Task.Run(() => PipeReadLoopAsync(_cts.Token));

                    // Start UDP listener for state broadcasts
                    _udpListenTask = Task.Run(() => UdpListenLoopAsync(_cts.Token));

                    // Dispatch initial state from welcome
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
            if (!_connected || _udpSender == null) return;
            try
            {
                var bytes = Encoding.UTF8.GetBytes(message.ToJsonString());
                await _udpSender.SendAsync(bytes, bytes.Length);
            }
            catch { }
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
                            // Heartbeat ack goes over pipe, not UDP
                            try { await _writer.WriteLineAsync(new JsonObject { ["type"] = "heartbeat_ack" }.ToJsonString()); }
                            catch { }
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

        private async Task UdpListenLoopAsync(CancellationToken ct)
        {
            try
            {
                using var udp = new UdpClient(UdpBroadcastPort);
                udp.Client.ReceiveTimeout = 5000;

                while (!ct.IsCancellationRequested)
                {
                    try
                    {
                        var result = await udp.ReceiveAsync(ct);
                        var json = Encoding.UTF8.GetString(result.Buffer);
                        var msg = JsonNode.Parse(json)?.AsObject();
                        if (msg != null && msg["type"]?.GetValue<string>() == "state")
                        {
                            OnStateUpdate?.Invoke(msg);
                        }
                    }
                    catch (OperationCanceledException) { break; }
                    catch { }
                }
            }
            catch { }
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

        private void TryLaunchCore()
        {
            try
            {
                var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                var corePath = Path.Combine(appData, "Apricadabra", CoreExeName);

                if (!File.Exists(corePath))
                {
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
            _udpSender?.Dispose();
            _pipe?.Dispose();
            _reader?.Dispose();
            _writer?.Dispose();
        }
    }
}
