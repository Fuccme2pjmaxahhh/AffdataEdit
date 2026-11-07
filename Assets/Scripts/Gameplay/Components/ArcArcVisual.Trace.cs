using Arcade.Gameplay.Chart;
using UnityEngine;

namespace Arcade.Gameplay
{
    internal sealed partial class ArcArcVisual
    {
        private int traceTiming = int.MinValue;
        private bool traceNoClip;
        private uint traceSeekVersion;
        private Vector3 traceJudgePoint;
        private bool hasTraceJudgePoint;
        private bool traceCrossing;
        private float traceCapFade = -1;

        private void ResetTraceState()
        {
            traceTiming = int.MinValue;
            traceJudgePoint = Vector3.zero;
            hasTraceJudgePoint = false;
            traceCrossing = false;
            arcCapEnable = false;
            traceCapFade = -1;
        }

        private void UpdateTrace()
        {
            var gameplay = ArcGameplayManager.Instance;
            int now = gameplay.ChartTiming;
            var timing = ArcTimingManager.Instance;
            bool noClip = arc.TimingGroup?.NoClip ?? false;
            float velocity = timing.CalculateVelocityAtTiming(arc.Timing, arc.TimingGroup);
            float direction = velocity < 0 ? -1 : 1;
            bool restore = traceTiming == int.MinValue || now < traceTiming
                || traceNoClip != noClip || traceSeekVersion != gameplay.SeekVersion;
            if (restore)
            {
                arcCapEnable = false;
                traceCapFade = -1;
                hasTraceJudgePoint = false;
                foreach (var segment in segments)
                {
                    segment.TraceHidden = false;
                    segment.TraceClippedStart = null;
                }
                // A visual is pooled when offscreen. Reconstruct consumed geometry on rent
                // or seek, including extrema crossed while no visual existed.
                if (!noClip && now > arc.Timing)
                {
                    float furthest = 0;
                    foreach (var point in timing.GetTiming(arc.TimingGroup))
                    {
                        if (point.Timing <= arc.Timing) continue;
                        if (point.Timing >= now) break;
                        float root = timing.CalculatePositionByTimingAndStart(arc.Timing, point.Timing, arc.TimingGroup) / 1000f;
                        if (root * direction > furthest * direction) furthest = root;
                    }
                    if (RootZ * direction > furthest * direction) furthest = RootZ;
                    ClipTrace(now, furthest, velocity, direction);
                    float lastCut = arc.NoInput() ? RootZ
                        : direction < 0 ? Mathf.Min(RootZ, -.01f) : Mathf.Max(RootZ, .01f);
                    if (arc.NoInput() || furthest * direction >= .01f)
                    {
                        if (!arc.NoInput()) hasTraceJudgePoint = false;
                        ClipTrace(now, lastCut, velocity, direction);
                    }
                }
            }
            if (!noClip) ClipTrace(now, RootZ, velocity, direction);
            traceJudgePoint = ResolveTraceJudgePoint(now, RootZ, out traceCrossing);
            traceTiming = now;
            traceNoClip = noClip;
            traceSeekVersion = gameplay.SeekVersion;

            // A 1 ms stationary carrier with exactly one arctap has no body or head.
            bool carrier = arc.EndTiming - arc.Timing == 1 && arc.ArcTaps.Count == 1
                && arc.XStart == arc.XEnd && arc.YStart == arc.YEnd;
            foreach (var segment in segments)
            {
                segment.IsTrace = true;
                segment.UseGoldTrace = UsesGoldTrace;
                segment.HighColor = SegmentColor(currentHighColor);
                segment.LowColor = SegmentColor(currentLowColor);
                // Same far limit as solid Arcs, measured along the drawn part of the segment.
                Vector3 start = segment.TraceClippedStart ?? segment.FromPos;
                float pos = -(RootZ + start.z), endPos = -(RootZ + segment.ToPos.z);
                float farClip = pos == endPos ? 0 : Mathf.Clamp01((100 - pos) / (endPos - pos));
                segment.From = pos > 100 ? farClip : 0;
                segment.To = endPos > 100 ? farClip : 1;
                segment.Enable = !carrier && (noClip || !segment.TraceHidden)
                    && Mathf.Min(pos, endPos) <= 100 && Mathf.Max(pos, endPos) >= -100;
                segment.DrawShadow = velocity > 0;
            }

            heightIndicatorEnable = false;
            headEnable = !carrier && arc.RenderHead && segments.Count > 0
                && (noClip || !segments[0].TraceHidden);
            if (headEnable)
            {
                HeadOffset = (noClip ? segments[0].FromPos
                    : segments[0].TraceClippedStart ?? segments[0].FromPos) - segments[0].FromPos;
                HeadHighColor = currentHighColor;
                HeadLowColor = currentLowColor;
            }
            UpdateTraceCap(now, timing);
        }

        private Vector3 ResolveTraceJudgePoint(int now, float root, out bool crossing)
        {
            // Keep the last sampled intersection after the note ends. A 1 ms trace can
            // pass entirely between frames: its retained X/Y is the wing left at a stop.
            if (now > arc.EndTiming && hasTraceJudgePoint)
            {
                crossing = false;
                return traceJudgePoint;
            }
            if (now > arc.EndTiming)
                root = ArcTimingManager.Instance.CalculatePositionByTimingAndStart(
                    arc.Timing, arc.EndTiming, arc.TimingGroup) / 1000f;
            traceJudgePoint = FindTraceJudgePoint(now, root, out crossing);
            hasTraceJudgePoint = true;
            return traceJudgePoint;
        }

        private Vector3 FindTraceJudgePoint(int now, float root, out bool crossing)
        {
            Vector3 point = traceJudgePoint;
            crossing = false;
            foreach (var segment in segments)
            {
                float from = root + segment.FromPos.z;
                float to = root + segment.ToPos.z;
                if (to > from) { from = -from; to = -to; }
                float t = 1f - to / (to - from);
                float clamped = float.IsNaN(t) ? 0 : Mathf.Clamp01(t);
                point = from < 0 ? segment.FromPos
                    : Vector3.LerpUnclamped(segment.FromPos, segment.ToPos, clamped);
                crossing = now >= arc.Timing && now <= arc.EndTiming && t >= 0 && t <= 1;
                if (crossing || from <= 0) break;
            }
            return point;
        }

        private void ClipTrace(int now, float root, float velocity, float direction)
        {
            if (now <= arc.Timing) return;
            Vector3 point = ResolveTraceJudgePoint(now, root, out _);
            bool clipping = arc.NoInput() || root * direction >= .01f;
            bool cutStart = false;
            foreach (var segment in segments)
            {
                if (segment.TraceHidden) continue;
                float endZ = root + segment.ToPos.z;
                if (clipping && !cutStart)
                {
                    if (endZ * direction >= .1f * direction)
                        segment.TraceHidden = true;
                    else
                    {
                        // Retain the cut in the body's local coordinates when scrolling reverses.
                        segment.TraceClippedStart = new Vector3(point.x, point.y, -root);
                        cutStart = true;
                    }
                }
                segment.TraceHidden |= velocity > 0 && endZ > 7;
            }
        }

        private void UpdateTraceCap(int now, ArcTimingManager timing)
        {
            if (now > arc.EndTiming)
            {
                // Connected successors take over the cap; only chain endpoints fade out.
                if (arcCapEnable && traceCapFade < 0)
                {
                    if (arc.RenderEndTiming > arc.EndTiming || HasTraceSuccessor()) arcCapEnable = false;
                    else traceCapFade = 0;
                }
                if (traceCapFade >= 0)
                {
                    float previous = 1 - traceCapFade / .07f;
                    traceCapFade += Time.deltaTime;
                    float opacity = Mathf.Max(0, 1 - traceCapFade / .07f);
                    arcCapColor.a = previous > 0 ? arcCapColor.a * opacity / previous : 0;
                    arcCapEnable = opacity > 0;
                }
                return;
            }
            traceCapFade = -1;
            float endZ = -timing.CalculatePositionByTiming(arc.EndTiming, arc.TimingGroup) / 1000f;
            if ((int)(endZ * 100f) > 0 || !traceCrossing) return;
            float progress = Mathf.Clamp01(1 + (int)(RootZ * 100f) / 10000f);
            float size = .21f + .5f * (1 - progress);
            Sprite sprite = Skin.ArcCapRenderer.sprite;
            arcCapEnable = true;
            arcCapColor = Color.white;
            arcCapColor.a = (int)(progress * 255f * .5f) / 255f;
            arcCapScale = new Vector3(size * 256 / sprite.rect.width, size * 256 / sprite.rect.height, 1);
            arcCapPosition = Parent.MultiplyPoint3x4(new Vector3(traceJudgePoint.x, traceJudgePoint.y, -RootZ));
            arcCapOrder = -1;
            JudgePosition = arcCapPosition;
        }

        private bool HasTraceSuccessor()
        {
            if (arc.ArcGroup == null) return false;
            foreach (var next in arc.ArcGroup)
                if (next != arc && next.IsVoid && System.Math.Abs((long)arc.EndTiming - next.Timing) <= 9
                    && Mathf.Abs(arc.XEnd - next.XStart) < .1f && Mathf.Abs(arc.YEnd - next.YStart) < .01f)
                    return true;
            return false;
        }
    }
}
