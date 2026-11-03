using System.Collections.Generic;
using Arcade.Gameplay.Chart;
using UnityEngine;

namespace Arcade.Gameplay
{
    public sealed partial class ArcNoteRenderer
    {
        private sealed class PickingMesh
        {
            public readonly List<Vector3> Vertices = new List<Vector3>();
            public readonly List<Vector2> Uv = new List<Vector2>();
            public readonly List<List<int>> Triangles = new List<List<int>>();
            public int Query;
        }

        private readonly Dictionary<Mesh, PickingMesh> pickingMeshes = new Dictionary<Mesh, PickingMesh>();
        private readonly List<Mesh> stalePickingMeshes = new List<Mesh>();
        private int pickingQuery;
        private ArcChart pickingChart;
        private struct PickingHit
        {
            public float Distance;
            public int Order;
        }
        private readonly Dictionary<ArcNote, PickingHit> pickingHits = new Dictionary<ArcNote, PickingHit>();
        private static readonly Vector2[] TracePickOffsets =
        {
            new Vector2(-2, 0), new Vector2(2, 0), new Vector2(0, -2), new Vector2(0, 2)
        };

        public static ArcNote PickNote(Camera camera, Vector2 screenPosition, float maxDistance = 120)
        {
            var owner = BeginPicking(camera);
            if (!owner) return null;

            float distance = maxDistance;
            ArcNote note = owner.RaycastNote(camera.ScreenPointToRay(screenPosition), ref distance, false);
            if (note != null) return note;
            // A small screen-space tolerance helps thin traces without expanding solid notes' hit areas.
            foreach (var offset in TracePickOffsets)
            {
                var hit = owner.RaycastNote(camera.ScreenPointToRay(screenPosition + offset), ref distance, true);
                if (hit != null) note = hit;
            }
            return note;
        }

        public static void PickNotes(Camera camera, Vector2 screenPosition, List<ArcNote> results, float maxDistance = 120)
        {
            results.Clear();
            var owner = BeginPicking(camera);
            if (!owner) return;
            owner.pickingHits.Clear();
            float distance = maxDistance;
            owner.RaycastNote(camera.ScreenPointToRay(screenPosition), ref distance, false, true);
            if (owner.pickingHits.Count == 0)
                foreach (var offset in TracePickOffsets)
                    owner.RaycastNote(camera.ScreenPointToRay(screenPosition + offset), ref distance, true, true);
            results.AddRange(owner.pickingHits.Keys);
            results.Sort(owner.ComparePickingHits);
            owner.pickingHits.Clear();
        }

        private int ComparePickingHits(ArcNote a, ArcNote b)
        {
            var first = pickingHits[a];
            var second = pickingHits[b];
            int depth = first.Distance.CompareTo(second.Distance);
            return depth != 0 ? depth : first.Order.CompareTo(second.Order);
        }

        internal static bool TryPickPoint(Camera camera, Vector2 position, ArcNote note, out Vector3 point)
        {
            point = default;
            var owner = BeginPicking(camera);
            if (!owner) return false;
            Ray ray = camera.ScreenPointToRay(position);
            float distance = 120;
            if (owner.RaycastNote(ray, ref distance, false, false, note) == null) return false;
            point = ray.GetPoint(distance);
            return true;
        }

        private static ArcNoteRenderer BeginPicking(Camera camera)
        {
            var owner = Instance;
            if (!owner || !camera || owner.cameraForFrame != camera || !ArcGameplayManager.Instance.IsLoaded
                || owner.pickingChart != ArcGameplayManager.Instance.Chart) return null;
            owner.stalePickingMeshes.Clear();
            foreach (var mesh in owner.pickingMeshes.Keys)
                if (!mesh) owner.stalePickingMeshes.Add(mesh);
            foreach (var mesh in owner.stalePickingMeshes) owner.pickingMeshes.Remove(mesh);
            owner.pickingQuery++;
            return owner;
        }

        private ArcNote RaycastNote(Ray ray, ref float closest, bool tracesOnly, bool collectAll = false, ArcNote onlyNote = null)
        {
            ArcNote result = null;
            for (int itemIndex = 0; itemIndex < items.Count; itemIndex++)
            {
                var geometryState = preparedGeometry[itemIndex];
                if (!geometryState.Visible) continue;
                var item = items[itemIndex];
                var data = nativeInputs[itemIndex].Data;
                Bounds bounds = geometryState.Bounds;
                if (onlyNote != null && item.Note != onlyNote) continue;
                if (item.Note == null || (!item.Note.Enable && !(item.Note is ArcSlide)) || data.Options.y <= 0) continue;
                if (tracesOnly && !(item.Note is ArcArc arc && arc.IsVoid)) continue;
                bool alreadyHit = pickingHits.TryGetValue(item.Note, out var previous);
                float itemDistance = collectAll && alreadyHit ? previous.Distance : closest;
                if (!bounds.IntersectRay(ray, out float entry)
                    || (!bounds.Contains(ray.origin) && entry > itemDistance)) continue;
                var geometry = GetPickingMesh(item.Mesh);
                var triangles = geometry.Triangles[item.Submesh];
                Matrix4x4 inverse = data.Transform.inverse;
                // Zero-duration Arc bodies flatten into XY, so their transform has no inverse.
                // Intersect their rendered triangles in world space instead.
                bool worldSpace = inverse == Matrix4x4.zero;
                Vector3 origin = worldSpace ? ray.origin : inverse.MultiplyPoint3x4(ray.origin);
                // Do not normalize: the intersection parameter must stay in world units under scaling.
                Vector3 direction = worldSpace ? ray.direction : inverse.MultiplyVector(ray.direction);
                int mode = (int)data.Options.x;
                bool hitItem = false;
                for (int i = 0; i < triangles.Count; i += 3)
                {
                    int a = triangles[i], b = triangles[i + 1], c = triangles[i + 2];
                    Vector3 va = geometry.Vertices[a], vb = geometry.Vertices[b], vc = geometry.Vertices[c];
                    if (worldSpace)
                    {
                        va = data.Transform.MultiplyPoint3x4(va);
                        vb = data.Transform.MultiplyPoint3x4(vb);
                        vc = data.Transform.MultiplyPoint3x4(vc);
                    }
                    if (!IntersectTriangle(origin, direction, va, vb, vc,
                        out float distance, out Vector2 barycentric) || distance >= itemDistance) continue;
                    if (mode == 1 || mode == 2)
                    {
                        float v = geometry.Uv[a].y * (1 - barycentric.x - barycentric.y)
                            + geometry.Uv[b].y * barycentric.x + geometry.Uv[c].y * barycentric.y;
                        if (v < data.ClipHeight.x || v > data.ClipHeight.y) continue;
                    }
                    if (item.Note is ArcSlide && !ArcSlideVisual.IsWithinRenderDistance(ray.GetPoint(distance).z)) continue;
                    itemDistance = distance;
                    hitItem = true;
                    if (!collectAll) closest = distance;
                    result = item.Note;
                }
                if (collectAll && hitItem)
                    pickingHits[item.Note] = new PickingHit { Distance = itemDistance, Order = alreadyHit ? previous.Order : pickingHits.Count };
            }
            return result;
        }

        private PickingMesh GetPickingMesh(Mesh mesh)
        {
            if (!pickingMeshes.TryGetValue(mesh, out var geometry))
                pickingMeshes.Add(mesh, geometry = new PickingMesh());
            if (geometry.Query == pickingQuery) return geometry;
            // Refresh once per click: Slide meshes can change while playing; the lists retain their capacity.
            mesh.GetVertices(geometry.Vertices);
            mesh.GetUVs(0, geometry.Uv);
            while (geometry.Triangles.Count < mesh.subMeshCount) geometry.Triangles.Add(new List<int>());
            for (int i = 0; i < mesh.subMeshCount; i++) mesh.GetTriangles(geometry.Triangles[i], i);
            geometry.Query = pickingQuery;
            return geometry;
        }

        private static bool IntersectTriangle(Vector3 origin, Vector3 direction, Vector3 a, Vector3 b, Vector3 c,
            out float distance, out Vector2 barycentric)
        {
            distance = 0;
            barycentric = default;
            Vector3 ab = b - a, ac = c - a;
            Vector3 p = Vector3.Cross(direction, ac);
            float determinant = Vector3.Dot(ab, p);
            if (Mathf.Abs(determinant) < 1e-7f) return false;
            Vector3 offset = origin - a;
            float u = Vector3.Dot(offset, p) / determinant;
            if (u < 0 || u > 1) return false;
            Vector3 q = Vector3.Cross(offset, ab);
            float v = Vector3.Dot(direction, q) / determinant;
            if (v < 0 || u + v > 1) return false;
            distance = Vector3.Dot(ac, q) / determinant;
            barycentric = new Vector2(u, v);
            return distance >= 0;
        }
    }
}
