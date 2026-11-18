using System.Globalization;
using UnityEngine;

namespace Arcade.Gameplay.Chart
{
    // AFF distinguishes fixed integer tracks from normalized decimal lane positions.
    public static class LanePosition
    {
        public static bool TryParse(string text, out int track, out float? lane)
        {
            lane = null;
            if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out track))
                return track >= 0 && track <= 5;
            if (!float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
                || float.IsNaN(value) || float.IsInfinity(value)) return false;
            lane = value;
            track = Mathf.Clamp(Mathf.FloorToInt(value * 4) + 1, 0, 5);
            return true;
        }

        public static float WorldX(int track, float? lane) => lane.HasValue
            ? 8.5f - 17f * lane.Value : 10.625f - 4.25f * track;

        internal static float FloatLaneFromWorldX(float x) => (8.5f - x) / 17f;

        public static string Format(int track, float? lane) => lane.HasValue
            ? lane.Value.ToString("0.0########", CultureInfo.InvariantCulture)
            : track.ToString(CultureInfo.InvariantCulture);
    }
}
