using System;
using System.IO;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Text = TMPro.TMP_Text;
using InputField = TMPro.TMP_InputField;
using Dropdown = TMPro.TMP_Dropdown;
using Newtonsoft.Json;
using Arcade.Gameplay;
using System.Text.RegularExpressions;

namespace Arcade.Compose.Dialog
{
	public class SkinPreference
	{
		public string SelectedSide;
		public string SelectedTheme;
		public string SelectedNote;
		public string SelectedBackground;
		public bool IsExternalBackground;
		public bool Customize;
		[JsonIgnore]
		public Side SkinSide
		{
			get
			{
				if (SelectedSide == "Light")
				{
					return Side.Light;
				}
				else if (SelectedSide == "Conflict")
				{
					return Side.Conflict;
				}
				else if (SelectedSide == "Colorless")
				{
					return Side.Colorless;
				}
				else if (SelectedSide == "Lephon")
				{
					return Side.Lephon;
				}
				SelectedSide = "Light";
				return Side.Light;
			}
			set
			{
				if (value == Side.Light)
				{
					SelectedSide = "Light";
				}
				else if (value == Side.Conflict)
				{
					SelectedSide = "Conflict";
				}
				else if (value == Side.Colorless)
				{
					SelectedSide = "Colorless";
				}
				else if (value == Side.Lephon)
				{
					SelectedSide = "Lephon";
				}
				else
				{
					SelectedSide = "Light";
				}
			}
		}
	}

	public class AdeSkinDialogContent : AdeDialogContent<AdeSingleDialog>
	{
		public static AdeSkinDialogContent Instance { get; private set; }

		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
		private static void ResetStaticState() => Instance = null;
		[SerializeField] private AdeRightSideCard sideCard;
		[SerializeField] private GameObject presetPage, customPage;
		[SerializeField] private Button presetTab, customTab;
		[SerializeField] private RectTransform customBackgroundContainer;
		private Dictionary<string, AdeSkinBackgroundOption> customBackgroundButtons;
		private int tabThemeRevision = -1;
        private AdeAnimatedTabs tabSelector;
        private InputField customBackgroundSearch;
        private RectTransform skinView;
        private Transform resourceActions;

		public GameObject BackgroundOptionPrefab;

		public InputField BackgroundSearchInput;

		public Dropdown SideDropdown;
		public Dropdown ThemeDropdown;
		public Dropdown NoteDropdown;

		public RectTransform InternalBackgroundContainer;
		public RectTransform ExternalBackgroundContainer;

		public Text InternalBackgroundLabel;
		public Text ExternalBackgroundLabel;

		private Dictionary<string, int> ThemeIds;
		private Dictionary<string, int> NoteIds;
		private Dictionary<string, AdeSkinBackgroundOption> InternalBackgroundButtons;
		private Dictionary<string, AdeSkinBackgroundOption> ExternalBackgroundButtons;


		private void Awake()
		{
			Instance = this;
			Dialog.OnClose += OnClose;
			if (sideCard) sideCard.Closed += OnClose;
		}

		private void OnDestroy()
		{
			tabSelector?.Dispose();
            Dialog.OnClose -= OnClose;
			if (sideCard) sideCard.Closed -= OnClose;
		}

		private void Start()
		{
			tabSelector = new AdeAnimatedTabs(presetTab, customTab);
            skinView = sideCard ? sideCard.transform.Find("View") as RectTransform : null;
            customBackgroundSearch = Instantiate(BackgroundSearchInput, ExternalBackgroundContainer.parent, false);
            customBackgroundSearch.name = "CustomBackgroundSearch";
            var actions = ExternalBackgroundContainer.parent.Find("资源");
            resourceActions = actions;
            customBackgroundSearch.transform.SetSiblingIndex(actions ? actions.GetSiblingIndex() + 1 : customBackgroundContainer.GetSiblingIndex() - 1);
            customBackgroundSearch.onValueChanged = new InputField.OnChangeEvent();
            customBackgroundSearch.onEndEdit = new InputField.SubmitEvent();
            customBackgroundSearch.SetTextWithoutNotify("");
            if (customBackgroundSearch.placeholder is Text searchHint) searchHint.text = "搜索背景";
            customBackgroundSearch.onValueChanged.AddListener(UpdateBackgroundFilterResult);
            StyleBackgroundSearch(BackgroundSearchInput);
            StyleBackgroundSearch(customBackgroundSearch);
            SideDropdown.onValueChanged.AddListener(SelectSide);
			NoteDropdown.onValueChanged.AddListener(SelectNote);
			ThemeDropdown.onValueChanged.AddListener(SelectTheme);
			BackgroundSearchInput.onValueChanged.AddListener(UpdateBackgroundFilterResult);
			LoadPreferences();
			LoadSkinOptions();
			VerifySkinOptions();
			LoadExternalBackgroundOptions();
			VerifyExternalBackgroundOptions();
			Initialize();
			Debug.Log($"skin:{JsonConvert.SerializeObject(preference)}");
		}

		private SkinPreference preference = new SkinPreference();


		public void OpenExternalBackgroundFolder()
		{
			if (!Directory.Exists(AdeSkinHost.Instance.ExternalBackgroundFolderPath))
			{
				try
				{
					Directory.CreateDirectory(AdeSkinHost.Instance.ExternalBackgroundFolderPath);
				}
				catch (IOException e)
				{
					Debug.LogWarning($"Cannot load external background, the background path is not a directory:{e}");
					return;
				}
			}
			Util.Shell.FileBrowser.OpenExplorer(AdeSkinHost.Instance.ExternalBackgroundFolderPath);
		}

		private void LoadSkinOptions()
		{
			ThemeDropdown.ClearOptions();
			List<string> themes = new List<string>(AdeSkinHost.Instance.skinData.ThemeDatas.Keys);
			ThemeIds = new Dictionary<string, int>();
			for (int i = 0; i < themes.Count; i++)
			{
				ThemeIds.Add(themes[i], i);
			}
			ThemeDropdown.AddOptions(themes);

			NoteDropdown.ClearOptions();
			List<string> notes = new List<string>(AdeSkinHost.Instance.skinData.NoteDatas.Keys);
			NoteIds = new Dictionary<string, int>();
			for (int i = 0; i < notes.Count; i++)
			{
				NoteIds.Add(notes[i], i);
			}
			NoteDropdown.AddOptions(notes);

			if (InternalBackgroundButtons != null)
			{
				foreach (AdeSkinBackgroundOption option in InternalBackgroundButtons.Values)
				{
					Destroy(option.gameObject);
				}
			}
			InternalBackgroundButtons = new Dictionary<string, AdeSkinBackgroundOption>();
			if (customBackgroundButtons != null)
				foreach (var option in customBackgroundButtons.Values) Destroy(option.gameObject);
			customBackgroundButtons = new Dictionary<string, AdeSkinBackgroundOption>();
			foreach (string bg in AdeSkinHost.Instance.skinData.BackgroundDatas.Keys)
			{
				AdeSkinBackgroundOption option = Instantiate(BackgroundOptionPrefab, InternalBackgroundContainer).GetComponent<AdeSkinBackgroundOption>();
				option.Initialize(bg, false, AdeSkinHost.Instance.skinData.BackgroundDatas[bg].background.value);
				InternalBackgroundButtons.Add(bg, option);
				if (customBackgroundContainer)
				{
					var custom = Instantiate(BackgroundOptionPrefab, customBackgroundContainer).GetComponent<AdeSkinBackgroundOption>();
					custom.Initialize(bg, false, AdeSkinHost.Instance.skinData.BackgroundDatas[bg].background.value);
					customBackgroundButtons.Add(bg, custom);
				}
			}
			InternalBackgroundLabel.text = "皮肤内置背景";
		}

		private void LoadExternalBackgroundOptions()
		{
			if (ExternalBackgroundButtons != null)
			{
				foreach (AdeSkinBackgroundOption option in ExternalBackgroundButtons.Values)
				{
					Destroy(option.gameObject);
				}
			}
			ExternalBackgroundButtons = new Dictionary<string, AdeSkinBackgroundOption>();
			foreach (string bg in AdeSkinHost.Instance.ExternalBackgrounds.Keys)
			{
				AdeSkinBackgroundOption option = Instantiate(BackgroundOptionPrefab, ExternalBackgroundContainer).GetComponent<AdeSkinBackgroundOption>();
				option.Initialize(bg, true, AdeSkinHost.Instance.ExternalBackgrounds[bg].value);
				ExternalBackgroundButtons.Add(bg, option);
			}
			ExternalBackgroundLabel.text = AdeSkinHost.Instance.ExternalBackgrounds.Count > 0 ? "自定义背景" : "没有可用的自定义背景";
		}

		private void VerifySkinOptions()
		{
			if (!preference.IsExternalBackground)
			{
				if (preference.SelectedBackground == null || !AdeSkinHost.Instance.skinData.BackgroundDatas.ContainsKey(preference.SelectedBackground))
				{
					preference.SelectedBackground = AdeSkinHost.Instance.skinData.DefaultBackground;
				}
				AdeSkinHost.BackgroundData BgData = AdeSkinHost.Instance.skinData.BackgroundDatas[preference.SelectedBackground];
				if (!preference.Customize)
				{
					preference.SkinSide = BgData.side ?? Side.Light;
					preference.SelectedTheme = BgData.theme ?? AdeSkinHost.Instance.skinData.DefaultThemeData;
					preference.SelectedNote = AdeSkinHost.Instance.skinData.DefaultNoteData;
				}
			}
			if (preference.SelectedTheme == null || !AdeSkinHost.Instance.skinData.ThemeDatas.ContainsKey(preference.SelectedTheme))
			{
				preference.SelectedTheme = AdeSkinHost.Instance.skinData.DefaultThemeData;
			}
			if (preference.SelectedNote == null || !AdeSkinHost.Instance.skinData.NoteDatas.ContainsKey(preference.SelectedNote))
			{
				preference.SelectedNote = AdeSkinHost.Instance.skinData.DefaultNoteData;
			}
		}

		private void VerifyExternalBackgroundOptions()
		{
			if (preference.IsExternalBackground)
			{
				if (!AdeSkinHost.Instance.ExternalBackgrounds.ContainsKey(preference.SelectedBackground))
				{
					preference.IsExternalBackground = false;
					preference.SelectedBackground = AdeSkinHost.Instance.skinData.DefaultBackground;
					AdeSkinHost.BackgroundData BgData = AdeSkinHost.Instance.skinData.BackgroundDatas[preference.SelectedBackground];
					if (!preference.Customize)
					{
						preference.SkinSide = BgData.side ?? Side.Light;
						preference.SelectedTheme = BgData.theme ?? AdeSkinHost.Instance.skinData.DefaultThemeData;
					}
				}
			}
		}

		private void Initialize()
		{
			SideDropdown.SetValueWithoutNotify(preference.SkinSide.Id());
			ThemeDropdown.SetValueWithoutNotify(ThemeIds[preference.SelectedTheme]);
			NoteDropdown.SetValueWithoutNotify(NoteIds[preference.SelectedNote]);
			CurrentBackgroundOption.SetSelected(true);
			if (!preference.IsExternalBackground && !preference.Customize)
			{
				AdeSkinHost.BackgroundData BgData = AdeSkinHost.Instance.skinData.BackgroundDatas[preference.SelectedBackground];
				SideDropdown.interactable = BgData.side == null;
				ThemeDropdown.interactable = BgData.theme == null;
			}
			else
			{
				SideDropdown.interactable = true;
				ThemeDropdown.interactable = true;
			}
			ApplySimpleSkin();
			ApplyBackground();
			ApplyNoteSideSkin();
			ApplyThemeSideSkin();
			UpdateTabs();
		}

		public void ShowPresets() => SetCustomize(false);
		public void ShowCustom() => SetCustomize(true);

		private void SetCustomize(bool custom)
		{
			CurrentBackgroundOption.SetSelected(false);
			preference.Customize = custom;
			if (!custom)
			{
				if (preference.IsExternalBackground)
				{
					preference.IsExternalBackground = false;
					preference.SelectedBackground = AdeSkinHost.Instance.skinData.DefaultBackground;
				}
				VerifySkinOptions();
			}
			Initialize();
			UpdateBackgroundFilterResult(custom ? customBackgroundSearch.text : BackgroundSearchInput.text);
			SavePreferences();
		}

		private void LateUpdate()
		{
			if (tabThemeRevision != AdeUiTheme.Revision) UpdateTabs();
            tabSelector?.Refresh();
            if (skinView && ArcadeComposeManager.Instance)
            {
                float width = Mathf.Min(760, ArcadeComposeManager.Instance.EditorCanvas.rect.width - 100);
                if (!Mathf.Approximately(skinView.rect.width, width)) skinView.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, width);
            }
            UpdateBackgroundGrid(InternalBackgroundContainer);
            UpdateBackgroundGrid(ExternalBackgroundContainer);
            UpdateBackgroundGrid(customBackgroundContainer);
		}

        private static void UpdateBackgroundGrid(RectTransform container)
        {
            if (!container) return;
            var grid = container.GetComponent<GridLayoutGroup>();
            if (!grid) return;
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount; grid.constraintCount = 3;
            float cellWidth = Mathf.Max(1, (container.rect.width - grid.padding.horizontal - grid.spacing.x * 2) / 3);
            var cell = new Vector2(cellWidth, 156);
            if (grid.cellSize != cell) grid.cellSize = cell;
        }

		private void UpdateTabs()
		{
			if (!presetPage || !customPage) return;
			tabThemeRevision = AdeUiTheme.Revision;
			presetPage.SetActive(!preference.Customize);
			customPage.SetActive(preference.Customize);
            tabSelector?.Select(preference.Customize ? 1 : 0);
            StyleResourceActions();
            StyleBackgroundSearch(BackgroundSearchInput);
            StyleBackgroundSearch(customBackgroundSearch);
		}

        // 重载皮肤／背景文件夹／重载背景 use the same bordered control surface and states as other panel buttons.
        private void StyleResourceActions()
        {
            if (!resourceActions) return;
            foreach (var button in resourceActions.GetComponentsInChildren<Button>(true))
            {
                AdeUiTheme.UseControlSurface(button);
                var label = button.GetComponentInChildren<Text>(true);
                if (label) label.color = AdeUiTheme.Current.Text;
            }
        }

        private static void StyleBackgroundSearch(InputField input)
        {
            if (!input) return;
            var size = input.GetComponent<LayoutElement>() ?? input.gameObject.AddComponent<LayoutElement>();
            size.minHeight = size.preferredHeight = 44;
            size.flexibleWidth = 1; size.flexibleHeight = 0;
            var background = input.GetComponent<Image>();
            background.sprite = AdeUiTheme.InputSurface; background.type = Image.Type.Sliced;
            background.color = Color.white;
            // TMP's viewport owns the inset so the caret, text and placeholder agree.
            input.textViewport.anchorMin = Vector2.zero; input.textViewport.anchorMax = Vector2.one;
            input.textViewport.offsetMin = new Vector2(12, 4);
            input.textViewport.offsetMax = new Vector2(-12, -4);
            AdeEditorText.Configure(input.textComponent, 20);
            input.textComponent.fontStyle = TMPro.FontStyles.Normal;
            input.textComponent.alignment = TMPro.TextAlignmentOptions.MidlineLeft;
            input.textComponent.color = AdeUiTheme.Current.Text;
            input.textComponent.margin = Vector4.zero;
            if (input.placeholder is Text hint)
            {
                AdeEditorText.Configure(hint, 20);
                hint.fontStyle = TMPro.FontStyles.Normal;
                hint.alignment = TMPro.TextAlignmentOptions.MidlineLeft;
                hint.color = AdeUiTheme.Current.Muted;
                hint.margin = Vector4.zero;
            }
        }

		private void LoadPreferences()
		{
			try
			{
				preference = JsonConvert.DeserializeObject<SkinPreference>(AdeSettings.GetString("AdeSkinDialog", ""));
				if (preference == null) preference = new SkinPreference();
			}
			catch (Exception Ex)
			{
				preference = new SkinPreference();
				Debug.Log(Ex);
			}
			if (preference.IsExternalBackground) preference.Customize = true;
		}

		public void SelectBackground(string name, bool external)
		{
			CurrentBackgroundOption.SetSelected(false);
			preference.IsExternalBackground = external;
			preference.SelectedBackground = name;
			ApplyBackground();
			CurrentBackgroundOption.SetSelected(true);
			if (!preference.IsExternalBackground && !preference.Customize)
			{
				AdeSkinHost.BackgroundData BgData = AdeSkinHost.Instance.skinData.BackgroundDatas[preference.SelectedBackground];
				preference.SkinSide = BgData.side ?? Side.Light;
				preference.SelectedTheme = BgData.theme ?? AdeSkinHost.Instance.skinData.DefaultThemeData;
				preference.SelectedNote = AdeSkinHost.Instance.skinData.DefaultNoteData;
				SideDropdown.SetValueWithoutNotify(preference.SkinSide.Id());
				ThemeDropdown.SetValueWithoutNotify(ThemeIds[preference.SelectedTheme]);
				NoteDropdown.SetValueWithoutNotify(NoteIds[preference.SelectedNote]);
				SideDropdown.interactable = BgData.side == null;
				ThemeDropdown.interactable = BgData.theme == null;
				ApplyNoteSideSkin();
				ApplyThemeSideSkin();
			}
			else
			{
				SideDropdown.interactable = true;
				ThemeDropdown.interactable = true;
			}
			SavePreferences();
		}

		public void SelectSide(int id)
		{
			Side? side;
			SideExtension.TryFromId(id, out side);
			preference.SkinSide = side ?? Side.Light;
			ApplyThemeSideSkin();
			ApplyNoteSideSkin();
		}

		public void SelectTheme(int id)
		{
			string theme = ThemeDropdown.options[id].text;
			preference.SelectedTheme = theme;
			ApplyThemeSideSkin();
		}

		public void SelectNote(int id)
		{
			string note = NoteDropdown.options[id].text;
			preference.SelectedNote = note;
			ApplyNoteSideSkin();
		}

		public void ReloadBackgroundFolder()
		{
			BackgroundSearchInput.text = "";
			CurrentBackgroundOption.SetSelected(false);
			AdeSkinHost.Instance.LoadExternalBackground();
			LoadExternalBackgroundOptions();
			VerifyExternalBackgroundOptions();
			Initialize();
		}

		public void ReloadSkinFolder()
		{
			BackgroundSearchInput.text = "";
			CurrentBackgroundOption.SetSelected(false);
			AdeSkinHost.Instance.LoadSkinDatas();
			LoadSkinOptions();
			VerifySkinOptions();
			Initialize();
		}

		public void OnClose()
		{
			BackgroundSearchInput.text = "";
		}

		private AdeSkinBackgroundOption CurrentBackgroundOption => (preference.IsExternalBackground ? ExternalBackgroundButtons
			: preference.Customize && customBackgroundContainer ? customBackgroundButtons : InternalBackgroundButtons)[preference.SelectedBackground];

		private void ApplyBackground()
		{
			AdeSkinHost.Instance.ApplyBackground(preference.SelectedBackground, preference.IsExternalBackground);
		}
		private void ApplySimpleSkin()
		{
			ArcSkinManager.Instance.SetSimpleSkin(AdeSkinHost.Instance.skinData);
		}
		private void ApplyNoteSideSkin()
		{
			AdeSkinHost.WithSideData<AdeSkinHost.NoteSideData> note = AdeSkinHost.Instance.skinData.NoteDatas[preference.SelectedNote];
			ArcSkinManager.Instance.SetNoteSideSkin(note.SelectWithSide(preference.SkinSide));
		}
		private void ApplyThemeSideSkin()
		{
			AdeSkinHost.WithSideData<AdeSkinHost.ThemeSideData> theme = AdeSkinHost.Instance.skinData.ThemeDatas[preference.SelectedTheme];
			ArcSkinManager.Instance.SetThemeSideSkin(theme.SelectWithSide(preference.SkinSide), preference.SkinSide);
		}

		public void ReapplySkin()
		{
			ApplyBackground();
			ApplyNoteSideSkin();
			ApplyThemeSideSkin();
		}

		// Applies a cloud chart's side, background and track without changing the saved preference.
		public void ApplyCloudSkin(Side side, Sprite background, Sprite track)
		{
			if (background != null)
			{
				AdeSkinHost.Instance.CancelBackgroundSelection();
				ArcSkinManager.Instance.SetBackground(background);
			}
			else
			{
				ApplyBackground();
			}
			ArcSkinManager.Instance.SetNoteSideSkin(AdeSkinHost.Instance.skinData.NoteDatas[preference.SelectedNote].SelectWithSide(side));
			ArcSkinManager.Instance.SetThemeSideSkin(AdeSkinHost.Instance.skinData.ThemeDatas[preference.SelectedTheme].SelectWithSide(side), side);
			if (track != null)
			{
				foreach (SpriteRenderer trackComponent in ArcSkinManager.Instance.TrackComponents)
				{
					trackComponent.sprite = track;
				}
			}
		}

		public void SavePreferences()
		{
			AdeSettings.SetString("AdeSkinDialog", JsonConvert.SerializeObject(preference));
		}
		private void OnApplicationQuit()
		{
			Debug.Log("saving skin setting when exit...");
			SavePreferences();
			Debug.Log("saved skin setting when exit");
		}

		public void UpdateBackgroundFilterResult(string pattern)
		{
			bool isClear = string.IsNullOrWhiteSpace(pattern);
			bool isRegexPattern = pattern.Contains("*") || pattern.Contains("?") || (pattern.Contains("[") && pattern.Contains("]"));
			Regex regex = null;
			if (isRegexPattern)
			{
				pattern = Regex.Escape(pattern);
				pattern = pattern.Replace("\\*", ".*");
				pattern = pattern.Replace("\\?", ".");
				pattern = pattern.Replace("\\[", "[");
				pattern = pattern.Replace("\\]", "]");
				try
				{
					regex = new Regex(pattern, RegexOptions.IgnoreCase);
				}
				catch
				{
					isRegexPattern = false;
					regex = null;
				}
			}
			int internalMatchedBgCount = 0;
			foreach (var bgPair in InternalBackgroundButtons)
			{
				bool show = isClear || (isRegexPattern ? regex.IsMatch(bgPair.Key) : bgPair.Key.IndexOf(pattern, StringComparison.OrdinalIgnoreCase) >= 0);
				bgPair.Value.gameObject.SetActive(show);
				if (show)
				{
					internalMatchedBgCount += 1;
				}
			}
			int externalMatchedBgCount = 0;
			foreach (var bgPair in ExternalBackgroundButtons)
			{
				bool show = isClear || (isRegexPattern ? regex.IsMatch(bgPair.Key) : bgPair.Key.IndexOf(pattern, StringComparison.OrdinalIgnoreCase) >= 0);
				bgPair.Value.gameObject.SetActive(show);
				if (show)
				{
					externalMatchedBgCount += 1;
				}
			}
            if (customBackgroundButtons != null)
                foreach (var bgPair in customBackgroundButtons)
                    bgPair.Value.gameObject.SetActive(isClear || (isRegexPattern ? regex.IsMatch(bgPair.Key)
                        : bgPair.Key.IndexOf(pattern, StringComparison.OrdinalIgnoreCase) >= 0));
			InternalBackgroundLabel.text = internalMatchedBgCount > 0 ? "皮肤内置背景" : "没有匹配的皮肤内置背景";
			ExternalBackgroundLabel.text = AdeSkinHost.Instance.ExternalBackgrounds.Count > 0 ? externalMatchedBgCount > 0 ? "自定义背景" : "没有匹配的自定义背景" : "没有可用的自定义背景";
		}
	}
}
