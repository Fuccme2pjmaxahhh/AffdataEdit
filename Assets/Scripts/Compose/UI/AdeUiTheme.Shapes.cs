using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Arcade.Compose
{
	public sealed partial class AdeUiTheme
	{
		private const float PanelRadius = 16, ControlRadius = 8;
		private readonly Dictionary<string, Sprite> shapes = new Dictionary<string, Sprite>();
		private readonly HashSet<Sprite> generatedShapes = new HashSet<Sprite>();
		private const string ControlShapePrefix = "ThemeControl/";

		internal static Sprite RoundedSurface => Instance.Shape("ThemeRoundedSurface", ControlRadius, Color.white, Color.white);
        internal static Sprite PanelSurface => Instance.Shape("ThemePanel", PanelRadius, Current.Panel, Current.Border);
        internal static Sprite ReadOnlySurface => Instance.Shape("ThemeReadOnly", ControlRadius, Current.Chrome, Current.Border);
        internal static Sprite ContextMenuSurface(bool highlighted) => Instance.Shape(
            highlighted ? "ThemeContextMenuHover" : "ThemeContextMenu", ControlRadius,
            highlighted ? Current.Selection : Current.Panel, highlighted ? Current.Accent : Current.Border);
        internal static Sprite VerticalContextMenuSurface => Instance.Shape(
            "ThemeVerticalContextMenu", PanelRadius, Current.Panel, Current.Border);
		internal static Sprite VerticalContextMenuHighlight(int corners) => Instance.Shape(
			"ThemeVerticalContextMenuHighlight" + corners, PanelRadius, Color.white, Color.white, roundedCorners: corners);

		// One geometry for the fill and its 1px inset border. Slicing preserves the radius
		// at any panel size; 2x sampling keeps the edge smooth on scaled canvases.
		private Sprite Shape(string name, float radius, Color fill, Color border, bool topOnly = false, int roundedCorners = 15)
		{
			string key = Current.Id + "/" + name;
			if (shapes.TryGetValue(key, out Sprite sprite)) return sprite;
			sprite = Resources.Load<Sprite>("AffdataEdit/Theme/Serialized/" + Current.Id + "/" + name.Replace('/', '_'));
			if (sprite) { shapes.Add(key, sprite); return sprite; }
			const int scale = 2;
			int size = Mathf.CeilToInt((radius * 2 + 4) * scale);
			float half = size / (2f * scale);
			var pixels = new Color32[size * size];
			for (int y = 0; y < size; y++)
			{
				for (int x = 0; x < size; x++)
				{
					int corner = y >= size / 2 ? (x < size / 2 ? 1 : 2) : (x < size / 2 ? 4 : 8);
					float r = (topOnly && y < size / 2) || (roundedCorners & corner) == 0 ? 0 : radius;
					Vector2 q = new Vector2(Mathf.Abs((x + 0.5f) / scale - half), Mathf.Abs((y + 0.5f) / scale - half)) - Vector2.one * (half - r);
					float distance = new Vector2(Mathf.Max(q.x, 0), Mathf.Max(q.y, 0)).magnitude + Mathf.Min(Mathf.Max(q.x, q.y), 0) - r;
					Color color = Color.Lerp(border, fill, Mathf.Clamp01(0.5f - (distance + 1) * scale));
					color.a *= Mathf.Clamp01(0.5f - distance * scale);
					pixels[y * size + x] = color;
				}
			}
			var texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { name = key, filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
			texture.SetPixels32(pixels);
			texture.Apply(false, true);
			float slice = (radius + 1) * scale;
			sprite = Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100 * scale, 0, SpriteMeshType.FullRect,
				new Vector4(slice, topOnly ? scale : slice, slice, slice));
			sprite.name = name;
			shapes.Add(key, sprite);
			generatedShapes.Add(sprite);
			return sprite;
		}

		private void CreateDialogShapes()
		{
			darkSprites[0] = Shape("ThemeHeader", PanelRadius, Current.Chrome, Current.Border, true);
			darkSprites[1] = Shape("ThemePanel", PanelRadius, Current.Panel, Current.Border);
			for (int i = 2; i <= 10; i += 3)
			{
				bool primary = i != 5;
				darkSprites[i] = Shape(primary ? "ThemePrimary" : "ThemeSecondary", ControlRadius, primary ? Current.Accent : Current.Input, primary ? Current.Accent : Current.Border);
				darkSprites[i + 1] = Shape(primary ? "ThemePrimaryDisabled" : "ThemeSecondaryDisabled", ControlRadius, Current.Input, Current.Border);
				darkSprites[i + 2] = Shape(primary ? "ThemePrimaryPressed" : "ThemeSecondaryPressed", ControlRadius, Color.Lerp(primary ? Current.Accent : Current.Input, Current.Chrome, .15f), Current.Border);
			}
		}

		private void StyleControl(Selectable selectable)
		{
			if (selectable is Slider slider && slider.handleRect && slider.handleRect.TryGetComponent<Image>(out var handle))
				handle.preserveAspect = true;
			if (selectable is Scrollbar || !(selectable.targetGraphic is Image image) || !image.sprite) return;
			string name = image.sprite.name;
			if (name == "ToolBackground" && selectable is Button)
			{
				if (!selectable.GetComponent<AdeToolbarPointerFocus>())
					selectable.gameObject.AddComponent<AdeToolbarPointerFocus>();
				SetStates(selectable, ToolbarEntry);
			}
			// Serialized asset names replace the control path separator with an underscore.
			if (name.StartsWith(ControlShapePrefix) || name.StartsWith("ThemeControl_"))
				name = name.Substring(ControlShapePrefix.Length);
			if (name != "UISprite" && name != "InputFieldBackground") return;
			image.sprite = Shape(ControlShapePrefix + name, ControlRadius, Current.Input, Current.Border);
			image.type = UnityEngine.UI.Image.Type.Sliced;
			image.color = Color.white;
			var colors = ColorBlock.defaultColorBlock;
			colors.highlightedColor = new Color(1.15f, 1.15f, 1.15f);
			colors.pressedColor = new Color(0.9f, 0.9f, 0.9f);
			colors.selectedColor = new Color(1.08f, 1.08f, 1.08f);
			colors.disabledColor = new Color(0.7f, 0.7f, 0.7f, 0.6f);
			selectable.colors = colors;
		}

		private void OnDestroy()
		{
			foreach (Sprite sprite in generatedShapes)
			{
				Destroy(sprite.texture);
				Destroy(sprite);
			}
			if (hudMaterial) Destroy(hudMaterial);
			if (Instance == this) Instance = null;
		}
	}
}
