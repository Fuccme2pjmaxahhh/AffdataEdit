using System.Collections.Generic;
using Arcade.Gameplay;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Text = TMPro.TMP_Text;

namespace Arcade.Compose
{
	// Editor chrome palettes; gameplay artwork and chart colors keep their own skin.
	public sealed partial class AdeUiTheme : MonoBehaviour
	{
		private const string EditorSceneName = "ArcEditor";
		private const string PreferenceKey = "AffdataEdit.DarkUi";

		public static AdeUiTheme Instance { get; private set; }

		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
		private static void ResetStaticState()
		{
			Instance = null;
			extraRoots.Clear();
			toolbarToggleStates.Clear();
			Revision = 0;
		}
		public static bool Dark { get; private set; } = true;

		private struct Entry
		{
			public string Sprite;
			public bool Text;
			public Color32 Light, Dark, Highlighted, Pressed;
			// Toolbar tiles blend into the surrounding chrome at rest.
			public bool HiddenAtRest;
			public bool HasStates => Highlighted.a != 0;
		}

		private static Color32 C(uint rgb) => new Color32((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb, 255);
		private static Entry Image(string sprite, uint light, uint dark, uint highlighted = 0, uint pressed = 0) => new Entry
		{
			Sprite = sprite, Light = C(light), Dark = C(dark),
			Highlighted = highlighted == 0 ? default : C(highlighted), Pressed = pressed == 0 ? default : C(pressed),
		};

		// Specific sprites first; a null sprite matches any image.
		private static Entry HiddenAtRest(Entry entry)
		{
			entry.HiddenAtRest = true;
			return entry;
		}

		private static readonly Entry ToolbarEntry = HiddenAtRest(Image("ToolBackground", 0xE6E6E6, 0x181818, 0x2A2D2E, 0x37373D));

		// VS Code Dark Modern colours. Specific sprites first; a null sprite matches any image.
		private static readonly Entry[] Palette =
		{
			Image("Top", 0xFFFFFF, 0x181818), Image("Left", 0xFFFFFF, 0x181818),
			Image("Right", 0xFFFFFF, 0x181818), Image("Bottom", 0xFFFFFF, 0x181818),
			ToolbarEntry,
			Image("ToolBackground", 0xFFFFFF, 0x37373D, 0x45454B, 0x505056),
			Image("UISprite", 0xFFFFFF, 0x313131, 0x3C3C3C, 0x454545),
			Image("InputFieldBackground", 0xFFFFFF, 0x313131, 0x353535, 0x3C3C3C),
			Image("Background", 0xFFFFFF, 0x3C3C3C, 0x454545, 0x4F4F4F),
			Image("Knob", 0xFFFFFF, 0xCCCCCC, 0xE0E0E0, 0xB0B0B0),
			Image("UIMask", 0xFFFFFF, 0x252526),
			Image("Refresh", 0x000000, 0xC5C5C6),
			Image(null, 0x7F7F7F, 0xC5C5C5),
			Image(null, 0x323232, 0xCCCCCC),
			Image(null, 0xF5F5F5, 0x2B2B2B),
			new Entry { Text = true, Light = C(0x323232), Dark = C(0xCCCCCC) },
			// Scene-serialized controls use the ChatGPT palette's text colors.
			new Entry { Text = true, Light = C(0x0D0D0D), Dark = C(0xEDEDED) },
		};

		private readonly List<Graphic> graphics = new List<Graphic>();
		private readonly List<Selectable> selectables = new List<Selectable>();
		private readonly Dictionary<Graphic, Selectable> targets = new Dictionary<Graphic, Selectable>();
		private readonly List<Transform> roots = new List<Transform>();
		// UI moved or built outside the editor canvas and dialog layers (the settings drawer).
		private static readonly List<Transform> extraRoots = new List<Transform>();

		public static void AddRoot(Transform root)
		{
			if (extraRoots.Contains(root)) return;
			extraRoots.Add(root);
			Refresh();
		}
		private bool refreshRequested = true;
		private Vector2Int screenSize;
		private Sprite[] darkSprites;
		private Sprite closeIcon;

		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
		private static void Bootstrap()
		{
			Dark = AdeSettings.GetInt(PreferenceKey, 1) != 0;
			LoadPresets();
			SceneManager.sceneLoaded -= OnSceneLoaded;
			SceneManager.sceneLoaded += OnSceneLoaded;
		}

		private static void OnSceneLoaded(UnityEngine.SceneManagement.Scene scene, UnityEngine.SceneManagement.LoadSceneMode mode)
		{
			if (scene.name == EditorSceneName)
				new GameObject(nameof(AdeUiTheme)).AddComponent<AdeUiTheme>();
		}

		private void Awake()
		{
			Instance = this;
		}

		private void Start()
		{
			CollectRoots();
			AddToggle();
			ApplySkinSprites();
			Sweep();
		}

		private void LateUpdate()
		{
			if (!refreshRequested && screenSize.x == Screen.width && screenSize.y == Screen.height) return;
			Sweep();
		}

		public static void SetDark(bool dark)
		{
			if (Dark == dark) return;
			Dark = dark;
			AdeSettings.SetInt(PreferenceKey, dark ? 1 : 0);
			ApplySelectedPreset();
		}

		// Aligns a dialog's controls as soon as it opens instead of on the next sweep.
		public static void OnDialogOpened()
		{
			if (!Instance) return;
			Refresh();
			Canvas.ForceUpdateCanvases();
			Instance.AlignDialogControls();
		}

		// Coalesce UI changes into one refresh after the frame's controls have been built.
		public static void Refresh()
		{
			if (Instance) Instance.refreshRequested = true;
		}

		// Called after the skin's UI sprites are (re)applied.
		public static void OnSkinApplied()
		{
			if (Instance) Instance.ApplySkinSprites();
			Refresh();
		}

		private void CollectRoots()
		{
			roots.Clear();
			var editor = GameObject.Find("EditorCanvas");
			if (editor)
			{
				foreach (Transform child in editor.transform)
					if (child.name != "InGame") roots.Add(child);
			}
			extraRoots.RemoveAll(root => !root);
			roots.AddRange(extraRoots);
			if (AdeDialogManager.Instance)
			{
				roots.Add(AdeDialogManager.Instance.Opening);
				roots.Add(AdeDialogManager.Instance.Closed);
			}
		}

		private void Sweep()
		{
			refreshRequested = false;
			screenSize = new Vector2Int(Screen.width, Screen.height);
			CollectRoots();
			ApplyWindowTheme();
            staleGraphics.Clear();
            foreach (var pair in painted) if (!pair.Key) staleGraphics.Add(pair.Key);
            foreach (var graphic in staleGraphics) painted.Remove(graphic);
			foreach (Transform root in roots)
			{
				if (!root) continue;
				targets.Clear();
				root.GetComponentsInChildren(true, selectables);
				foreach (Selectable selectable in selectables)
				{
					StyleControl(selectable);
					if (selectable is TMPro.TMP_Dropdown dropdown) StyleDropdown(dropdown);
					if (selectable.transition == Selectable.Transition.ColorTint && selectable.targetGraphic)
						targets[selectable.targetGraphic] = selectable;
				}
				root.GetComponentsInChildren(true, graphics);
				foreach (Graphic graphic in graphics) Apply(graphic);
			}
			ApplyToolbarToggleStates();
			StyleEditorPanels();
			AlignDialogControls();
		}

		// Places the buttons and the close button EdgeGap inside the visible edge of the
		// dialog sprites, measured from the laid-out rects for a consistent margin.
		private void AlignDialogControls()
		{
			var skin = ArcSkinManager.Instance;
			if (!skin) return;
			foreach (AdeSingleDialog dialog in skin.SingleDialogs)
			{
				if (!dialog || !dialog.View.activeInHierarchy) continue;
				AlignButton(dialog.CompleteButton, dialog.DialogBackground, 0);
				var basic = dialog.GetComponent<Dialog.AdeBasicSingleDialogContent>();
				if (basic && basic.CopyButton && basic.CopyButton.gameObject.activeSelf)
					AlignButton(basic.CopyButton, dialog.DialogBackground, 1);
				AlignClose(dialog, dialog.DialogTop);
			}
			foreach (AdeDualDialog dialog in skin.DualDialogs)
			{
				if (!dialog || !dialog.View.activeInHierarchy) continue;
				AlignButton(dialog.RightButton, dialog.DialogBackground, 0);
				AlignButton(dialog.LeftButton, dialog.DialogBackground, 1);
				AlignClose(dialog, dialog.DialogTop);
			}
		}

		private static readonly Vector3[] corners = new Vector3[4];

		private static void AlignButton(Button button, UnityEngine.UI.Image background, int slot)
		{
			if (!button || !background) return;
			var rect = (RectTransform)button.transform;
			var parent = (RectTransform)rect.parent;
			background.rectTransform.GetWorldCorners(corners);
			Vector2 edge = parent.InverseTransformPoint(corners[3]);
			Vector2 anchor = new Vector2(parent.rect.xMax, parent.rect.yMin);
			rect.anchoredPosition = edge - anchor + new Vector2(-BackgroundMargin.x - EdgeGap - slot * (ButtonWidth + ButtonGap), BackgroundMargin.y + EdgeGap);
		}

		private static void AlignClose(AdeDialog dialog, UnityEngine.UI.Image header)
		{
			Transform close = dialog.View.transform.Find(CloseName);
			if (!close || !header) return;
			var rect = (RectTransform)close;
			var parent = (RectTransform)rect.parent;
			header.rectTransform.GetWorldCorners(corners);
			Vector2 edge = parent.InverseTransformPoint(corners[2]);
			Vector2 anchor = new Vector2(parent.rect.xMax, parent.rect.yMax);
			rect.anchoredPosition = edge - anchor - new Vector2(HeaderMargin.x + EdgeGap, HeaderMargin.y + EdgeGap);
		}

        private void Apply(Graphic graphic)
        {
            // Playback HUD uses its own surface palette.
            if (IsPlaybackHud(graphic)) return;
            if (AdeToast.Instance && (graphic == AdeToast.Instance.Content || graphic == AdeToast.Instance.Background))
            {
                AdeToast.Instance.RefreshAppearance();
                return;
            }
            if (graphic.GetComponentInParent<ArcBitmapText>()) return;
            var button = graphic is Text ? graphic.GetComponentInParent<Button>(true) : null;
            if (button && button.image && button.image.sprite && button.image.sprite.name == "ThemeCloseButton")
            {
                graphic.color = C(0x141413);
                return;
            }
            if (button && button.image && button.image.sprite
                && (button.image.sprite.name == "ThemePrimary" || button.image.sprite.name == "ThemeSecondary"))
            {
                graphic.color = !button.IsInteractable() ? Current.Muted
                    : button.image.sprite.name == "ThemePrimary" ? Current.AccentText : Current.Text;
                return;
            }
            bool text = graphic is Text;
            string sprite = graphic is Image image && image.sprite ? image.sprite.name : null;
            if (sprite != null && sprite.StartsWith("Theme")) return;
            targets.TryGetValue(graphic, out Selectable selectable);
            Color32 color = graphic.color;
            Color32 normal = selectable ? (Color32)selectable.colors.normalColor : default;
            if (painted.TryGetValue(graphic, out var previous) && previous.Sprite == sprite
                && color.Equals(previous.Color) && normal.Equals(previous.Normal))
            {
                Paint(graphic, selectable, sprite, previous.Entry);
                return;
            }
            foreach (Entry entry in Palette)
            {
                if (entry.Text != text || (entry.Sprite != null && entry.Sprite != sprite)) continue;
                bool tinted = selectable && entry.HasStates;
                bool matches = Same(color, entry.Light) || Same(color, entry.Dark)
                    || (tinted && Same(color, Color.white) && Same(normal, entry.Dark));
                // Cloned controls may already use the selected palette.
                matches |= (text || entry.Sprite != null) && (!tinted || Same(color, Color.white))
                    && Same(tinted ? normal : color, Resolve(entry));
                if (!matches) continue;
                Paint(graphic, selectable, sprite, entry);
                return;
            }
            painted.Remove(graphic);
            // Preserve state colors supplied by tools (e.g. AUTO enabled).
            if (selectable && sprite == "ToolBackground")
                selectable.colors = ColorBlock.defaultColorBlock;
        }

        private void Paint(Graphic graphic, Selectable selectable, string sprite, Entry entry)
        {
            bool tinted = selectable && entry.HasStates;
            Color color = tinted ? Color.white : Resolve(entry);
            color.a = graphic.color.a;
            graphic.color = color;
            if (tinted) SetStates(selectable, entry);
            painted[graphic] = new PaintedGraphic { Entry = entry, Sprite = sprite, Color = graphic.color,
                Normal = selectable ? (Color32)selectable.colors.normalColor : default };
        }

        private static void SetStates(Selectable selectable, Entry entry)
        {
            Color color = Resolve(entry);
            var colors = ColorBlock.defaultColorBlock;
            colors.normalColor = colors.selectedColor = color;
            colors.highlightedColor = Color.Lerp(color, Current.Text, .08f);
            colors.pressedColor = Color.Lerp(color, Current.Text, .16f);
            colors.disabledColor = new Color(color.r, color.g, color.b, .5f);
            if (entry.HiddenAtRest)
            {
                // Like Settings navigation, keep alpha opaque so RGB/alpha interpolation
                // cannot create a dark flash between hover, press and release.
                colors.normalColor = colors.disabledColor = Current.Chrome;
                colors.highlightedColor = colors.selectedColor = Current.Selection;
                colors.pressedColor = Color.Lerp(Current.Selection, Current.Text, .12f);
            }
            selectable.colors = colors;
        }

		private static bool Same(Color32 a, Color32 b) => a.r == b.r && a.g == b.g && a.b == b.b;

		private void ApplySkinSprites()
		{
			if (!ArcSkinManager.Instance) return;
			if (darkSprites == null) darkSprites = LoadDarkSprites();
			if (darkSprites == null) return;
			CreateDialogShapes();
			ApplyDialogLayouts();
			Sprite Get(int i) => darkSprites[i];
			var skin = ArcSkinManager.Instance;
			foreach (AdeSingleDialog dialog in skin.SingleDialogs)
			{
				if (!dialog) continue;
				bool whiteClose = Current.WhiteCloseButton && dialog.ButtonText.text == "关闭";
				dialog.SetDialogSkin(Get(0), Get(1),
					whiteClose ? Shape("ThemeCloseButton", ControlRadius, Color.white, Current.Border) : Get(2), Get(3),
					whiteClose ? Shape("ThemeCloseButtonPressed", ControlRadius, C(0xE6E6E6), Current.Border) : Get(4));
				Apply(dialog.ButtonText);
				var basic = dialog.GetComponent<Dialog.AdeBasicSingleDialogContent>();
				if (basic && basic.CopyButton)
				{
					basic.CopyButton.image.sprite = Shape("ThemeSecondary", ControlRadius, Current.Input, Current.Border);
					basic.CopyButton.GetComponentInChildren<Text>().color = Current.Text;
				}
			}
			foreach (AdeDualDialog dialog in skin.DualDialogs)
			{
				if (!dialog) continue;
				dialog.SetDialogSkin(Get(0), Get(1), Get(5), Get(6), Get(7), Get(8), Get(9), Get(10));
				Apply(dialog.LeftButtonText);
				Apply(dialog.RightButtonText);
			}
			skin.Toast.sprite = Get(1);
			StylePlaybackHud();
		}

		// All palettes share the same title, close button and footer geometry.
		private void ApplyDialogLayouts()
		{
			var skin = ArcSkinManager.Instance;
			foreach (AdeSingleDialog dialog in skin.SingleDialogs)
			{
				if (!dialog) continue;
				LayoutTitle(dialog.Title);
				LayoutDialogFrame(dialog.DialogBackground, dialog.DialogTop);
				LayoutButton(dialog.CompleteButton, dialog.ButtonText);
				var basic = dialog.GetComponent<Dialog.AdeBasicSingleDialogContent>();
				if (basic && basic.CopyButton)
					LayoutButton(basic.CopyButton, basic.CopyButton.GetComponentInChildren<Text>());
				LayoutClose(dialog);
			}
			foreach (AdeDualDialog dialog in skin.DualDialogs)
			{
				if (!dialog) continue;
				LayoutTitle(dialog.Title);
				LayoutDialogFrame(dialog.DialogBackground, dialog.DialogTop);
				LayoutButton(dialog.LeftButton, dialog.LeftButtonText);
				LayoutButton(dialog.RightButton, dialog.RightButtonText);
				LayoutClose(dialog);
			}
		}

		private const float ButtonWidth = 170, ButtonHeight = 46, ButtonGap = 12, TitlePadding = 28, TitleHeight = 74;
		private const float EdgeGap = 20;
		private static readonly Vector2 BackgroundMargin = Vector2.zero, HeaderMargin = Vector2.zero;
		private const string TitleSpacerName = "ThemeTitleSpacer", CloseName = "ThemeClose";

		private static void LayoutDialogFrame(Image background, Image header)
		{
			background.rectTransform.sizeDelta = Vector2.zero;
			background.rectTransform.anchoredPosition = Vector2.zero;
			header.rectTransform.sizeDelta = new Vector2(0, TitleHeight);
			header.rectTransform.anchoredPosition = Vector2.zero;
			background.type = header.type = UnityEngine.UI.Image.Type.Sliced;
		}

		// The view's VerticalLayoutGroup owns the title rect, so the title leaves
		// the layout and a spacer of the same height keeps the content below in place.
		private static void LayoutTitle(Text title)
		{
			if (!title) return;
			var rect = (RectTransform)title.transform;
			var element = title.GetComponent<LayoutElement>();
			Transform spacer = rect.parent.Find(TitleSpacerName);
			if (!spacer && element)
			{
				var go = new GameObject(TitleSpacerName, typeof(RectTransform), typeof(LayoutElement));
				go.transform.SetParent(rect.parent, false);
				go.transform.SetSiblingIndex(rect.GetSiblingIndex());
				go.GetComponent<LayoutElement>().preferredHeight = TitleHeight;
			}

			if (element) element.ignoreLayout = true;
			rect.pivot = new Vector2(0.5f, 0.5f);
			rect.anchorMin = new Vector2(0, 1);
			rect.anchorMax = new Vector2(1, 1);
			rect.sizeDelta = new Vector2(-TitlePadding * 2, TitleHeight);
			rect.anchoredPosition = new Vector2(0, -TitleHeight / 2);
			title.alignment = TMPro.TextAlignmentOptions.Left;
			title.fontSize = 30;
			title.color = Current.Text;
		}

		// Footer buttons share one size; AlignDialogControls positions them from the right.
		private static void LayoutButton(Button button, Text label)
		{
			if (!button) return;
			var rect = (RectTransform)button.transform;
			var footer = rect.parent.GetComponent<LayoutElement>();
			if (footer) footer.preferredHeight = ButtonHeight + EdgeGap * 2;
			Vector2 anchor = new Vector2(1, 0);
			rect.anchorMin = rect.anchorMax = rect.pivot = anchor;
			// AlignDialogControls sets the position once the layout is known.

			rect.sizeDelta = new Vector2(ButtonWidth, ButtonHeight);
			button.image.type = UnityEngine.UI.Image.Type.Sliced;
			if (label) label.fontSize = 26;
		}

		private void LayoutClose(AdeDialog dialog)
		{
			if (darkSprites == null) return;
			Transform view = dialog.View.transform;
			Transform existing = view.Find(CloseName);
			if (existing)
			{
				existing.gameObject.SetActive(true);
                existing.GetComponent<Image>().sprite = darkSprites[5];
                existing.Find("Icon").GetComponent<Image>().color = Current.Text;
				// Cloned dialogs copy the button with its listeners stripped.
				existing.SetAsLastSibling();
				AdeUiKit.SetOnClick(existing.GetComponent<Button>(), dialog.Close);
				return;
			}
			var go = new GameObject(CloseName, typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
			go.GetComponent<LayoutElement>().ignoreLayout = true;
			var rect = (RectTransform)go.transform;
			rect.SetParent(view, false);
			rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(1, 1);
			rect.sizeDelta = new Vector2(42, 42);
			// The secondary button sprite only shows while hovered or pressed.
			var background = go.GetComponent<Image>();
			background.sprite = darkSprites[5];
			background.type = UnityEngine.UI.Image.Type.Sliced;
			var button = go.GetComponent<Button>();
			ColorBlock colors = ColorBlock.defaultColorBlock;
			colors.normalColor = new Color(1, 1, 1, 0);
			colors.selectedColor = new Color(1, 1, 1, 0);
			colors.highlightedColor = Color.white;
			colors.pressedColor = new Color(1.2f, 1.2f, 1.2f, 1);
			button.colors = colors;
			AdeUiKit.SetOnClick(button, dialog.Close);
			var icon = new GameObject("Icon", typeof(RectTransform), typeof(Image));
			var iconRect = (RectTransform)icon.transform;
			iconRect.SetParent(rect, false);
			iconRect.sizeDelta = new Vector2(24, 24);
			var iconImage = icon.GetComponent<Image>();
			iconImage.sprite = closeIcon;
			iconImage.color = Current.Text;
			iconImage.raycastTarget = false;
		}

		private static Sprite[] LoadDarkSprites()
		{
			string[] names =
			{
				"Dialog/DialogTop", "Dialog/DialogBackground",
				"Dialog/ButtonSingle", "Dialog/ButtonSingleDisabled", "Dialog/ButtonSinglePressed",
				"Dialog/ButtonDualLeft", "Dialog/ButtonDualLeftDisabled", "Dialog/ButtonDualLeftPressed",
				"Dialog/ButtonDualRight", "Dialog/ButtonDualRightDisabled", "Dialog/ButtonDualRightPressed",
				"SongInfo", "PlayPause/Play", "PlayPause/PlayPressed", "PlayPause/Pause", "PlayPause/PausePressed",
			};
			var sprites = new Sprite[names.Length];
			if (Instance) Instance.closeIcon = Resources.Load<Sprite>("AffdataEdit/Icons/Close");
			for (int i = 0; i < names.Length; i++)
			{
				sprites[i] = Resources.Load<Sprite>("AffdataEdit/Theme/" + (i < 11 ? "Default/" : "Dark/") + names[i]);
				if (!sprites[i])
				{
					Debug.LogWarning($"Dark theme sprite missing: {names[i]}");
					return null;
				}
			}
			return sprites;
		}

		// Settings row next to the playback sync toggle, like the arc cross-timing switch.
		private void AddToggle()
		{
			Toggle template = ArcadeComposeManager.Instance ? ArcadeComposeManager.Instance.PlaybackSyncToggle : null;
			Transform templateRow = template ? template.GetComponentInParent<HorizontalLayoutGroup>(true)?.transform : null;
			if (templateRow == null) return;
			GameObject row = AdeUiKit.CloneRow(templateRow, templateRow.parent, "DarkUi");
			row.transform.SetSiblingIndex(templateRow.GetSiblingIndex() + 1);
			row.GetComponentInChildren<Text>(true).text = "深色界面";
			Toggle toggle = row.GetComponentInChildren<Toggle>(true);
			toggle.SetIsOnWithoutNotify(Dark);
			toggle.onValueChanged.AddListener(SetDark);
		}
	}
}
