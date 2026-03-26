using System;
using System.Threading.Tasks;
using Apricadabra.Client;
using Apricadabra.Trackpad.Core.Bindings;
using Apricadabra.Trackpad.Core.Gestures;
using Apricadabra.Trackpad.Core.Input;

namespace Apricadabra.Trackpad.Core
{
    public class TrackpadService : IDisposable
    {
        public RawInputCapture Input { get; private set; }
        public GestureRecognizer Recognizer { get; private set; }
        public BindingEngine Bindings { get; private set; }
        public ApricadabraClient Client { get; private set; }
        public TrackpadSettings Settings { get; private set; }
        public BindingConfig BindingConfig { get; private set; }

        public async Task Start()
        {
            // Load config
            Settings = TrackpadSettings.Load();
            BindingConfig = BindingConfig.Load();

            // Initialize input capture
            Input = new RawInputCapture();
            Input.SelectedDevicePath = Settings.SelectedDevicePath;

            // Initialize gesture pipeline
            Recognizer = new GestureRecognizer(Settings);
            Input.OnContactFrame += frame => Recognizer.ProcessFrame(frame);

            // Initialize binding engine
            Bindings = new BindingEngine(BindingConfig);

            // Initialize core connection
            Client = new ApricadabraClient("trackpad", broadcastPort: 19874);

            // Wire binding engine → client
            Bindings.OnSendAxis += (axis, mode, diff, sens, decay, steps) =>
            {
                var axisMode = mode switch
                {
                    "spring" => AxisMode.Spring,
                    "detent" => AxisMode.Detent,
                    _ => AxisMode.Hold
                };
                Client.SendAxis(axis, axisMode, diff, sens, decay, steps);
            };
            Bindings.OnSendButton += (button, mode, state) =>
            {
                var btnMode = mode switch
                {
                    "toggle" => ButtonMode.Toggle,
                    "pulse" => ButtonMode.Pulse,
                    "double" => ButtonMode.Double,
                    "rapid" => ButtonMode.Rapid,
                    "longshort" => ButtonMode.LongShort,
                    _ => ButtonMode.Momentary
                };
                ButtonState? btnState = state switch
                {
                    "down" => Apricadabra.Client.ButtonState.Down,
                    "up" => Apricadabra.Client.ButtonState.Up,
                    _ => null
                };
                Client.SendButton(button, btnMode, btnState);
            };

            // Wire gesture events → binding engine
            Recognizer.OnGestureEvent += gesture => Bindings.ProcessGesture(gesture);

            // Start input capture
            Input.Start();

            // Connect to core
            await Client.ConnectAsync();
        }

        public void Stop()
        {
            Input?.Stop();
            Client?.Dispose();
            Settings?.Save();
        }

        public void Dispose()
        {
            Stop();
            Input?.Dispose();
        }
    }
}
