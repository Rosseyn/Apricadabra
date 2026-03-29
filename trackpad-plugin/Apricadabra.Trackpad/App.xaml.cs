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
