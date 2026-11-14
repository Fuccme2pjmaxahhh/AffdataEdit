using UnityEngine;
using System.Collections.Generic;
using Arcade.Gameplay.Chart;

namespace Arcade.Gameplay
{
    // Alpha's UI-camera world units are mapped into the gameplay camera's effect plane.
    public sealed class ArcLongNoteEffect : MonoBehaviour
    {
        private static readonly List<ArcLongNoteEffect> activeEffects = new List<ArcLongNoteEffect>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticState() { activeEffects.Clear(); AppearanceScale = 1; }


        internal static void ResetAll()
        {
            // ResetState disables each effect and removes it from the registry.
            for (int i = activeEffects.Count - 1; i >= 0; i--) activeEffects[i].ResetState();
        }
        internal static float AppearanceScale = 1;
        public Color StartColor = new Color(.4f, .2f, .4f, 1);
        public Color EndColor = new Color(.4f, .2f, .8f, 1);
        public Texture Texture;
        private ParticleSystem particles;
        private Material material;
        private ArcLongNoteParticlePool pool;
        private ArcLongNoteParticlePool.Instance instance;
        private bool requested, emitting, played;
        private float stopAt = -1;

        // Emitting or still inside the 200 ms stop delay.
        public bool Active => requested || emitting;
        internal ArcArcVisual FollowTarget { get; set; }

        public void RefreshSkin()
        {
            if (!particles) return;
            Color start = StartColor, end = EndColor;
            var diff = new Color(.1f, .1f, .1f, .5f);
            var max = new Gradient();
            max.SetKeys(new[] { new GradientColorKey(start - diff, 0), new GradientColorKey(start, .5f),
                new GradientColorKey(start + diff, 1) }, new[] { new GradientAlphaKey(1, 0), new GradientAlphaKey(1, 1) });
            var min = new Gradient();
            min.SetKeys(new[] { new GradientColorKey(end, 0), new GradientColorKey(end, 1) },
                new[] { new GradientAlphaKey(1, 0), new GradientAlphaKey(1, 1) });
            var main = particles.main;
            main.startColor = new ParticleSystem.MinMaxGradient(min, max);
            material.mainTexture = Texture;
        }

        public void SetEmission(bool value)
        {
            if (value && !requested && !emitting) activeEffects.Add(this);
            requested = value;
            if (value) stopAt = -1;
            else if (emitting && stopAt < 0) stopAt = Time.time + .2f;
            enabled = requested || emitting;
        }

        public void ResetState()
        {
            stopAt = -1;
            requested = emitting = played = false;
            Release();
            enabled = false;
        }

        private void Release()
        {
            if (pool && particles) pool.Release(instance);
            instance = null;
            particles = null;
            material = null;
        }

        private void LateUpdate()
        {
            if (stopAt >= 0 && Time.time >= stopAt)
            {
                Release();
                emitting = false;
                stopAt = -1;
            }
            if (!requested && !emitting)
            {
                enabled = false;
                return;
            }
            if (!particles)
            {
                pool = ArcLongNoteParticlePool.Get();
                instance = pool.Acquire(gameObject.layer);
                particles = instance.Particles;
                material = instance.Material;
                RefreshSkin();
            }
            // The Arc renderer updates the hit position during Update. Position first,
            // then prewarm/emit, so no particles are born at the prefab's old location.
            if (FollowTarget != null)
            {
                var arc = FollowTarget.Arc;
                if (arc.GroupHide() || (!arc.RenderHead && ArcGameplayManager.Instance.ChartTiming == arc.Timing))
                    FollowTarget.UpdateJudgePosition();
                transform.position = FollowTarget.JudgePosition;
            }
            UpdateSimulationSpace();
            if (!emitting)
            {
                var main = particles.main;
                main.prewarm = played || !ArcGameplayManager.Instance.IsPlaying
                    || (Arcade.Compose.AdeOperationManager.Instance && Arcade.Compose.AdeOperationManager.Instance.HasOngoingOperation);
                particles.Play();
                // A cold start must not wait for the first emission interval to become visible.
                if (!main.prewarm) particles.Emit(1);
                emitting = played = true;
            }
        }

        private void UpdateSimulationSpace()
        {
            var manager = ArcEffectManager.Instance;
            var camera = manager.EffectPlane.ReferenceCamera;
            Vector3 position = manager.EffectPlane.GetPositionOnPlane(transform.position);
            float depth = Vector3.Dot(position - camera.transform.position, camera.transform.forward);
            float height = camera.orthographic ? camera.orthographicSize * 2
                : 2 * Mathf.Max(.0001f, depth) * Mathf.Tan(camera.fieldOfView * Mathf.Deg2Rad / 2);
            // Alpha's UI camera has orthographicSize=20. Local particle scaling
            // ignores the CanvasScaler parent: size AND velocity are world units.
            float worldUnitScale = height / 40f;
            particles.transform.SetPositionAndRotation(position, camera.transform.rotation);
            float scale = 1.1f * AppearanceScale * Mathf.Min(camera.aspect / (16f / 9), 1);
            particles.transform.localScale = new Vector3(scale, scale, 1);
            // Match Alpha's screen-relative size and motion using the UI camera's
            // 40-unit visible height, not the canvas's 1080 logical pixels.
            var main = particles.main;
            main.startSize = Scale(instance.Size, worldUnitScale);
            main.startSpeed = Scale(instance.Speed, worldUnitScale);
            var velocity = particles.velocityOverLifetime;
            velocity.x = Scale(instance.VelocityX, worldUnitScale);
            velocity.y = Scale(instance.VelocityY, worldUnitScale);
            velocity.z = Scale(instance.VelocityZ, worldUnitScale);
        }

        private static ParticleSystem.MinMaxCurve Scale(ParticleSystem.MinMaxCurve curve, float factor)
        {
            if (curve.mode == ParticleSystemCurveMode.Constant)
                curve.constant *= factor;
            else if (curve.mode == ParticleSystemCurveMode.TwoConstants)
            {
                curve.constantMin *= factor;
                curve.constantMax *= factor;
            }
            else curve.curveMultiplier *= factor;
            return curve;
        }

        private void OnDisable()
        {
            activeEffects.Remove(this);
            stopAt = -1;
            requested = emitting = false;
            if (!gameObject.activeInHierarchy) played = false;
            Release();
        }
        private void OnDestroy() { activeEffects.Remove(this); Release(); }
    }
}
