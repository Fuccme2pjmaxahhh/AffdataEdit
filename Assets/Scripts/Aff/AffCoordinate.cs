using System.Globalization;

namespace Arcade.Aff
{
    // Shared by AFF output and the Arc/Slide editors so rounding and display agree.
    public static class AffCoordinate
    {
        public static string Format(float value, bool lowPrecision = false)
        {
            string text = value.ToString(lowPrecision ? "0.##" : "0.###", CultureInfo.InvariantCulture);
            return text == "-0" ? "0" : text;
        }

        public static float Round(float value, bool lowPrecision = false)
            => float.Parse(Format(value, lowPrecision), CultureInfo.InvariantCulture);
    }
}
