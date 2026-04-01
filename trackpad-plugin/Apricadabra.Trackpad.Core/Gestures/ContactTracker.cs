using System;
using System.Collections.Generic;
using System.Linq;
using Apricadabra.Trackpad.Core.Models;

namespace Apricadabra.Trackpad.Core.Gestures
{
    public class TrackerState
    {
        public int FingerCount { get; set; }
        public float CenterDeltaX { get; set; }
        public float CenterDeltaY { get; set; }
        public float SpreadDelta { get; set; }
        public float RotationDelta { get; set; }
        public float CumulativeDeltaX { get; set; }
        public float CumulativeDeltaY { get; set; }
        public float CumulativeDistance { get; set; }
        public float Velocity { get; set; }
        public bool AllFingersLifted { get; set; }
        public DateTime GestureStartTime { get; set; }
        public DateTime Timestamp { get; set; }
    }

    public class ContactTracker
    {
        private Dictionary<int, ContactPoint> _previousContacts = new();
        private float _previousSpread;
        private float _previousAngle;
        private bool _spreadInitialized;
        private float _cumulativeDistance;
        private float _cumulativeDeltaX;
        private float _cumulativeDeltaY;
        private DateTime _gestureStartTime;
        private bool _hasGesture;

        public TrackerState CurrentState { get; private set; } = new();

        public void Update(ContactFrame frame)
        {
            var onSurface = frame.Contacts.Where(c => c.OnSurface).OrderBy(c => c.Id).ToArray();
            var state = new TrackerState
            {
                FingerCount = onSurface.Length,
                Timestamp = frame.Timestamp
            };

            if (onSurface.Length == 0)
            {
                state.AllFingersLifted = _hasGesture;
                state.CumulativeDeltaX = _cumulativeDeltaX;
                state.CumulativeDeltaY = _cumulativeDeltaY;
                state.CumulativeDistance = _cumulativeDistance;
                state.GestureStartTime = _gestureStartTime;
                CurrentState = state;
                Reset();
                return;
            }

            if (!_hasGesture)
            {
                _gestureStartTime = frame.Timestamp;
                _hasGesture = true;
            }

            // Center of mass
            float cx = onSurface.Average(c => c.X);
            float cy = onSurface.Average(c => c.Y);

            // Center delta (compared to previous frame)
            if (_previousContacts.Count > 0)
            {
                var prevOnSurface = _previousContacts.Values.Where(c => c.OnSurface).ToArray();
                if (prevOnSurface.Length > 0)
                {
                    float pcx = prevOnSurface.Average(c => c.X);
                    float pcy = prevOnSurface.Average(c => c.Y);
                    state.CenterDeltaX = cx - pcx;
                    state.CenterDeltaY = cy - pcy;
                }
            }

            // Cumulative distance and displacement
            float frameDist = MathF.Sqrt(state.CenterDeltaX * state.CenterDeltaX + state.CenterDeltaY * state.CenterDeltaY);
            _cumulativeDistance += frameDist;
            _cumulativeDeltaX += state.CenterDeltaX;
            _cumulativeDeltaY += state.CenterDeltaY;
            state.CumulativeDistance = _cumulativeDistance;
            state.CumulativeDeltaX = _cumulativeDeltaX;
            state.CumulativeDeltaY = _cumulativeDeltaY;
            state.GestureStartTime = _gestureStartTime;

            // Velocity
            var elapsed = (frame.Timestamp - _gestureStartTime).TotalSeconds;
            state.Velocity = elapsed > 0 ? _cumulativeDistance / (float)elapsed : 0;

            // Spread (average distance between contacts) — for pinch
            if (onSurface.Length >= 2)
            {
                float spread = ComputeSpread(onSurface);
                float angle = MathF.Atan2(
                    onSurface[1].Y - onSurface[0].Y,
                    onSurface[1].X - onSurface[0].X);

                if (!_spreadInitialized)
                {
                    // First multi-contact frame: seed previous values, emit zero deltas
                    _previousSpread = spread;
                    _previousAngle = angle;
                    _spreadInitialized = true;
                    state.SpreadDelta = 0;
                    state.RotationDelta = 0;
                }
                else
                {
                    state.SpreadDelta = spread - _previousSpread;
                    _previousSpread = spread;
                    state.RotationDelta = AngleDelta(_previousAngle, angle);
                    _previousAngle = angle;
                }
            }
            else
            {
                _previousSpread = 0;
                _previousAngle = 0;
                _spreadInitialized = false;
            }

            // Store current contacts for next frame
            _previousContacts.Clear();
            foreach (var c in frame.Contacts)
                _previousContacts[c.Id] = c;

            CurrentState = state;
        }

        public void Reset()
        {
            _previousContacts.Clear();
            _previousSpread = 0;
            _previousAngle = 0;
            _cumulativeDistance = 0;
            _cumulativeDeltaX = 0;
            _cumulativeDeltaY = 0;
            _spreadInitialized = false;
            _hasGesture = false;
        }

        private static float ComputeSpread(ContactPoint[] contacts)
        {
            if (contacts.Length < 2) return 0;
            float totalDist = 0;
            int pairs = 0;
            for (int i = 0; i < contacts.Length; i++)
            {
                for (int j = i + 1; j < contacts.Length; j++)
                {
                    float dx = contacts[i].X - contacts[j].X;
                    float dy = contacts[i].Y - contacts[j].Y;
                    totalDist += MathF.Sqrt(dx * dx + dy * dy);
                    pairs++;
                }
            }
            return pairs > 0 ? totalDist / pairs : 0;
        }

        private static float AngleDelta(float prev, float current)
        {
            float delta = current - prev;
            if (delta > MathF.PI) delta -= 2 * MathF.PI;
            if (delta < -MathF.PI) delta += 2 * MathF.PI;
            return delta;
        }
    }
}
