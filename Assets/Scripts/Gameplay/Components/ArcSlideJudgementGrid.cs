using System;
using System.Collections.Generic;
using Affdata.Core.Geometry;
using UnityEngine;
using UnityEngine.Rendering;

namespace Arcade.Gameplay
{
    /// <summary>
    /// Port of the original's <c>JudgementLineMeshEffect</c> - the triangle grid that flickers
    /// along the sky judgement line.
    ///
    /// The effect is never visible on its own: in the original, JUDGE_SKY/TestGrid has its
    /// MeshRenderer disabled and is only drawn offscreen, blurred, and handed to the
    /// SkyJudgementLineGlow material as <c>_DownscaledGrid</c> and <c>_DownscaledBlurredGrid</c>.
    /// The sky line's pattern IS this grid; they are not two separate effects.
    ///
    /// The original grid uses 8 rows and 90 columns. AFF doubles horizontal coverage and
    /// columns together so extended Slides retain the same triangle density.
    ///
    /// Per-triangle opacity is uploaded to a StructuredBuffer that the grid shader indexes with
    /// SV_VertexID / 3, so the mesh must keep three unshared vertices per triangle and an identity
    /// index buffer.
    /// </summary>
    public sealed class ArcSlideJudgementGrid : IDisposable
    {
        // ---- grid shape (scene values, not the C# defaults) --------------------------------
        private const int Rows = 8;
        private const int Columns = 180; // Double coverage while preserving triangle density.
        private const int TrianglesPerRow = Columns * 2 + 1;      // 361
        private const int TriangleCount = Rows * TrianglesPerRow; // 2888

        // ---- opacity curve (G:1478-1524) ---------------------------------------------------
        /// <summary>Hold time before a lit triangle starts to fade, seconds.</summary>
        private const float HoldSeconds = 0.1f;
        /// <summary>Inside: hold below 0.1 s, then 1-age*2.5; zero at total age 0.4 s.</summary>
        private const float DecayInside = 2.5f;
        /// <summary>Outside/inactive: hold below 0.1 s, then 1-age*5; zero at total age 0.2 s.</summary>
        private const float DecayOutside = 5f;
        private const float NoiseTimeX = 19.5f;
        private const float NoiseTimeY = 17.7f;
        private const float LightThreshold = 0.7f;
        private const float LightMargin = 0.2f;
        /// <summary>Below this interval width the effect counts as inactive (G:1482).</summary>
        private const float ActiveWidthEpsilon = 0.01f;

        /// <summary>Blur strength the original sets before the chain (G:1440); see Spc/Blur.</summary>
        private const float BlurAmount = 0.0001f;

        private static readonly int TriIdToOpacityId = Shader.PropertyToID("_TriId_To_Opacity");
        private static readonly int BlurTexelsId = Shader.PropertyToID("_BlurTexels");

        private Mesh mesh;
        private Material gridMaterial;
        private Material blurMaterial;
        private GraphicsBuffer opacityBuffer;
        private CommandBuffer commandBuffer;

        private RenderTexture gridRT;       // the grid itself, square
        private RenderTexture sharpRT;      // one blur pass  -> _DownscaledGrid
        private RenderTexture blurredRT;    // full chain      -> _DownscaledBlurredGrid
        private RenderTexture downOne, upOne, downTwo, upTwo;

        private float[] opacity;            // nb: uploaded when its exact values change
        private float[] columnX;            // Nb: normalised x within the row
        private float[] rowWeight;          // ob: normalised distance from the grid's vertical centre
        private float[] litAt;              // Ob: when each triangle was last lit
        private bool[] previouslyInside;

        private IReadOnlyList<Vector2> ranges;
        private readonly List<Vector2> previousRanges = new List<Vector2>();
        private bool rangeDirty = true;
        private bool hasHistory;
        private float rangeLeft;
        private float rangeRight;
        private float clock;
        private float presentationClock = -1f;
        private int renderWidth;
        private int renderHeight;
        private bool built;
        private bool opacityUploadDirty;
        private bool texturesDirty;

        public bool Built => built;
        /// <summary>Last absolute time used by the source Perlin/decay equations.</summary>
        public float ClockSeconds => clock;
        /// <summary>Triangles currently at more than half opacity - for capture diagnostics.</summary>
        public int LitTriangles { get; private set; }
        /// <summary>Includes dim tails below LitTriangles' half-opacity diagnostic threshold.</summary>
        public bool HasNonZeroOpacity { get; private set; }
        /// <summary>Actual uploads/draw-chain submissions since the last Build; not GPU timings.</summary>
        public int OpacityUploadCount { get; private set; }
        public int TextureUpdateCount { get; private set; }

        /// <summary>Last sky range handed in, for capture diagnostics.</summary>
        public Vector2 SkyRange => new Vector2(rangeLeft, rangeRight);
        /// <summary>Whether that range counts as active this frame.</summary>
        public bool RangeActive => rangeRight - rangeLeft >= ActiveWidthEpsilon;

        /// <summary>The one-pass blur, bound to the sky glow as <c>_DownscaledGrid</c>.</summary>
        public Texture SharpTexture => sharpRT;
        /// <summary>The full chain, bound as <c>_DownscaledBlurredGrid</c>.</summary>
        public Texture BlurredTexture => blurredRT;

        /// <summary>
        /// Builds the mesh, buffer and render textures. The caller binds <see cref="SharpTexture"/>
        /// and <see cref="BlurredTexture"/> onto the sky glow quad; the original set them on a
        /// shared material and only got away with it because of initialisation order.
        /// </summary>
        public void Build(Material gridSource, Material blurSource, int pixelWidth, int pixelHeight)
        {
            Dispose();
            OpacityUploadCount = 0;
            TextureUpdateCount = 0;
            HasNonZeroOpacity = false;
            rangeDirty = true;
            hasHistory = false;
            if (gridSource == null || blurSource == null) return;

            gridMaterial = new Material(gridSource) { name = "Spc_JudgementGrid (instance)" };
            blurMaterial = new Material(blurSource) { name = "Spc_Blur (instance)" };

            mesh = BuildMesh();
            BuildArrays();

            opacityBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, TriangleCount, sizeof(float));
            opacityBuffer.SetData(opacity);
            OpacityUploadCount++;
            opacityUploadDirty = false;
            gridMaterial.SetBuffer(TriIdToOpacityId, opacityBuffer);

            renderWidth = Mathf.Max(pixelWidth, 1);
            renderHeight = Mathf.Max(pixelHeight, 1);
            AllocateTargets(renderWidth);
            commandBuffer = new CommandBuffer { name = "Spc Judgement Grid" };
            built = true;
            texturesDirty = true; // Created RT contents are undefined until the first complete draw/blur.
        }

        /// <summary>
        /// The active sky area's left and right bounds in normalised sky-track units, exactly the
        /// pair the original feeds to <c>judgementLineMeshEffect.$p(a, b)</c>. Pass (0, 0) when no
        /// area is active.
        /// </summary>
        public void SetSkyRanges(IReadOnlyList<Vector2> values)
        {
            rangeDirty |= (ranges == null) != (values == null);
            int count = values?.Count ?? 0;
            rangeDirty |= previousRanges.Count != count;
            for (int i = 0; i < count; i++)
            {
                if (i >= previousRanges.Count) { previousRanges.Add(values[i]); rangeDirty = true; }
                else if (previousRanges[i] != values[i]) { previousRanges[i] = values[i]; rangeDirty = true; }
            }
            if (previousRanges.Count > count) previousRanges.RemoveRange(count, previousRanges.Count - count);
            ranges = values;
        }

        public void SetSkyRange(float left, float right)
        {
            rangeDirty |= ranges != null || rangeLeft != left || rangeRight != right;
            ranges = null;
            Array.Clear(previouslyInside, 0, previouslyInside.Length);
            rangeLeft = left;
            rangeRight = right;
        }

        /// <summary>Sets the next sample's absolute clock; does not simulate a frame.</summary>
        public void SetPresentationClock(float seconds)
        {
            if (float.IsNaN(seconds) || float.IsInfinity(seconds) || seconds < 0f)
                throw new ArgumentOutOfRangeException(nameof(seconds));
            presentationClock = seconds;
        }

        public void ClearPresentationClock() => presentationClock = -1f;

        /// <summary>Advances the flicker and refreshes changed output. Call once per frame.</summary>
        public void UpdateFrame(float deltaSeconds, bool render = true)
        {
            if (!built) return;
            // JudgementLineMeshEffect.Update reads _WE._b -> BeforeUpdateHook._A,
            // an absolute Stopwatch value cast to float. Accumulating song deltas
            // changes the Perlin phase, especially during event replay and retries.
            // A supplied replay clock is already the current time, never current + dt.
            float nextClock = presentationClock >= 0f ? presentationClock :
                (float)((double)System.Diagnostics.Stopwatch.GetTimestamp() /
                    System.Diagnostics.Stopwatch.Frequency);
            if (clock != nextClock || rangeDirty)
            {
                clock = nextClock;
                UpdateOpacity(clock);
                rangeDirty = false;
            }
            if (render) RenderCurrent();
        }

        /// <summary>Refreshes dirty or lost output without advancing the simulated clock.</summary>
        public void RenderCurrent()
        {
            if (!built) return;
            bool recreated = EnsureCreated(gridRT) | EnsureCreated(sharpRT) | EnsureCreated(blurredRT)
                | EnsureCreated(downOne) | EnsureCreated(upOne) | EnsureCreated(downTwo) | EnsureCreated(upTwo);
            if (opacityBuffer == null || !opacityBuffer.IsValid())
            {
                opacityBuffer?.Dispose();
                opacityBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, TriangleCount, sizeof(float));
                gridMaterial.SetBuffer(TriIdToOpacityId, opacityBuffer);
                recreated = true;
            }
            if (recreated) InvalidateRender();
            if (!texturesDirty) return;
            if (opacityUploadDirty)
            {
                opacityBuffer.SetData(opacity);
                OpacityUploadCount++;
                opacityUploadDirty = false;
            }
            Render();
            TextureUpdateCount++;
            texturesDirty = false;
        }

        /// <summary>Invalidates output without advancing Perlin, decay, or random state.</summary>
        public void InvalidateRender()
        {
            opacityUploadDirty = true;
            texturesDirty = true;
        }

        private static bool EnsureCreated(RenderTexture texture)
        {
            if (texture.IsCreated()) return false;
            if (!texture.Create()) throw new InvalidOperationException("Could not recreate a judgement grid target.");
            return true;
        }

        /// <summary>Clears every triangle and the sky range, for a song restart.</summary>
        public void ResetState()
        {
            if (!built) return;
            if (hasHistory)
            {
                Array.Clear(opacity, 0, opacity.Length);
                Array.Fill(litAt, float.NegativeInfinity);
                hasHistory = false;
            }
            if (HasNonZeroOpacity) InvalidateRender();
            ranges = null;
            rangeDirty = true;
            rangeLeft = 0f;
            rangeRight = 0f;
            clock = 0f;
            LitTriangles = 0;
            HasNonZeroOpacity = false;
        }

        public void Dispose()
        {
            built = false;
            opacityBuffer?.Dispose();
            opacityBuffer = null;
            commandBuffer?.Dispose();
            commandBuffer = null;
            Release(ref gridRT);
            Release(ref sharpRT);
            Release(ref blurredRT);
            Release(ref downOne);
            Release(ref upOne);
            Release(ref downTwo);
            Release(ref upTwo);
            DestroySafe(ref mesh);
            DestroySafe(ref gridMaterial);
            DestroySafe(ref blurMaterial);
        }

        // ---- per-frame opacity (G:1478-1524) -----------------------------------------------

        private bool ContainsColumn(float x)
        {
            if (ranges == null) return RangeActive && rangeLeft <= x && x <= rangeRight;
            for (int i = 0; i < ranges.Count; i++)
            {
                var range = ranges[i];
                if (range.y - range.x >= ActiveWidthEpsilon && range.x <= x && x <= range.y) return true;
            }
            return false;
        }

        private void UpdateOpacity(float t)
        {
            if (!HasNonZeroOpacity)
            {
                bool active = ranges == null && RangeActive;
                if (ranges != null)
                    for (int i = 0; i < ranges.Count && !active; i++)
                        active = ranges[i].y - ranges[i].x >= ActiveWidthEpsilon;
                if (!active) return;
            }
            int lit = 0;
            bool anyNonZero = false;
            for (int i = 0; i < opacity.Length; i++)
            {
                float value;
                bool inside = ContainsColumn(columnX[i]);
                if (inside)
                {
                    float o = rowWeight[i];
                    // Seed the noise history immediately when a moving Slide reaches a new
                    // column; a narrow fast range may leave before the next random emission.
                    if (!previouslyInside[i])
                    {
                        for (int step = 0; step <= 12; step++)
                        {
                            float sample = t - step / 60f;
                            float noise = Mathf.PerlinNoise(i + sample * NoiseTimeX, sample * NoiseTimeY) - o * .5f;
                            if (noise <= LightThreshold || noise - o <= LightMargin) continue;
                            litAt[i] = Mathf.Max(litAt[i], sample);
                            hasHistory = true;
                            break;
                        }
                    }
                    float n = Mathf.PerlinNoise(i + t * NoiseTimeX, t * NoiseTimeY) - o * 0.5f;
                    if (n > LightThreshold && n - o > LightMargin)
                    {
                        value = 1f;
                        litAt[i] = t;
                        hasHistory = true;
                    }
                    else
                    {
                        value = Decay(t - litAt[i], DecayInside);
                    }
                }
                else
                {
                    value = Decay(t - litAt[i], DecayOutside);
                }
                previouslyInside[i] = inside;
                if (opacity[i] != value)
                {
                    opacity[i] = value;
                    opacityUploadDirty = true;
                    texturesDirty = true;
                }
                anyNonZero |= value != 0f;
                if (value > 0.5f) lit++;
            }
            LitTriangles = lit;
            HasNonZeroOpacity = anyNonZero;
        }

        private static float Decay(float since, float rate) =>
            since < HoldSeconds ? 1f : 1f - Mathf.Clamp01(since * rate);

        // ---- offscreen render ---------------------------------------------------------------

        private void Render()
        {
            commandBuffer.Clear();
            commandBuffer.SetRenderTarget(gridRT);
            commandBuffer.ClearRenderTarget(true, true, Color.clear);
            commandBuffer.DrawMesh(mesh, Matrix4x4.identity, gridMaterial, 0, 0);
            Graphics.ExecuteCommandBuffer(commandBuffer);

            // The original's chain, pass for pass (G:1444-1472). Pass 0 is the vertical kernel and
            // pass 1 the horizontal one; the original's debug labels have them the other way round
            // but the disassembly does not.
            Blur(gridRT, sharpRT, 0);
            Blur(sharpRT, downOne, 1);
            Blur(downOne, downTwo, 0);
            Blur(downTwo, upTwo, 1);
            Blur(upTwo, downOne, 0);
            Blur(downOne, upOne, 1);
            Blur(upOne, blurredRT, 1);
        }

        private void Blur(RenderTexture source, RenderTexture destination, int pass)
        {
            // The original derives its tap offset from _ScreenParams, so its radius follows the
            // window size; Spc/Blur takes texels instead, so this reproduces that scaling here.
            // These are the camera's pixel dimensions, not Screen's - under batchmode capture the
            // two differ by a lot and Screen's would shrink every target to a few hundred pixels.
            float reference = pass == 0 ? renderHeight : renderWidth;
            blurMaterial.SetFloat(BlurTexelsId, BlurAmount * Mathf.Max(reference, 1f));
            Graphics.Blit(source, destination, blurMaterial, pass);
        }

        // ---- construction --------------------------------------------------------------------

        /// <summary>
        /// The grid from G:1268-1394. Rows alternate phase, triangles alternate point-down and
        /// point-up, and every triangle writes its own three vertices so that SV_VertexID / 3 is a
        /// triangle index. TotalWidth is deliberately unused - the original only reads it in an
        /// editor preview class, and the runtime mesh is a normalised unit block.
        /// </summary>
        private static Mesh BuildMesh()
            => NoteMeshDefinitions.CreateSlideJudgementGrid(Rows, Columns, Vector2.zero);

        /// <summary>
        /// Nb and ob from G:1356-1368. Note the divisor for Nb is the triangles per row, not
        /// the column count - ob then makes the middle rows flicker most and the outer rows least.
        /// </summary>
        private void BuildArrays()
        {
            opacity = new float[TriangleCount];
            columnX = new float[TriangleCount];
            rowWeight = new float[TriangleCount];
            litAt = new float[TriangleCount];
            previouslyInside = new bool[TriangleCount];
            // Original _Ob starts at zero while _WE._b is an absolute Stopwatch clock,
            // so never-lit triangles are already older than the fade duration. Our local
            // replay clock starts at zero; preserve that initial state instead of flashing
            // every triangle white for the first 100 ms after Reset.
            Array.Fill(litAt, float.NegativeInfinity);

            const float rowCentre = (Rows - 1) * 0.5f;
            const float rowSpan = Rows - 1;
            var vertices = mesh.vertices;
            for (int i = 0; i < TriangleCount; i++)
            {
                float meshX = (vertices[i * 3].x + vertices[i * 3 + 1].x + vertices[i * 3 + 2].x) / 3;
                // Grid vertex shader: clipX=meshX*1.7-.85; glow samples at .4*skyX+.3.
                // Derive coverage from the drawn triangle, not its ordinal in the row.
                float textureX = (meshX * 1.7f - .85f + 1) * .5f;
                columnX[i] = (textureX - .3f) / .4f;
                rowWeight[i] = Mathf.Abs(rowCentre - i / TrianglesPerRow) / rowSpan;
            }
        }

        /// <summary>Render target sizes from G:1397-1435, scaled off a 3840-wide reference.</summary>
        private void AllocateTargets(int pixelWidth)
        {
            float k = Mathf.Clamp(pixelWidth / 3840f, 0.125f, 1f);
            int square = Mathf.Max(64, (int)(k * 2048f));
            int wide = square;
            int tall = Mathf.Max(16, (int)(k * 512f));

            gridRT = Create("ArcSlideJudgementGrid", square, square);
            sharpRT = Create("ArcSlideJudgementGrid_Sharp", wide, tall);
            blurredRT = Create("ArcSlideJudgementGrid_Blurred", wide, tall);
            downOne = Create("ArcSlideJudgementGrid_DownOne", wide / 2, Mathf.Max(8, tall / 2));
            upOne = Create("ArcSlideJudgementGrid_UpOne", wide / 2, Mathf.Max(8, tall / 2));
            downTwo = Create("ArcSlideJudgementGrid_DownTwo", wide / 4, Mathf.Max(4, tall / 4));
            upTwo = Create("ArcSlideJudgementGrid_UpTwo", wide / 4, Mathf.Max(4, tall / 4));
        }

        private static RenderTexture Create(string name, int width, int height)
        {
            var rt = new RenderTexture(Mathf.Max(4, width), Mathf.Max(4, height), 0, RenderTextureFormat.ARGB32)
            {
                name = name,
                useMipMap = false,
                autoGenerateMips = false,
                antiAliasing = 1,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
            };
            rt.Create();
            return rt;
        }

        private static void Release(ref RenderTexture rt)
        {
            if (rt == null) return;
            rt.Release();
            DestroyObject(rt);
            rt = null;
        }

        private static void DestroySafe<T>(ref T obj) where T : UnityEngine.Object
        {
            if (obj == null) return;
            DestroyObject(obj);
            obj = null;
        }

        private static void DestroyObject(UnityEngine.Object obj)
        {
            if (Application.isPlaying) UnityEngine.Object.Destroy(obj);
            else UnityEngine.Object.DestroyImmediate(obj);
        }
    }
}
