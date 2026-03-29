using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using Apricadabra.Trackpad.Core;
using Apricadabra.Trackpad.Core.Input;

namespace Apricadabra.Trackpad.ViewModels
{
    public class MainViewModel : ViewModelBase, IDisposable
    {
        private readonly TrackpadService _service = new();
        private string _activeTab = "Bindings";
        private bool _isRunning;
        private bool _isConnected;
        private string _coreVersion;
        private string _selectedDevicePath;

        public TrackpadService Service => _service;

        public string ActiveTab
        {
            get => _activeTab;
            set => SetProperty(ref _activeTab, value);
        }

        public bool IsRunning
        {
            get => _isRunning;
            set
            {
                if (SetProperty(ref _isRunning, value))
                    OnPropertyChanged(nameof(StartStopLabel));
            }
        }

        public string StartStopLabel => IsRunning ? "Stop" : "Start";

        public bool IsConnected
        {
            get => _isConnected;
            set => SetProperty(ref _isConnected, value);
        }

        public string CoreVersion
        {
            get => _coreVersion;
            set => SetProperty(ref _coreVersion, value);
        }

        public ObservableCollection<TouchpadDevice> AvailableDevices { get; } = new();

        public string SelectedDevicePath
        {
            get => _selectedDevicePath;
            set
            {
                if (SetProperty(ref _selectedDevicePath, value))
                {
                    if (_service.Input != null)
                        _service.Input.SelectedDevicePath = value;
                    if (_service.Settings != null)
                    {
                        _service.Settings.SelectedDevicePath = value;
                        _service.Settings.Save();
                    }
                }
            }
        }

        public ICommand ShowWindowCommand { get; }
        public ICommand ShowBindingsCommand { get; }
        public ICommand ShowSettingsCommand { get; }
        public ICommand StartStopCommand { get; }
        public ICommand ExitCommand { get; }

        public BindingsViewModel BindingsVM { get; private set; }
        public SettingsViewModel SettingsVM { get; private set; }

        public MainViewModel()
        {
            ShowWindowCommand = new RelayCommand(() =>
            {
                Application.Current.MainWindow?.Show();
                Application.Current.MainWindow?.Activate();
            });
            ShowBindingsCommand = new RelayCommand(() => ActiveTab = "Bindings");
            ShowSettingsCommand = new RelayCommand(() => ActiveTab = "Settings");
            StartStopCommand = new RelayCommand(ToggleService);
            ExitCommand = new RelayCommand(() => Application.Current.Shutdown());
        }

        public async Task InitializeAsync()
        {
            await _service.Start();
            IsRunning = true;

            // Wire events
            _service.Client.OnConnected += (version, _) =>
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    IsConnected = true;
                    CoreVersion = version;
                });
            };
            _service.Client.OnDisconnected += () =>
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    IsConnected = false;
                    CoreVersion = null;
                });
            };
            _service.Input.OnDevicesChanged += () =>
            {
                Application.Current.Dispatcher.Invoke(RefreshDevices);
            };

            RefreshDevices();
            SelectedDevicePath = _service.Settings.SelectedDevicePath;

            // Create child view models
            BindingsVM = new BindingsViewModel(_service);
            SettingsVM = new SettingsViewModel(_service);
            OnPropertyChanged(nameof(BindingsVM));
            OnPropertyChanged(nameof(SettingsVM));
        }

        private void RefreshDevices()
        {
            AvailableDevices.Clear();
            if (_service.Input?.AvailableDevices != null)
            {
                foreach (var d in _service.Input.AvailableDevices)
                    AvailableDevices.Add(d);
            }
        }

        private async void ToggleService()
        {
            if (IsRunning)
            {
                _service.Stop();
                IsRunning = false;
                IsConnected = false;
            }
            else
            {
                await _service.Start();
                IsRunning = true;
            }
        }

        public void Dispose()
        {
            _service.Dispose();
        }
    }
}
