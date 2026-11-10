using UnityEngine;
using UnityEngine.UI;

namespace Arcade.Compose.MarkingMenu
{
    // Small line icons stay sharp at the UI's scale and follow the active theme.
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class ContextMenuIcon : MaskableGraphic
    {
        public string Kind;

        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear();
            switch (Kind)
            {
                case "mouse":
                    Path(mesh, 7, 4, 9, 2, 15, 2, 18, 5, 18, 17, 15, 21, 9, 21, 6, 18, 6, 6, 7, 4);
                    Path(mesh, 12, 2, 12, 10); Path(mesh, 6, 10, 18, 10);
                    Polygon(mesh, 12, 3, 15, 3, 17, 5, 17, 9, 12, 9); break;
                case "clock":
                    Circle(mesh, 12, 12, 9); Path(mesh, 12, 6, 12, 12, 16, 15); break;
                case "sync":
                    Path(mesh, 20, 9, 19, 5, 15, 3, 10, 3, 6, 5, 4, 8);
                    Path(mesh, 4, 3, 4, 8, 9, 8);
                    Path(mesh, 4, 15, 5, 19, 9, 21, 14, 21, 18, 19, 20, 16);
                    Path(mesh, 20, 21, 20, 16, 15, 16); break;
                case "copy":
                    Box(mesh, 8, 8, 12, 13); Path(mesh, 15, 4, 4, 4, 4, 16); break;
                case "cut":
                case "horizontal-cut":
                    Circle(mesh, 6, 7, 3); Circle(mesh, 6, 17, 3);
                    Path(mesh, 8, 9, 20, 20); Path(mesh, 8, 15, 20, 4); break;
                case "grid":
                    Box(mesh, 3, 3, 18, 18);
                    Path(mesh, 9, 3, 9, 21); Path(mesh, 15, 3, 15, 21);
                    Path(mesh, 3, 9, 21, 9); Path(mesh, 3, 15, 21, 15); break;
                case "delete":
                    Path(mesh, 4, 6, 20, 6); Path(mesh, 9, 6, 9, 3, 15, 3, 15, 6);
                    Path(mesh, 6, 6, 7, 21, 17, 21, 18, 6);
                    Path(mesh, 10, 10, 10, 17); Path(mesh, 14, 10, 14, 17); break;
                case "mirror-x":
                    Path(mesh, 12, 3, 12, 21); Path(mesh, 8, 6, 3, 17, 8, 17, 8, 6);
                    Path(mesh, 16, 6, 21, 17, 16, 17, 16, 6); break;
                case "mirror-y":
                    Path(mesh, 3, 12, 21, 12); Path(mesh, 6, 8, 17, 3, 17, 8, 6, 8);
                    Path(mesh, 6, 16, 17, 21, 17, 16, 6, 16); break;
                case "snap-time":
                    Path(mesh, 5, 3, 5, 21); Path(mesh, 10, 7, 20, 7);
                    Path(mesh, 10, 12, 17, 12); Path(mesh, 10, 17, 20, 17); break;
                case "create-toggle":
                    Path(mesh, 5, 3, 5, 20, 10, 15, 14, 21, 17, 19, 13, 13, 20, 12, 5, 3); break;
                case "create-none":
                    Circle(mesh, 12, 12, 9); Path(mesh, 6, 6, 18, 18); break;
                case "create-tap": Box(mesh, 3, 8, 18, 8); break;
                case "create-hold":
                    Box(mesh, 7, 3, 10, 18); Path(mesh, 12, 7, 12, 17); break;
                case "create-arc":
                    Path(mesh, 4, 20, 6, 13, 14, 11, 19, 4); Path(mesh, 8, 21, 10, 16, 17, 14, 22, 5); break;
                case "create-arctap":
                case "create-single-arctap":
                    Path(mesh, 12, 3, 21, 12, 12, 21, 3, 12, 12, 3);
                    if (Kind == "create-single-arctap") Dot(mesh);
                    break;
                case "create-slide":
                case "convert-slide":
                    Path(mesh, 7, 5, 21, 5, 17, 19, 3, 19, 7, 5);
                    break;
                case "create-exit":
                    Path(mesh, 11, 3, 4, 3, 4, 21, 11, 21);
                    Path(mesh, 10, 12, 21, 12, 17, 8); Path(mesh, 21, 12, 17, 16); break;
                case "check": Path(mesh, 5, 12, 10, 17, 19, 7); break;
                case "back":
                    Path(mesh, 10, 5, 3, 12, 10, 19); Path(mesh, 3, 12, 21, 12); break;
            }
        }

        private void Box(VertexHelper mesh, float x, float y, float width, float height)
            => Path(mesh, x, y, x + width, y, x + width, y + height, x, y + height, x, y);

        private void Polygon(VertexHelper mesh, params float[] points)
        {
            var rect = rectTransform.rect;
            int first = mesh.currentVertCount;
            for (int i = 0; i < points.Length; i += 2)
            {
                mesh.AddVert(new Vector2(rect.xMin + points[i] / 24 * rect.width,
                    rect.yMax - points[i + 1] / 24 * rect.height), color, Vector2.zero);
                if (i >= 4) mesh.AddTriangle(first, first + i / 2 - 1, first + i / 2);
            }
        }

        private void Dot(VertexHelper mesh)
        {
            var rect = rectTransform.rect;
            var center = rect.center;
            int first = mesh.currentVertCount;
            mesh.AddVert(center, color, Vector2.zero);
            for (int i = 0; i <= 12; i++)
            {
                float angle = i * Mathf.PI / 6;
                mesh.AddVert(center + new Vector2(Mathf.Cos(angle) * rect.width, Mathf.Sin(angle) * rect.height) / 15,
                    color, Vector2.zero);
                if (i > 0) mesh.AddTriangle(first, first + i, first + i + 1);
            }
        }

        private void Circle(VertexHelper mesh, float x, float y, float radius)
        {
            for (int i = 0; i < 24; i++)
            {
                float a = i * Mathf.PI / 12, b = (i + 1) * Mathf.PI / 12;
                Segment(mesh, new Vector2(x + Mathf.Cos(a) * radius, y + Mathf.Sin(a) * radius),
                    new Vector2(x + Mathf.Cos(b) * radius, y + Mathf.Sin(b) * radius));
            }
        }

        private void Path(VertexHelper mesh, params float[] points)
        {
            for (int i = 2; i < points.Length; i += 2)
                Segment(mesh, new Vector2(points[i - 2], points[i - 1]), new Vector2(points[i], points[i + 1]));
        }

        private void Segment(VertexHelper mesh, Vector2 start, Vector2 end)
        {
            var rect = rectTransform.rect;
            start = new Vector2(rect.xMin + start.x / 24 * rect.width, rect.yMax - start.y / 24 * rect.height);
            end = new Vector2(rect.xMin + end.x / 24 * rect.width, rect.yMax - end.y / 24 * rect.height);
            Vector2 delta = end - start;
            Vector2 normal = new Vector2(-delta.y, delta.x).normalized * (rect.width / 24 * .9f);
            int first = mesh.currentVertCount;
            mesh.AddVert(start - normal, color, Vector2.zero);
            mesh.AddVert(start + normal, color, Vector2.zero);
            mesh.AddVert(end + normal, color, Vector2.zero);
            mesh.AddVert(end - normal, color, Vector2.zero);
            mesh.AddTriangle(first, first + 1, first + 2);
            mesh.AddTriangle(first, first + 2, first + 3);
        }
    }
}
