using System;
using Apricadabra.Trackpad.Core.Bindings;
using Apricadabra.Trackpad.Core.Models;

namespace Apricadabra.Trackpad.Core.Gestures
{
    public class GestureRecognizer
    {
        private readonly TrackpadSettings _settings;
        private readonly ContactTracker _tracker = new();
        private GestureType? _committedGesture;
        private int _committedFingers;
        private int _peakFingerCount;
        private bool _beginFired;

        public event Action<GestureEvent> OnGestureEvent;

        public GestureRecognizer(TrackpadSettings settings)
        {
            _settings = settings;
        }

        public void ProcessFrame(ContactFrame frame)
        {
            _tracker.Update(frame);
            var state = _tracker.CurrentState;

            // Track the max finger count seen during this gesture
            if (state.FingerCount > _peakFingerCount)
                _peakFingerCount = state.FingerCount;

            if (state.AllFingersLifted)
            {
                HandleFingersLifted(state);
                ResetGesture();
                return;
            }

            if (state.FingerCount < 2) return;

            if (_committedGesture == null)
            {
                TryClassify(state);
            }
            else
            {
                EmitContinuousUpdate(state);
            }
        }

        private void TryClassify(TrackerState state)
        {
            float linearMag = MathF.Sqrt(state.CenterDeltaX * state.CenterDeltaX + state.CenterDeltaY * state.CenterDeltaY);
            float spreadMag = MathF.Abs(state.SpreadDelta);
            float rotationMag = MathF.Abs(state.RotationDelta);

            // Need minimum movement to classify
            float threshold = 0.005f;
            if (linearMag < threshold && spreadMag < threshold && rotationMag < 0.02f)
                return;

            // Priority: pinch > rotate > scroll > swipe
            if (state.FingerCount == 2 && spreadMag > linearMag && spreadMag > rotationMag)
            {
                CommitGesture(GestureType.Pinch, 2);
            }
            else if (state.FingerCount >= 2 && rotationMag > linearMag * 2 && rotationMag > spreadMag)
            {
                CommitGesture(GestureType.Rotate, state.FingerCount);
            }
            else if (state.FingerCount == _settings.ScrollFingerCount)
            {
                CommitGesture(GestureType.Scroll, state.FingerCount);
            }
            else if (state.FingerCount > _settings.ScrollFingerCount)
            {
                // Don't commit swipe yet — wait for lift
                _committedGesture = GestureType.Swipe;
                _committedFingers = state.FingerCount;
            }
        }

        private void CommitGesture(GestureType type, int fingers)
        {
            _committedGesture = type;
            _committedFingers = fingers;
        }

        private void EmitContinuousUpdate(TrackerState state)
        {
            if (_committedGesture == GestureType.Swipe) return; // swipe fires on lift only

            var direction = _committedGesture switch
            {
                GestureType.Scroll => GetLinearDirection(state.CenterDeltaX, state.CenterDeltaY),
                GestureType.Pinch => state.SpreadDelta > 0 ? GestureDirection.Out : GestureDirection.In,
                GestureType.Rotate => state.RotationDelta > 0 ? GestureDirection.Clockwise : GestureDirection.CounterClockwise,
                _ => GestureDirection.None
            };

            float delta = _committedGesture switch
            {
                GestureType.Scroll => MathF.Sqrt(state.CenterDeltaX * state.CenterDeltaX + state.CenterDeltaY * state.CenterDeltaY),
                GestureType.Pinch => state.SpreadDelta,
                GestureType.Rotate => state.RotationDelta,
                _ => 0
            };

            if (direction == GestureDirection.None || MathF.Abs(delta) < 0.001f) return;

            var phase = _beginFired ? GesturePhase.Update : GesturePhase.Begin;
            _beginFired = true;

            OnGestureEvent?.Invoke(new GestureEvent(_committedGesture.Value, _committedFingers, direction, phase, delta));
        }

        private void HandleFingersLifted(TrackerState state)
        {
            if (_committedGesture == GestureType.Swipe)
            {
                // Check swipe thresholds
                if (state.CumulativeDistance >= _settings.SwipeDistanceThreshold &&
                    state.Velocity >= _settings.SwipeSpeedThreshold)
                {
                    var dir = GetCardinalDirection(state);
                    OnGestureEvent?.Invoke(new GestureEvent(GestureType.Swipe, _committedFingers, dir, GesturePhase.End));
                }
            }
            else if (_committedGesture != null && _beginFired)
            {
                // End continuous gesture
                OnGestureEvent?.Invoke(new GestureEvent(_committedGesture.Value, _committedFingers, GestureDirection.None, GesturePhase.End));
            }
            else if (_committedGesture == null)
            {
                // Check for tap: short duration + minimal movement
                var duration = (state.Timestamp - state.GestureStartTime).TotalMilliseconds;
                if (duration > 0 && duration <= _settings.TapMaxDuration &&
                    state.CumulativeDistance <= _settings.TapMaxMovement)
                {
                    // Use peak finger count since _committedFingers is 0 for taps
                    int fingers = _peakFingerCount > 0 ? _peakFingerCount : 1;
                    // Fire begin+end for tap
                    OnGestureEvent?.Invoke(new GestureEvent(GestureType.Tap, fingers, GestureDirection.None, GesturePhase.Begin));
                    OnGestureEvent?.Invoke(new GestureEvent(GestureType.Tap, fingers, GestureDirection.None, GesturePhase.End));
                }
            }
        }

        private void ResetGesture()
        {
            _committedGesture = null;
            _committedFingers = 0;
            _peakFingerCount = 0;
            _beginFired = false;
        }

        private static GestureDirection GetLinearDirection(float dx, float dy)
        {
            if (MathF.Abs(dy) > MathF.Abs(dx))
                return dy < 0 ? GestureDirection.Up : GestureDirection.Down;
            return dx > 0 ? GestureDirection.Right : GestureDirection.Left;
        }

        private static GestureDirection GetCardinalDirection(TrackerState state)
        {
            // Use cumulative center delta direction
            return GetLinearDirection(state.CenterDeltaX, state.CenterDeltaY);
        }
    }
}
