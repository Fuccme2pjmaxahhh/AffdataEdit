using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using Arcade.Compose.Dialog;
using Arcade.Compose.Feature;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Arcade.Compose
{
    // The keys accepted by this facade are the old PlayerPrefs keys. Their values are
    // stored as readable JSON sections, rather than escaped JSON inside JSON strings.
    public static class AdeSettings
    {
        private sealed class Entry
        {
            public readonly string Key, Name, LegacyFile;
            public readonly Type Type;
            public Entry(string key, string name, Type type, string legacyFile = null)
            {
                Key = key; Name = name; Type = type; LegacyFile = legacyFile;
            }

            public JToken Parse(string value)
            {
                if (Type == typeof(string)) return new JValue(value);
                if (Type == typeof(JObject) && string.IsNullOrEmpty(value)) return new JObject { ["bindings"] = new JArray() };
                JToken parsed = JToken.Parse(value);
                if (parsed.Type != JTokenType.Object)
                    throw new JsonSerializationException("Expected an object for " + Name);
                // Check the existing model before deleting the source of the migration.
                parsed.ToObject(Type);
                return parsed;
            }
        }

        private static readonly Entry[] Entries =
        {
            new Entry("ArcadeComposeManagerPreference", "general", typeof(ArcadePreference), "Preferences.json"),
            new Entry(AdeWindowState.SettingsKey, "window", typeof(AdeWindowPlacement)),
            new Entry("AdeSoundDialog", "sound", typeof(SoundPreferences), "Sound.json"),
            new Entry("AdeGridManager", "grid", typeof(CustomGrid), "Grid.json"),
            new Entry("AdeSkinDialog", "skin", typeof(SkinPreference), "Skin.json"),
            new Entry("AdeOBSDialog", "obs", typeof(OBSPreferences), "OBS.json"),
            new Entry("hotkeyRebinding", "hotkeys", typeof(JObject)),
            new Entry(AdeHotkeyPresets.SettingsKey, "hotkeyPresets", typeof(JObject)),
            new Entry("AffdataEdit.DarkUi", "darkMode", typeof(int)),
            new Entry("AffdataEdit.DragMode", "dragMode", typeof(int)),
            new Entry("AffdataEdit.UseChartTiming", "useChartTiming", typeof(int)),
            new Entry("AffdataEdit.EditorTheme.Dark", "darkTheme", typeof(string)),
            new Entry("AffdataEdit.EditorTheme.Light", "lightTheme", typeof(string)),
            new Entry("AffdataEdit.WindowsAudioOutput", "windowsAudioOutput", typeof(int)),
            new Entry("AffdataEdit.WindowsAudioBufferMs", "windowsAudioBufferMs", typeof(int)),
            new Entry("AdeCloudRealtime", "cloudRealtime", typeof(int))
        };

        public static string FilePath => AdeStorage.SettingsPath;

        private static AdeSettingsFile file;
        private static AdeSettingsFile account;
        private static bool canSave;
        private static Action pendingCleanup;
        private static AdeLegacyPreferences legacy;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset()
        {
            file = null;
            account = null;
            canSave = false;
            pendingCleanup = null;
            legacy = null;
        }

        private static Entry Find(string key)
        {
            foreach (Entry entry in Entries) if (entry.Key == key) return entry;
            throw new ArgumentException("Unknown setting: " + key, nameof(key));
        }

        private static void EnsureLoaded()
        {
            if (file != null) return;
            file = new AdeSettingsFile(FilePath);
            try
            {
                if (Application.isEditor)
                {
                    string previous = Path.Combine(AdeStorage.DataFolder, "UserSettings", "settings.json");
                    AdeStorageMigration.MoveFile(previous, FilePath);
                    AdeStorageMigration.MoveFile(previous + ".bak", FilePath + ".bak");
                    string charts = Path.Combine(AdeStorage.DataFolder, "UserSettings", "Charts");
                    AdeStorageMigration.MoveDirectory(Path.Combine(charts, "Cloud"), AdeStorage.CloudChartsFolder);
                    AdeStorageMigration.MoveDirectory(Path.Combine(charts, "Remote"), AdeStorage.CloudChartsFolder);
                }
                file.Load();
                AdeStorage.PrepareAccountFolder();
                account = new AdeSettingsFile(AdeStorage.AccountPath);
                account.Load();
                legacy = new AdeLegacyPreferences();
                canSave = true;
            }
            catch (Exception error) when (IsStorageError(error))
            {
                Report("读取失败，原文件已保留，本次不会覆盖它", error);
            }

            if (!canSave || file.Get("migrationVersion")?.Value<int>() == 2) return;

            string legacyFolder = AdeStorage.LegacyFolder;
            var imported = new Dictionary<string, JToken>();
            var oldFiles = new List<string>();
            bool migrationValid = true;
            foreach (Entry entry in Entries)
            {
                string oldFile = entry.LegacyFile == null ? null : Path.Combine(legacyFolder, entry.LegacyFile);
                bool hasFile = oldFile != null && File.Exists(oldFile);
                bool hasKey = legacy.HasKey(entry.Key);
                if (!hasFile && !hasKey) continue;
                try
                {
                    // Match the previous loaders: legacy JSON takes precedence over PlayerPrefs.
                    JToken value = hasFile ? entry.Parse(File.ReadAllText(oldFile))
                        : entry.Type == typeof(int) ? new JValue(legacy.GetInt(entry.Key))
                        : entry.Parse(legacy.GetString(entry.Key));
                    imported.Add(entry.Name, value);
                    if (hasFile) oldFiles.Add(oldFile);
                }
                catch (Exception error) when (IsStorageError(error))
                {
                    migrationValid = false;
                    Report("旧设置 " + entry.Name + " 迁移失败，源数据已保留", error);
                }
            }
            pendingCleanup = () =>
            {
                var login = new Dictionary<string, JToken>();
                if (legacy.HasKey("AffdataNetAccount"))
                {
                    var session = JObject.Parse(legacy.GetString("AffdataNetAccount"));
                    if (session["Token"]?.Type != JTokenType.String || session["Username"]?.Type != JTokenType.String
                        || session["ExpiresAt"] == null)
                        throw new JsonSerializationException("Invalid legacy account session; PlayerPrefs will be retained.");
                    session["ExpiresAt"].Value<DateTime>();
                    login.Add("session", session);
                }
                if (legacy.HasKey("AffdataNetAccountUser"))
                    login.Add("username", new JValue(legacy.GetString("AffdataNetAccountUser")));
                account.Migrate(login, () => { });
                AdeStorageMigration.MoveDirectory(Path.Combine(legacyFolder, "Cloud"), AdeStorage.CloudChartsFolder);
                // Also migrate the intermediate development layout before the Cloud rename.
                AdeStorageMigration.MoveDirectory(Path.Combine(AdeStorage.DataFolder, "Charts", "Remote"), AdeStorage.CloudChartsFolder);
                foreach (string oldFile in oldFiles) File.Delete(oldFile);
                // Settings, account and cloud files have all been verified at this point.
                legacy.Clear();
                file.Set("migrationVersion", new JValue(2));
                file.Save();
            };
            if (!canSave || !migrationValid)
            {
                // Do not let later default-value saves bypass a failed migration.
                canSave = false;
                foreach (var entry in imported)
                    if (file.Get(entry.Key) == null) file.Set(entry.Key, entry.Value);
                return;
            }
            try
            {
                file.Migrate(imported, pendingCleanup);
                pendingCleanup = null;
            }
            catch (Exception error) when (IsStorageError(error))
            {
                Report("迁移尚未完成，将保留未清理的旧设置并在下次保存时重试", error);
            }
        }

        public static string GetString(string key, string defaultValue = "")
        {
            EnsureLoaded();
            Entry entry = Find(key);
            JToken value = file.Get(entry.Name);
            if (value == null) return defaultValue;
            return entry.Type == typeof(string) ? value.Value<string>() : value.ToString(Formatting.None);
        }

        public static int GetInt(string key, int defaultValue = 0)
        {
            EnsureLoaded();
            JToken value = file.Get(Find(key).Name);
            return value?.Type == JTokenType.Integer ? value.Value<int>() : defaultValue;
        }

        public static void SetString(string key, string value)
        {
            EnsureLoaded();
            Entry entry = Find(key);
            file.Set(entry.Name, entry.Parse(value));
            Save();
        }

        public static void SetInt(string key, int value)
        {
            EnsureLoaded();
            file.Set(Find(key).Name, new JValue(value));
            Save();
        }

        public static void Save()
        {
            EnsureLoaded();
            if (!canSave) return;
            try
            {
                if (pendingCleanup != null)
                {
                    file.Migrate(new Dictionary<string, JToken>(), pendingCleanup);
                    pendingCleanup = null;
                }
                else file.Save();
            }
            catch (Exception error) when (IsStorageError(error))
            {
                Report("保存失败，磁盘上的旧设置已保留", error);
            }
        }

        public static string GetAccountSession()
        {
            EnsureLoaded();
            return account?.Get("session")?.ToString(Formatting.None) ?? "";
        }

        public static string GetAccountUsername()
        {
            EnsureLoaded();
            return account?.Get("username")?.Value<string>() ?? "";
        }

        public static void SaveAccountSession(string session, string username)
        {
            EnsureLoaded();
            if (!canSave) throw new IOException("Account storage is unavailable: " + AdeStorage.AccountPath);
            account.Set("session", session == null ? JValue.CreateNull() : JObject.Parse(session));
            if (username != null) account.Set("username", new JValue(username));
            account.Save();
        }

        private static bool IsStorageError(Exception error) => error is IOException
            || error is UnauthorizedAccessException || error is JsonException
            || error is FormatException || error is OverflowException || error is InvalidCastException || error is Win32Exception;

        private static void Report(string operation, Exception error)
        {
            Debug.LogError("设置" + operation + "。\n" + FilePath + "\n" + error);
        }
    }
}
