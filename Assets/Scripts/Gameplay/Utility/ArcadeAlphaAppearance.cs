using UnityEngine;

namespace Arcade.Gameplay
{
    // Alpha 1.12.2 presentation; AE keeps its batching and long-note clipping.
    public static class ArcadeAlphaAppearance
    {
        public const float ArcOpacity = 225f / 255f;

        public static float TapAlpha(float forwardDistance)
            => Mathf.Clamp01((90f - forwardDistance) / 10f);

        public static float TapLength(float forwardDistance, float velocity)
            => Mathf.Max(0, 2f + 3.4f * (forwardDistance / 100f)
                * Mathf.Clamp(Mathf.Abs(velocity) * .015f, 1f, 1.5f));

        public static float ArcDistanceAlpha(float forwardDistance, bool zeroDuration)
        {
            float threshold = zeroDuration ? 80f : 85f;
            return forwardDistance < threshold ? 1f
                : Mathf.Lerp(225f / 255f, 75f / 255f, (forwardDistance - threshold) * .1f);
        }

        public static float TiltTarget(float leftExtent, float rightExtent, float enwidenScale)
            => Mathf.Clamp((leftExtent - rightExtent) / enwidenScale / 4.25f, -1, 1) * .05f;

        public static float StepTilt(float current, float target, float deltaTime)
        {
            float delta = target - current;
            return Mathf.Abs(delta) > .0001f
                ? current + (target > .0001f ? .04f : .02f) * delta * (deltaTime / (1f / 240f))
                : target;
        }

        public static float BackgroundOffset(Vector2 designResolution)
            => Mathf.Abs(designResolution.y - 720f) < .001f && designResolution.x > 1280f
                ? -designResolution.x * 50f / 1560f : 0f;
    }
}
