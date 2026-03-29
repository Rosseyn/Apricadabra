using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Apricadabra.Trackpad.Core.Bindings
{
    public class TrackpadSettings
    {
        [JsonPropertyName("selectedDevicePath")]
        public string SelectedDevicePath { get; set; }

        [JsonPropertyName("scrollFingerCount")]
        public int ScrollFingerCount { get; set; } = 2;

        [JsonPropertyName("scrollSensitivity")]
        public float ScrollSensitivity { get; set; } = 1.0f;

        [JsonPropertyName("swipeDistanceThreshold")]
        public float SwipeDistanceThreshold { get; set; } = 0.15f;

        [JsonPropertyName("swipeSpeedThreshold")]
        public float SwipeSpeedThreshold { get; set; } = 0.3f;

        [JsonPropertyName("pinchSensitivity")]
        public float PinchSensitivity { get; set; } = 1.0f;

        [JsonPropertyName("rotateSensitivity")]
        public float RotateSensitivity { get; set; } = 1.0f;

        [JsonPropertyName("tapMaxDuration")]
        public int TapMaxDuration { get; set; } = 300;

        [JsonPropertyName("tapMaxMovement")]
        public float TapMaxMovement { get; set; } = 0.03f;

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

        private static string SettingsPath =>
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Apricadabra", "trackpad", "settings.json");

        public static TrackpadSettings Load()
        {
            try
            {
                if (File.Exists(SettingsPath))
                    return JsonSerializer.Deserialize<TrackpadSettings>(File.ReadAllText(SettingsPath));
            }
            catch { }
            return new TrackpadSettings();
        }

        public void Save()
        {
            var dir = Path.GetDirectoryName(SettingsPath);
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(SettingsPath, json);
        }
    }
}
