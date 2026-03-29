# Trackpad UI Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build a WPF system tray application that provides a configuration UI for the Apricadabra trackpad listener — binding editor, settings panel, and live gesture test visualization.

**Architecture:** WPF MVVM app consuming `Apricadabra.Trackpad.Core`. MainWindow has a top bar (tab toggle + connection status + device selector), left panel (bindings or settings view), and persistent right test panel. System tray via `Hardcodet.NotifyIcon.Wpf`. Dark/Light/System theming via resource dictionaries.

**Tech Stack:** C# / .NET 8.0-windows (WPF), Hardcodet.NotifyIcon.Wpf, Apricadabra.Trackpad.Core (project reference)

**Spec:** `docs/superpowers/specs/2026-03-26-trackpad-ui-design.md`

---

## File Structure

### New Files
- `trackpad-plugin/Apricadabra.Trackpad/Apricadabra.Trackpad.csproj`
- `trackpad-plugin/Apricadabra.Trackpad/App.xaml` / `App.xaml.cs`
- `trackpad-plugin/Apricadabra.Trackpad/MainWindow.xaml` / `MainWindow.xaml.cs`
- `trackpad-plugin/Apricadabra.Trackpad/Themes/DarkTheme.xaml`
- `trackpad-plugin/Apricadabra.Trackpad/Themes/LightTheme.xaml`
- `trackpad-plugin/Apricadabra.Trackpad/ViewModels/ViewModelBase.cs`
- `trackpad-plugin/Apricadabra.Trackpad/ViewModels/RelayCommand.cs`
- `trackpad-plugin/Apricadabra.Trackpad/ViewModels/MainViewModel.cs`
- `trackpad-plugin/Apricadabra.Trackpad/ViewModels/BindingsViewModel.cs`
- `trackpad-plugin/Apricadabra.Trackpad/ViewModels/SettingsViewModel.cs`
- `trackpad-plugin/Apricadabra.Trackpad/Views/BindingsView.xaml` / `.cs`
- `trackpad-plugin/Apricadabra.Trackpad/Views/SettingsView.xaml` / `.cs`
- `trackpad-plugin/Apricadabra.Trackpad/Controls/TestPanel.xaml` / `.cs`
- `trackpad-plugin/Apricadabra.Trackpad/Controls/BindingEditor.xaml` / `.cs`

### Modified Files
- `trackpad-plugin/Apricadabra.Trackpad.Core/Bindings/TrackpadSettings.cs` — Add window position + theme fields

---

## Task 1: Project Scaffold and Themes

**Files:**
- Create: `trackpad-plugin/Apricadabra.Trackpad/Apricadabra.Trackpad.csproj`
- Create: `trackpad-plugin/Apricadabra.Trackpad/App.xaml`
- Create: `trackpad-plugin/Apricadabra.Trackpad/App.xaml.cs`
- Create: `trackpad-plugin/Apricadabra.Trackpad/Themes/DarkTheme.xaml`
- Create: `trackpad-plugin/Apricadabra.Trackpad/Themes/LightTheme.xaml`
- Modify: `trackpad-plugin/Apricadabra.Trackpad.Core/Bindings/TrackpadSettings.cs`

- [ ] **Step 1: Create .csproj**

Create `trackpad-plugin/Apricadabra.Trackpad/Apricadabra.Trackpad.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>WinExe</OutputType>
    <TargetFramework>net8.0-windows</TargetFramework>
    <UseWPF>true</UseWPF>
    <LangVersion>latest</LangVersion>
    <ApplicationIcon>apricadabra.ico</ApplicationIcon>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\Apricadabra.Trackpad.Core\Apricadabra.Trackpad.Core.csproj" />
    <PackageReference Include="Hardcodet.NotifyIcon.Wpf" Version="1.1.0" />
  </ItemGroup>
</Project>
```

- [ ] **Step 2: Create DarkTheme.xaml**

Create `trackpad-plugin/Apricadabra.Trackpad/Themes/DarkTheme.xaml`:

```xml
<ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
    <Color x:Key="BgPrimaryColor">#1d1d1f</Color>
    <Color x:Key="BgSecondaryColor">#2d2d2f</Color>
    <Color x:Key="BgTertiaryColor">#3d3d3f</Color>
    <Color x:Key="BorderColor">#424245</Color>
    <Color x:Key="TextPrimaryColor">#f5f5f7</Color>
    <Color x:Key="TextSecondaryColor">#86868b</Color>
    <Color x:Key="TextTertiaryColor">#636366</Color>
    <Color x:Key="AccentColor">#0a84ff</Color>
    <Color x:Key="AccentHoverColor">#409cff</Color>
    <Color x:Key="SuccessColor">#30d158</Color>
    <Color x:Key="ErrorColor">#ff453a</Color>

    <SolidColorBrush x:Key="BgPrimary" Color="{StaticResource BgPrimaryColor}"/>
    <SolidColorBrush x:Key="BgSecondary" Color="{StaticResource BgSecondaryColor}"/>
    <SolidColorBrush x:Key="BgTertiary" Color="{StaticResource BgTertiaryColor}"/>
    <SolidColorBrush x:Key="BorderBrush" Color="{StaticResource BorderColor}"/>
    <SolidColorBrush x:Key="TextPrimary" Color="{StaticResource TextPrimaryColor}"/>
    <SolidColorBrush x:Key="TextSecondary" Color="{StaticResource TextSecondaryColor}"/>
    <SolidColorBrush x:Key="TextTertiary" Color="{StaticResource TextTertiaryColor}"/>
    <SolidColorBrush x:Key="Accent" Color="{StaticResource AccentColor}"/>
    <SolidColorBrush x:Key="AccentHover" Color="{StaticResource AccentHoverColor}"/>
    <SolidColorBrush x:Key="Success" Color="{StaticResource SuccessColor}"/>
    <SolidColorBrush x:Key="Error" Color="{StaticResource ErrorColor}"/>
</ResourceDictionary>
```

- [ ] **Step 3: Create LightTheme.xaml**

Create `trackpad-plugin/Apricadabra.Trackpad/Themes/LightTheme.xaml`:

```xml
<ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
    <Color x:Key="BgPrimaryColor">#f5f5f7</Color>
    <Color x:Key="BgSecondaryColor">#ffffff</Color>
    <Color x:Key="BgTertiaryColor">#e5e5e7</Color>
    <Color x:Key="BorderColor">#d1d1d6</Color>
    <Color x:Key="TextPrimaryColor">#1d1d1f</Color>
    <Color x:Key="TextSecondaryColor">#86868b</Color>
    <Color x:Key="TextTertiaryColor">#aeaeb2</Color>
    <Color x:Key="AccentColor">#0071e3</Color>
    <Color x:Key="AccentHoverColor">#0077ed</Color>
    <Color x:Key="SuccessColor">#34c759</Color>
    <Color x:Key="ErrorColor">#ff3b30</Color>

    <SolidColorBrush x:Key="BgPrimary" Color="{StaticResource BgPrimaryColor}"/>
    <SolidColorBrush x:Key="BgSecondary" Color="{StaticResource BgSecondaryColor}"/>
    <SolidColorBrush x:Key="BgTertiary" Color="{StaticResource BgTertiaryColor}"/>
    <SolidColorBrush x:Key="BorderBrush" Color="{StaticResource BorderColor}"/>
    <SolidColorBrush x:Key="TextPrimary" Color="{StaticResource TextPrimaryColor}"/>
    <SolidColorBrush x:Key="TextSecondary" Color="{StaticResource TextSecondaryColor}"/>
    <SolidColorBrush x:Key="TextTertiary" Color="{StaticResource TextTertiaryColor}"/>
    <SolidColorBrush x:Key="Accent" Color="{StaticResource AccentColor}"/>
    <SolidColorBrush x:Key="AccentHover" Color="{StaticResource AccentHoverColor}"/>
    <SolidColorBrush x:Key="Success" Color="{StaticResource SuccessColor}"/>
    <SolidColorBrush x:Key="Error" Color="{StaticResource ErrorColor}"/>
</ResourceDictionary>
```

- [ ] **Step 4: Create App.xaml**

Create `trackpad-plugin/Apricadabra.Trackpad/App.xaml`:

```xml
<Application x:Class="Apricadabra.Trackpad.App"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             ShutdownMode="OnExplicitShutdown">
    <Application.Resources>
        <ResourceDictionary>
            <ResourceDictionary.MergedDictionaries>
                <ResourceDictionary Source="Themes/DarkTheme.xaml"/>
            </ResourceDictionary.MergedDictionaries>
        </ResourceDictionary>
    </Application.Resources>
</Application>
```

Note: `ShutdownMode="OnExplicitShutdown"` prevents the app from closing when the window is hidden (tray behavior).

- [ ] **Step 5: Create App.xaml.cs with theme switching and single-instance**

Create `trackpad-plugin/Apricadabra.Trackpad/App.xaml.cs`:

```csharp
using System;
using System.Threading;
using System.Windows;
using Apricadabra.Trackpad.Core.Bindings;

namespace Apricadabra.Trackpad
{
    public partial class App : Application
    {
        private static Mutex _singleInstanceMutex;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // Single instance check
            _singleInstanceMutex = new Mutex(true, "ApricadabraTrackpadMutex", out bool createdNew);
            if (!createdNew)
            {
                MessageBox.Show("Apricadabra Trackpad is already running.", "Apricadabra",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                Shutdown();
                return;
            }

            // Load theme from settings
            var settings = TrackpadSettings.Load();
            ApplyTheme(settings.Theme ?? "dark");
        }

        public void ApplyTheme(string theme)
        {
            var actualTheme = theme.ToLower();
            if (actualTheme == "system")
            {
                // Detect Windows theme from registry
                try
                {
                    var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                        @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
                    var value = key?.GetValue("AppsUseLightTheme");
                    actualTheme = (value is int i && i == 0) ? "dark" : "light";
                }
                catch { actualTheme = "dark"; }
            }

            var uri = actualTheme == "light"
                ? new Uri("Themes/LightTheme.xaml", UriKind.Relative)
                : new Uri("Themes/DarkTheme.xaml", UriKind.Relative);

            Resources.MergedDictionaries.Clear();
            Resources.MergedDictionaries.Add(new ResourceDictionary { Source = uri });
        }
    }
}
```

- [ ] **Step 6: Add window state and theme fields to TrackpadSettings**

Modify `trackpad-plugin/Apricadabra.Trackpad.Core/Bindings/TrackpadSettings.cs` — add these properties:

```csharp
[JsonPropertyName("windowLeft")]
public double? WindowLeft { get; set; }

[JsonPropertyName("windowTop")]
public double? WindowTop { get; set; }

[JsonPropertyName("windowWidth")]
public double WindowWidth { get; set; } = 800;

[JsonPropertyName("windowHeight")]
public double WindowHeight { get; set; } = 500;

[JsonPropertyName("theme")]
public string Theme { get; set; } = "dark";
```

- [ ] **Step 7: Commit**

```bash
git add trackpad-plugin/Apricadabra.Trackpad/ trackpad-plugin/Apricadabra.Trackpad.Core/Bindings/TrackpadSettings.cs
git commit -m "feat(trackpad-ui): scaffold WPF project with dark/light themes and app entry point"
```

---

## Task 2: MVVM Infrastructure

**Files:**
- Create: `trackpad-plugin/Apricadabra.Trackpad/ViewModels/ViewModelBase.cs`
- Create: `trackpad-plugin/Apricadabra.Trackpad/ViewModels/RelayCommand.cs`

- [ ] **Step 1: Create ViewModelBase**

Create `trackpad-plugin/Apricadabra.Trackpad/ViewModels/ViewModelBase.cs`:

```csharp
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Apricadabra.Trackpad.ViewModels
{
    public abstract class ViewModelBase : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }

        protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string name = null)
        {
            if (Equals(field, value)) return false;
            field = value;
            OnPropertyChanged(name);
            return true;
        }
    }
}
```

- [ ] **Step 2: Create RelayCommand**

Create `trackpad-plugin/Apricadabra.Trackpad/ViewModels/RelayCommand.cs`:

```csharp
using System;
using System.Windows.Input;

namespace Apricadabra.Trackpad.ViewModels
{
    public class RelayCommand : ICommand
    {
        private readonly Action _execute;
        private readonly Func<bool> _canExecute;

        public RelayCommand(Action execute, Func<bool> canExecute = null)
        {
            _execute = execute;
            _canExecute = canExecute;
        }

        public event EventHandler CanExecuteChanged
        {
            add => CommandManager.RequerySuggested += value;
            remove => CommandManager.RequerySuggested -= value;
        }

        public bool CanExecute(object parameter) => _canExecute?.Invoke() ?? true;
        public void Execute(object parameter) => _execute();
    }
}
```

- [ ] **Step 3: Commit**

```bash
git add trackpad-plugin/Apricadabra.Trackpad/ViewModels/
git commit -m "feat(trackpad-ui): add ViewModelBase and RelayCommand MVVM infrastructure"
```

---

## Task 3: MainViewModel and System Tray

**Files:**
- Create: `trackpad-plugin/Apricadabra.Trackpad/ViewModels/MainViewModel.cs`

- [ ] **Step 1: Create MainViewModel**

Create `trackpad-plugin/Apricadabra.Trackpad/ViewModels/MainViewModel.cs`:

```csharp
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

        public ICommand ShowBindingsCommand { get; }
        public ICommand ShowSettingsCommand { get; }
        public ICommand StartStopCommand { get; }
        public ICommand ExitCommand { get; }

        public BindingsViewModel BindingsVM { get; private set; }
        public SettingsViewModel SettingsVM { get; private set; }

        public MainViewModel()
        {
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
```

- [ ] **Step 2: Commit**

```bash
git add trackpad-plugin/Apricadabra.Trackpad/ViewModels/MainViewModel.cs
git commit -m "feat(trackpad-ui): add MainViewModel with service lifecycle and device management"
```

---

## Task 4: MainWindow with System Tray

**Files:**
- Create: `trackpad-plugin/Apricadabra.Trackpad/MainWindow.xaml`
- Create: `trackpad-plugin/Apricadabra.Trackpad/MainWindow.xaml.cs`

- [ ] **Step 1: Create MainWindow.xaml**

Create `trackpad-plugin/Apricadabra.Trackpad/MainWindow.xaml`:

```xml
<Window x:Class="Apricadabra.Trackpad.MainWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:tb="http://www.hardcodet.net/taskbar"
        xmlns:views="clr-namespace:Apricadabra.Trackpad.Views"
        xmlns:controls="clr-namespace:Apricadabra.Trackpad.Controls"
        Title="Apricadabra Trackpad"
        Width="800" Height="500"
        MinWidth="600" MinHeight="400"
        Background="{DynamicResource BgPrimary}"
        WindowStartupLocation="CenterScreen">

    <!-- System Tray Icon -->
    <tb:TaskbarIcon x:Name="TrayIcon"
                    ToolTipText="Apricadabra Trackpad"
                    DoubleClickCommand="{Binding ShowWindowCommand}">
        <tb:TaskbarIcon.ContextMenu>
            <ContextMenu>
                <MenuItem Header="Open" Click="TrayOpen_Click"/>
                <MenuItem Header="{Binding StartStopLabel}" Command="{Binding StartStopCommand}"/>
                <Separator/>
                <MenuItem Header="Exit" Command="{Binding ExitCommand}"/>
            </ContextMenu>
        </tb:TaskbarIcon.ContextMenu>
    </tb:TaskbarIcon>

    <Grid>
        <Grid.RowDefinitions>
            <RowDefinition Height="44"/>
            <RowDefinition Height="*"/>
        </Grid.RowDefinitions>

        <!-- Top Bar -->
        <Border Grid.Row="0" BorderBrush="{DynamicResource BorderBrush}" BorderThickness="0,0,0,1"
                Background="{DynamicResource BgSecondary}">
            <DockPanel Margin="16,0">
                <!-- Tab toggle (left) -->
                <StackPanel DockPanel.Dock="Left" Orientation="Horizontal">
                    <Button Content="Bindings" Click="TabBindings_Click"
                            Style="{StaticResource TabButtonStyle}"
                            Tag="{Binding ActiveTab}"/>
                    <Button Content="Settings" Click="TabSettings_Click"
                            Style="{StaticResource TabButtonStyle}"
                            Tag="{Binding ActiveTab}"/>
                </StackPanel>

                <!-- Device selector (right) -->
                <ComboBox DockPanel.Dock="Right" Width="180" Margin="8,0,0,0"
                          VerticalAlignment="Center"
                          ItemsSource="{Binding AvailableDevices}"
                          DisplayMemberPath="Name"
                          SelectedValuePath="DevicePath"
                          SelectedValue="{Binding SelectedDevicePath}">
                    <ComboBox.ItemsSource>
                        <CompositeCollection>
                            <ComboBoxItem Content="All Devices" Tag=""/>
                        </CompositeCollection>
                    </ComboBox.ItemsSource>
                </ComboBox>

                <!-- Connection status (right of center) -->
                <StackPanel DockPanel.Dock="Right" Orientation="Horizontal"
                            VerticalAlignment="Center" Margin="0,0,8,0">
                    <Ellipse Width="8" Height="8" Margin="0,0,6,0">
                        <Ellipse.Style>
                            <Style TargetType="Ellipse">
                                <Setter Property="Fill" Value="{DynamicResource Error}"/>
                                <Style.Triggers>
                                    <DataTrigger Binding="{Binding IsConnected}" Value="True">
                                        <Setter Property="Fill" Value="{DynamicResource Success}"/>
                                    </DataTrigger>
                                </Style.Triggers>
                            </Style>
                        </Ellipse.Style>
                    </Ellipse>
                    <TextBlock VerticalAlignment="Center" FontSize="11"
                               Foreground="{DynamicResource TextTertiary}">
                        <TextBlock.Style>
                            <Style TargetType="TextBlock">
                                <Setter Property="Text" Value="Disconnected"/>
                                <Style.Triggers>
                                    <DataTrigger Binding="{Binding IsConnected}" Value="True">
                                        <Setter Property="Text"
                                                Value="{Binding CoreVersion, StringFormat='Connected · Core v{0}'}"/>
                                    </DataTrigger>
                                </Style.Triggers>
                            </Style>
                        </TextBlock.Style>
                    </TextBlock>
                </StackPanel>

                <Border/> <!-- spacer -->
            </DockPanel>
        </Border>

        <!-- Content area -->
        <Grid Grid.Row="1">
            <Grid.ColumnDefinitions>
                <ColumnDefinition Width="*"/>
                <ColumnDefinition Width="200"/>
            </Grid.ColumnDefinitions>

            <!-- Left panel: Bindings or Settings -->
            <ContentControl Grid.Column="0" x:Name="LeftPanel"/>

            <!-- Right panel: Test (always visible) -->
            <Border Grid.Column="1" BorderBrush="{DynamicResource BorderBrush}" BorderThickness="1,0,0,0">
                <controls:TestPanel x:Name="TestPanelControl"/>
            </Border>
        </Grid>
    </Grid>
</Window>
```

- [ ] **Step 2: Create MainWindow.xaml.cs**

Create `trackpad-plugin/Apricadabra.Trackpad/MainWindow.xaml.cs`:

```csharp
using System;
using System.ComponentModel;
using System.Windows;
using Apricadabra.Trackpad.Core.Bindings;
using Apricadabra.Trackpad.ViewModels;
using Apricadabra.Trackpad.Views;

namespace Apricadabra.Trackpad
{
    public partial class MainWindow : Window
    {
        private readonly MainViewModel _vm;
        private BindingsView _bindingsView;
        private SettingsView _settingsView;

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

            // Restore window position
            RestoreWindowPosition();

            // Watch tab changes
            _vm.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(MainViewModel.ActiveTab))
                    LeftPanel.Content = _vm.ActiveTab == "Settings" ? _settingsView : (object)_bindingsView;
            };
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
```

- [ ] **Step 3: Commit**

```bash
git add trackpad-plugin/Apricadabra.Trackpad/MainWindow.*
git commit -m "feat(trackpad-ui): add MainWindow with top bar, tab toggle, tray icon, and window position memory"
```

---

## Task 5: TestPanel Control

**Files:**
- Create: `trackpad-plugin/Apricadabra.Trackpad/Controls/TestPanel.xaml`
- Create: `trackpad-plugin/Apricadabra.Trackpad/Controls/TestPanel.xaml.cs`

- [ ] **Step 1: Create TestPanel.xaml**

Create `trackpad-plugin/Apricadabra.Trackpad/Controls/TestPanel.xaml`:

```xml
<UserControl x:Class="Apricadabra.Trackpad.Controls.TestPanel"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             Background="{DynamicResource BgPrimary}">
    <StackPanel Margin="12" HorizontalAlignment="Center">
        <!-- Header -->
        <TextBlock Text="LIVE TEST" FontSize="11" Foreground="{DynamicResource TextSecondary}"
                   HorizontalAlignment="Center" Margin="0,0,0,12"
                   FontWeight="SemiBold" LetterSpacing="1"/>

        <!-- Trackpad visualization -->
        <Border Width="140" Height="100" CornerRadius="10"
                Background="{DynamicResource BgTertiary}"
                BorderBrush="{DynamicResource BorderBrush}" BorderThickness="1">
            <Canvas x:Name="TouchCanvas" ClipToBounds="True"/>
        </Border>

        <!-- Gesture display -->
        <TextBlock x:Name="GestureName" Text="" FontSize="14" FontWeight="Medium"
                   Foreground="{DynamicResource Success}" HorizontalAlignment="Center"
                   Margin="0,12,0,2"/>
        <TextBlock x:Name="GestureDetails" Text="" FontSize="11"
                   Foreground="{DynamicResource TextTertiary}" HorizontalAlignment="Center"
                   Margin="0,0,0,12"/>

        <!-- Matched binding -->
        <Border x:Name="MatchedPanel" CornerRadius="6" Padding="8"
                Background="#1c3a5c" BorderBrush="{DynamicResource Accent}" BorderThickness="1"
                Visibility="Collapsed">
            <StackPanel HorizontalAlignment="Center">
                <TextBlock Text="MATCHED" FontSize="10" Foreground="{DynamicResource TextTertiary}"
                           HorizontalAlignment="Center"/>
                <TextBlock x:Name="MatchedAction" FontSize="12"
                           Foreground="{DynamicResource TextPrimary}" HorizontalAlignment="Center"/>
            </StackPanel>
        </Border>
    </StackPanel>
</UserControl>
```

- [ ] **Step 2: Create TestPanel.xaml.cs**

Create `trackpad-plugin/Apricadabra.Trackpad/Controls/TestPanel.xaml.cs`:

```csharp
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
            GestureDetails.Text = $"{gesture.Fingers} fingers · δ {gesture.Delta:F3}";

            // Check for matched binding
            var match = _service.BindingConfig.Bindings.FirstOrDefault(b =>
                b.GestureType == TypeToString(gesture.Type) &&
                b.GestureFingers == gesture.Fingers &&
                b.GestureDirection == DirectionToString(gesture.Direction));

            if (match != null)
            {
                MatchedPanel.Visibility = Visibility.Visible;
                MatchedAction.Text = match.ActionType == "axis"
                    ? $"→ Axis {match.ActionAxis} ({match.ActionMode})"
                    : $"→ Button {match.ActionButton} ({match.ActionMode})";
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
```

- [ ] **Step 3: Commit**

```bash
git add trackpad-plugin/Apricadabra.Trackpad/Controls/TestPanel.*
git commit -m "feat(trackpad-ui): add TestPanel with live finger visualization and gesture display"
```

---

## Task 6: SettingsView and SettingsViewModel

**Files:**
- Create: `trackpad-plugin/Apricadabra.Trackpad/ViewModels/SettingsViewModel.cs`
- Create: `trackpad-plugin/Apricadabra.Trackpad/Views/SettingsView.xaml`
- Create: `trackpad-plugin/Apricadabra.Trackpad/Views/SettingsView.xaml.cs`

- [ ] **Step 1: Create SettingsViewModel**

Create `trackpad-plugin/Apricadabra.Trackpad/ViewModels/SettingsViewModel.cs`:

```csharp
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
```

- [ ] **Step 2: Create SettingsView.xaml**

Create `trackpad-plugin/Apricadabra.Trackpad/Views/SettingsView.xaml`:

```xml
<UserControl x:Class="Apricadabra.Trackpad.Views.SettingsView"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             Background="{DynamicResource BgPrimary}">
    <ScrollViewer VerticalScrollBarVisibility="Auto" Padding="16">
        <StackPanel MaxWidth="500">

            <!-- Gesture Recognition -->
            <TextBlock Text="GESTURE RECOGNITION" FontSize="11" FontWeight="SemiBold"
                       Foreground="{DynamicResource TextSecondary}" Margin="0,0,0,12"
                       LetterSpacing="1"/>

            <Grid Margin="0,0,0,8">
                <Grid.ColumnDefinitions>
                    <ColumnDefinition Width="180"/>
                    <ColumnDefinition Width="*"/>
                    <ColumnDefinition Width="40"/>
                </Grid.ColumnDefinitions>
                <TextBlock Text="Scroll finger count" Foreground="{DynamicResource TextPrimary}"
                           VerticalAlignment="Center"/>
                <ComboBox Grid.Column="1" ItemsSource="{Binding FingerCountOptions}"
                          SelectedItem="{Binding ScrollFingerCount}" VerticalAlignment="Center"/>
            </Grid>

            <Grid Margin="0,0,0,8">
                <Grid.ColumnDefinitions>
                    <ColumnDefinition Width="180"/>
                    <ColumnDefinition Width="*"/>
                    <ColumnDefinition Width="40"/>
                </Grid.ColumnDefinitions>
                <TextBlock Text="Swipe distance" Foreground="{DynamicResource TextPrimary}"
                           VerticalAlignment="Center"/>
                <Slider Grid.Column="1" Minimum="0.05" Maximum="0.50"
                        Value="{Binding SwipeDistanceThreshold}" VerticalAlignment="Center"
                        TickFrequency="0.05" IsSnapToTickEnabled="False"/>
                <TextBlock Grid.Column="2" Text="{Binding SwipeDistanceThreshold, StringFormat='{}{0:F2}'}"
                           Foreground="{DynamicResource TextTertiary}" VerticalAlignment="Center"/>
            </Grid>

            <Grid Margin="0,0,0,8">
                <Grid.ColumnDefinitions>
                    <ColumnDefinition Width="180"/>
                    <ColumnDefinition Width="*"/>
                    <ColumnDefinition Width="40"/>
                </Grid.ColumnDefinitions>
                <TextBlock Text="Swipe speed" Foreground="{DynamicResource TextPrimary}"
                           VerticalAlignment="Center"/>
                <Slider Grid.Column="1" Minimum="0.1" Maximum="1.0"
                        Value="{Binding SwipeSpeedThreshold}" VerticalAlignment="Center"/>
                <TextBlock Grid.Column="2" Text="{Binding SwipeSpeedThreshold, StringFormat='{}{0:F1}'}"
                           Foreground="{DynamicResource TextTertiary}" VerticalAlignment="Center"/>
            </Grid>

            <Grid Margin="0,0,0,8">
                <Grid.ColumnDefinitions>
                    <ColumnDefinition Width="180"/>
                    <ColumnDefinition Width="*"/>
                    <ColumnDefinition Width="40"/>
                </Grid.ColumnDefinitions>
                <TextBlock Text="Tap max duration" Foreground="{DynamicResource TextPrimary}"
                           VerticalAlignment="Center"/>
                <Slider Grid.Column="1" Minimum="100" Maximum="1000"
                        Value="{Binding TapMaxDuration}" VerticalAlignment="Center"
                        IsSnapToTickEnabled="True" TickFrequency="50"/>
                <TextBlock Grid.Column="2" Text="{Binding TapMaxDuration, StringFormat='{}{0}ms'}"
                           Foreground="{DynamicResource TextTertiary}" VerticalAlignment="Center"/>
            </Grid>

            <Grid Margin="0,0,0,16">
                <Grid.ColumnDefinitions>
                    <ColumnDefinition Width="180"/>
                    <ColumnDefinition Width="*"/>
                    <ColumnDefinition Width="40"/>
                </Grid.ColumnDefinitions>
                <TextBlock Text="Tap max movement" Foreground="{DynamicResource TextPrimary}"
                           VerticalAlignment="Center"/>
                <Slider Grid.Column="1" Minimum="0.01" Maximum="0.10"
                        Value="{Binding TapMaxMovement}" VerticalAlignment="Center"/>
                <TextBlock Grid.Column="2" Text="{Binding TapMaxMovement, StringFormat='{}{0:F2}'}"
                           Foreground="{DynamicResource TextTertiary}" VerticalAlignment="Center"/>
            </Grid>

            <!-- Sensitivity -->
            <TextBlock Text="SENSITIVITY" FontSize="11" FontWeight="SemiBold"
                       Foreground="{DynamicResource TextSecondary}" Margin="0,0,0,12"
                       LetterSpacing="1"/>

            <Grid Margin="0,0,0,8">
                <Grid.ColumnDefinitions>
                    <ColumnDefinition Width="180"/>
                    <ColumnDefinition Width="*"/>
                    <ColumnDefinition Width="40"/>
                </Grid.ColumnDefinitions>
                <TextBlock Text="Scroll" Foreground="{DynamicResource TextPrimary}"
                           VerticalAlignment="Center"/>
                <Slider Grid.Column="1" Minimum="0.1" Maximum="5.0"
                        Value="{Binding ScrollSensitivity}" VerticalAlignment="Center"/>
                <TextBlock Grid.Column="2" Text="{Binding ScrollSensitivity, StringFormat='{}{0:F1}'}"
                           Foreground="{DynamicResource TextTertiary}" VerticalAlignment="Center"/>
            </Grid>

            <Grid Margin="0,0,0,8">
                <Grid.ColumnDefinitions>
                    <ColumnDefinition Width="180"/>
                    <ColumnDefinition Width="*"/>
                    <ColumnDefinition Width="40"/>
                </Grid.ColumnDefinitions>
                <TextBlock Text="Pinch" Foreground="{DynamicResource TextPrimary}"
                           VerticalAlignment="Center"/>
                <Slider Grid.Column="1" Minimum="0.1" Maximum="5.0"
                        Value="{Binding PinchSensitivity}" VerticalAlignment="Center"/>
                <TextBlock Grid.Column="2" Text="{Binding PinchSensitivity, StringFormat='{}{0:F1}'}"
                           Foreground="{DynamicResource TextTertiary}" VerticalAlignment="Center"/>
            </Grid>

            <Grid Margin="0,0,0,16">
                <Grid.ColumnDefinitions>
                    <ColumnDefinition Width="180"/>
                    <ColumnDefinition Width="*"/>
                    <ColumnDefinition Width="40"/>
                </Grid.ColumnDefinitions>
                <TextBlock Text="Rotate" Foreground="{DynamicResource TextPrimary}"
                           VerticalAlignment="Center"/>
                <Slider Grid.Column="1" Minimum="0.1" Maximum="5.0"
                        Value="{Binding RotateSensitivity}" VerticalAlignment="Center"/>
                <TextBlock Grid.Column="2" Text="{Binding RotateSensitivity, StringFormat='{}{0:F1}'}"
                           Foreground="{DynamicResource TextTertiary}" VerticalAlignment="Center"/>
            </Grid>

            <!-- Appearance -->
            <TextBlock Text="APPEARANCE" FontSize="11" FontWeight="SemiBold"
                       Foreground="{DynamicResource TextSecondary}" Margin="0,0,0,12"
                       LetterSpacing="1"/>

            <Grid Margin="0,0,0,8">
                <Grid.ColumnDefinitions>
                    <ColumnDefinition Width="180"/>
                    <ColumnDefinition Width="*"/>
                </Grid.ColumnDefinitions>
                <TextBlock Text="Theme" Foreground="{DynamicResource TextPrimary}"
                           VerticalAlignment="Center"/>
                <ComboBox Grid.Column="1" ItemsSource="{Binding ThemeOptions}"
                          SelectedItem="{Binding Theme}" VerticalAlignment="Center"/>
            </Grid>

        </StackPanel>
    </ScrollViewer>
</UserControl>
```

- [ ] **Step 3: Create SettingsView.xaml.cs**

Create `trackpad-plugin/Apricadabra.Trackpad/Views/SettingsView.xaml.cs`:

```csharp
using System.Windows.Controls;

namespace Apricadabra.Trackpad.Views
{
    public partial class SettingsView : UserControl
    {
        public SettingsView()
        {
            InitializeComponent();
        }
    }
}
```

- [ ] **Step 4: Commit**

```bash
git add trackpad-plugin/Apricadabra.Trackpad/ViewModels/SettingsViewModel.cs trackpad-plugin/Apricadabra.Trackpad/Views/SettingsView.*
git commit -m "feat(trackpad-ui): add SettingsView with live-apply sliders and theme switching"
```

---

## Task 7: BindingsView and BindingsViewModel

**Files:**
- Create: `trackpad-plugin/Apricadabra.Trackpad/ViewModels/BindingsViewModel.cs`
- Create: `trackpad-plugin/Apricadabra.Trackpad/Views/BindingsView.xaml`
- Create: `trackpad-plugin/Apricadabra.Trackpad/Views/BindingsView.xaml.cs`

- [ ] **Step 1: Create BindingsViewModel**

Create `trackpad-plugin/Apricadabra.Trackpad/ViewModels/BindingsViewModel.cs`:

```csharp
using System;
using System.Collections.ObjectModel;
using System.Text.Json.Nodes;
using System.Windows.Input;
using Apricadabra.Trackpad.Core;
using Apricadabra.Trackpad.Core.Bindings;

namespace Apricadabra.Trackpad.ViewModels
{
    public class BindingRowViewModel : ViewModelBase
    {
        public BindingEntry Entry { get; set; }
        private bool _isEditing;

        public bool IsEditing
        {
            get => _isEditing;
            set => SetProperty(ref _isEditing, value);
        }

        public string GestureDisplay => Entry == null ? "" :
            $"{(Entry.GestureFingers > 0 ? Entry.GestureFingers + "-finger " : "")}" +
            $"{Entry.GestureType} {Entry.GestureDirection}".Trim();

        public string ActionDisplay => Entry == null ? "" :
            Entry.ActionType == "axis"
                ? $"Axis {Entry.ActionAxis} ({Entry.ActionMode})"
                : $"Button {Entry.ActionButton} ({Entry.ActionMode})";

        // Edit fields
        public string EditGestureType { get; set; } = "scroll";
        public int EditFingers { get; set; } = 2;
        public string EditDirection { get; set; } = "up";
        public string EditActionType { get; set; } = "axis";
        public int EditAxis { get; set; } = 1;
        public int EditButton { get; set; } = 1;
        public string EditMode { get; set; } = "hold";
        public float EditSensitivity { get; set; } = 0.02f;
        public float EditDecayRate { get; set; } = 0.95f;
        public int EditSteps { get; set; } = 5;

        public void LoadFromEntry()
        {
            if (Entry == null) return;
            EditGestureType = Entry.GestureType ?? "scroll";
            EditFingers = Entry.GestureFingers > 0 ? Entry.GestureFingers : 2;
            EditDirection = Entry.GestureDirection ?? "up";
            EditActionType = Entry.ActionType ?? "axis";
            EditAxis = Entry.ActionAxis;
            EditButton = Entry.ActionButton;
            EditMode = Entry.ActionMode ?? "hold";
            EditSensitivity = Entry.ActionSensitivity;
            EditDecayRate = Entry.ActionDecayRate;
            EditSteps = Entry.ActionSteps;
        }

        public BindingEntry ToEntry()
        {
            var gesture = new JsonObject
            {
                ["type"] = EditGestureType,
                ["fingers"] = EditFingers,
                ["direction"] = EditDirection
            };
            var action = new JsonObject { ["type"] = EditActionType, ["mode"] = EditMode };
            if (EditActionType == "axis")
            {
                action["axis"] = EditAxis;
                action["sensitivity"] = EditSensitivity;
                if (EditMode == "spring") action["decayRate"] = EditDecayRate;
                if (EditMode == "detent") action["steps"] = EditSteps;
            }
            else
            {
                action["button"] = EditButton;
            }

            return new BindingEntry
            {
                Id = Entry?.Id ?? $"{EditGestureType}-{EditFingers}-{EditDirection}-{Guid.NewGuid():N}".Substring(0, 32),
                Gesture = gesture,
                Action = action
            };
        }
    }

    public class BindingsViewModel : ViewModelBase
    {
        private readonly TrackpadService _service;
        public ObservableCollection<BindingRowViewModel> Rows { get; } = new();

        public ICommand AddCommand { get; }
        public ICommand EditCommand { get; }
        public ICommand DeleteCommand { get; }
        public ICommand SaveEditCommand { get; }
        public ICommand CancelEditCommand { get; }

        private BindingRowViewModel _editingRow;

        public BindingsViewModel(TrackpadService service)
        {
            _service = service;
            LoadRows();

            AddCommand = new RelayCommand(AddBinding);
            SaveEditCommand = new RelayCommand(SaveEdit);
            CancelEditCommand = new RelayCommand(CancelEdit);
        }

        private void LoadRows()
        {
            Rows.Clear();
            foreach (var entry in _service.BindingConfig.Bindings)
            {
                Rows.Add(new BindingRowViewModel { Entry = entry });
            }
        }

        public void StartEdit(BindingRowViewModel row)
        {
            CancelEdit();
            row.LoadFromEntry();
            row.IsEditing = true;
            _editingRow = row;
        }

        public void DeleteBinding(BindingRowViewModel row)
        {
            _service.BindingConfig.Bindings.Remove(row.Entry);
            _service.BindingConfig.Save();
            Rows.Remove(row);
        }

        private void AddBinding()
        {
            CancelEdit();
            var row = new BindingRowViewModel { IsEditing = true };
            Rows.Add(row);
            _editingRow = row;
        }

        private void SaveEdit()
        {
            if (_editingRow == null) return;
            var newEntry = _editingRow.ToEntry();

            if (_editingRow.Entry != null)
            {
                // Editing existing
                var idx = _service.BindingConfig.Bindings.IndexOf(_editingRow.Entry);
                if (idx >= 0) _service.BindingConfig.Bindings[idx] = newEntry;
            }
            else
            {
                // Adding new
                _service.BindingConfig.Bindings.Add(newEntry);
            }

            _editingRow.Entry = newEntry;
            _editingRow.IsEditing = false;
            _editingRow.OnPropertyChanged(nameof(BindingRowViewModel.GestureDisplay));
            _editingRow.OnPropertyChanged(nameof(BindingRowViewModel.ActionDisplay));
            _editingRow = null;
            _service.BindingConfig.Save();
        }

        private void CancelEdit()
        {
            if (_editingRow == null) return;
            if (_editingRow.Entry == null)
                Rows.Remove(_editingRow); // was a new row, remove it
            else
                _editingRow.IsEditing = false;
            _editingRow = null;
        }
    }
}
```

- [ ] **Step 2: Create BindingsView.xaml**

Create `trackpad-plugin/Apricadabra.Trackpad/Views/BindingsView.xaml`:

```xml
<UserControl x:Class="Apricadabra.Trackpad.Views.BindingsView"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:controls="clr-namespace:Apricadabra.Trackpad.Controls"
             Background="{DynamicResource BgPrimary}">
    <DockPanel>
        <ItemsControl DockPanel.Dock="Top" ItemsSource="{Binding Rows}" Margin="12">
            <ItemsControl.ItemTemplate>
                <DataTemplate>
                    <Border Margin="0,0,0,6" CornerRadius="6"
                            Background="{DynamicResource BgTertiary}" Padding="10,8">
                        <!-- Collapsed row -->
                        <StackPanel Visibility="{Binding IsEditing, Converter={StaticResource InverseBoolToVis}}">
                            <DockPanel>
                                <StackPanel DockPanel.Dock="Right" Orientation="Horizontal" VerticalAlignment="Center">
                                    <TextBlock Text="edit" Foreground="{DynamicResource TextTertiary}"
                                               FontSize="11" Cursor="Hand" Margin="0,0,8,0"
                                               MouseDown="Edit_Click" Tag="{Binding}"/>
                                    <TextBlock Text="×" Foreground="{DynamicResource TextTertiary}"
                                               FontSize="13" Cursor="Hand"
                                               MouseDown="Delete_Click" Tag="{Binding}"/>
                                </StackPanel>
                                <StackPanel Orientation="Horizontal">
                                    <TextBlock Text="{Binding GestureDisplay}"
                                               Foreground="{DynamicResource TextPrimary}" FontSize="13"/>
                                    <TextBlock Text=" → " Foreground="{DynamicResource TextTertiary}" FontSize="13"/>
                                    <TextBlock Text="{Binding ActionDisplay}"
                                               Foreground="{DynamicResource Accent}" FontSize="13"/>
                                </StackPanel>
                            </DockPanel>
                        </StackPanel>

                        <!-- Expanded editor -->
                        <controls:BindingEditor Visibility="{Binding IsEditing, Converter={StaticResource BoolToVis}}"
                                                DataContext="{Binding}"/>
                    </Border>
                </DataTemplate>
            </ItemsControl.ItemTemplate>
        </ItemsControl>

        <!-- Add button -->
        <TextBlock DockPanel.Dock="Top" Text="+ Add Binding" FontSize="13"
                   Foreground="{DynamicResource Accent}" HorizontalAlignment="Center"
                   Cursor="Hand" Margin="0,4,0,12"
                   MouseDown="Add_Click"/>
    </DockPanel>
</UserControl>
```

- [ ] **Step 3: Create BindingsView.xaml.cs**

Create `trackpad-plugin/Apricadabra.Trackpad/Views/BindingsView.xaml.cs`:

```csharp
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Apricadabra.Trackpad.ViewModels;

namespace Apricadabra.Trackpad.Views
{
    public partial class BindingsView : UserControl
    {
        private BindingsViewModel VM => DataContext as BindingsViewModel;

        public BindingsView()
        {
            InitializeComponent();

            // Add value converters
            Resources.Add("BoolToVis", new BooleanToVisibilityConverter());
            Resources.Add("InverseBoolToVis", new InverseBoolToVisConverter());
        }

        private void Edit_Click(object sender, MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement el && el.Tag is BindingRowViewModel row)
                VM?.StartEdit(row);
        }

        private void Delete_Click(object sender, MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement el && el.Tag is BindingRowViewModel row)
                VM?.DeleteBinding(row);
        }

        private void Add_Click(object sender, MouseButtonEventArgs e) => VM?.AddCommand.Execute(null);
    }

    public class InverseBoolToVisConverter : System.Windows.Data.IValueConverter
    {
        public object Convert(object value, System.Type targetType, object parameter,
            System.Globalization.CultureInfo culture)
            => value is true ? Visibility.Collapsed : Visibility.Visible;

        public object ConvertBack(object value, System.Type targetType, object parameter,
            System.Globalization.CultureInfo culture)
            => throw new System.NotSupportedException();
    }
}
```

- [ ] **Step 4: Commit**

```bash
git add trackpad-plugin/Apricadabra.Trackpad/ViewModels/BindingsViewModel.cs trackpad-plugin/Apricadabra.Trackpad/Views/BindingsView.*
git commit -m "feat(trackpad-ui): add BindingsView with binding list, inline edit, add/delete"
```

---

## Task 8: BindingEditor Control

**Files:**
- Create: `trackpad-plugin/Apricadabra.Trackpad/Controls/BindingEditor.xaml`
- Create: `trackpad-plugin/Apricadabra.Trackpad/Controls/BindingEditor.xaml.cs`

- [ ] **Step 1: Create BindingEditor.xaml**

Create `trackpad-plugin/Apricadabra.Trackpad/Controls/BindingEditor.xaml`:

```xml
<UserControl x:Class="Apricadabra.Trackpad.Controls.BindingEditor"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
    <StackPanel Margin="4">
        <!-- Gesture row -->
        <StackPanel Orientation="Horizontal" Margin="0,0,0,8">
            <TextBlock Text="Gesture:" Foreground="{DynamicResource TextSecondary}"
                       Width="60" VerticalAlignment="Center" FontSize="12"/>
            <ComboBox x:Name="GestureType" Width="90" Margin="4,0"
                      SelectedItem="{Binding EditGestureType}"
                      SelectionChanged="GestureType_Changed">
                <ComboBoxItem Content="scroll"/>
                <ComboBoxItem Content="pinch"/>
                <ComboBoxItem Content="rotate"/>
                <ComboBoxItem Content="swipe"/>
                <ComboBoxItem Content="tap"/>
            </ComboBox>
            <TextBlock Text="Fingers:" Foreground="{DynamicResource TextSecondary}"
                       VerticalAlignment="Center" FontSize="12" Margin="8,0,4,0"
                       x:Name="FingersLabel"/>
            <ComboBox x:Name="Fingers" Width="50" SelectedItem="{Binding EditFingers}">
                <ComboBoxItem Content="2"/>
                <ComboBoxItem Content="3"/>
                <ComboBoxItem Content="4"/>
            </ComboBox>
            <TextBlock Text="Direction:" Foreground="{DynamicResource TextSecondary}"
                       VerticalAlignment="Center" FontSize="12" Margin="8,0,4,0"
                       x:Name="DirectionLabel"/>
            <ComboBox x:Name="Direction" Width="120" SelectedItem="{Binding EditDirection}"/>
        </StackPanel>

        <!-- Action row -->
        <StackPanel Orientation="Horizontal" Margin="0,0,0,8">
            <TextBlock Text="Action:" Foreground="{DynamicResource TextSecondary}"
                       Width="60" VerticalAlignment="Center" FontSize="12"/>
            <ComboBox x:Name="ActionType" Width="70" Margin="4,0"
                      SelectedItem="{Binding EditActionType}"
                      SelectionChanged="ActionType_Changed">
                <ComboBoxItem Content="axis"/>
                <ComboBoxItem Content="button"/>
            </ComboBox>

            <!-- Axis fields -->
            <StackPanel x:Name="AxisFields" Orientation="Horizontal">
                <TextBlock Text="Axis:" Foreground="{DynamicResource TextSecondary}"
                           VerticalAlignment="Center" FontSize="12" Margin="8,0,4,0"/>
                <ComboBox Width="45" SelectedItem="{Binding EditAxis}">
                    <ComboBoxItem Content="1"/><ComboBoxItem Content="2"/>
                    <ComboBoxItem Content="3"/><ComboBoxItem Content="4"/>
                    <ComboBoxItem Content="5"/><ComboBoxItem Content="6"/>
                    <ComboBoxItem Content="7"/><ComboBoxItem Content="8"/>
                </ComboBox>
                <TextBlock Text="Mode:" Foreground="{DynamicResource TextSecondary}"
                           VerticalAlignment="Center" FontSize="12" Margin="8,0,4,0"/>
                <ComboBox x:Name="AxisMode" Width="80" SelectedItem="{Binding EditMode}"
                          SelectionChanged="AxisMode_Changed">
                    <ComboBoxItem Content="hold"/>
                    <ComboBoxItem Content="spring"/>
                    <ComboBoxItem Content="detent"/>
                </ComboBox>
            </StackPanel>

            <!-- Button fields -->
            <StackPanel x:Name="ButtonFields" Orientation="Horizontal" Visibility="Collapsed">
                <TextBlock Text="Button:" Foreground="{DynamicResource TextSecondary}"
                           VerticalAlignment="Center" FontSize="12" Margin="8,0,4,0"/>
                <ComboBox Width="55" SelectedItem="{Binding EditButton}" IsEditable="True"/>
                <TextBlock Text="Mode:" Foreground="{DynamicResource TextSecondary}"
                           VerticalAlignment="Center" FontSize="12" Margin="8,0,4,0"/>
                <ComboBox Width="100" SelectedItem="{Binding EditMode}">
                    <ComboBoxItem Content="momentary"/>
                    <ComboBoxItem Content="toggle"/>
                    <ComboBoxItem Content="pulse"/>
                    <ComboBoxItem Content="double"/>
                    <ComboBoxItem Content="rapid"/>
                    <ComboBoxItem Content="longshort"/>
                </ComboBox>
            </StackPanel>
        </StackPanel>

        <!-- Sensitivity (for axis) -->
        <StackPanel x:Name="SensitivityPanel" Orientation="Horizontal" Margin="0,0,0,8">
            <TextBlock Text="" Width="60"/>
            <TextBlock Text="Sensitivity:" Foreground="{DynamicResource TextSecondary}"
                       VerticalAlignment="Center" FontSize="12" Margin="4,0"/>
            <Slider Width="150" Minimum="0.001" Maximum="0.1"
                    Value="{Binding EditSensitivity}" VerticalAlignment="Center"/>
            <TextBlock Text="{Binding EditSensitivity, StringFormat='{}{0:F3}'}"
                       Foreground="{DynamicResource TextTertiary}" VerticalAlignment="Center" Margin="8,0"/>
        </StackPanel>

        <!-- Save/Cancel -->
        <StackPanel Orientation="Horizontal" HorizontalAlignment="Right" Margin="0,4,0,0">
            <Button Content="Save" Padding="12,4" Margin="0,0,8,0"
                    Click="Save_Click"/>
            <Button Content="Cancel" Padding="12,4"
                    Click="Cancel_Click"/>
        </StackPanel>
    </StackPanel>
</UserControl>
```

- [ ] **Step 2: Create BindingEditor.xaml.cs**

Create `trackpad-plugin/Apricadabra.Trackpad/Controls/BindingEditor.xaml.cs`:

```csharp
using System.Windows;
using System.Windows.Controls;
using Apricadabra.Trackpad.ViewModels;

namespace Apricadabra.Trackpad.Controls
{
    public partial class BindingEditor : UserControl
    {
        public BindingEditor()
        {
            InitializeComponent();
            DataContextChanged += (s, e) => UpdateVisibility();
        }

        private void GestureType_Changed(object sender, SelectionChangedEventArgs e)
        {
            UpdateDirectionOptions();
            UpdateFingerVisibility();
        }

        private void ActionType_Changed(object sender, SelectionChangedEventArgs e)
        {
            UpdateActionFieldVisibility();
        }

        private void AxisMode_Changed(object sender, SelectionChangedEventArgs e)
        {
            // Could show/hide decay rate or steps fields here
        }

        private void UpdateVisibility()
        {
            UpdateDirectionOptions();
            UpdateFingerVisibility();
            UpdateActionFieldVisibility();
        }

        private void UpdateDirectionOptions()
        {
            if (Direction == null) return;
            Direction.Items.Clear();
            var vm = DataContext as BindingRowViewModel;
            var type = vm?.EditGestureType ?? "scroll";

            switch (type)
            {
                case "scroll":
                case "swipe":
                    Direction.Items.Add("up");
                    Direction.Items.Add("down");
                    Direction.Items.Add("left");
                    Direction.Items.Add("right");
                    break;
                case "pinch":
                    Direction.Items.Add("in");
                    Direction.Items.Add("out");
                    break;
                case "rotate":
                    Direction.Items.Add("clockwise");
                    Direction.Items.Add("counterclockwise");
                    break;
                case "tap":
                    Direction.Items.Add("none");
                    break;
            }
            if (Direction.Items.Count > 0)
                Direction.SelectedIndex = 0;
        }

        private void UpdateFingerVisibility()
        {
            if (FingersLabel == null) return;
            var vm = DataContext as BindingRowViewModel;
            var type = vm?.EditGestureType ?? "scroll";
            var hidden = type == "pinch"; // pinch is always 2
            FingersLabel.Visibility = hidden ? Visibility.Collapsed : Visibility.Visible;
            Fingers.Visibility = hidden ? Visibility.Collapsed : Visibility.Visible;
            DirectionLabel.Visibility = type == "tap" ? Visibility.Collapsed : Visibility.Visible;
            Direction.Visibility = type == "tap" ? Visibility.Collapsed : Visibility.Visible;
        }

        private void UpdateActionFieldVisibility()
        {
            if (AxisFields == null) return;
            var vm = DataContext as BindingRowViewModel;
            var isAxis = vm?.EditActionType == "axis";
            AxisFields.Visibility = isAxis ? Visibility.Visible : Visibility.Collapsed;
            ButtonFields.Visibility = isAxis ? Visibility.Collapsed : Visibility.Visible;
            SensitivityPanel.Visibility = isAxis ? Visibility.Visible : Visibility.Collapsed;
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            // Walk up to find BindingsView and call save
            var bindingsView = FindParent<Views.BindingsView>(this);
            if (bindingsView?.DataContext is BindingsViewModel bvm)
                bvm.SaveEditCommand.Execute(null);
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            var bindingsView = FindParent<Views.BindingsView>(this);
            if (bindingsView?.DataContext is BindingsViewModel bvm)
                bvm.CancelEditCommand.Execute(null);
        }

        private static T FindParent<T>(DependencyObject child) where T : DependencyObject
        {
            var parent = VisualTreeHelper.GetParent(child);
            while (parent != null)
            {
                if (parent is T typed) return typed;
                parent = VisualTreeHelper.GetParent(parent);
            }
            return null;
        }
    }
}
```

- [ ] **Step 3: Commit**

```bash
git add trackpad-plugin/Apricadabra.Trackpad/Controls/BindingEditor.*
git commit -m "feat(trackpad-ui): add BindingEditor control with dynamic gesture/action fields"
```

---

## Task 9: Wire Everything Together

**Files:**
- Modify: `trackpad-plugin/Apricadabra.Trackpad/App.xaml.cs`

- [ ] **Step 1: Update App.xaml.cs to create and show MainWindow**

Update `App.xaml.cs` `OnStartup` to create the window, initialize it, and start hidden (tray only):

Add after the theme application:

```csharp
var mainWindow = new MainWindow();
mainWindow.Initialize();
// Start hidden — tray icon is visible
```

The window starts hidden. The tray icon's "Open" and double-click will call `mainWindow.Show()`.

- [ ] **Step 2: Verify all namespaces and references are correct**

Check that:
- `MainWindow.xaml` references `xmlns:views` and `xmlns:controls` with correct namespaces
- `BindingsView.xaml` references `xmlns:controls` for `BindingEditor`
- All code-behind files have correct `using` statements
- `App.xaml` `x:Class` matches `Apricadabra.Trackpad.App`

- [ ] **Step 3: Commit**

```bash
git add -u
git commit -m "feat(trackpad-ui): wire App startup, window initialization, and tray integration"
```

---

## Task 10: Final Validation

**Files:** All created files

- [ ] **Step 1: Verify all files exist**

Check every file in the file structure section exists with correct path.

- [ ] **Step 2: Check for TODOs/placeholders**

Search all `.cs` and `.xaml` files for TODO, FIXME, NotImplementedException.

- [ ] **Step 3: Verify type consistency**

Check that:
- `MainViewModel` creates `BindingsViewModel` and `SettingsViewModel` with correct constructor args
- `TestPanel.AttachService()` is called with the right `TrackpadService` instance
- `BindingEditor` DataContext is `BindingRowViewModel` (set via `DataTemplate` in `BindingsView`)
- Theme resource key names are consistent between `DarkTheme.xaml`, `LightTheme.xaml`, and all XAML consumers

- [ ] **Step 4: Commit any fixes**

```bash
git add -u
git commit -m "chore: final validation and cleanup for trackpad UI"
```
