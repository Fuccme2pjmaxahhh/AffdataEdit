using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Arcade.Util.Pooling;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Experimental.Rendering;

namespace Arcade.Gameplay
{
    [DefaultExecutionOrder(31000)]
	public class ArcTapNoteEffectComponent : MonoBehaviour, IPoolable
	{
		public bool Available { get; private set; } = true;
        private double endTime;
		public ParticleSystem Effect;
		public Texture2D Texture;
		[SerializeField] private bool isSfx;
		[SerializeField] private bool useConflictBlend;
		private Material material;
        private sealed class SharedMaterial
        {
            public Material Material;
            public int Users;
        }
        // All instances of a pool receive the same skin/blend updates from ArcEffectManager.
        private static readonly Dictionary<(Material, bool, bool), SharedMaterial> sharedMaterials = new Dictionary<(Material, bool, bool), SharedMaterial>();
        private (Material, bool, bool) materialKey;
        private Material sourceMaterial;
        private ParticleSystemRenderer particleRenderer;
        private bool aggregateActive;
        private MaterialPropertyBlock hitProperties;
        private static Shader aggregateShader;
        private static readonly int RepeatId = Shader.PropertyToID("_HitRepeat"), QuantizeId = Shader.PropertyToID("_HitQuantize");
        private readonly Dictionary<(Vector3, Quaternion, float), uint> frameHits = new Dictionary<(Vector3, Quaternion, float), uint>();
        private readonly Dictionary<uint, int> pendingRepeats = new Dictionary<uint, int>();
        private readonly List<Vector4> customData = new List<Vector4>();
        private ParticleSystem.Particle[] particles = System.Array.Empty<ParticleSystem.Particle>();
        private uint nextSeed;
        private int hitFrame = -1;
        private SharedMaterial sharedMaterial;
        private CancellationTokenSource effectCancellation;

		private void Awake()
		{
            hitProperties = new MaterialPropertyBlock();
			Effect.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
			var renderer = Effect.GetComponent<ParticleSystemRenderer>();
            particleRenderer = renderer;
            sourceMaterial = renderer.sharedMaterial;
            AcquireMaterial(false);
			renderer.sortingLayerName = "Effect";
			renderer.renderingLayerMask = 1u & ~ArcGameplayManager.Instance.SelectionLayerMask;
			renderer.minParticleSize = 0;
			renderer.maxParticleSize = 1;
			renderer.alignment = isSfx ? ParticleSystemRenderSpace.View : ParticleSystemRenderSpace.World;
            renderer.sortMode = ParticleSystemSortMode.Distance;
			var main = Effect.main;
			main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startRotation3D = !isSfx;
			main.scalingMode = ParticleSystemScalingMode.Local;
			main.simulationSpeed = 1;
			main.duration = isSfx ? .48f : .256f;
			main.startLifetime = isSfx ? .48f : .26f;
			SetTexture(Texture);
			SetConflictBlend(useConflictBlend);
		}

        public void PlayAt(Vector3 pos, bool combine = false)
        {
            bool startLifetime = Available;
            Available = false;
            if (startLifetime)
            {
                var target = ArcEffectManager.Instance.EffectPlane.ReferenceCamera.targetTexture;
                var format = target ? target.graphicsFormat : SystemInfo.GetGraphicsFormat(DefaultFormat.LDR);
                bool unorm8 = format == GraphicsFormat.R8G8B8A8_UNorm || format == GraphicsFormat.B8G8R8A8_UNorm;
                bool canCombine = !isSfx && SystemInfo.graphicsDeviceType == GraphicsDeviceType.Metal
                    && (unorm8 || format == GraphicsFormat.R32G32B32A32_SFloat);
                if (canCombine && !aggregateShader) aggregateShader = Resources.Load<Shader>("AffdataPlayParticles/JudgeParticleAggregate");
                AcquireMaterial(canCombine && aggregateShader && aggregateShader.isSupported);
                hitProperties.Clear();
                hitProperties.SetFloat(QuantizeId, unorm8 ? 1 : 0);
                hitProperties.SetFloat(RepeatId, 0);
                particleRenderer.SetPropertyBlock(hitProperties);
                if (aggregateActive)
                    particleRenderer.SetActiveVertexStreams(new List<ParticleSystemVertexStream> {
                        ParticleSystemVertexStream.Position, ParticleSystemVertexStream.Color,
                        ParticleSystemVertexStream.UV, ParticleSystemVertexStream.Custom1X });
            }
			var camera = ArcEffectManager.Instance.EffectPlane.ReferenceCamera;
            // Particle rotations use the inverse Euler angles; keep earlier world-space hits fixed.
            Quaternion rotation = camera.transform.rotation;
			float depth = Vector3.Dot(pos - camera.transform.position, camera.transform.forward);
			float height = camera.orthographic ? camera.orthographicSize * 2
				: 2 * Mathf.Max(.0001f, depth) * Mathf.Tan(camera.fieldOfView * Mathf.Deg2Rad / 2);
			var main = Effect.main;
            float size = 9.5f * 1.5f * 1.8f * height / 40f * Mathf.Min(camera.aspect / (16f / 9), 1);
            if (hitFrame != Time.frameCount)
            {
                frameHits.Clear();
                hitFrame = Time.frameCount;
            }
            var key = (pos, rotation, size);
            if (aggregateActive && combine && frameHits.TryGetValue(key, out uint seed) && pendingRepeats.TryGetValue(seed, out int repeat))
                pendingRepeats[seed] = repeat + 1;
            else
            {
                if (Effect.particleCount == main.maxParticles) main.maxParticles *= 2;
                if (++nextSeed == 0) nextSeed++;
                Effect.Emit(new ParticleSystem.EmitParams { position = pos, startSize = size,
                    rotation3D = isSfx ? Vector3.zero : -rotation.eulerAngles, randomSeed = nextSeed,
                    applyShapeToPosition = false }, 1);
                if (aggregateActive)
                {
                    pendingRepeats.Add(nextSeed, 1);
                    if (combine) frameHits[key] = nextSeed;
                }
            }
            endTime = Time.timeAsDouble + Effect.main.startLifetime.constantMax;
            if (startLifetime)
            {
                effectCancellation = CancellationTokenSource.CreateLinkedTokenSource(destroyCancellationToken);
                WaitForEndAsync(effectCancellation).Forget();
            }
		}

        private void LateUpdate()
        {
            if (!aggregateActive || pendingRepeats.Count == 0) return;
            int count = Effect.particleCount;
            if (particles.Length < count) particles = new ParticleSystem.Particle[Mathf.NextPowerOfTwo(count)];
            count = Effect.GetParticles(particles);
            Effect.GetCustomParticleData(customData, ParticleSystemCustomData.Custom1);
            while (customData.Count < count) customData.Add(Vector4.one);
            for (int i = 0; i < count; i++)
                if (pendingRepeats.TryGetValue(particles[i].randomSeed, out int repeat))
                    customData[i] = new Vector4(repeat, 0, 0, 0);
            Effect.SetCustomParticleData(customData, ParticleSystemCustomData.Custom1);
            pendingRepeats.Clear();
        }

        private async UniTask WaitForEndAsync(CancellationTokenSource cancellation)
        {
            try
            {
                await UniTask.WaitUntil(() => Time.timeAsDouble >= endTime, cancellationToken: cancellation.Token);
                Effect.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                Available = true;
            }
            finally
            {
                if (effectCancellation == cancellation) effectCancellation = null;
                cancellation.Dispose();
            }
        }

        private void OnDisable()
        {
            effectCancellation?.Cancel();
            effectCancellation = null;
            if (Effect) Effect.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            Available = true;
            frameHits.Clear(); pendingRepeats.Clear(); customData.Clear();
        }

		public void SetTexture(Texture2D texture)
		{
			Texture = texture;
			if (material) material.mainTexture = texture;
		}

		public void SetConflictBlend(bool value)
		{
			useConflictBlend = value;
			if (!material) return;
			material.SetInt("_SrcFactor", (int)(value ? BlendMode.DstAlpha : BlendMode.SrcAlpha));
			material.SetInt("_DstFactor", (int)BlendMode.OneMinusSrcAlpha);
            if (aggregateActive) material.SetFloat("_HitConflict", value ? 1 : 0);
		}

        private void AcquireMaterial(bool aggregate)
        {
            if (sharedMaterial != null && aggregateActive == aggregate) return;
            ReleaseMaterial();
            aggregateActive = aggregate;
            materialKey = (sourceMaterial, isSfx, aggregate);
            if (!sharedMaterials.TryGetValue(materialKey, out sharedMaterial))
            {
                var copy = aggregate ? new Material(aggregateShader) : new Material(sourceMaterial);
                if (aggregate) copy.CopyPropertiesFromMaterial(sourceMaterial);
                sharedMaterial = new SharedMaterial { Material = copy };
                sharedMaterials.Add(materialKey, sharedMaterial);
            }
            sharedMaterial.Users++;
            material = sharedMaterial.Material;
            particleRenderer.sharedMaterial = material;
            SetTexture(Texture);
            SetConflictBlend(useConflictBlend);
        }
        private void ReleaseMaterial()
        {
            if (sharedMaterial == null) return;
            if (--sharedMaterial.Users == 0)
            {
                sharedMaterials.Remove(materialKey);
                Destroy(sharedMaterial.Material);
            }
            sharedMaterial = null;
        }
        private void OnDestroy() => ReleaseMaterial();
	}
}
