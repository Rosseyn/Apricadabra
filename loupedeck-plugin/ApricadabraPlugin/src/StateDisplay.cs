using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

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

        public void UpdateFromState(Dictionary<int, float> axes, Dictionary<int, bool> buttons)
        {
            ConnectionStatus = null;
            ErrorMessage = null;

            foreach (var kvp in axes)
            {
                _axes[kvp.Key] = kvp.Value;
            }

            foreach (var kvp in buttons)
            {
                _buttons[kvp.Key] = kvp.Value;
            }
        }
    }
}
