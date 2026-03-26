using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Apricadabra.Trackpad.Core.Bindings
{
    public class BindingEntry
    {
        [JsonPropertyName("id")]
        public string Id { get; set; }

        [JsonPropertyName("gesture")]
        public JsonObject Gesture { get; set; }

        [JsonPropertyName("action")]
        public JsonObject Action { get; set; }

        // Parsed gesture fields for matching
        [JsonIgnore] public string GestureType => Gesture?["type"]?.GetValue<string>();
        [JsonIgnore] public int GestureFingers => Gesture?["fingers"]?.GetValue<int>() ?? 0;
        [JsonIgnore] public string GestureDirection => Gesture?["direction"]?.GetValue<string>() ?? "none";

        // Parsed action fields for dispatch
        [JsonIgnore] public string ActionType => Action?["type"]?.GetValue<string>();
        [JsonIgnore] public int ActionAxis => Action?["axis"]?.GetValue<int>() ?? 1;
        [JsonIgnore] public int ActionButton => Action?["button"]?.GetValue<int>() ?? 1;
        [JsonIgnore] public string ActionMode => Action?["mode"]?.GetValue<string>() ?? "hold";
        [JsonIgnore] public float ActionSensitivity => Action?["sensitivity"]?.GetValue<float>() ?? 0.02f;
        [JsonIgnore] public float ActionDecayRate => Action?["decayRate"]?.GetValue<float>() ?? 0.95f;
        [JsonIgnore] public int ActionSteps => Action?["steps"]?.GetValue<int>() ?? 5;
    }

    public class BindingConfig
    {
        [JsonPropertyName("schema")]
        public int Schema { get; set; } = 1;

        [JsonPropertyName("plugin")]
        public string Plugin { get; set; } = "trackpad";

        [JsonPropertyName("bindings")]
        public List<BindingEntry> Bindings { get; set; } = new();

        private static string ConfigPath =>
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Apricadabra", "trackpad", "bindings.json");

        public static BindingConfig Load()
        {
            try
            {
                if (File.Exists(ConfigPath))
                    return JsonSerializer.Deserialize<BindingConfig>(File.ReadAllText(ConfigPath));
            }
            catch { }
            return new BindingConfig();
        }

        public void Save()
        {
            var dir = Path.GetDirectoryName(ConfigPath);
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(ConfigPath, json);
        }
    }
}
