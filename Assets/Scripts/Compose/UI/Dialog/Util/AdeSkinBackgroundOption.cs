using System;
using Arcade.Compose.Dialog;
using UnityEngine;
using UnityEngine.UI;
using Text = TMPro.TMP_Text;

namespace Arcade.Compose
{
	public class AdeSkinBackgroundOption : MonoBehaviour
	{
		public Image Preview;
		public Text Label;
		public string BackgroundName;
		public bool External;
		public string PresetName;
		public Color SelectedColor;
		public Color UnselectedColor;
		private bool cachedPreview;
		private RectTransform viewport;
		private readonly Vector3[] corners = new Vector3[4];

		public void OnSelected()
		{
			if (!string.IsNullOrEmpty(PresetName)) AdeSkinDialogContent.Instance.SelectPreset(PresetName);
			else AdeSkinDialogContent.Instance.SelectBackground(BackgroundName, External);
		}

		public void Initialize(string bg, bool external, Sprite image)
		{
			BackgroundName = bg;
			Label.text = bg;
			Preview.sprite = image;
			Preview.enabled = image;
			cachedPreview = !image;
			// Options are created while the skin card/page is closed. Include their inactive
			// scroll view, or the narrow toolbar Canvas is mistaken for the flyout viewport.
			var scroll = GetComponentInParent<ScrollRect>(true);
			viewport = scroll ? scroll.viewport : null;
			if (!viewport)
			{
				var mask = GetComponentInParent<RectMask2D>(true);
				if (mask) viewport = mask.rectTransform;
			}
			if (!viewport)
			{
				var mask = GetComponentInParent<Mask>(true);
				if (mask) viewport = mask.rectTransform;
			}
			if (!viewport)
			{
				var canvas = GetComponentInParent<Canvas>(true);
				if (canvas) viewport = (RectTransform)canvas.transform;
			}
			External = external;
		}

		private void LateUpdate()
		{
			if (!cachedPreview || !AdeSkinHost.Instance || !PreviewDistance(out float distance)) return;
			Sprite preview = AdeSkinHost.Instance.GetBackgroundPreview(BackgroundName, External, distance > 0, distance);
			if (Preview.sprite != preview) Preview.sprite = preview;
			Preview.enabled = preview;
		}

		private bool IsVisible() => PreviewDistance(out float distance) && distance == 0;

		private bool PreviewDistance(out float distance)
		{
			distance = 0;
			if (!viewport) return true;
			Preview.rectTransform.GetWorldCorners(corners);
			Vector2 min = new Vector2(float.PositiveInfinity, float.PositiveInfinity), max = -min;
			for (int i = 0; i < 4; i++)
			{
				Vector2 point = viewport.InverseTransformPoint(corners[i]);
				min = Vector2.Min(min, point); max = Vector2.Max(max, point);
			}
			Rect clip = viewport.rect;
			if (max.x < clip.xMin || min.x > clip.xMax) return false;
			distance = Mathf.Max(0, Mathf.Max(clip.yMin - max.y, min.y - clip.yMax));
			return distance <= clip.height;
		}

		private void OnDisable()
		{
			if (cachedPreview && Preview) { Preview.sprite = null; Preview.enabled = false; }
		}

		public void SetSelected(bool selected)
		{
			Preview.color = selected ? SelectedColor : UnselectedColor;
		}
	}
}
