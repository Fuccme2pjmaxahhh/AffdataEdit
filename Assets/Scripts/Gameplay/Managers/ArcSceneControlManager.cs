using System.Collections.Generic;
using Arcade.Gameplay;
using Arcade.Gameplay.Chart;
using UnityEngine;
using UnityEngine.UI;
using System.Linq;
using Arcade.Aff;
using System;

public class ArcSceneControlManager : MonoBehaviour
{
	public static ArcSceneControlManager Instance { get; private set; }

	[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
	private static void ResetStaticState() => Instance = null;
	private void Awake()
	{
		Instance = this;
	}
	public Image BackgroundDarkenLayer;
	public SpriteRenderer[] TrackMainRenderers;
	public SpriteRenderer[] TrackBorderRenderers;
	public SpriteRenderer[] ExtraLaneRenderers;
	public SpriteRenderer[] ExtraTrackBorderRenderers;
	public SpriteRenderer[] MainLaneDividerRenderers;
	public SpriteRenderer[] ExtraLaneDividerRenderers;
	public SpriteRenderer[] ExtraLaneCriticalLineRenderers;
	public Transform SkyInput;
	[HideInInspector]
	[System.NonSerialized]
	public List<ArcSceneControl> SceneControls = new List<ArcSceneControl>();
    private readonly List<ArcSceneControl> orderedSceneControls = new List<ArcSceneControl>();
    public IReadOnlyList<ArcSceneControl> OrderedSceneControls => orderedSceneControls;
    public int Revision { get; private set; }
	private const float trackAnimationDefaultDuration = 1f;
	// Background darken mirrors AffdataPlay's TrackDisplay darken: alpha, timings and the
	// scale animation of the 1024px radial sprite (shown width = 96% of the background).
	private const float darkenAlpha = 0.9411765f;
	private const int darkenInDuration = 250, darkenOutDuration = 200, darkenScaleDuration = 250;
	private const float darkenShownScale = 1.2f, darkenTallScreenShownScaleY = 1.5f;
	private const float darkenHiddenScaleY = 0.2f, darkenInitialScale = 0.5f;
	private const float darkenShownWidthOfBackground = 1228.8f / 1280f;

	public void Load(List<ArcSceneControl> sceneControls)
	{
		ClearArcahvEffects();
		SceneControls = sceneControls;
        // Stable ordering preserves precedence for events at the same timestamp.
        orderedSceneControls.Clear();
        orderedSceneControls.AddRange(sceneControls.OrderBy(sc => sc.Timing));
        Revision++;
        LoadArcahvEffects();
		ResetScene();
	}
	public void Clean()
	{
		ClearArcahvEffects();
		SceneControls.Clear();
        orderedSceneControls.Clear();
        Revision++;
	}
	public void ResetScene()
	{
		UpdateEnwidenCameraRatio(0);
		UpdateLane(1, 0);
        UpdateArcahvEffects();
	}

	private void Update()
	{
		float startLaneOpacity = 1;
		float laneOpacity = 1;
		float darkenStart = 0, darkenTarget = 0;
		int darkenStartTiming = 0, darkenDuration = 0;
		bool darkenEaseOut = false;
		Vector2 darkenInitial = Vector2.one * (darkenInitialScale / darkenShownScale);
		Vector2 scaleStart = darkenInitial, scaleTarget = darkenInitial;
		int scaleStartTiming = 0, scaleDuration = 0;
		float enwidenCameraRatio = 0;
		float enwidenLaneRatio = 0;
		foreach (ArcTimingGroup tg in ArcTimingManager.Instance.timingGroups)
		{
			tg.GroupHide = false;
		}

		foreach (ArcSceneControl sc in orderedSceneControls)
		{
			if (sc.Timing > ArcGameplayManager.Instance.ChartTiming)
			{
				break;
			}
			switch (sc.Type)
			{
				case SceneControlType.TrackHide:
				case SceneControlType.TrackShow:
				case SceneControlType.TrackDisplay:
					{
						float animationDuration = trackAnimationDefaultDuration;
						if (sc.Type == SceneControlType.TrackDisplay)
						{
							animationDuration = sc.Duration;
						}
						float targetLaneOpacity = 0;
						if (sc.Type == SceneControlType.TrackShow)
						{
							targetLaneOpacity = 1;
						}
						else if (sc.Type == SceneControlType.TrackHide)
						{
							targetLaneOpacity = 0;
						}
						else if (sc.Type == SceneControlType.TrackDisplay)
						{
							animationDuration = sc.Duration;
							targetLaneOpacity = ((float)(sc.TrackDisplayValue % 256)) / 255;
						}
						float animationProgress = Mathf.Clamp01((ArcGameplayManager.Instance.ChartTiming - sc.Timing) / (animationDuration * 1000));
						laneOpacity = Mathf.Lerp(startLaneOpacity, targetLaneOpacity, animationProgress);
						if (ArcGameplayManager.Instance.ChartTiming - sc.Timing > animationDuration * 1000)
						{
							startLaneOpacity = targetLaneOpacity;
						}

						float nextDarken = darkenAlpha;
						if (sc.Type == SceneControlType.TrackShow)
						{
							nextDarken = 0;
						}
						else if (sc.Type == SceneControlType.TrackDisplay)
						{
							nextDarken = sc.TrackDisplayValue < 255 ? darkenAlpha : 0;
						}
						darkenStart = EvaluateDarken(darkenStart, darkenTarget, darkenStartTiming, darkenDuration, sc.Timing, darkenEaseOut);
						darkenTarget = nextDarken;
						darkenStartTiming = sc.Timing;
						darkenDuration = nextDarken > 0.5f ? darkenInDuration : darkenOutDuration;
						darkenEaseOut = nextDarken > 0.5f;
						scaleStart = EvaluateDarkenScale(scaleStart, scaleTarget, scaleStartTiming, scaleDuration, sc.Timing);
						scaleTarget = new Vector2(1, (nextDarken > 0 ? DarkenShownScaleY() : darkenHiddenScaleY) / darkenShownScale);
						scaleStartTiming = sc.Timing;
						scaleDuration = darkenScaleDuration;
					}
					break;
				case SceneControlType.HideGroup:
					var timingGroup = sc.TimingGroup;
					if (timingGroup != null)
					{
						timingGroup.GroupHide = sc.Enable;
					}
					break;
				case SceneControlType.EnwidenCamera:
					{
						int offset = ArcGameplayManager.Instance.ChartTiming - sc.Timing;
						if (offset <= 0)
						{
							enwidenCameraRatio = sc.Enable ? 0 : 1;
						}
						else if (offset >= sc.Duration)
						{
							enwidenCameraRatio = sc.Enable ? 1 : 0;
						}
						else
						{
							float precent = (ArcGameplayManager.Instance.ChartTiming - sc.Timing) / sc.Duration;
							enwidenCameraRatio = sc.Enable ? precent : 1 - precent;
						}
					}
					break;
				case SceneControlType.EnwidenLanes:
					{
						int offset = ArcGameplayManager.Instance.ChartTiming - sc.Timing;
						if (offset <= 0)
						{
							enwidenLaneRatio = sc.Enable ? 0 : 1;
						}
						else if (offset >= sc.Duration)
						{
							enwidenLaneRatio = sc.Enable ? 1 : 0;
						}
						else
						{
							float precent = (ArcGameplayManager.Instance.ChartTiming - sc.Timing) / sc.Duration;
							enwidenLaneRatio = sc.Enable ? precent : 1 - precent;
						}
					}
					break;
			}
		}
		UpdateEnwidenCameraRatio(enwidenCameraRatio);
		UpdateLane(laneOpacity, enwidenLaneRatio);
		int timing = ArcGameplayManager.Instance.ChartTiming;
		UpdateBackgroundDarkenLayer(
			EvaluateDarken(darkenStart, darkenTarget, darkenStartTiming, darkenDuration, timing, darkenEaseOut),
			EvaluateDarkenScale(scaleStart, scaleTarget, scaleStartTiming, scaleDuration, timing));
        UpdateArcahvEffects();
	}

    private GameObject arcahvRoot;
    private CanvasGroup arcahvDistort, arcahvDebris;
    private AnimationClip debrisShake, redlineFadeIn, redlineShake;
    private readonly Dictionary<ArcSceneControl, GameObject> redlines = new Dictionary<ArcSceneControl, GameObject>();

    private void LoadArcahvEffects()
    {
        if (!SceneControls.Any(sc => sc.Type == SceneControlType.ArcahvDistort
            || sc.Type == SceneControlType.ArcahvDebris || sc.Type == SceneControlType.Redline)) return;
        // Use the same background image space (1280 x 960) as Alpha, beneath gameplay/UI.
        Transform background = BackgroundDarkenLayer.transform.parent.Find("Image");
        arcahvRoot = Instantiate(Resources.Load<GameObject>("AlphaSceneControl/Arcahv"), background);
        arcahvDistort = arcahvRoot.transform.Find("Distort").GetComponent<CanvasGroup>();
        arcahvDebris = arcahvRoot.transform.Find("Debris").GetComponent<CanvasGroup>();
        debrisShake = Resources.Load<AnimationClip>("AlphaSceneControl/DebrisShake");
        redlineFadeIn = Resources.Load<AnimationClip>("AlphaSceneControl/redlineFadeIn");
        redlineShake = Resources.Load<AnimationClip>("AlphaSceneControl/redlineShake");
        var prefab = Resources.Load<GameObject>("AlphaSceneControl/RedlinePrefab");
        foreach (var sc in SceneControls)
        {
            if (sc.Type != SceneControlType.Redline) continue;
            var line = Instantiate(prefab, arcahvRoot.transform);
            line.GetComponent<RectTransform>().anchoredPosition = new Vector2(0, UnityEngine.Random.Range(300f, 760f));
            line.SetActive(false);
            redlines.Add(sc, line);
        }
    }

    private void ClearArcahvEffects()
    {
        if (arcahvRoot)
        {
            arcahvRoot.SetActive(false);
            Destroy(arcahvRoot);
        }
        arcahvRoot = null;
        arcahvDistort = arcahvDebris = null;
        redlines.Clear();
    }

    private void OnDestroy()
    {
        ClearArcahvEffects();
    }

    private struct ArcahvFade
    {
        public float Start, Target, Timing, Duration;
        public float ValueAt(float timing)
        {
            if (Duration <= 0) return Target;
            float progress = Mathf.Clamp01((timing - Timing) / Duration);
            // OutQuad, evaluated in chart time for seeking and pause.
            return Mathf.Lerp(Start, Target, 1 - (1 - progress) * (1 - progress));
        }
        public void Apply(ArcSceneControl sc)
        {
            Start = ValueAt(sc.Timing);
            Target = Mathf.Clamp01(sc.EffectValue / 255f);
            Timing = sc.Timing;
            Duration = (sc.Duration == 0 ? 1 : sc.Duration) * 1000;
        }
    }

    private void UpdateArcahvEffects()
    {
        if (!arcahvRoot) return;
        int timing = ArcGameplayManager.Instance.ChartTiming;
        var distort = new ArcahvFade();
        var debris = new ArcahvFade();
        foreach (var sc in orderedSceneControls)
        {
            if (sc.Timing > timing) break;
            if (sc.Type == SceneControlType.ArcahvDistort) distort.Apply(sc);
            else if (sc.Type == SceneControlType.ArcahvDebris) debris.Apply(sc);
        }
        arcahvDistort.alpha = distort.ValueAt(timing);
        arcahvDebris.alpha = debris.ValueAt(timing);
        debrisShake.SampleAnimation(arcahvDebris.gameObject,
            Mathf.Repeat((timing - debris.Timing) / 1000f, debrisShake.length));
        foreach (var pair in redlines)
        {
            var sc = pair.Key;
            float elapsed = (timing - sc.Timing) / 1000f;
            bool visible = elapsed >= 0 && elapsed <= sc.Duration;
            pair.Value.SetActive(visible);
            if (!visible) continue;
            if (elapsed < redlineFadeIn.length)
                redlineFadeIn.SampleAnimation(pair.Value, elapsed);
            else
                redlineShake.SampleAnimation(pair.Value,
                    Mathf.Repeat(elapsed - redlineFadeIn.length, redlineShake.length));
        }
    }

	private void UpdateEnwidenCameraRatio(float enwidenCameraRatio)
	{
		Vector3 SkyInputPosition = SkyInput.localPosition;
		SkyInputPosition.y = 5.5f * (1 + enwidenCameraRatio * .5f);
		SkyInput.localPosition = SkyInputPosition;
		ArcCameraManager.Instance.EnwidenRatio = enwidenCameraRatio;
	}

	private void UpdateLane(float laneOpacity, float enwidenLaneRatio)
	{
		foreach (var ExtraLaneRenderer in ExtraLaneRenderers)
		{
			ExtraLaneRenderer.gameObject.SetActive(enwidenLaneRatio > 0);
			ExtraLaneRenderer.size = new Vector2(ExtraLaneRenderer.size.x, 53.5f + 100 * enwidenLaneRatio);
			ExtraLaneRenderer.color = new Color(1, 1, 1, enwidenLaneRatio * laneOpacity);
		}
		foreach (var TrackMainRenderer in TrackMainRenderers)
		{
			TrackMainRenderer.color = new Color(1, 1, 1, laneOpacity);
		}
		foreach (var TrackBorderRenderer in TrackBorderRenderers)
		{
			TrackBorderRenderer.gameObject.SetActive(enwidenLaneRatio < 1);
			TrackBorderRenderer.color = new Color(1, 1, 1, (1 - enwidenLaneRatio) * laneOpacity);
		}
		foreach (var ExtraTrackBorderRenderer in ExtraTrackBorderRenderers)
		{
			ExtraTrackBorderRenderer.gameObject.SetActive(enwidenLaneRatio > 0);
			ExtraTrackBorderRenderer.color = new Color(1, 1, 1, enwidenLaneRatio * laneOpacity);
		}
		foreach (var MainLaneDividerRenderer in MainLaneDividerRenderers)
		{
			MainLaneDividerRenderer.color = new Color(1, 1, 1, laneOpacity);
		}
		foreach (var ExtraLaneDividerRenderer in ExtraLaneDividerRenderers)
		{
			ExtraLaneDividerRenderer.gameObject.SetActive(enwidenLaneRatio > 0);
			ExtraLaneDividerRenderer.color = new Color(1, 1, 1, enwidenLaneRatio * laneOpacity);
		}
		foreach (var ExtraLaneCriticalLineRenderer in ExtraLaneCriticalLineRenderers)
		{
			ExtraLaneCriticalLineRenderer.gameObject.SetActive(enwidenLaneRatio > 0);
			ExtraLaneCriticalLineRenderer.color = new Color(1, 1, 1, enwidenLaneRatio);
		}
		ArcTimingManager.Instance.BeatlineEnwidenRatio = enwidenLaneRatio;
	}

	private static float EvaluateDarken(float start, float target, int startTiming, int duration, int timing, bool easeOut)
	{
		if (duration <= 0) return target;
		float progress = Mathf.Clamp01((float)(timing - startTiming) / duration);
		if (easeOut) progress = 1 - Mathf.Pow(1 - progress, 3);
		return Mathf.Lerp(start, target, progress);
	}

	private static Vector2 EvaluateDarkenScale(Vector2 start, Vector2 target, int startTiming, int duration, int timing)
	{
		if (duration <= 0) return target;
		float progress = Mathf.Clamp01((float)(timing - startTiming) / duration);
		return Vector2.Lerp(start, target, 1 - Mathf.Pow(1 - progress, 3));
	}

	// Taller viewports stretch the darken vertically (1280-wide design height 720 -> 960).
	private static float DarkenShownScaleY()
	{
		Camera camera = ArcCameraManager.Instance ? ArcCameraManager.Instance.GameplayCamera : null;
		float width = camera && camera.pixelWidth > 0 ? camera.pixelWidth : Screen.width;
		float height = camera && camera.pixelHeight > 0 ? camera.pixelHeight : Screen.height;
		float designHeight = Mathf.Max(720f, 1280f * height / width);
		return Mathf.Lerp(darkenShownScale, darkenTallScreenShownScaleY, Mathf.InverseLerp(720f, 960f, designHeight));
	}

	private RectTransform darkenBackground;
	private void UpdateBackgroundDarkenLayer(float alpha, Vector2 scale)
	{
		BackgroundDarkenLayer.color = new Color(0, 0, 0, alpha);
		if (!darkenBackground) darkenBackground = (RectTransform)BackgroundDarkenLayer.transform.parent.Find("Image");
		float size = darkenBackground.rect.width * darkenShownWidthOfBackground;
		BackgroundDarkenLayer.rectTransform.sizeDelta = new Vector2(size * scale.x, size * scale.y);
	}
}

