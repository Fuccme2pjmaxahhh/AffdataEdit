using System;
using Arcade.Gameplay;
using Arcade.Gameplay.Chart;
using UnityEngine;

namespace Arcade.Compose.Editing
{
    // Rectangular XY region extruded through a chart-time interval. Intersections
    // are evaluated at matching times, rather than against a loose path bounding box.
    public static class AdeSelectionVolume
    {
        public static bool Intersects(ArcNote note, int from, int to, Rect region)
        {
            int start = Math.Min(from, to), end = Math.Max(from, to);
            int noteEnd = note is ArcLongNote longNote ? longNote.EndTiming : note.Timing;
            if (note.Timing > end || noteEnd < start) return false;
            float lo = noteEnd == note.Timing ? 0 : Mathf.Clamp01((start - (float)note.Timing) / ((long)noteEnd - note.Timing));
            float hi = noteEnd == note.Timing ? 0 : Mathf.Clamp01((end - (float)note.Timing) / ((long)noteEnd - note.Timing));
            if (note is ArcArc arc)
                return Clip(t => ArcAlgorithm.X(arc.XStart, arc.XEnd, t, arc.CurveType), region.xMin, region.xMax, ref lo, ref hi)
                    && Clip(t => ArcAlgorithm.Y(arc.YStart, arc.YEnd, t, arc.CurveType), region.yMin, region.yMax, ref lo, ref hi);
            if (note is ArcArcTap tap)
            {
                var parent = tap.Arc;
                float t = parent.EndTiming == parent.Timing ? 0 : (tap.Timing - (float)parent.Timing) / ((long)parent.EndTiming - parent.Timing);
                return Contains(region, ArcAlgorithm.X(parent.XStart, parent.XEnd, t, parent.CurveType),
                    ArcAlgorithm.Y(parent.YStart, parent.YEnd, t, parent.CurveType));
            }
            if (note is ArcSlide slide)
            {
                float y = ArcAlgorithm.WorldYToArc(slide.WorldHeight);
                return y >= region.yMin && y <= region.yMax
                    && Clip(t => .25f + .5f * slide.Range(t).x, float.NegativeInfinity, region.xMax, ref lo, ref hi)
                    && Clip(t => .25f + .5f * slide.Range(t).y, region.xMin, float.PositiveInfinity, ref lo, ref hi);
            }
            if (note is ArcTap groundTap)
                return Contains(region, ArcAlgorithm.WorldXToArc(LanePosition.WorldX(groundTap.Track, groundTap.FloatLane)), ArcAlgorithm.WorldYToArc(0));
            if (note is ArcHold hold)
                return Contains(region, ArcAlgorithm.WorldXToArc(LanePosition.WorldX(hold.Track, hold.FloatLane)), ArcAlgorithm.WorldYToArc(0));
            return false;
        }

        private static bool Contains(Rect region, float x, float y) => x >= region.xMin && x <= region.xMax && y >= region.yMin && y <= region.yMax;

        // Arc and Slide boundary easing functions are individually monotone.
        private static bool Clip(Func<float, float> value, float min, float max, ref float lo, ref float hi)
        {
            float a = value(lo), b = value(hi);
            if (Mathf.Max(a, b) < min || Mathf.Min(a, b) > max) return false;
            bool increasing = b >= a;
            if (a < min || a > max) lo = Crossing(value, lo, hi, a < min ? min : max, increasing);
            if (b < min || b > max) hi = Crossing(value, lo, hi, b < min ? min : max, increasing);
            return lo <= hi;
        }

        private static float Crossing(Func<float, float> value, float lo, float hi, float target, bool increasing)
        {
            for (int i = 0; i < 32; i++)
            {
                float mid = (lo + hi) * .5f;
                if ((value(mid) < target) == increasing) lo = mid;
                else hi = mid;
            }
            return (lo + hi) * .5f;
        }
    }
}
