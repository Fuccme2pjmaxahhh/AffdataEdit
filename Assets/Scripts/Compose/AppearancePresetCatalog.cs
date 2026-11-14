using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using UnityEngine;

namespace Arcade.Compose
{
    [Serializable]
    public sealed class AppearancePreset
    {
        public string Name;
        public string Background;
        public string Diamond;
        public string Track;
        public string CriticalLine;
        public string SingleLine;
        public string ComboColor;
        public int Note;
        public string Particle;
        public string HitEffect;
        public int Side;
    }

    // This catalog retains paths and small preset records, never decoded image data.
    public static class AppearancePresetCatalog
    {
        private static IReadOnlyList<AppearancePreset> presets;
        private static Dictionary<string, AppearancePreset> byName;

        public static string FolderPath => Path.Combine(Application.streamingAssetsPath, "Appearance");

        public static IReadOnlyList<AppearancePreset> All
        {
            get
            {
                EnsureLoaded();
                return presets;
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        public static void Reload()
        {
            presets = null;
            byName = null;
        }

        public static bool TryGet(string name, out AppearancePreset preset)
        {
            EnsureLoaded();
            preset = null;
            return name != null && byName.TryGetValue(name, out preset);
        }

        public static string BackgroundPath(string id)
        {
            if (string.IsNullOrEmpty(id) || Path.GetFileName(id) != id || id.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                return null;
            string path = Path.Combine(FolderPath, "Backgrounds", id + ".jpg");
            return File.Exists(path) ? path : null;
        }

        private static void EnsureLoaded()
        {
            if (presets != null) return;
            var records = new List<AppearancePreset>();
            byName = new Dictionary<string, AppearancePreset>(StringComparer.Ordinal);
            string path = Path.Combine(FolderPath, "Presets.json");
            try
            {
                var loaded = JsonConvert.DeserializeObject<List<AppearancePreset>>(File.ReadAllText(path));
                if (loaded == null) throw new JsonSerializationException("The appearance preset list is empty.");
                foreach (var item in loaded)
                {
                    if (item == null || string.IsNullOrEmpty(item.Name) || string.IsNullOrEmpty(item.Background))
                        throw new JsonSerializationException("An appearance preset has no name or background.");
                    if (byName.ContainsKey(item.Name))
                        throw new JsonSerializationException("Duplicate appearance preset: " + item.Name);
                    byName.Add(item.Name, item);
                    records.Add(item);
                }
            }
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException || error is JsonException)
            {
                records.Clear();
                byName.Clear();
                Debug.LogError("Cannot load appearance presets from " + path + ": " + error.Message);
            }
            presets = records.AsReadOnly();
        }
    }
}
