using System;
using System.Globalization;
using System.IO;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Arcade.Compose
{
    // Small metadata conversion and atomic output, independent of editor UI or chart rendering.
    public static class AdeSongListExporter
    {
        public static JObject Build(ArcadeProjectMetadata project, string folder, int first, int second,
            int audioOffset, int side, string background)
        {
            if (project == null) throw new ArgumentNullException(nameof(project));
            long start = Math.Min((long)first, second) + audioOffset;
            long end = Math.Max((long)first, second) + audioOffset;
            if (start < 0 || end <= start || end > int.MaxValue)
                throw new ArgumentException("预览范围须在音频时间 0 ms 之后，且终点晚于起点。");
            string id = Identifier(project.Title);
            if (id.Length == 0) id = Identifier(new DirectoryInfo(folder).Name);
            if (id.Length == 0) throw new ArgumentException("曲名和文件夹名无法生成歌曲 ID，请使用含英文字母或数字的文件夹名。");
            float bpm = ValidBpm(project.BaseBpm);
            background = (background ?? (side == 1 || side == 4 ? "base_conflict" : "base_light")).TrimStart('*');
            var difficulties = new JArray();
            for (int i = 0; i < AdeProjectManager.DIFFICULTY_COUNT; i++)
            {
                var metadata = project.Difficulties != null && i < project.Difficulties.Length ? project.Difficulties[i] : null;
                if (i >= 3 && metadata == null && !File.Exists(Path.Combine(folder, i + ".aff"))) continue;
                // Ratings are free text such as "Future 10+"; the level is the last word.
                string ratingText = (metadata?.Rating ?? "").Trim();
                int space = ratingText.LastIndexOf(' ');
                if (space >= 0) ratingText = ratingText.Substring(space + 1);
                bool plus = ratingText.EndsWith("+", StringComparison.Ordinal);
                if (!int.TryParse(plus ? ratingText.Substring(0, ratingText.Length - 1) : ratingText,
                    NumberStyles.Integer, CultureInfo.InvariantCulture, out int rating) || rating < 0) rating = -1;
                var difficulty = new JObject
                {
                    ["ratingClass"] = i, ["rating"] = rating,
                    ["chartDesigner"] = "", ["jacketDesigner"] = ""
                };
                if (plus && rating >= 0) difficulty["ratingPlus"] = true;
                if (rating >= 11) difficulty["legacy11"] = true;
                bool audioOverride = Exists(folder, i, ".ogg", ".mp3", ".wav", ".opus");
                if (audioOverride)
                {
                    difficulty["audioOverride"] = true;
                    float difficultyBpm = metadata != null && metadata.BaseBpm > 0 ? ValidBpm(metadata.BaseBpm) : bpm;
                    difficulty["bpm_base"] = difficultyBpm;
                    difficulty["bpm"] = difficultyBpm.ToString(CultureInfo.InvariantCulture);
                }
                if (Exists(folder, i, ".jpg") || File.Exists(Path.Combine(folder, "1080_" + i + ".jpg"))
                    || File.Exists(Path.Combine(folder, i + "_256.jpg")) || File.Exists(Path.Combine(folder, "1080_" + i + "_256.jpg")))
                    difficulty["jacketOverride"] = true;
                difficulties.Add(difficulty);
            }
            var song = new JObject
            {
                ["id"] = id, ["title_localized"] = new JObject { ["en"] = project.Title ?? "" },
                ["artist"] = project.Artist ?? "", ["bpm"] = bpm.ToString(CultureInfo.InvariantCulture),
                ["bpm_base"] = bpm, ["set"] = "base", ["purchase"] = "",
                ["audioPreview"] = start, ["audioPreviewEnd"] = end, ["side"] = side,
                ["bg"] = background, ["date"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                ["version"] = "", ["difficulties"] = difficulties
            };
            string inverse = InverseBackground(background);
            if (inverse != null) song["bg_inverse"] = inverse;
            return new JObject { ["songs"] = new JArray(song) };
        }

        private static float ValidBpm(float bpm) => float.IsNaN(bpm) || float.IsInfinity(bpm) || bpm <= 0 ? 100 : bpm;
        private static bool Exists(string folder, int difficulty, params string[] extensions)
        {
            foreach (string extension in extensions)
                if (File.Exists(Path.Combine(folder, difficulty + extension))) return true;
            return false;
        }
        private static string Identifier(string text)
        {
            var id = new StringBuilder();
            foreach (char character in (text ?? "").ToLowerInvariant())
                if (character >= 'a' && character <= 'z' || character >= '0' && character <= '9' || character == '_') id.Append(character);
            return id.ToString();
        }
        private static string InverseBackground(string name)
        {
            if (name == "rei") return "yugamu";
            if (name == "yugamu") return "rei";
            string[] names = { "light", "conflict", "solarsphere", "darksphere" };
            for (int i = 0; i < names.Length; i++)
                foreach (char separator in new[] { '_', '-' })
                    if (name.EndsWith(separator + names[i], StringComparison.Ordinal))
                        return name.Substring(0, name.Length - names[i].Length) + names[i ^ 1];
            return null;
        }

        public static void Write(string folder, JObject songList, bool overwrite)
        {
            string output = Path.Combine(folder, "songlist.json");
            string json = songList.ToString(Formatting.Indented);
            string temporary = Path.Combine(folder, ".songlist-" + Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                File.WriteAllText(temporary, json, new UTF8Encoding(false));
                if (overwrite && File.Exists(output)) File.Replace(temporary, output, output + ".bak");
                else File.Move(temporary, output);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
    }
}
