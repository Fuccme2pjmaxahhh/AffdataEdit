using System.Collections.Generic;
using Affdata.Core.Geometry;
using Arcade.Gameplay.Chart;
using UnityEngine;
using UnityEngine.Rendering;

namespace Arcade.Gameplay
{
    // Render state of one Slide without a GameObject, as in AffdataPlay: a ribbon mesh relative to
    // its head, drawn with its shadow and entry brackets by ArcNoteRenderer.
    public sealed class ArcSlideVisual
    {
        public const float RenderDistance = 100f;
        public static bool IsWithinRenderDistance(float worldZ) => Mathf.Abs(worldZ) <= RenderDistance;
        private static readonly Vector3 BracketScale = Vector3.one * (8.5f / 1.62f);
        private ArcSlide note;
        private readonly Mesh mesh;
        private bool bodyVisible, shadowVisible;
        internal bool IsVisible => bodyVisible || shadowVisible || bracketVisible[0] || bracketVisible[1];
        private readonly bool[] bracketVisible = new bool[2];
        private readonly Vector3[] bracketPositions = new Vector3[2];
        private float startZ;
        private Color bodyColor;
        private Vector4 slideParameters, missingRange;
        private readonly List<Vector3> vertices = new List<Vector3>();
        private readonly List<Vector2> uv0 = new List<Vector2>(), uv1 = new List<Vector2>(), uv2 = new List<Vector2>();
        private readonly List<int> indices = new List<int>();
        private int lastStart = int.MinValue, signature;
        private bool lastConsumed;
        public ArcSlideVisual(ArcSlide slide)
        {
            mesh = new Mesh { name = "Slide ribbon", indexFormat = IndexFormat.UInt32 };
            Bind(slide);
        }
        internal void Bind(ArcSlide slide)
        {
            note = slide;
            lastStart = int.MinValue; signature = 0; lastConsumed = false;
            bodyVisible = shadowVisible = bracketVisible[0] = bracketVisible[1] = false;
        }
        internal void Unbind()
        {
            note = null;
            bodyVisible = shadowVisible = bracketVisible[0] = bracketVisible[1] = false;
        }
        public void Dispose()
        {
            note = null;
            if (Application.isPlaying) Object.Destroy(mesh);
            else Object.DestroyImmediate(mesh);
        }
        public void Invalidate() => lastStart = int.MinValue;
        public void Present(int now, bool consume)
        {
            if (!note.IsValid)
            {
                bodyVisible = shadowVisible = bracketVisible[0] = bracketVisible[1] = false;
                return;
            }
            var timing = ArcTimingManager.Instance;
            startZ = -timing.CalculatePositionByTimingAndStart(now, note.Timing, note.TimingGroup) / 1000f;
            bool visible = !note.GroupHide() && !(consume && now >= note.EndTiming);
            bodyVisible = shadowVisible = visible;
            var headRange = note.Range(0);
            for (int i = 0; i < bracketVisible.Length; i++)
            {
                bracketVisible[i] = visible && IsWithinRenderDistance(startZ) && note.IsGroupHead && (long)note.Timing - now > 5;
                bracketPositions[i] = new Vector3(note.WorldX(i == 0 ? headRange.x : headRange.y), .026f, 0);
            }
            if (!visible) return;
            int hash = System.HashCode.Combine(note.Timing, note.EndTiming, note.StartCenter, note.StartWidth,
                note.EndCenter, note.EndWidth, note.LeftCurve, note.RightCurve);
            hash = System.HashCode.Combine(hash, timing.BaseBpm, timing.SettingVelocity, note.Height);
            foreach (var point in timing.GetTiming(note.TimingGroup))
                hash = System.HashCode.Combine(hash, point.Timing, point.Bpm);
            int start = consume ? Mathf.Max(note.Timing, now) : note.Timing;
            if (lastStart != start || signature != hash || lastConsumed != consume)
            {
                Fill(start); lastStart = start; signature = hash; lastConsumed = consume;
            }
            // Use the whole sampled mesh, not just its endpoints: reversed BPM can bring
            // a middle section into view while both endpoints remain beyond the cutoff.
            bool inRange = mesh.bounds.max.z + startZ >= -RenderDistance
                && mesh.bounds.min.z + startZ <= RenderDistance;
            bodyVisible = inRange;
            shadowVisible = inRange && !note.IsFloor;
            if (!inRange) return;
            float visualStart = timing.CalculatePositionByTimingAndStart(0, note.Timing, note.TimingGroup) / 1000f * NoteMeshDefinitions.SlideVisualUnitsPerWorldUnit;
            float visualNow = timing.CalculatePositionByTimingAndStart(0, now, note.TimingGroup) / 1000f * NoteMeshDefinitions.SlideVisualUnitsPerWorldUnit;
            slideParameters = new Vector4(visualStart, visualNow, note.IsGroupHead ? 1 : 0, note.IsGroupTail ? 1 : 0);
            missingRange = note.Overlap;
            // Darker floor fill stays readable against the pale lanes; keep source edge/glow colors.
            bodyColor = note.IsFloor ? new Color(0.42f, 0.32f, 0.70f, 1f) : ArcSlideManager.Instance.BodyMaterial.GetColor("_Color");
        }
        private void Fill(int start)
        {
            vertices.Clear(); uv0.Clear(); uv1.Clear(); uv2.Clear(); indices.Clear();
            int duration = note.EndTiming - note.Timing;
            // Bound geometry for very long editor notes, while retaining the source's 5ms sampling for normal notes.
            int step = Mathf.Max(5, Mathf.CeilToInt(duration / 8192f));
            float length = ArcTimingManager.Instance.CalculatePositionByTimingAndStart(note.Timing, note.EndTiming, note.TimingGroup) / 1000f * NoteMeshDefinitions.SlideVisualUnitsPerWorldUnit;
            if (start < note.EndTiming) AddRow(start);
            for (int t = start; t < note.EndTiming;)
            {
                int next = (int)System.Math.Min((long)t + step, note.EndTiming);
                int i = vertices.Count - 2;
                AddRow(next);
                NoteMeshDefinitions.AppendSlideSurfaceSegment(indices, i, i + 2);
                t = next;
            }
            mesh.Clear(); mesh.SetVertices(vertices); mesh.SetUVs(0, uv0); mesh.SetUVs(1, uv1); mesh.SetUVs(2, uv2);
            mesh.SetTriangles(indices, 0); mesh.RecalculateBounds();
            void AddRow(int time)
            {
                float progress = (time - note.Timing) / (float)duration;
                Vector2 range = note.Range(progress);
                float z = -ArcTimingManager.Instance.CalculatePositionByTimingAndStart(note.Timing, time, note.TimingGroup) / 1000f;
                float distance = ArcTimingManager.Instance.CalculatePositionByTimingAndStart(note.Timing, time, note.TimingGroup) / 1000f * NoteMeshDefinitions.SlideVisualUnitsPerWorldUnit;
                NoteMeshDefinitions.AppendSlideSurfaceRow(vertices, uv0, uv1, uv2,
                    new Vector3(note.WorldX(range.x), 0, z), new Vector3(note.WorldX(range.y), 0, z),
                    range, progress, distance, length);
            }
        }
        internal void Submit(ArcNoteRenderer renderer)
        {
            var manager = ArcSlideManager.Instance;
            // Geometry is stored relative to the head; scrolling only moves this matrix.
            Matrix4x4 root = Matrix4x4.Translate(new Vector3(0, note.WorldHeight, startZ));
            if (bodyVisible)
                renderer.SubmitSlide(mesh, root, 6, bodyColor, slideParameters, missingRange, null,
                    note.IsFloor ? "Grid" : "Arc", note.IsFloor ? 1 : 0, manager.BodyMaterial.renderQueue, note.Selected, note);
            if (shadowVisible)
                renderer.SubmitSlide(mesh, root * Matrix4x4.Translate(new Vector3(0, -note.WorldHeight + 0.02f, 0)), 8,
                    ArcSkinManager.Instance.GetShadowTint(), Vector4.zero, Vector4.zero, null, "Shadow", 0, manager.ShadowMaterial.renderQueue, false);
            Material bracket = manager.BracketMaterial;
            for (int i = 0; i < bracketVisible.Length; i++)
            {
                if (!bracketVisible[i]) continue;
                var matrix = root * Matrix4x4.TRS(bracketPositions[i], Quaternion.Euler(0, i == 0 ? 180 : 0, 0), BracketScale);
                renderer.SubmitSlide(ArcNoteMeshes.Bracket, matrix, 7, bracket.HasColor("_Color") ? bracket.GetColor("_Color") : Color.white,
                    Vector4.zero, Vector4.zero, bracket.mainTexture, note.IsFloor ? "Grid" : "Arc", note.IsFloor ? 1 : 0, bracket.renderQueue, false);
            }
        }
    }
}
