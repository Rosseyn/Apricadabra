using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using Apricadabra.Trackpad.Core;
using Apricadabra.Trackpad.Core.Gestures;
using Apricadabra.Trackpad.Core.Models;

namespace Apricadabra.Trackpad.Controls
{
    public partial class TestPanel : UserControl
    {
        private TrackpadService _service;
        private readonly DispatcherTimer _clearTimer;
        private DateTime _lastFrameUpdate;
        private const double ThrottleMs = 33; // ~30fps

        public TestPanel()
        {
            InitializeComponent();
            _clearTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            _clearTimer.Tick += (s, e) =>
            {
                _clearTimer.Stop();
                GestureName.Text = "";
                GestureDetails.Text = "";
                MatchedPanel.Visibility = Visibility.Collapsed;
                TouchCanvas.Children.Clear();
            };
        }

        public void AttachService(TrackpadService service)
        {
            _service = service;

            service.Input.OnContactFrame += frame =>
            {
                var now = DateTime.UtcNow;
                if ((now - _lastFrameUpdate).TotalMilliseconds < ThrottleMs) return;
                _lastFrameUpdate = now;
                Dispatcher.BeginInvoke(() => UpdateFingerDots(frame));
            };

            service.Recognizer.OnGestureEvent += gesture =>
            {
                Dispatcher.BeginInvoke(() => UpdateGestureDisplay(gesture));
            };
        }

        private void UpdateFingerDots(ContactFrame frame)
        {
            TouchCanvas.Children.Clear();
            _clearTimer.Stop();

            foreach (var contact in frame.Contacts.Where(c => c.OnSurface))
            {
                var dot = new Ellipse
                {
                    Width = 12, Height = 12,
                    Fill = (Brush)FindResource("Accent"),
                    Opacity = 0.8
                };
                Canvas.SetLeft(dot, contact.X * (TouchCanvas.ActualWidth - 12));
                Canvas.SetTop(dot, contact.Y * (TouchCanvas.ActualHeight - 12));
                TouchCanvas.Children.Add(dot);
            }

            if (!frame.Contacts.Any(c => c.OnSurface))
                _clearTimer.Start();
        }

        private void UpdateGestureDisplay(GestureEvent gesture)
        {
            _clearTimer.Stop();

            string name = gesture.Type switch
            {
                GestureType.Scroll => $"Scroll {gesture.Direction}",
                GestureType.Pinch => $"Pinch {gesture.Direction}",
                GestureType.Rotate => $"Rotate {gesture.Direction}",
                GestureType.Swipe => $"{gesture.Fingers}-finger Swipe {gesture.Direction}",
                GestureType.Tap => $"{gesture.Fingers}-finger Tap",
                _ => ""
            };

            GestureName.Text = name;
            GestureDetails.Text = $"{gesture.Fingers} fingers \u00b7 \u03b4 {gesture.Delta:F3}";

            // Check for matched binding
            var match = _service.BindingConfig.Bindings.FirstOrDefault(b =>
                b.GestureType == TypeToString(gesture.Type) &&
                b.GestureFingers == gesture.Fingers &&
                b.GestureDirection == DirectionToString(gesture.Direction));

            if (match != null)
            {
                MatchedPanel.Visibility = Visibility.Visible;
                MatchedAction.Text = match.ActionType == "axis"
                    ? $"\u2192 Axis {match.ActionAxis} ({match.ActionMode})"
                    : $"\u2192 Button {match.ActionButton} ({match.ActionMode})";
            }
            else
            {
                MatchedPanel.Visibility = Visibility.Collapsed;
            }

            if (gesture.Phase == GesturePhase.End)
                _clearTimer.Start();
        }

        private static string TypeToString(GestureType type) => type switch
        {
            GestureType.Scroll => "scroll",
            GestureType.Pinch => "pinch",
            GestureType.Rotate => "rotate",
            GestureType.Swipe => "swipe",
            GestureType.Tap => "tap",
            _ => "unknown"
        };

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
    }
}
