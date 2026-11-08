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
		}
		private void OnDestroy()
		{
			toastMotion.TryCancel();
			if (backgroundSprite) Destroy(backgroundSprite);
			if (backgroundTexture) Destroy(backgroundTexture);
			if (Instance == this) Instance = null;
		}
		public void Show(string text)
		{
			if (!isActiveAndEnabled || string.IsNullOrWhiteSpace(text)) return;
			toastMotion.TryCancel();
			// Replace a visible message only after it has faded out.
			if (noticeGroup.alpha > 0f) FadeNotice(0f, () => BeginNotice(text));
			else BeginNotice(text);
		}
		private void BeginNotice(string text)
		{
			Content.text = text.Replace("\r\n", " ").Replace('\r', ' ').Replace('\n', ' ');
			RefreshAppearance();
			FitMessage();
			float stay = Stay * Mathf.Max(1f, Content.preferredHeight / 60f);
			FadeNotice(1f, () => FadeNotice(0f, null, stay));
		}
		private void FadeNotice(float alpha, Action onComplete, float delay = 0f)
		{
			toastMotion = LMotion.Create(noticeGroup.alpha, alpha, Duration)
				.WithEase(Ease.OutQuad).WithScheduler(MotionScheduler.UpdateIgnoreTimeScale)
				.WithDelay(delay).WithOnComplete(onComplete)
				.Bind(value => noticeGroup.alpha = value);
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
