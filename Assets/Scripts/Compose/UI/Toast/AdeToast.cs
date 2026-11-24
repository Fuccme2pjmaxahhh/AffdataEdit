using System;
using UnityEngine;
using Text = TMPro.TMP_Text;
using LitMotion;
using UnityEngine.UI;

namespace Arcade.Compose
{
	public class AdeToast : MonoBehaviour
	{
		public const float Duration = 0.12f;
		// Save notices share their fade with the glow, so they ease in and out more softly.
		private const float SavedFadeIn = .28f, SavedFadeOut = .6f, SavedStay = .45f;
		private bool savedFade;
		public const float Stay = 1f;
		public static AdeToast Instance { get; private set; }

		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
		private static void ResetStaticState() => Instance = null;
		public RectTransform ContentRect;
		public Text Content;
		[SerializeField] private CanvasGroup noticeGroup;
		private MotionHandle toastMotion;
		private RectTransform panel;
		private Sprite backgroundSprite;
		private Texture2D backgroundTexture;
		internal Image Background { get; private set; }
		private const float HorizontalPadding = 80;
		private const string SaveIconPath = "ArcaeaEditor/EditorCanvas/Bars/Left/View/Top/Save/Image";
		private MotionHandle saveIconMotion;
		private bool glowActive;
		private Image savedGlow, savedRim, saveIcon;
		private Texture2D glowTexture, rimTexture;
		private Sprite glowSprite, rimSprite;
		private Sprite saveSprite, checkSprite, rippleSprite;
		private Texture2D rippleTexture;
		private Image saveRipple;
		private MotionHandle rippleMotion;
		private Image.Type saveIconType;
		private Color saveIconColor;
		private bool saveIconThemed, saveIconFlashing, unsaved;
		private Image unsavedDot;
		private Texture2D dotTexture;
		private Sprite dotSprite;

		private void Awake()
		{
			Instance = this;
			panel = (RectTransform)ContentRect.parent;
			panel.anchorMin = panel.anchorMax = new Vector2(.5f, 1f);
			panel.pivot = new Vector2(.5f, 1f);
			panel.anchoredPosition = Vector2.zero;
			ContentRect.offsetMin = new Vector2(HorizontalPadding, 8);
			ContentRect.offsetMax = new Vector2(-HorizontalPadding, -8);
			Content.raycastTarget = false;
			Content.textWrappingMode = TMPro.TextWrappingModes.Normal;
			noticeGroup.interactable = noticeGroup.blocksRaycasts = false;
			transform.SetAsLastSibling();
			CreateBackground();
			RefreshAppearance();
		}
		private void OnDisable()
		{
			toastMotion.TryCancel();
			if (noticeGroup) noticeGroup.alpha = 0;
			EndGlow();
			RestoreSaveIcon();
		}
		private void OnDestroy()
		{
			toastMotion.TryCancel();
			if (glowSprite) Destroy(glowSprite);
			if (glowTexture) Destroy(glowTexture);
			if (rimSprite) Destroy(rimSprite);
			if (rimTexture) Destroy(rimTexture);
			if (rippleSprite) Destroy(rippleSprite);
			if (rippleTexture) Destroy(rippleTexture);
			RestoreSaveIcon();
			if (backgroundSprite) Destroy(backgroundSprite);
			if (backgroundTexture) Destroy(backgroundTexture);
			if (dotSprite) Destroy(dotSprite);
			if (dotTexture) Destroy(dotTexture);
			if (Instance == this) Instance = null;
		}
		public void Show(string text) => ShowNotice(text, false);
		private void ShowNotice(string text, bool saved)
		{
			if (!isActiveAndEnabled || string.IsNullOrWhiteSpace(text)) return;
			savedFade = saved;
            // Binding instructions must remain visible above the open settings dialog.
            var canvas = GetComponent<Canvas>();
            if (canvas) { canvas.overrideSorting = true; canvas.sortingOrder = 100; }
			toastMotion.TryCancel();
			// Saving again while the same notice shows brightens it again from where it is.
			// Any other visible message is cleared quickly before the new one fades in.
			// The light belongs to save notices only.
			if (!saved) EndGlow();
			if (noticeGroup.alpha <= 0f || (saved && Content.text == SingleLine(text))) BeginNotice(text);
			else FadeNotice(0f, () => BeginNotice(text), 0f, true);
		}
		// Save confirmation: the notice, an accent line along the top bar, and a check on the Save button.
		public void ShowSaved(string text)
		{
			ShowNotice(text, true);
			if (!isActiveAndEnabled) return;
			PlaySavedGlow();
			PlaySaveIconCheck();
		}
		// Accent light from the top bar's lower edge: the edge lights up and the light pours
		// down into the chart view, then drains back up into the bar. It runs on the notice's
		// fade, and every save replays it from the start.
		private void PlaySavedGlow()
		{
			if (!savedGlow)
			{
				// Bars/Top/View ends at the visible lower edge of the bar (the bar sprite's last
				// 20 units are transparent).
				var bar = transform.parent as RectTransform;
				if (!bar) return;
				CreateGlowSprites();
				savedGlow = CreateGlowImage(bar, "Saved glow", glowSprite);
				savedRim = CreateGlowImage(bar, "Saved glow rim", rimSprite);
			}
			glowActive = true;
			savedGlow.enabled = savedRim.enabled = true;
			SyncGlow(0f, true);
		}
		private static Image CreateGlowImage(RectTransform parent, string name, Sprite sprite)
		{
			var go = new GameObject(name, typeof(RectTransform), typeof(LayoutElement), typeof(Image));
			go.GetComponent<LayoutElement>().ignoreLayout = true;
			go.transform.SetParent(parent, false);
			go.transform.SetAsFirstSibling();
			var image = go.GetComponent<Image>();
			image.sprite = sprite;
			image.raycastTarget = false;
			var rect = image.rectTransform;
			rect.anchorMin = rect.anchorMax = new Vector2(.5f, 0f);
			rect.pivot = new Vector2(.5f, 1f);
			return image;
		}
		// progress: eased 0 to 1 while the notice fades in, then the notice alpha (1 to 0) while it fades out.
		private void SyncGlow(float progress, bool rising)
		{
			if (!glowActive || !savedGlow || !savedRim) return;
			// Centred under the notice, top edge on the bar's visible edge.
			var parentRect = (RectTransform)savedGlow.rectTransform.parent;
			float centre = parentRect.InverseTransformPoint(panel.position).x - parentRect.rect.center.x;
			float width = Mathf.Max(720f, panel.rect.width * 1.8f);
			float reach = Mathf.Clamp01(progress);
			// The edge lights quickly as the light starts to pour, and dims as it drains away.
			float rim = rising ? Mathf.Clamp01(progress * 3f) : reach;
			Color accent = AdeUiTheme.Current.Accent;
			var body = savedGlow.rectTransform;
			body.anchoredPosition = new Vector2(centre, 0f);
			body.sizeDelta = new Vector2(width, 150f * reach);
			savedGlow.color = new Color(accent.r, accent.g, accent.b, reach > 0f ? .85f : 0f);
			var edge = savedRim.rectTransform;
			edge.anchoredPosition = new Vector2(centre, 0f);
			edge.sizeDelta = new Vector2(width, 2f);
			savedRim.color = new Color(accent.r, accent.g, accent.b, .85f * rim);
		}
		private void EndGlow()
		{
			glowActive = false;
			if (savedGlow) savedGlow.enabled = false;
			if (savedRim) savedRim.enabled = false;
		}
		private void CreateGlowSprites()
		{
			const int width = 256, height = 128;
			var pixels = new Color[width * height];
			for (int y = 0; y < height; y++)
				for (int x = 0; x < width; x++)
				{
					// y = height - 1 touches the bar edge; the light falls off downward and toward both ends.
					float u = Mathf.Abs((x + .5f) / width * 2f - 1f), v = 1f - (y + .5f) / height;
					float across = Mathf.Pow(Mathf.Clamp01(1f - u * u), 1.6f);
					pixels[y * width + x] = new Color(1, 1, 1, across * Mathf.Pow(1f - v, 3.2f) * .45f);
				}
			glowTexture = new Texture2D(width, height, TextureFormat.RGBA32, false)
				{ name = "Saved glow", filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
			glowTexture.SetPixels(pixels);
			glowTexture.Apply(false, true);
			glowSprite = Sprite.Create(glowTexture, new Rect(0, 0, width, height), new Vector2(.5f, .5f));
			// Crisp bright edge, brightest under the notice.
			var rimPixels = new Color[width * 4];
			for (int y = 0; y < 4; y++)
				for (int x = 0; x < width; x++)
				{
					float u = Mathf.Abs((x + .5f) / width * 2f - 1f);
					rimPixels[y * width + x] = new Color(1, 1, 1, Mathf.Pow(Mathf.Clamp01(1f - u), .5f) * .95f);
				}
			rimTexture = new Texture2D(width, 4, TextureFormat.RGBA32, false)
				{ name = "Saved glow rim", filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
			rimTexture.SetPixels(rimPixels);
			rimTexture.Apply(false, true);
			rimSprite = Sprite.Create(rimTexture, new Rect(0, 0, width, 4), new Vector2(.5f, .5f));
		}
		// Unsaved-changes dot on the Save button. Returns false until the button can be found.
		public bool SetUnsaved(bool value)
		{
			unsaved = value;
			if (!FindSaveIcon()) return false;
			if (!unsavedDot) CreateUnsavedDot();
			unsavedDot.color = AdeUiTheme.Current.Accent;
			unsavedDot.enabled = unsaved && !saveIconFlashing;
			return true;
		}
		private bool FindSaveIcon()
		{
			if (!saveIcon)
			{
				var icon = GameObject.Find(SaveIconPath);
				if (icon) saveIcon = icon.GetComponent<Image>();
			}
			return saveIcon;
		}
		private void CreateUnsavedDot()
		{
			const int size = 32;
			var pixels = new Color[size * size];
			for (int y = 0; y < size; y++)
				for (int x = 0; x < size; x++)
				{
					float distance = new Vector2(x + .5f - size / 2f, y + .5f - size / 2f).magnitude;
					pixels[y * size + x] = new Color(1, 1, 1, Mathf.Clamp01(size / 2f - distance));
				}
			dotTexture = new Texture2D(size, size, TextureFormat.RGBA32, false)
				{ name = "Unsaved dot", filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
			dotTexture.SetPixels(pixels);
			dotTexture.Apply(false, true);
			dotSprite = Sprite.Create(dotTexture, new Rect(0, 0, size, size), new Vector2(.5f, .5f));
			var dot = new GameObject("Unsaved dot", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
			dot.GetComponent<LayoutElement>().ignoreLayout = true;
			var rect = (RectTransform)dot.transform;
			rect.SetParent(saveIcon.rectTransform, false);
			rect.anchorMin = rect.anchorMax = new Vector2(1f, 1f);
			rect.pivot = new Vector2(.5f, .5f);
			rect.anchoredPosition = new Vector2(-2f, -2f);
			rect.sizeDelta = new Vector2(9f, 9f);
			unsavedDot = dot.GetComponent<Image>();
			unsavedDot.sprite = dotSprite;
			unsavedDot.raycastTarget = false;
		}
		private static Color CheckColor => AdeUiTheme.Dark ? new Color32(0x4A, 0xDE, 0x80, 0xFF) : new Color32(0x16, 0xA3, 0x4A, 0xFF);
		private Color SaveIconRestColor => saveIconThemed ? AdeUiTheme.IconColor : saveIconColor;
		private static float BackOut(float t)
		{
			const float c1 = 1.70158f, c3 = c1 + 1f;
			float p = t - 1f;
			return 1f + c3 * p * p * p + c1 * p * p;
		}
		// Save icon shrinks away, the check draws itself in left to right with a ripple,
		// then the check shrinks away and the save icon grows back.
		private void PlaySaveIconCheck()
		{
			FindSaveIcon();
			if (!checkSprite) checkSprite = Resources.Load<Sprite>("AffdataEdit/Icons/Checkmark");
			if (!saveIcon || !checkSprite) return;
			saveIconMotion.TryCancel();
			if (!saveIconFlashing)
			{
				saveSprite = saveIcon.sprite;
				saveIconType = saveIcon.type;
				saveIconColor = saveIcon.color;
				saveIconThemed = SameColor(saveIconColor, AdeUiTheme.IconColor);
				saveIconFlashing = true;
			}
			if (unsavedDot) unsavedDot.enabled = false;
			// Saving again while the check shows redraws it.
			if (saveIcon.sprite == checkSprite) { DrawCheck(); return; }
			var rect = saveIcon.rectTransform;
			Color from = saveIcon.color;
			float scale = rect.localScale.x;
			saveIconMotion = LMotion.Create(0f, 1f, .12f)
				.WithEase(Ease.InQuad).WithScheduler(MotionScheduler.UpdateIgnoreTimeScale)
				.WithOnComplete(DrawCheck)
				.Bind(t =>
				{
					if (!saveIcon) return;
					float s = Mathf.Lerp(scale, .6f, t);
					rect.localScale = new Vector3(s, s, 1f);
					saveIcon.color = new Color(from.r, from.g, from.b, from.a * (1f - t));
				});
		}
		private void DrawCheck()
		{
			if (!saveIcon) return;
			var rect = saveIcon.rectTransform;
			saveIcon.sprite = checkSprite;
			saveIcon.type = Image.Type.Filled;
			saveIcon.fillMethod = Image.FillMethod.Horizontal;
			saveIcon.fillOrigin = (int)Image.OriginHorizontal.Left;
			saveIcon.fillAmount = 0f;
			saveIcon.color = CheckColor;
			PlayRipple();
			saveIconMotion = LMotion.Create(0f, 1f, .34f)
				.WithEase(Ease.Linear).WithScheduler(MotionScheduler.UpdateIgnoreTimeScale)
				.WithOnComplete(() => saveIconMotion = LMotion.Create(0f, 1f, .01f)
					.WithScheduler(MotionScheduler.UpdateIgnoreTimeScale).WithDelay(1.1f)
					.WithOnComplete(HideCheck)
					.Bind(_ => { }))
				.Bind(t =>
				{
					if (!saveIcon) return;
					// The short stroke draws quickly, the long stroke eases out.
					saveIcon.fillAmount = 1f - Mathf.Pow(1f - t, 2.2f);
					float s = Mathf.LerpUnclamped(.75f, 1f, BackOut(t));
					rect.localScale = new Vector3(s, s, 1f);
				});
		}
		private void HideCheck()
		{
			if (!saveIcon) { saveIconFlashing = false; return; }
			var rect = saveIcon.rectTransform;
			Color check = saveIcon.color;
			saveIconMotion = LMotion.Create(0f, 1f, .14f)
				.WithEase(Ease.InQuad).WithScheduler(MotionScheduler.UpdateIgnoreTimeScale)
				.WithOnComplete(() =>
				{
					if (!saveIcon) return;
					saveIcon.sprite = saveSprite;
					saveIcon.type = saveIconType;
					saveIcon.fillAmount = 1f;
					saveIconMotion = LMotion.Create(0f, 1f, .2f)
						.WithEase(Ease.OutCubic).WithScheduler(MotionScheduler.UpdateIgnoreTimeScale)
						.WithOnComplete(RestoreSaveIcon)
						.Bind(t =>
						{
							if (!saveIcon) return;
							Color rest = SaveIconRestColor;
							float s = Mathf.Lerp(.7f, 1f, t);
							rect.localScale = new Vector3(s, s, 1f);
							saveIcon.color = new Color(rest.r, rest.g, rest.b, rest.a * t);
						});
				})
				.Bind(t =>
				{
					if (!saveIcon) return;
					float s = Mathf.Lerp(1f, .6f, t);
					rect.localScale = new Vector3(s, s, 1f);
					saveIcon.color = new Color(check.r, check.g, check.b, check.a * (1f - t));
				});
		}
		private void PlayRipple()
		{
			if (!saveRipple)
			{
				const int size = 64;
				var pixels = new Color[size * size];
				for (int y = 0; y < size; y++)
					for (int x = 0; x < size; x++)
					{
						float d = new Vector2(x + .5f - size / 2f, y + .5f - size / 2f).magnitude / (size / 2f);
						pixels[y * size + x] = new Color(1, 1, 1, Mathf.Clamp01(1f - Mathf.Abs(d - .86f) * 9f));
					}
				rippleTexture = new Texture2D(size, size, TextureFormat.RGBA32, false)
					{ name = "Saved ripple", filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
				rippleTexture.SetPixels(pixels);
				rippleTexture.Apply(false, true);
				rippleSprite = Sprite.Create(rippleTexture, new Rect(0, 0, size, size), new Vector2(.5f, .5f));
				var ripple = new GameObject("Saved ripple", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
				ripple.GetComponent<LayoutElement>().ignoreLayout = true;
				var rippleRect = (RectTransform)ripple.transform;
				rippleRect.SetParent(saveIcon.transform.parent, false);
				// Behind the icon, above the button background.
				rippleRect.SetSiblingIndex(saveIcon.transform.GetSiblingIndex());
				rippleRect.anchorMin = rippleRect.anchorMax = saveIcon.rectTransform.anchorMin == saveIcon.rectTransform.anchorMax
					? saveIcon.rectTransform.anchorMin : new Vector2(.5f, .5f);
				rippleRect.anchoredPosition = saveIcon.rectTransform.anchoredPosition;
				saveRipple = ripple.GetComponent<Image>();
				saveRipple.sprite = rippleSprite;
				saveRipple.raycastTarget = false;
			}
			var rect = saveRipple.rectTransform;
			Vector2 iconSize = saveIcon.rectTransform.rect.size;
			rect.position = saveIcon.rectTransform.position;
			rect.sizeDelta = Vector2.one * Mathf.Max(iconSize.x, iconSize.y);
			saveRipple.enabled = true;
			rippleMotion.TryCancel();
			Color color = CheckColor;
			rippleMotion = LMotion.Create(0f, 1f, .55f)
				.WithEase(Ease.OutCubic).WithScheduler(MotionScheduler.UpdateIgnoreTimeScale)
				.WithOnComplete(() => { if (saveRipple) saveRipple.enabled = false; })
				.Bind(t =>
				{
					if (!saveRipple) return;
					float s = Mathf.Lerp(.6f, 1.7f, t);
					rect.localScale = new Vector3(s, s, 1f);
					saveRipple.color = new Color(color.r, color.g, color.b, .6f * (1f - t));
				});
		}
		private void RestoreSaveIcon()
		{
			saveIconMotion.TryCancel();
			rippleMotion.TryCancel();
			if (saveRipple) saveRipple.enabled = false;
			if (!saveIconFlashing) return;
			saveIconFlashing = false;
			if (!saveIcon) return;
			saveIcon.sprite = saveSprite;
			saveIcon.type = saveIconType;
			saveIcon.fillAmount = 1f;
			// A theme switch during the check would make the captured colour stale.
			saveIcon.color = SaveIconRestColor;
			saveIcon.rectTransform.localScale = Vector3.one;
			if (unsavedDot) unsavedDot.enabled = unsaved;
		}
		private static bool SameColor(Color a, Color b) =>
			Mathf.Abs(a.r - b.r) < .01f && Mathf.Abs(a.g - b.g) < .01f && Mathf.Abs(a.b - b.b) < .01f && Mathf.Abs(a.a - b.a) < .01f;
		private void BeginNotice(string text)
		{
			Content.text = SingleLine(text);
			RefreshAppearance();
			FitMessage();
			float stay = (savedFade ? SavedStay : Stay) * Mathf.Max(1f, Content.preferredHeight / 60f);
			FadeNotice(1f, () => FadeNotice(0f, EndGlow, stay));
		}
		private static string SingleLine(string text) => text.Replace("\r\n", " ").Replace('\r', ' ').Replace('\n', ' ');
		private void FadeNotice(float alpha, Action onComplete, float delay = 0f, bool quick = false)
		{
			bool rising = alpha > 0f;
			bool soft = savedFade && !quick;
			float duration = !soft ? Duration : rising ? SavedFadeIn : SavedFadeOut;
			Ease ease = !soft ? Ease.OutQuad : rising ? Ease.OutCubic : Ease.InOutSine;
			float from = noticeGroup.alpha;
			// Driven by progress, so the light pours again even when the notice is already visible.
			toastMotion = LMotion.Create(0f, 1f, duration)
				.WithEase(ease).WithScheduler(MotionScheduler.UpdateIgnoreTimeScale)
				.WithDelay(delay).WithOnComplete(onComplete)
				.Bind(progress =>
				{
					float value = Mathf.Lerp(from, alpha, progress);
					noticeGroup.alpha = value;
					SyncGlow(rising ? progress : value, rising);
				});
		}
		private void FitMessage()
		{
			float available = Mathf.Max(1, ((RectTransform)transform).rect.width - 24);
			float width = Mathf.Min(available, Mathf.Max(320,
				Content.GetPreferredValues(Content.text).x + HorizontalPadding * 2));
			float height = Mathf.Max(((RectTransform)transform).rect.height,
				Content.GetPreferredValues(Content.text, Mathf.Max(1, width - HorizontalPadding * 2), 0).y + 16);
			panel.sizeDelta = new Vector2(width, height);
		}
		internal void RefreshAppearance()
		{
			if (Background) Background.color = AdeUiTheme.Current.Chrome;
			Content.color = AdeUiTheme.Current.Text;
		}
		private void OnRectTransformDimensionsChange()
		{
			if (panel && Content && noticeGroup && noticeGroup.alpha > 0) FitMessage();
		}
		private void CreateBackground()
		{
			const int width = 192;
			var pixels = new Color[width * 2];
			for (int x = 0; x < width; x++)
			{
				float alpha = Mathf.SmoothStep(0, 1f, Mathf.Min(x, width - 1 - x) / 48f);
				pixels[x] = pixels[x + width] = new Color(1, 1, 1, alpha);
			}
			backgroundTexture = new Texture2D(width, 2, TextureFormat.RGBA32, false)
				{ name = "Toast soft edges", filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
			backgroundTexture.SetPixels(pixels);
			backgroundTexture.Apply(false, true);
			backgroundSprite = Sprite.Create(backgroundTexture, new Rect(0, 0, width, 2),
				new Vector2(.5f, .5f), 100, 0, SpriteMeshType.FullRect, new Vector4(48, 0, 48, 0));
			var background = new GameObject("Toast background", typeof(RectTransform), typeof(Image));
			background.transform.SetParent(panel, false);
			background.transform.SetAsFirstSibling();
			var image = background.GetComponent<Image>();
			Background = image;
			image.sprite = backgroundSprite;
			image.type = Image.Type.Sliced;
			image.raycastTarget = false;
			image.rectTransform.anchorMin = Vector2.zero;
			image.rectTransform.anchorMax = Vector2.one;
			image.rectTransform.offsetMin = image.rectTransform.offsetMax = Vector2.zero;
		}
	}
}
