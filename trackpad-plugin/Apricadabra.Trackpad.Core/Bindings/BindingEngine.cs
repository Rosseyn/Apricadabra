using System;
using System.Collections.Generic;
using System.Linq;
using Apricadabra.Trackpad.Core.Gestures;

namespace Apricadabra.Trackpad.Core.Bindings
{
    public class BindingEngine
    {
        private readonly BindingConfig _config;
        private readonly Dictionary<string, float> _accumulators = new();

        public event Action<int, string, int, float, float, int> OnSendAxis;  // axis, mode, diff, sensitivity, decayRate, steps
        public event Action<int, string, string> OnSendButton;  // button, mode, state

        public BindingEngine(BindingConfig config)
        {
            _config = config;
        }

        public void ProcessGesture(GestureEvent gesture)
        {
            var dirString = DirectionToString(gesture.Direction);
            var typeString = TypeToString(gesture.Type);

            var matches = _config.Bindings.Where(b =>
                b.GestureType == typeString &&
                b.GestureFingers == gesture.Fingers &&
                b.GestureDirection == dirString).ToList();

            foreach (var binding in matches)
            {
                DispatchBinding(binding, gesture);
            }
        }

        private void DispatchBinding(BindingEntry binding, GestureEvent gesture)
        {
            bool isContinuous = gesture.Type == GestureType.Scroll ||
                                gesture.Type == GestureType.Pinch ||
                                gesture.Type == GestureType.Rotate;

            if (isContinuous && gesture.Phase == GesturePhase.Update)
            {
                if (binding.ActionType == "axis")
                {
                    int diff = (int)(gesture.Delta * binding.ActionSensitivity * 1000);
                    if (diff != 0)
                        OnSendAxis?.Invoke(binding.ActionAxis, binding.ActionMode, diff,
                            binding.ActionSensitivity, binding.ActionDecayRate, binding.ActionSteps);
                }
                else if (binding.ActionType == "button")
                {
                    // Accumulate delta, fire when threshold reached
                    var key = binding.Id;
                    if (!_accumulators.ContainsKey(key)) _accumulators[key] = 0;
                    _accumulators[key] += MathF.Abs(gesture.Delta);
                    float threshold = 1.0f / Math.Max(binding.ActionSensitivity * 100, 1);
                    if (_accumulators[key] >= threshold)
                    {
                        _accumulators[key] = 0;
                        OnSendButton?.Invoke(binding.ActionButton, "pulse", null);
                    }
                }
            }
            else if (gesture.Type == GestureType.Swipe && gesture.Phase == GesturePhase.End)
            {
                if (binding.ActionType == "button")
                    OnSendButton?.Invoke(binding.ActionButton, binding.ActionMode, null);
                else if (binding.ActionType == "axis")
                    OnSendAxis?.Invoke(binding.ActionAxis, binding.ActionMode, 1,
                        binding.ActionSensitivity, binding.ActionDecayRate, binding.ActionSteps);
            }
            else if (gesture.Type == GestureType.Tap)
            {
                if (binding.ActionType == "button")
                {
                    if (gesture.Phase == GesturePhase.Begin)
                        OnSendButton?.Invoke(binding.ActionButton, binding.ActionMode, "down");
                    else if (gesture.Phase == GesturePhase.End)
                        OnSendButton?.Invoke(binding.ActionButton, binding.ActionMode, "up");
                }
                else if (binding.ActionType == "axis" && gesture.Phase == GesturePhase.Begin)
                {
                    OnSendAxis?.Invoke(binding.ActionAxis, binding.ActionMode, 1,
                        binding.ActionSensitivity, binding.ActionDecayRate, binding.ActionSteps);
                }
            }
        }

        private static string DirectionToString(GestureDirection dir) => dir switch
        {
            GestureDirection.Up => "up",
            GestureDirection.Down => "down",
            GestureDirection.Left => "left",
            GestureDirection.Right => "right",
            GestureDirection.In => "in",
            GestureDirection.Out => "out",
            GestureDirection.Clockwise => "clockwise",
            GestureDirection.CounterClockwise => "counterclockwise",
            _ => "none"
        };

        private static string TypeToString(GestureType type) => type switch
        {
            GestureType.Scroll => "scroll",
            GestureType.Pinch => "pinch",
            GestureType.Rotate => "rotate",
            GestureType.Swipe => "swipe",
            GestureType.Tap => "tap",
            _ => "unknown"
        };
    }
}
