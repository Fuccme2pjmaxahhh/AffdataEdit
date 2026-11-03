using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Arcade.Util.Pooling;
using UnityEngine;
using UnityEngine.Rendering;

namespace Arcade.Gameplay
{
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
        private static readonly Dictionary<(Material, bool), SharedMaterial> sharedMaterials = new Dictionary<(Material, bool), SharedMaterial>();
        private (Material, bool) materialKey;
        private SharedMaterial sharedMaterial;
        private CancellationTokenSource effectCancellation;

		private void Awake()
		{
			Effect.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
			var renderer = Effect.GetComponent<ParticleSystemRenderer>();
            materialKey = (renderer.sharedMaterial, isSfx);
            if (!sharedMaterials.TryGetValue(materialKey, out sharedMaterial))
            {
                sharedMaterial = new SharedMaterial { Material = new Material(renderer.sharedMaterial) };
                sharedMaterials.Add(materialKey, sharedMaterial);
            }
            sharedMaterial.Users++;
            material = sharedMaterial.Material;
			renderer.sharedMaterial = material;
			renderer.sortingLayerName = "Effect";
			renderer.renderingLayerMask = 1u & ~ArcGameplayManager.Instance.SelectionLayerMask;
			renderer.minParticleSize = 0;
			renderer.maxParticleSize = 1;
			renderer.alignment = isSfx ? ParticleSystemRenderSpace.View : ParticleSystemRenderSpace.Local;
			var main = Effect.main;
			main.simulationSpace = ParticleSystemSimulationSpace.World;
			main.scalingMode = ParticleSystemScalingMode.Local;
			main.simulationSpeed = 1;
			main.duration = isSfx ? .48f : .256f;
			main.startLifetime = isSfx ? .48f : .26f;
			SetTexture(Texture);
			SetConflictBlend(useConflictBlend);
		}

		public void PlayAt(Vector3 pos)
		{
            bool startLifetime = Available;
            Available = false;
			var camera = ArcEffectManager.Instance.EffectPlane.ReferenceCamera;
			transform.SetPositionAndRotation(pos, camera.transform.rotation);
			float depth = Vector3.Dot(pos - camera.transform.position, camera.transform.forward);
			float height = camera.orthographic ? camera.orthographicSize * 2
				: 2 * Mathf.Max(.0001f, depth) * Mathf.Tan(camera.fieldOfView * Mathf.Deg2Rad / 2);
			var main = Effect.main;
			main.startSize = 9.5f * 1.5f * 1.8f * height / 40f * Mathf.Min(camera.aspect / (16f / 9), 1);
			Effect.Emit(new ParticleSystem.EmitParams { position = pos, applyShapeToPosition = true }, 1);
            endTime = Time.timeAsDouble + Effect.main.startLifetime.constantMax;
            if (startLifetime)
            {
                effectCancellation = CancellationTokenSource.CreateLinkedTokenSource(destroyCancellationToken);
                WaitForEndAsync(effectCancellation).Forget();
            }
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
		}

		private void OnDestroy()
		{
            if (sharedMaterial == null) return;
            sharedMaterial.Users--;
            if (sharedMaterial.Users == 0)
            {
                sharedMaterials.Remove(materialKey);
                Destroy(sharedMaterial.Material);
            }
		}
	}
}
