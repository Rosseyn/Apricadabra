using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using Apricadabra.Trackpad.Core.Bindings;
using Apricadabra.Trackpad.Core.Input;
using Apricadabra.Trackpad.ViewModels;
using Apricadabra.Trackpad.Views;

namespace Apricadabra.Trackpad
{
    public partial class MainWindow : Window
    {
        private readonly MainViewModel _vm;
        private BindingsView _bindingsView;
        private SettingsView _settingsView;

        /// <summary>Wrapper for ComboBox items so we can include an "All Devices" entry.</summary>
        private sealed class DeviceItem
        {
            public string Name { get; }
            public string DevicePath { get; }
            public DeviceItem(string name, string devicePath) { Name = name; DevicePath = devicePath; }
            public override string ToString() => Name;
        }

        public MainWindow()
        {
            InitializeComponent();
            _vm = new MainViewModel();
            DataContext = _vm;
        }

        public async void Initialize()
        {
            await _vm.InitializeAsync();

            _bindingsView = new BindingsView { DataContext = _vm.BindingsVM };
            _settingsView = new SettingsView { DataContext = _vm.SettingsVM };
            LeftPanel.Content = _bindingsView;

            // Wire test panel to service
            TestPanelControl.AttachService(_vm.Service);

            // Populate device combo with "All Devices" + actual devices
            RefreshDeviceCombo();
            _vm.AvailableDevices.CollectionChanged += (s, e) => RefreshDeviceCombo();

            // Restore window position
            RestoreWindowPosition();

            // Watch tab changes
            _vm.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(MainViewModel.ActiveTab))
                    LeftPanel.Content = _vm.ActiveTab == "Settings" ? _settingsView : (object)_bindingsView;
            };
        }

        private void RefreshDeviceCombo()
        {
            var selectedPath = _vm.SelectedDevicePath;
            DeviceCombo.SelectionChanged -= DeviceCombo_SelectionChanged;

            DeviceCombo.Items.Clear();
            DeviceCombo.Items.Add(new DeviceItem("All Devices", null));
            foreach (var device in _vm.AvailableDevices)
                DeviceCombo.Items.Add(new DeviceItem(device.Name, device.DevicePath));

            // Re-select the current device or fall back to "All Devices"
            DeviceCombo.SelectedIndex = 0;
            if (selectedPath != null)
            {
                for (int i = 1; i < DeviceCombo.Items.Count; i++)
                {
                    if (DeviceCombo.Items[i] is DeviceItem di && di.DevicePath == selectedPath)
                    {
                        DeviceCombo.SelectedIndex = i;
                        break;
                    }
                }
            }

            DeviceCombo.DisplayMemberPath = "Name";
            DeviceCombo.SelectionChanged += DeviceCombo_SelectionChanged;
        }

        private void DeviceCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (DeviceCombo.SelectedItem is DeviceItem di)
                _vm.SelectedDevicePath = di.DevicePath;
        }

        private void RestoreWindowPosition()
        {
            var settings = _vm.Service.Settings;
            if (settings.WindowLeft.HasValue && settings.WindowTop.HasValue)
            {
                Left = settings.WindowLeft.Value;
                Top = settings.WindowTop.Value;

                // Validate on-screen
                var screen = SystemParameters.WorkArea;
                if (Left < screen.Left - Width || Left > screen.Right ||
                    Top < screen.Top - Height || Top > screen.Bottom)
                {
                    WindowStartupLocation = WindowStartupLocation.CenterScreen;
                }
            }
            Width = settings.WindowWidth;
            Height = settings.WindowHeight;
        }

        private void SaveWindowPosition()
        {
            var settings = _vm.Service.Settings;
            settings.WindowLeft = Left;
            settings.WindowTop = Top;
            settings.WindowWidth = Width;
            settings.WindowHeight = Height;
            settings.Save();
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            // Minimize to tray instead of closing
            e.Cancel = true;
            SaveWindowPosition();
            Hide();
        }

        private void TabBindings_Click(object sender, RoutedEventArgs e) => _vm.ActiveTab = "Bindings";
        private void TabSettings_Click(object sender, RoutedEventArgs e) => _vm.ActiveTab = "Settings";
        private void TrayOpen_Click(object sender, RoutedEventArgs e) { Show(); Activate(); }
    }
}
