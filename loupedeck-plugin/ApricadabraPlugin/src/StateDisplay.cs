using System;
using System.Collections.Concurrent;
using System.Text.Json.Nodes;

namespace Loupedeck.ApricadabraPlugin
{
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
