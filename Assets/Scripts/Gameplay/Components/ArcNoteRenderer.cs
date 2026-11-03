using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Affdata.Core.Chart;
using Arcade.Gameplay.Chart;
using UnityEngine;
using UnityEngine.Rendering;

namespace Arcade.Gameplay
{
    // Managers retain editor state and visibility decisions. Only visible geometry reaches these buffers.
    [DefaultExecutionOrder(32000)]
    public sealed partial class ArcNoteRenderer : MonoBehaviour
    {
        internal static ArcNoteRenderer Instance { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticState() => Instance = null;
        // AffdataPlay's Arc shader applies this after the skin and note alpha.
        internal const float ArcOpacityMultiplier = 0.9f;
        [StructLayout(LayoutKind.Sequential)]
        internal struct NoteInstance
        {
            public Matrix4x4 Transform;
            public Vector4 HighColor, LowColor, UvTransform, ClipHeight, Options, Slide, Overlap;
            public static NoteInstance Create(Matrix4x4 transform, int mode, bool selected = false) => new NoteInstance
            {
                Transform = transform, HighColor = Color.white, LowColor = Color.white,
                UvTransform = new Vector4(1,1,0,0), ClipHeight = new Vector4(0,1,0,0),
                Options = new Vector4(mode,1,0,selected ? 1 : 0)
            };
        }
        private struct Item
        {
            public ArcNote Note;
            public Mesh Mesh;
            public Texture Texture;
            public int Submesh, Layer, LayerValue, Order, Queue, DepthTest, DepthWrite;
            public bool SameDraw(Item other) => ReferenceEquals(Mesh, other.Mesh) && ReferenceEquals(Texture, other.Texture) && Submesh == other.Submesh
                && Layer == other.Layer && Queue == other.Queue
                && DepthTest == other.DepthTest && DepthWrite == other.DepthWrite;
        }
        private sealed class Draw
        {
            public Item Item;
            public Material Material;
            public bool Selected;
            public readonly MaterialPropertyBlock Properties = new MaterialPropertyBlock();
            public readonly MaterialPropertyBlock SelectionProperties = new MaterialPropertyBlock();
        }
        private readonly List<Item> items = new List<Item>();
        private readonly List<GraphicsBuffer.IndirectDrawIndexedArgs> commands = new List<GraphicsBuffer.IndirectDrawIndexedArgs>();
        private readonly List<Draw> draws = new List<Draw>();
        private readonly Dictionary<(int,int,int), Material> materials = new Dictionary<(int,int,int), Material>();
        private readonly Plane[] frustum = new Plane[6];
        private readonly Dictionary<Mesh, Bounds> meshBounds = new Dictionary<Mesh, Bounds>();
        private readonly Dictionary<string, (int id, int value)> sortingLayers = new Dictionary<string, (int, int)>();
        private Vector3 cameraPosition, cameraForward;
        private bool orthographic;
        private GraphicsBuffer instanceBuffer, commandBuffer;
        private Camera cameraForFrame;
        private Texture whiteTexture;
        private Mesh connectionMesh;
        private Matrix4x4 undoTapSkin;
        private Color connectionColor;
        private readonly Dictionary<ArcArcTap, Vector2> connectionPositions = new Dictionary<ArcArcTap, Vector2>();
        private int frame = -1;
        public int VisibleInstanceCount => visibleItemCount;
        public int DrawCount => commands.Count;
        private static readonly int NotesId = Shader.PropertyToID("_Notes"), OffsetId = Shader.PropertyToID("_NoteOffset"),
            SelectionId = Shader.PropertyToID("_NoteSelection"), TextureId = Shader.PropertyToID("_MainTex");

        private void Awake() { Instance = this; }
        private void LateUpdate()
        {

            frame = Time.frameCount;
            items.Clear(); commands.Clear(); visibleItemCount = 0;
            meshBounds.Clear(); sortingLayers.Clear();
            if (pickingChart != ArcGameplayManager.Instance.Chart)
            {
                pickingMeshes.Clear();
                pickingChart = ArcGameplayManager.Instance.Chart;
            }
            cameraForFrame = ArcCameraManager.Instance ? ArcCameraManager.Instance.GameplayCamera : null;
            if (!cameraForFrame || !ArcGameplayManager.Instance.IsLoaded) return;
            GeometryUtility.CalculateFrustumPlanes(cameraForFrame, frustum);
            cameraPosition = cameraForFrame.transform.position;
            cameraForward = cameraForFrame.transform.forward;
            orthographic = cameraForFrame.orthographic;
            whiteTexture = Texture2D.whiteTexture;
            connectionMesh = ArcNoteMeshes.Connection;
            undoTapSkin = Matrix4x4.Scale(new Vector3(-1f / ArcTapNoteManager.Instance.WidthScale, 1, 1));
            connectionColor = ArcArcManager.Instance.ConnectionColor;
            connectionPositions.Clear();
            PrepareStyles();
            ArcTimingManager.Instance.SubmitBeatlines(this, cameraForFrame);
            Arcade.Compose.AdeGridManager.Instance?.SubmitGrid(this, cameraForFrame);
            var taps = ArcTapNoteManager.Instance;
            var sprite = taps.DefaultSprite;
            if (sprite)
            {
                Mesh mesh = ArcNoteMeshes.Sprite(sprite);
                Texture texture = sprite.texture;
                int queue = taps.ShaderdMaterial.renderQueue;
                foreach (var tap in taps.RenderingTaps)
                {
                    SubmitGroundNote(tap.RenderMatrix, mesh, texture, tap.Selected, tap.Alpha, 0, 1, 0, queue, tap);
                    SubmitConnections(tap);
                }
            }
            foreach (var hold in ArcHoldNoteManager.Instance.RenderingHolds)
                if (hold.Enable) SubmitGroundNote(hold.RenderMatrix,
                    hold.Highlight ? ArcHoldNoteManager.Instance.HighlightSprite : ArcHoldNoteManager.Instance.DefaultSprite,
                    hold.Selected, hold.Alpha, hold.From, hold.To, 1, ArcHoldNoteManager.Instance.HoldNoteMatrial.renderQueue, hold);
            foreach (var arc in ArcArcManager.Instance.RenderingArcs)
                if (arc.Enable && arc.arcRenderer != null) arc.arcRenderer.Submit(this);
            foreach (var tap in ArcArcManager.Instance.RenderingArcTaps) SubmitArcTap(tap);
            foreach (var slide in ArcSlideManager.Instance.RenderingSlides)
                slide.Visual?.Submit(this);
            Upload();
        }

        private void SubmitArcTap(ArcArcTap tap)
        {
            if (!tap.Enable) return;
            var manager = ArcArcManager.Instance;
            Matrix4x4 parent = tap.GetParentMatrix(arcLayerMatrix);
            Mesh mesh = tap.Arc.IsSfx ? ArcNoteMeshes.Sfx : ArcNoteMeshes.Cube;
            var data = NoteInstance.Create(parent * tap.ModelMatrix, 3, tap.Selected);
            data.HighColor = tap.TintColor;
            data.Options.y = tap.Alpha;
            for (int submesh = 0, count = tap.Arc.IsSfx ? sfxTapSubmeshes : normalTapSubmeshes; submesh < count; submesh++)
            {
                Material material = tap.Arc.IsSfx ? (submesh == 0 ? manager.SfxArcTapNoteMaterial : manager.SfxArcTapCoreMaterial) : manager.ArcTapMaterial;
                var style = GetStyle(material);
                data.UvTransform = style.Uv;
                Submit(mesh, style.Texture, data, "ArcTap", 0, style.Queue, submesh, depthWrite: 1, note: tap);
            }
            var shadow = tap.Arc.IsSfx ? sfxTapShadow : normalTapShadow;
            if (manager.ArcTapShadowSkin)
            {
                var style = GetStyle(shadow);
                Matrix4x4 local = tap.GetShadowMatrix(GetSprite(manager.ArcTapShadowSkin).Bounds, style.Rotation);
                SubmitSprite(manager.ArcTapShadowSkin, parent * local, tap.ShadowColor, shadow, shadow.sortingOrder, 0);
            }
        }

        private void SubmitConnections(ArcTap tap)
        {
            if (tap.ConnectedArcTaps.Count == 0) return;
            // Undo the skin's X flip and width scale, as the former connection prefab did.
            Matrix4x4 parent = tap.RenderMatrix * undoTapSkin;
            Vector3 start = parent.MultiplyPoint3x4(Vector3.zero);
            // Keep the quad in the transformed connection plane, including vertical lines.
            Vector3 normal = Vector3.Cross(parent.MultiplyVector(Vector3.right), parent.MultiplyVector(Vector3.forward));
            if (normal.sqrMagnitude <= 0.000000000001f) return;
            Color color = connectionColor;
            color.a = Mathf.Clamp01(tap.Alpha) * ConnectionLineStyle.Opacity;
            foreach (var arcTap in tap.ConnectedArcTaps)
            {
                if (!connectionPositions.TryGetValue(arcTap, out Vector2 position))
                {
                    position = arcTap.GetConnectionPosition();
                    connectionPositions.Add(arcTap, position);
                }
                var data = NoteInstance.Create(parent, 0);
                data.HighColor = color;
                Submit(connectionMesh, null, data, "Arc", 3, connection: true,
                    connectionEnd: new Vector3(position.x - tap.WorldX, 0, position.y), connectionNormal: normal);
            }
        }

        private static Matrix4x4 ConnectionTransform(Vector3 start, Vector3 direction, Vector3 normal, float length)
        {
            Vector3 forward = direction / length;
            Vector3 right = Vector3.Cross(normal, forward);
            if (right.sqrMagnitude <= .000000000001f)
                return Matrix4x4.TRS(start, Quaternion.LookRotation(direction, normal), new Vector3(1, 1, length));
            right.Normalize();
            Vector3 up = Vector3.Cross(forward, right);
            return new Matrix4x4(new Vector4(right.x, right.y, right.z, 0), new Vector4(up.x, up.y, up.z, 0),
                new Vector4(direction.x, direction.y, direction.z, 0), new Vector4(start.x, start.y, start.z, 1));
        }

        private void SubmitGroundNote(Matrix4x4 matrix, Sprite sprite, bool selected, float alpha,
            float from, float to, int mode, int queue, ArcNote note)
        {
            if (!sprite) return;
            SubmitGroundNote(matrix, ArcNoteMeshes.Sprite(sprite), sprite.texture, selected, alpha, from, to, mode, queue, note);
        }

        private void SubmitGroundNote(Matrix4x4 matrix, Mesh mesh, Texture texture, bool selected, float alpha,
            float from, float to, int mode, int queue, ArcNote note)
        {
            var data = NoteInstance.Create(matrix, mode, selected);
            data.Options.y = alpha;
            data.ClipHeight = new Vector4(from, to, 0, 0);
            Submit(mesh, texture, data, "Note", 0, queue, note: note);
        }

        // A sprite drawn from values; the renderer supplies only its sorting and material settings.
        internal void SubmitSprite(Sprite sprite, Matrix4x4 matrix, Color color, SpriteRenderer style, int order, int mode, bool always = false)
        {
            if (!sprite) return;
            var cached = GetStyle(style);
            var geometry = GetSprite(sprite);
            matrix *= cached.Flip;
            var data = NoteInstance.Create(matrix, mode);
            data.HighColor = color;
            Submit(geometry.Mesh, geometry.Texture, data, cached.Layer,
                order, cached.Queue, depthTest: always ? 8 : 4);
        }

        internal void SubmitHead(ArcArcVisual arc, Matrix4x4 parent, Texture texture)
        {
            if (!arc.EnableHead) return;
            float width = arc.Arc.IsVoid ? ArcArcRenderer.OffsetVoid : ArcArcRenderer.OffsetNormal;
            var local = Matrix4x4.TRS(new Vector3(ArcAlgorithm.ArcXToWorld(arc.Arc.XStart), ArcAlgorithm.ArcYToWorld(arc.Arc.YStart),0),
                Quaternion.identity, Vector3.one * width);
            var data = NoteInstance.Create(parent * Matrix4x4.Translate(arc.HeadOffset) * local, 2, arc.Selected);
            data.HighColor = arc.HeadHighColor;
            data.LowColor = arc.HeadLowColor;
            data.ClipHeight.z = data.ClipHeight.w = arc.Arc.YStart;
            data.Options.y = arc.Arc.IsVoid ? 1 : ArcOpacityMultiplier;
            Submit(ArcNoteMeshes.Head, texture, data, "ArcTap", 1, note: arc.Arc);
        }

        internal void SubmitSlide(Mesh mesh, Matrix4x4 matrix, int mode, Color color, Vector4 slide, Vector4 overlap,
            Texture texture, string layer, int order, int queue, bool selected, ArcNote note = null)
        {
            if (mesh.vertexCount == 0) return;
            var data = NoteInstance.Create(matrix, mode, selected);
            data.HighColor = color;
            data.Slide = slide;
            data.Overlap = overlap;
            Submit(mesh, texture, data, layer, order, queue, note: note);
        }
        internal void Submit(Mesh mesh, Texture texture, in NoteInstance data, string layer, int order,
            int queue = 3000, int submesh = 0, int depthWrite = 0, int depthTest = 4, int? arcSortKey = null, ArcNote note = null, bool connection = false, Vector3 connectionEnd = default, Vector3 connectionNormal = default)
        {
            // Shared meshes and sorting layers need one native lookup per frame, not per segment.
            if (!meshBounds.TryGetValue(mesh, out var localBounds))
            {
                localBounds = mesh.bounds;
                meshBounds.Add(mesh, localBounds);
            }
            if (!sortingLayers.TryGetValue(layer, out var sortingLayer))
            {
                int id = SortingLayer.NameToID(layer);
                sortingLayer = (id, SortingLayer.GetLayerValueFromID(id));
                sortingLayers.Add(layer, sortingLayer);
            }
            EnsureSortCapacity(items.Count + 1);
            frameInputs[items.Count] = new FrameInput { Data = data, LocalBounds = localBounds, Connection = connection, ConnectionEnd = connectionEnd, ConnectionNormal = connectionNormal };
            sortKeys[items.Count] = new SortKey { Index = items.Count, LayerValue = sortingLayer.value, Order = order, Queue = queue,
                DistanceOrdered = layer == "Arc", ArcFace = arcSortKey.HasValue, ArcKey = arcSortKey.GetValueOrDefault() };
            items.Add(new Item { Note = note, Mesh = mesh, Texture = texture ? texture : whiteTexture,
                Layer = sortingLayer.id, LayerValue = sortingLayer.value, Order = order, Queue = queue,
                Submesh = submesh, DepthWrite = depthWrite, DepthTest = depthTest });
        }
        private Material GetMaterial(Item item)
        {
            var key = (item.Queue,item.DepthWrite,item.DepthTest);
            if (materials.TryGetValue(key, out var material)) return material;
            material = new Material(Resources.Load<Shader>("NoteIndirect")) { name = "Indirect notes", enableInstancing = true, renderQueue = item.Queue };
            material.SetInt("_ZWrite", item.DepthWrite); material.SetInt("_ZTest", item.DepthTest);
            if (slideLines) material.SetTexture("_ArchTexture", slideLines);
            if (slideDots) material.SetTexture("_DotTexture", slideDots);
            materials.Add(key, material);
            return material;
        }
        // ArcSlideManager loads these with the first Slide.
        private Texture2D slideLines, slideDots;
        internal void SetSlideTextures(Texture2D lines, Texture2D dots)
        {
            slideLines = lines;
            slideDots = dots;
            foreach (var material in materials.Values)
            {
                material.SetTexture("_ArchTexture", lines);
                material.SetTexture("_DotTexture", dots);
            }
        }
        private void Upload()
        {

            if (items.Count == 0) return;
            // Sort compact keys, keeping matrices and instance data out of the sort's copies.
            SortVisibleItems();
            // Keep transparent order: adjacent compatible items can share a draw even across sort orders.
            for (int start = 0; start < visibleItemCount;)
            {
                Item item = items[sortKeys[start].Index];
                int command = commands.Count;
                if (command == draws.Count) draws.Add(new Draw());
                Draw draw = draws[command];
                draw.Item = item; draw.Material = GetMaterial(item); draw.Selected = false;
                int end = start;
                do
                {
                    draw.Selected |= frameInputs[sortKeys[end].Index].Data.Options.w > .5f;
                    end++;
                } while (end < visibleItemCount && item.SameDraw(items[sortKeys[end].Index]));
                commands.Add(new GraphicsBuffer.IndirectDrawIndexedArgs {
                    indexCountPerInstance = item.Mesh.GetIndexCount(item.Submesh), instanceCount = (uint)(end-start),
                    startIndex = item.Mesh.GetIndexStart(item.Submesh), baseVertexIndex = item.Mesh.GetBaseVertex(item.Submesh), startInstance = 0 });
                draw.Properties.SetInt(OffsetId, start);
                draw.SelectionProperties.SetInt(OffsetId, start);
                start = end;
            }
            if (visibleItemCount == 0) return;
            EnsureBuffer(ref instanceBuffer, GraphicsBuffer.Target.Structured, visibleItemCount, Marshal.SizeOf<NoteInstance>());
            EnsureBuffer(ref commandBuffer, GraphicsBuffer.Target.IndirectArguments, commands.Count, GraphicsBuffer.IndirectDrawIndexedArgs.size);
            instanceBuffer.SetData(packedInstances, 0, 0, visibleItemCount);
            commandBuffer.SetData(commands);
            for (int i = 0; i < commands.Count; i++)
            {
                var draw = draws[i];
                SetProperties(draw.Properties, draw.Item.Texture, false);
                SetProperties(draw.SelectionProperties, draw.Item.Texture, true);
            }
        }
        private void SetProperties(MaterialPropertyBlock properties, Texture texture, bool selection)
        {
            properties.SetBuffer(NotesId, instanceBuffer); properties.SetTexture(TextureId, texture);
            properties.SetFloat(SelectionId, selection ? 1 : 0);
        }
        private static void EnsureBuffer(ref GraphicsBuffer buffer, GraphicsBuffer.Target target, int count, int stride)
        {
            if (buffer != null && buffer.count >= count) return;
            buffer?.Dispose();
            buffer = new GraphicsBuffer(target, Mathf.NextPowerOfTwo(Mathf.Max(16,count)), stride);
        }
        // Whether this frame's notes for the camera include a selected one.
        internal static bool HasSelection(Camera camera)
        {
            var owner = Instance;
            if (!owner || owner.frame != Time.frameCount || owner.cameraForFrame != camera) return false;
            for (int i = 0; i < owner.commands.Count; i++)
                if (owner.draws[i].Selected) return true;
            return false;
        }
        internal static void DrawSelection(RasterCommandBuffer command, Camera camera)
        {
            var owner = Instance;
            if (!owner || owner.frame != Time.frameCount || owner.cameraForFrame != camera) return;
            for (int i = 0; i < owner.commands.Count; i++)
            {
                Draw draw = owner.draws[i];
                if (draw.Selected) command.DrawMeshInstancedIndirect(draw.Item.Mesh, draw.Item.Submesh, draw.Material, 0,
                    owner.commandBuffer, i * GraphicsBuffer.IndirectDrawIndexedArgs.size, draw.SelectionProperties);
            }
        }
        internal static void DrawLayer(RasterCommandBuffer command, Camera camera, int layer)
        {
            var owner = Instance;
            if (!owner || owner.frame != Time.frameCount || owner.cameraForFrame != camera) return;
            for (int i = 0; i < owner.commands.Count; i++)
            {
                Draw draw = owner.draws[i];
                if (draw.Item.LayerValue == layer) command.DrawMeshInstancedIndirect(draw.Item.Mesh, draw.Item.Submesh, draw.Material, 0,
                    owner.commandBuffer, i * GraphicsBuffer.IndirectDrawIndexedArgs.size, draw.Properties);
            }
        }
        private void OnDestroy()
        {
            instanceBuffer?.Dispose(); commandBuffer?.Dispose();
            DisposeFrameBuffers();
            foreach (var material in materials.Values) Destroy(material);
            ArcNoteMeshes.Release();
            if (Instance == this) Instance = null;
        }
    }
}
