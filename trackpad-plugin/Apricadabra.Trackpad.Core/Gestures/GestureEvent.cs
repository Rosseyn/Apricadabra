namespace Apricadabra.Trackpad.Core.Gestures
{
    public enum GestureType { Scroll, Pinch, Rotate, Swipe, Tap }

    public enum GestureDirection
    {
        Up, Down, Left, Right,
        In, Out,
        Clockwise, CounterClockwise,
        None
    }

    public enum GesturePhase { Begin, Update, End }

    public class GestureEvent
    {
        public GestureType Type { get; }
        public int Fingers { get; }
        public GestureDirection Direction { get; }
        public GesturePhase Phase { get; }
        public float Delta { get; }

        public GestureEvent(GestureType type, int fingers, GestureDirection direction,
            GesturePhase phase, float delta = 0f)
        {
            Type = type;
            Fingers = fingers;
            Direction = direction;
            Phase = phase;
            Delta = delta;
        }
    }
}
