using System;
using System.Threading;
using System.Threading.Tasks;
using Arcade.Audio;
using Arcade.Gameplay;
using UnityEngine;
using UnityEngine.Rendering;

namespace Arcade.Compose
{
    // Alpha's track spectrogram: 100 world units of time, 17 units of frequency.
    public sealed class AdeAudioSpectrum : MonoBehaviour
    {
        // FftSize is the longest of the shader's FFTs (log scale uses 2048/1024/512, linear 512); its tables serve all.
        private const int Width = 1024, Height = 128, FftSize = 2048;
        private const int UploadChunkFrames = 256 * 1024;
        private BassClip clip;
        private ComputeShader compute;
        private BassClip.SpectrumReader reader;
        private float[] chunk;
        private ComputeBuffer chunkBuffer;
        private int uploadKernel;
        private Task<int> decode;
        private ComputeShader computeAsset;
        private Shader materialShaderAsset;
        private CancellationTokenSource uploadCancellation;
        private double retryAt;
        private int retryDelay;
        private ComputeBuffer samplesBuffer, reversalBuffer, twiddleBuffer, bandRangesBuffer, columnTimesBuffer;
        // Audio time of each column's centre; the spectrum scrolls with the chart's BPM like the notes,
        // with the BPM factor capped to 0.1..2.5 x base BPM (keeps zero/negative/extreme BPM usable).
        private readonly float[] columnTimes = new float[Width], lastColumnTimes = new float[Width];
        private const double MinBpmFactor = 0.1, MaxBpmFactor = 2.5, TrackLength = 100;
        private RenderTexture texture;
        private Material material;
        private Mesh mesh;
        private GameObject surface;
        private int kernel, sampleRate, sampleCount;
        private bool hasColumnTimes, logScale;
        private float logMin, logMax; // log-axis range the current texture was built with

        private void Awake()
        {
            // The settings UI adds this component during editor startup. Prime the same kernels
            // and material here so first-use shader creation doesn't interrupt the playhead.
            if (!SystemInfo.supportsComputeShaders) return;
            try
            {
                sampleRate = 44100;
                samplesBuffer = new ComputeBuffer(1, sizeof(float));
                chunkBuffer = new ComputeBuffer(1, sizeof(float));
                UploadChunk(new float[1], 1);
                CreateSpectrum();
            }
            catch (Exception error) { Debug.Log($"频谱预加载未完成，开启时将重新加载。\n{error}", this); }
            finally { Release(); }
        }

        private void LateUpdate()
        {
            var compose = ArcadeComposeManager.Instance;
            var audio = ArcAudioManager.Instance;
            var gameplay = ArcGameplayManager.Instance;
            BassClip current = compose && compose.ArcadePreference.ShowSpectrum && gameplay && gameplay.IsLoaded && audio
                ? audio.Clip : null;
            bool wantLogScale = compose && compose.ArcadePreference.SpectrumLogScale;
            SpectrumTuning tuning = compose ? (compose.ArcadePreference.SpectrumTuning ??= new SpectrumTuning()) : null;
            bool logRangeChanged = wantLogScale && tuning != null
                && (tuning.LogMinFrequency != logMin || tuning.LogMaxFrequency != logMax);
            if (!ReferenceEquals(clip, current))
            {
                Release();
                clip = current;
                retryAt = 0;
                retryDelay = 0;
            }
            if (!current) return;
            bool profileChanged = wantLogScale != logScale || logRangeChanged;
            logScale = wantLogScale;
            if (tuning != null) { logMin = tuning.LogMinFrequency; logMax = tuning.LogMaxFrequency; }
            if (!texture)
            {
                if (!AdvanceLoad(current)) return;
            }
            else if (profileChanged) ProfileBands();

            material.SetFloat("_Opacity", Mathf.Clamp01(compose.ArcadePreference.SpectrumOpacity));
            // Applied even while paused; both intensity values share the same FFT texture.
            material.SetFloat("_LinearAmplitude", compose.ArcadePreference.SpectrumLinearAmplitude ? 1 : 0);
            ApplyTuning(tuning);
            // Match the quiet colors to the current track side (Conflict dark / Light).
            bool darkSide = !ArcSkinManager.Instance || ArcSkinManager.Instance.IsDarkSide;
            material.SetFloat("_Dark", darkSide ? 1 : 0);
            // Additive on Conflict (like Arcade Alpha), premultiplied alpha on Light.
            material.SetFloat("_DstBlend", (float)(darkSide ? BlendMode.One : BlendMode.OneMinusSrcAlpha));

            FillColumnTimes(gameplay);
            // Recompute whenever any column's time changes (time, speed or timing edits). A former
            // 16 ms skip threshold left the spectrum frozen behind the notes after pausing.
            if (hasColumnTimes && SameColumnTimes()) return;
            columnTimesBuffer.SetData(columnTimes);
            compute.Dispatch(kernel, Width, 1, 1);
            System.Array.Copy(columnTimes, lastColumnTimes, Width);
            hasColumnTimes = true;
        }

        // dB levels are passed as 0..1 of the stored -80..0 dB scale; the linear gain is relative to the
        // compute's stored magnitude x0.25.
        private void ApplyTuning(SpectrumTuning t)
        {
            material.SetFloat("_DbFloor", (t.DbFloor + 80) / 80);
            material.SetFloat("_DbCeiling", (t.DbCeiling + 80) / 80);
            material.SetFloat("_BandMix", t.DbBandShare / 100);
            material.SetFloat("_Contrast", t.DbContrast);
            material.SetFloat("_Gate", (t.DbGate + 80) / 80);
            material.SetFloat("_LinearGain", t.LinearGain * 4);
            material.SetFloat("_LinearContrast", t.LinearContrast);
            material.SetFloat("_LinearBoost", t.LinearBoost);
        }

        private bool SameColumnTimes()
        {
            for (int c = 0; c < Width; c++)
                if (columnTimes[c] != lastColumnTimes[c]) return false;
            return true;
        }

        // Inverse of the notes' position formula (base timing group): walk the timing sections from the
        // current chart time, each covering distance at BPM / base BPM x note speed.
        private void FillColumnTimes(ArcGameplayManager gameplay)
        {
            var timing = ArcTimingManager.Instance;
            var events = timing.Timings;
            double velocity = timing.SettingVelocity / 1000.0, baseBpm = timing.BaseBpm; // world units per ms at base BPM
            double now = gameplay.ChartTiming, offset = gameplay.ChartAudioOffset;
            if (events == null || events.Count == 0 || velocity <= 0 || baseBpm <= 0)
            {
                double span = TrackLength / System.Math.Max(velocity, 0.001);
                for (int c = 0; c < Width; c++) columnTimes[c] = (float)((now + offset + (c + 0.5) / Width * span) / 1000.0);
                return;
            }
            int index = Mathf.Max(0, ArcTimingManager.FindTimingIndex(events, gameplay.ChartTiming));
            double start = now, distance = 0;
            int column = 0;
            while (column < Width)
            {
                double factor = System.Math.Min(MaxBpmFactor, System.Math.Max(MinBpmFactor, events[index].Bpm / baseBpm));
                double rate = factor * velocity;
                double end = index + 1 < events.Count ? System.Math.Max(start, events[index + 1].Timing) : double.PositiveInfinity;
                while (column < Width)
                {
                    double at = start + ((column + 0.5) / Width * TrackLength - distance) / rate;
                    if (at > end) break;
                    columnTimes[column++] = (float)((at + offset) / 1000.0);
                }
                if (column >= Width) break;
                distance += (end - start) * rate;
                start = end;
                index++;
            }
        }

        private bool AdvanceLoad(BassClip current)
        {
            if (Time.realtimeSinceStartupAsDouble < retryAt) return false;
            try
            {
                if (reader == null) StartUpload(current);
                if (!UploadNextChunk()) return false;
                CloseReader();
                CreateSpectrum();
                retryDelay = 0;
                return true;
            }
            catch (Exception error)
            {
                Release();
                clip = current;
                computeAsset = null;
                materialShaderAsset = null;
                if (retryDelay == 0)
                {
                    Debug.Log($"频谱加载失败，将自动重试。\n{error}", this);
                    AdeToast.Instance?.Show("频谱加载失败，将自动重试");
                }
                retryDelay = retryDelay == 0 ? 1 : Mathf.Min(8, retryDelay * 2);
                retryAt = Time.realtimeSinceStartupAsDouble + retryDelay;
                return false;
            }
        }

        private void StartUpload(BassClip source)
        {
            reader = new BassClip.SpectrumReader(source);
            sampleRate = reader.SampleRate;
            int capacity = Mathf.Max(1, reader.FrameCount);
            samplesBuffer = new ComputeBuffer(capacity, sizeof(float));
            chunk = new float[Mathf.Min(UploadChunkFrames, capacity)];
            chunkBuffer = new ComputeBuffer(chunk.Length, sizeof(float));
            uploadCancellation = new CancellationTokenSource();
            BassAudio.ShuttingDown += Release;
            StartChunk();
        }

        private void StartChunk()
        {
            int count = Mathf.Min(chunk.Length, reader.FrameCount - sampleCount);
            var token = uploadCancellation.Token;
            decode = Task.Run(() => reader.Read(chunk, count, token), token);
        }

        private bool UploadNextChunk()
        {
            if (!decode.IsCompleted) return false;
            var finished = decode;
            decode = null;
            int count = finished.GetAwaiter().GetResult();
            if (count > 0 || sampleCount == 0) UploadChunk(chunk, Mathf.Max(1, count));
            sampleCount += count;
            if (count < chunk.Length || sampleCount >= reader.FrameCount) return true;
            StartChunk();
            return false;
        }

        private void UploadChunk(float[] samples, int written)
        {
            // Only the small staging buffer is CPU-updated. The full-song buffer stays on
            // the GPU, including on drivers that would copy a whole buffer for a partial write.
            if (!compute)
            {
                if (!computeAsset) computeAsset = Resources.Load<ComputeShader>("AffdataEdit/Spectrum");
                compute = Instantiate(computeAsset);
                uploadKernel = compute.FindKernel("UploadSamples");
                compute.SetBuffer(uploadKernel, "_UploadSource", chunkBuffer);
                compute.SetBuffer(uploadKernel, "_UploadTarget", samplesBuffer);
            }
            chunkBuffer.SetData(samples, 0, 0, written);
            compute.SetInt("_UploadOffset", sampleCount);
            compute.SetInt("_UploadCount", written);
            compute.Dispatch(uploadKernel, (written + 127) / 128, 1, 1);
        }

        private void CloseReader()
        {
            BassAudio.ShuttingDown -= Release;
            uploadCancellation?.Dispose();
            uploadCancellation = null;
            reader?.Dispose();
            reader = null;
            chunk = null;
            chunkBuffer?.Release();
            chunkBuffer = null;
        }

        private void CreateSpectrum()
        {
            kernel = compute.FindKernel("CSMain");
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

            // This is numeric dB data, not sRGB artwork: keep sampling linear.
            texture = new RenderTexture(Width, Height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear)
            {
                name = "Audio spectrum", enableRandomWrite = true,
                wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Point // crisp cells: no smoothing between time columns
            };
            if (!texture.Create()) throw new System.InvalidOperationException("Unable to create the audio spectrum texture.");
            compute.SetBuffer(kernel, "_AudioSamples", samplesBuffer);
            compute.SetBuffer(kernel, "_BitReversalTable", reversalBuffer);
            compute.SetBuffer(kernel, "_TwiddleFactors", twiddleBuffer);
            compute.SetTexture(kernel, "_ResultTexture", texture);
            compute.SetInt("_TotalSamples", sampleCount);
            compute.SetFloat("_SampleRate", sampleRate);
            // Build a stable per-frequency opacity range once per song.
            // Reuse the same texture; no CPU readback or extra audio cache.
            bandRangesBuffer = new ComputeBuffer(Height, sizeof(float) * 2);
            compute.SetBuffer(kernel, "_BandRanges", bandRangesBuffer);
            columnTimesBuffer = new ComputeBuffer(Width, sizeof(float));
            compute.SetBuffer(kernel, "_ColumnTimes", columnTimesBuffer);
            ProfileBands();
            CreateSurface();
        }

        private void ProfileBands()
        {
            compute.SetInt("_LogScale", logScale ? 1 : 0);
            float maxFrequency = Mathf.Clamp(logMax, 1000, sampleRate * 0.49f);
            compute.SetFloat("_MaxFrequency", maxFrequency);
            compute.SetFloat("_MinFrequency", Mathf.Clamp(logMin, 20, maxFrequency * 0.5f));
            compute.SetInt("_UseColumnTimes", 0);
            compute.SetInt("_NormalizeBands", 0);
            compute.SetFloat("_StartTime", FftSize / (float)sampleRate);
            compute.SetFloat("_Duration", Mathf.Max(0, (sampleCount - FftSize) / (float)sampleRate));
            compute.Dispatch(kernel, Width, 1, 1);
            int profileKernel = compute.FindKernel("ProfileBands");
            compute.SetTexture(profileKernel, "_ResultTexture", texture);
            compute.SetBuffer(profileKernel, "_BandRanges", bandRangesBuffer);
            compute.Dispatch(profileKernel, Height, 1, 1);
            compute.SetInt("_NormalizeBands", 1);
            compute.SetInt("_UseColumnTimes", 1);
            hasColumnTimes = false;
        }

        private void CreateSurface()
        {
            if (!materialShaderAsset) materialShaderAsset = Resources.Load<Shader>("AffdataEdit/Spectrum");
            material = new Material(materialShaderAsset)
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

        internal void Release()
        {
            // The decoder owns its BASS stream until the current read has stopped.
            if (decode != null)
            {
                uploadCancellation.Cancel();
                try { decode.GetAwaiter().GetResult(); }
                catch (OperationCanceledException) { }
                catch (Exception error) { Debug.LogException(error, this); }
                decode = null;
            }
            CloseReader();
            clip = null;
            sampleCount = 0;
            hasColumnTimes = false;
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
            bandRangesBuffer?.Release();
            columnTimesBuffer?.Release();
            samplesBuffer = reversalBuffer = twiddleBuffer = bandRangesBuffer = columnTimesBuffer = null;
        }

        private void OnDisable() { Release(); }
    }
}
