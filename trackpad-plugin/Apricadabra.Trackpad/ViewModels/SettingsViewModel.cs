using System.Collections.Generic;
using System.Windows;
using Apricadabra.Trackpad.Core;
using Apricadabra.Trackpad.Core.Bindings;

namespace Apricadabra.Trackpad.ViewModels
{
    public class SettingsViewModel : ViewModelBase
    {
        private readonly TrackpadService _service;
        private TrackpadSettings Settings => _service.Settings;

        public SettingsViewModel(TrackpadService service)
        {
            _service = service;
        }

        public int ScrollFingerCount
        {
            get => Settings.ScrollFingerCount;
            set { Settings.ScrollFingerCount = value; OnPropertyChanged(); Save(); }
        }

        public float SwipeDistanceThreshold
        {
            get => Settings.SwipeDistanceThreshold;
            set { Settings.SwipeDistanceThreshold = value; OnPropertyChanged(); Save(); }
        }

        public float SwipeSpeedThreshold
        {
            get => Settings.SwipeSpeedThreshold;
            set { Settings.SwipeSpeedThreshold = value; OnPropertyChanged(); Save(); }
        }

        public int TapMaxDuration
        {
            get => Settings.TapMaxDuration;
            set { Settings.TapMaxDuration = value; OnPropertyChanged(); Save(); }
        }

        public float TapMaxMovement
        {
            get => Settings.TapMaxMovement;
            set { Settings.TapMaxMovement = value; OnPropertyChanged(); Save(); }
        }

        public float ScrollSensitivity
        {
            get => Settings.ScrollSensitivity;
            set { Settings.ScrollSensitivity = value; OnPropertyChanged(); Save(); }
        }

        public float PinchSensitivity
        {
            get => Settings.PinchSensitivity;
            set { Settings.PinchSensitivity = value; OnPropertyChanged(); Save(); }
        }

        public float RotateSensitivity
        {
            get => Settings.RotateSensitivity;
            set { Settings.RotateSensitivity = value; OnPropertyChanged(); Save(); }
        }

        public string Theme
        {
            get => Settings.Theme ?? "dark";
            set
            {
                Settings.Theme = value;
                OnPropertyChanged();
                Save();
                ((App)Application.Current).ApplyTheme(value);
            }
        }

        public List<string> ThemeOptions { get; } = new() { "Dark", "Light", "System" };
        public List<int> FingerCountOptions { get; } = new() { 2, 3, 4 };

        private void Save() => Settings.Save();
    }
}
