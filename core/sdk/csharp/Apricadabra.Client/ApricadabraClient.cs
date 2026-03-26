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

        // --- Connection (placeholder for Task 4) ---

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
