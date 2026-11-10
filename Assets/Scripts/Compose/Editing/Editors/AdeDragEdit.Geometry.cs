using System;
using Arcade.Gameplay;
using Arcade.Gameplay.Chart;
using Arcade.Compose.UI;
using UnityEngine;

namespace Arcade.Compose.Editing
{
    public sealed partial class AdeDragEdit
    {
        private static readonly Plane Ground = new Plane(Vector3.up, Vector3.zero);

        private static bool TryGroundTiming(Ray ray, int reference, out int timing, out Vector3 point)
        {
            timing = reference; point = default;
            if (!Ground.Raycast(ray, out float distance)) return false;
            point = ray.GetPoint(distance);
            return ArcTimingManager.Instance.TryCalculateChartTimingByPosition(-point.z * 1000,
                AdeTimingEditor.Instance.currentTimingGroup, reference, out timing);
        }

        internal static bool TryChooseEndpoint(ArcNote target, DragMode mode, Vector2 pointer, out bool isEnd)
        {
            isEnd = false;
            if (!(target is ArcLongNote longNote)) return true;
            var camera = AdeCursorManager.Instance.GameplayCamera;
            if (!camera) return false;
            if (mode == DragMode.Position && target is ArcArc arc)
            {
                Vector3 start = EndpointPosition(arc, false), end = EndpointPosition(arc, true);
                if (Mathf.Abs(start.z - end.z) > .0001f && ArcNoteRenderer.TryPickPoint(camera, pointer, arc, out var point))
                    isEnd = Mathf.Abs(point.z - end.z) <= Mathf.Abs(point.z - start.z);
                else
                {
                    return TryChooseScreenEndpoint(camera, pointer, start, end, out isEnd);
                }
                return true;
            }
            var group = ((IHasTimingGroup)target).TimingGroup;
            Ray ray = camera.ScreenPointToRay(pointer);
            if (!Ground.Raycast(ray, out float enter)) return false;
            int reference = (int)(((long)target.Timing + longNote.EndTiming) / 2);
            if (!ArcTimingManager.Instance.TryCalculateChartTimingByPosition(-ray.GetPoint(enter).z * 1000, group, reference, out int timing)) return false;
            isEnd = (long)timing - target.Timing >= (long)longNote.EndTiming - timing;
            return true;
        }

        internal static bool TryChooseScreenEndpoint(Camera camera, Vector2 pointer, Vector3 first, Vector3 second, out bool chooseSecond)
        {
            Vector3 a = camera.WorldToScreenPoint(first), b = camera.WorldToScreenPoint(second);
            chooseSecond = a.z <= 0 || (b.z > 0 && ((Vector2)b - pointer).sqrMagnitude <= ((Vector2)a - pointer).sqrMagnitude);
            return a.z > 0 || b.z > 0;
        }

        internal static bool TryChooseSlideCorner(ArcSlide slide, Vector2 pointer, out bool isEnd, out bool isRight)
        {
            var camera = AdeCursorManager.Instance.GameplayCamera;
            isEnd = isRight = false;
            if (!camera) return false;
            float best = float.PositiveInfinity;
            for (int i = 0; i < 4; i++)
            {
                var point = EndpointPosition(slide, i >= 2);
                point.x = slide.WorldX(AdeSlideEditor.EditingRange(slide, i >= 2)[i % 2]);
                if (!IsVisibleSlideEndpoint(camera, point)) continue;
                float distance = ((Vector2)camera.WorldToScreenPoint(point) - pointer).sqrMagnitude;
                if (distance >= best) continue;
                best = distance; isEnd = i >= 2; isRight = i % 2 == 1;
            }
            return !float.IsPositiveInfinity(best);
        }

        internal static bool IsVisibleSlideEndpoint(Camera camera, Vector3 point)
        {
            var screen = camera.WorldToViewportPoint(point);
            return Mathf.Abs(point.z) <= ArcSlideVisual.RenderDistance && screen.z >= camera.nearClipPlane
                && screen.z <= camera.farClipPlane && screen.x >= 0 && screen.x <= 1 && screen.y >= 0 && screen.y <= 1;
        }

        internal static bool TryChooseGroupAnchor(ArcNote[] targets, Camera camera, Vector2 pointer, out Vector3 anchor, out int timing, out ArcTimingGroup anchorGroup)
        {
            anchor = default; timing = 0; anchorGroup = null;
            if (!camera) return false;
            float best = float.PositiveInfinity;
            foreach (var target in targets)
            {
                if (((IHasTimingGroup)target).GroupHide()) continue;
                int count = target is ArcSlide ? 4 : target is ArcLongNote ? 2 : 1;
                for (int i = 0; i < count; i++)
                {
                    bool end = count > 1 && i >= count / 2;
                    var point = EndpointPosition(target, end);
                    if (target is ArcSlide slide) point.x = slide.WorldX(AdeSlideEditor.EditingRange(slide, end)[i % 2]);
                    if (!IsVisibleSlideEndpoint(camera, point)) continue;
                    float distance = ((Vector2)camera.WorldToScreenPoint(point) - pointer).sqrMagnitude;
                    if (distance >= best) continue;
                    best = distance; anchor = point; timing = end ? ((ArcLongNote)target).EndTiming : target.Timing;
                    anchorGroup = ((IHasTimingGroup)target).TimingGroup;
                }
            }
            return !float.IsPositiveInfinity(best);
        }

        private Vector3 DragEndpointPosition()
        {
            var point = EndpointPosition(note, end);
            if (activeMode == DragMode.Position && note is ArcSlide slide)
                point.x = slide.WorldX(AdeSlideEditor.EditingRange(slide, end)[right ? 1 : 0]);
            return point;
        }

        internal static Vector3 EndpointPosition(ArcNote target, bool isEnd)
        {
            int timing = isEnd ? ((ArcLongNote)target).EndTiming : target.Timing;
            var group = target is ArcArcTap arcTap ? arcTap.Arc.TimingGroup : ((IHasTimingGroup)target).TimingGroup;
            float z = -ArcTimingManager.Instance.CalculatePositionByTiming(timing, group) / 1000;
            if (target is ArcArc arc)
                return new Vector3(ArcAlgorithm.ArcXToWorld(isEnd ? arc.XEnd : arc.XStart), ArcAlgorithm.ArcYToWorld(isEnd ? arc.YEnd : arc.YStart), z);
            if (target is ArcSlide slide) return new Vector3(slide.WorldX(isEnd ? slide.EndCenter : slide.StartCenter), slide.WorldHeight, z);
            if (target is ArcHold hold) return new Vector3(hold.WorldX, 0, z);
            if (target is ArcTap tap) return new Vector3(tap.WorldX, 0, z);
            var skyTap = (ArcArcTap)target;
            float progress = skyTap.Arc.EndTiming == skyTap.Arc.Timing ? 0 : (timing - skyTap.Arc.Timing) / (float)(skyTap.Arc.EndTiming - skyTap.Arc.Timing);
            return new Vector3(ArcAlgorithm.ArcXToWorld(ArcAlgorithm.X(skyTap.Arc.XStart, skyTap.Arc.XEnd, progress, skyTap.Arc.CurveType)),
                ArcAlgorithm.ArcYToWorld(ArcAlgorithm.Y(skyTap.Arc.YStart, skyTap.Arc.YEnd, progress, skyTap.Arc.CurveType)), z);
        }

        private static bool Changed(ArcNote a, ArcNote b)
        {
            if (a.Timing != b.Timing || a is ArcLongNote first && b is ArcLongNote second && first.EndTiming != second.EndTiming) return true;
            if (a is ArcArc arc && b is ArcArc other)
                return arc.XStart != other.XStart || arc.XEnd != other.XEnd || arc.YStart != other.YStart || arc.YEnd != other.YEnd;
            if (a is ArcTap tap && b is ArcTap otherTap) return tap.Track != otherTap.Track || tap.FloatLane != otherTap.FloatLane;
            if (a is ArcHold hold && b is ArcHold otherHold) return hold.Track != otherHold.Track || hold.FloatLane != otherHold.FloatLane;
            return a is ArcSlide slide && b is ArcSlide otherSlide && (slide.StartCenter != otherSlide.StartCenter || slide.EndCenter != otherSlide.EndCenter
                || slide.StartWidth != otherSlide.StartWidth || slide.EndWidth != otherSlide.EndWidth);
        }
    }
}
