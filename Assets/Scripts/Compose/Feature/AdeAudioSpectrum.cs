using Arcade.Audio;
using Arcade.Gameplay;
using UnityEngine;
using UnityEngine.Rendering;

namespace Arcade.Compose
{
    // Alpha's track spectrogram: 100 world units of time, 17 units of frequency.
    public sealed class AdeAudioSpectrum : MonoBehaviour
    {
        private const int Width = 512, Height = 128, FftSize = 256;
        private BassClip clip;
        private ComputeShader compute;
        private ComputeBuffer samplesBuffer, reversalBuffer, twiddleBuffer;
        private RenderTexture texture;
        private Material material;
        private Mesh mesh;
        private GameObject surface;
        private int kernel, sampleRate;
        private float lastTime = float.NaN, lastDuration;
        private bool? darkSide;

        private void LateUpdate()
        {
            var compose = ArcadeComposeManager.Instance;
            var audio = ArcAudioManager.Instance;
            var gameplay = ArcGameplayManager.Instance;
            BassClip current = compose && compose.ArcadePreference.ShowSpectrum && gameplay && gameplay.IsLoaded && audio
                ? audio.Clip : null;
            if (!ReferenceEquals(clip, current))
            {
                Release();
                if (current) Load(current);
            }
            if (!texture) return;

            material.SetFloat("_Opacity", Mathf.Clamp01(compose.ArcadePreference.SpectrumOpacity));

            bool dark = ArcSkinManager.Instance.IsDarkSide;
            if (darkSide != dark)
            {
                darkSide = dark;
                material.SetInt("_DstFactor", (int)(dark ? BlendMode.One : BlendMode.OneMinusSrcAlpha));
                material.SetColor("_MinColor", dark ? new Color32(255, 0, 0, 255) : new Color32(255, 108, 108, 255));
                material.SetColor("_MiddleColor", dark ? new Color32(136, 0, 255, 255) : new Color32(255, 0, 51, 255));
                material.SetColor("_MaxColor", dark ? new Color32(255, 85, 0, 255) : new Color32(255, 107, 0, 255));
            }

            float time = gameplay.AudioTiming / 1000f;
            float duration = 100f / Mathf.Max(1, ArcTimingManager.Instance.SettingVelocity);
            if (Mathf.Abs(time - lastTime) < 0.016f * audio.PlayBackSpeed && duration == lastDuration) return;
            compute.SetFloat("_StartTime", time);
            compute.SetFloat("_Duration", duration);
            compute.Dispatch(kernel, Width, 1, 1);
            lastTime = time;
            lastDuration = duration;
        }

        private void Load(BassClip source)
        {
            try
            {
                compute = Instantiate(Resources.Load<ComputeShader>("AffdataEdit/Spectrum"));
                kernel = compute.FindKernel("CSMain");
                float[] samples = source.ReadSpectrumSamples(out sampleRate);
                samplesBuffer = new ComputeBuffer(Mathf.Max(1, samples.Length), sizeof(float));
                if (samples.Length > 0) samplesBuffer.SetData(samples);
                else samplesBuffer.SetData(new float[1]);

                var reversal = new int[FftSize];
                for (int i = 0; i < FftSize; i++)
                {
                    int reversed = 0;
                    for (int bit = FftSize >> 1, value = i; bit > 0; bit >>= 1, value >>= 1)
                        reversed = (reversed << 1) | (value & 1);
                    reversal[i] = reversed;
                }
                reversalBuffer = new ComputeBuffer(FftSize, sizeof(int));
                reversalBuffer.SetData(reversal);
                var twiddles = new Vector2[FftSize / 2];
                for (int i = 0; i < twiddles.Length; i++)
                {
                    float angle = -2 * Mathf.PI * i / FftSize;
                    twiddles[i] = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                }
                twiddleBuffer = new ComputeBuffer(twiddles.Length, sizeof(float) * 2);
                twiddleBuffer.SetData(twiddles);

                texture = new RenderTexture(Width, Height, 0, RenderTextureFormat.ARGB32)
                {
                    name = "Audio spectrum", enableRandomWrite = true,
                    wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear
                };
                if (!texture.Create()) throw new System.InvalidOperationException("Unable to create the audio spectrum texture.");
                compute.SetBuffer(kernel, "_AudioSamples", samplesBuffer);
                compute.SetBuffer(kernel, "_BitReversalTable", reversalBuffer);
                compute.SetBuffer(kernel, "_TwiddleFactors", twiddleBuffer);
                compute.SetTexture(kernel, "_ResultTexture", texture);
                compute.SetInt("_TotalSamples", samples.Length);
                compute.SetFloat("_SampleRate", sampleRate);
                CreateSurface();
                clip = source;
            }
            catch
            {
                Release();
                throw;
            }
        }

        private void CreateSurface()
        {
            material = new Material(Resources.Load<Shader>("AffdataEdit/Spectrum"))
            {
                name = "Audio spectrum", mainTexture = texture
            };
            mesh = new Mesh { name = "Audio spectrum" };
            // Time increases away from the judgement line; Alpha's low frequencies are on +X.
            mesh.vertices = new[] { new Vector3(8.5f, 0, 0), new Vector3(8.5f, 0, -100),
                new Vector3(-8.5f, 0, -100), new Vector3(-8.5f, 0, 0) };
            mesh.uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) };
            mesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };
            mesh.RecalculateBounds();
            var track = ArcSkinManager.Instance.TrackComponents[0];
            surface = new GameObject("AudioSpectrum", typeof(MeshFilter), typeof(MeshRenderer));
            surface.layer = track.gameObject.layer;
            surface.transform.SetParent(track.transform.parent, false);
            surface.GetComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = surface.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.sortingLayerID = track.sortingLayerID;
            renderer.sortingOrder = 3;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
        }

        private void Release()
        {
            clip = null;
            lastTime = float.NaN;
            darkSide = null;
            if (surface) { surface.SetActive(false); Destroy(surface); }
            if (mesh) Destroy(mesh);
            if (material) Destroy(material);
            if (texture) { texture.Release(); Destroy(texture); }
            if (compute) Destroy(compute);
            surface = null;
            mesh = null;
            material = null;
            texture = null;
            compute = null;
            samplesBuffer?.Release();
            reversalBuffer?.Release();
            twiddleBuffer?.Release();
            samplesBuffer = reversalBuffer = twiddleBuffer = null;
        }

        private void OnDisable() { Release(); }
    }
}
