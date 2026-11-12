using System.Globalization;

namespace Arcade.Aff
{
    // Shared by AFF output and the Arc/Slide editors so rounding and display agree.
    public static class AffCoordinate
    {
        public static string Format(float value, bool lowPrecision = false)
            => value.ToString(lowPrecision ? "f2" : "f3", CultureInfo.InvariantCulture);

        public static float Round(float value, bool lowPrecision = false)
            => float.Parse(Format(value, lowPrecision), CultureInfo.InvariantCulture);
    }
}
