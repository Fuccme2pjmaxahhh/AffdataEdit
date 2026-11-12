using System.Collections.Generic;
using UnityEngine;

namespace Arcade.Gameplay
{
    // One owner for all long-note instances, independent of note lifetimes.
    public sealed class ArcLongNoteParticlePool : MonoBehaviour
    {
        public sealed class Instance
        {
            public ParticleSystem Particles;
            public Material Material;
            public ParticleSystem.MinMaxCurve Size, Speed, VelocityX, VelocityY, VelocityZ;
        }

        private static ArcLongNoteParticlePool current;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticState() => current = null;

        private readonly Stack<Instance> available = new Stack<Instance>();
        private readonly List<Instance> instances = new List<Instance>();
        private const int MaxIdleInstances = 32;

        public static ArcLongNoteParticlePool Get()
        {
            if (current) return current;
            var root = new GameObject("Long note particles");
            root.transform.SetParent(ArcEffectManager.Instance.transform, false);
            current = root.AddComponent<ArcLongNoteParticlePool>();
            return current;
        }

        public Instance Acquire(int layer)
        {
            Instance instance;
            if (available.Count > 0) instance = available.Pop();
            else
            {
                var particles = Instantiate(Resources.Load<GameObject>("AlphaLongNote/LongNote"), transform)
                    .GetComponent<ParticleSystem>();
                particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                var main = particles.main;
                var velocity = particles.velocityOverLifetime;
                var renderer = particles.GetComponent<ParticleSystemRenderer>();
                instance = new Instance
                {
                    Particles = particles,
                    Material = new Material(renderer.sharedMaterial),
                    Size = main.startSize,
                    Speed = main.startSpeed,
                    VelocityX = velocity.x,
                    VelocityY = velocity.y,
                    VelocityZ = velocity.z
                };
                renderer.sharedMaterial = instance.Material;
                renderer.sortingLayerName = "Effect";
                renderer.renderingLayerMask = 1u & ~ArcGameplayManager.Instance.SelectionLayerMask;
                // Alpha's UI camera faces XY directly; our gameplay camera is tilted.
                renderer.alignment = ParticleSystemRenderSpace.View;
                // Keep live particles attached to the hit point instead of leaving a trail
                // at previous positions when an Arc or FloatLane hold moves.
                main.simulationSpace = ParticleSystemSimulationSpace.Local;
                main.scalingMode = ParticleSystemScalingMode.Local;
                main.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;
                instances.Add(instance);
            }
            instance.Particles.gameObject.layer = layer;
            instance.Particles.gameObject.SetActive(true);
            return instance;
        }

        public void Release(Instance instance)
        {
            instance.Particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            instance.Particles.gameObject.SetActive(false);
            // Retain the current chart's working set during playback; repeated dense bursts
            // should reuse their particles instead of allocating/destroying them each time.
            if (available.Count < MaxIdleInstances || ArcGameplayManager.Instance && ArcGameplayManager.Instance.IsPlaying)
                available.Push(instance);
            else DestroyInstance(instance);
        }

        private void DestroyInstance(Instance instance)
        {
            instances.Remove(instance);
            if (instance.Material) Destroy(instance.Material);
            if (instance.Particles) Destroy(instance.Particles.gameObject);
        }

        internal static void TrimIdle(int retainedCount)
        {
            if (!current) return;
            while (current.available.Count > retainedCount)
                current.DestroyInstance(current.available.Pop());
        }

        private void OnDestroy()
        {
            foreach (var instance in instances)
                if (instance.Material) Destroy(instance.Material);
            if (current == this) current = null;
        }
    }
}
