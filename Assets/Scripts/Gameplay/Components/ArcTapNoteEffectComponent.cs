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
		public ParticleSystem Effect;
		public Texture2D Texture;
		[SerializeField] private bool isSfx;
		[SerializeField] private bool useConflictBlend;
		private Material material;
        private CancellationTokenSource effectCancellation;

		private void Awake()
		{
			Effect.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
			var renderer = Effect.GetComponent<ParticleSystemRenderer>();
			material = new Material(renderer.sharedMaterial);
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
			Available = false;
			var camera = ArcEffectManager.Instance.EffectPlane.ReferenceCamera;
			transform.SetPositionAndRotation(pos, camera.transform.rotation);
			float depth = Vector3.Dot(pos - camera.transform.position, camera.transform.forward);
			float height = camera.orthographic ? camera.orthographicSize * 2
				: 2 * Mathf.Max(.0001f, depth) * Mathf.Tan(camera.fieldOfView * Mathf.Deg2Rad / 2);
			var main = Effect.main;
			main.startSize = 9.5f * 1.5f * 1.8f * height / 40f * Mathf.Min(camera.aspect / (16f / 9), 1);
			Effect.Emit(new ParticleSystem.EmitParams { position = pos, applyShapeToPosition = true }, 1);
			effectCancellation?.Cancel();
            effectCancellation = CancellationTokenSource.CreateLinkedTokenSource(destroyCancellationToken);
            WaitForEndAsync(effectCancellation).Forget();
		}

        private async UniTask WaitForEndAsync(CancellationTokenSource cancellation)
        {
            try
            {
                await UniTask.WaitForSeconds(Effect.main.startLifetime.constantMax, cancellationToken: cancellation.Token);
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
			if (material) Destroy(material);
		}
	}
}
