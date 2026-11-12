using Cysharp.Threading.Tasks;
using TMPro;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using Text = TMPro.TMP_Text;

namespace Arcade.Compose
{
    // The existing controls are borrowed only while this dialog is open and returned on close.
    // Their original dialogs and callbacks remain available as a fallback if setup fails.
    public sealed partial class AdeSettingsDialog : MonoBehaviour
    {
        public enum Page { General, Appearance, Editing, Audio, Spectrum, Grid, ContextMenu, Hotkeys, Account, Storage, About }
        public static AdeSettingsDialog Instance { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticState() => Instance = null;
        public bool Ready { get; private set; }
        private static readonly string[] Titles = { "常规", "外观", "编辑", "音频", "频谱", "网格", "右键", "热键", "账号", "存储", "关于" };
        private static readonly HashSet<string> EditingRows = new HashSet<string>
        {
            "CancelLimit", "FixNotesTimeOnPaste", "EnableTapWidthEditing", "ChartSortMode", "DragEditMode",
            "ShowJudgementDetails"
        };
        private readonly RectTransform[] pages = new RectTransform[Titles.Length];
        private readonly Button[] navigation = new Button[Titles.Length];
        private readonly Graphic[] icons = new Graphic[Titles.Length];
        private static readonly Page[] SourcePages = { Page.Audio, Page.Grid, Page.Hotkeys, Page.Account };
        private Toggle darkToggle;
        private readonly List<Image> checks = new List<Image>();
        private readonly AdeDialog[] sources = new AdeDialog[Titles.Length];
        private readonly List<BorrowedRow> rows = new List<BorrowedRow>();
        private AdeSingleDialog dialog;
        private RectTransform view;
        private Sprite checkmark;
        private Image sidebar, divider;
        private Button accountAction;
        private TMP_Text accountActionText;
        private Page activePage;
        private bool mounted;
        private TMP_FontAsset font;

        private sealed class BorrowedRow
        {
            public Transform Row, Parent, Host;
            public int Index;
            public Page Page;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Bootstrap()
        {
            UnityEngine.SceneManagement.SceneManager.sceneLoaded -= OnSceneLoaded;
            UnityEngine.SceneManagement.SceneManager.sceneLoaded += OnSceneLoaded;
        }

        private static void OnSceneLoaded(UnityEngine.SceneManagement.Scene scene, UnityEngine.SceneManagement.LoadSceneMode mode)
        {
            if (scene.name == "ArcEditor") new GameObject(nameof(AdeSettingsDialog)).AddComponent<AdeSettingsDialog>();
        }

        private void Start() => InitializeAsync().Forget();

        private async UniTask InitializeAsync()
        {
            // Theme rows and account controls are built in Start.
            await UniTask.NextFrame(destroyCancellationToken);
            var compose = ArcadeComposeManager.Instance;
            var originals = FindObjectsByType<AdeDialog>(FindObjectsInactive.Include);
            var template = originals.OfType<AdeSingleDialog>().FirstOrDefault(d => d.name == "Setting");
            var settings = template ? AdeUiKit.Content(template) : null;
            sources[(int)Page.Grid] = originals.FirstOrDefault(d => d.name == "Grid");
            sources[(int)Page.Audio] = originals.FirstOrDefault(d => d.name == "Sound");
            sources[(int)Page.Hotkeys] = originals.FirstOrDefault(d => d.name == "Hotkeys");
            sources[(int)Page.Account] = AdeAccountManager.Instance ? AdeAccountManager.Instance.SettingsDialog : null;
            if (!settings || !template || SourcePages.Any(p => !sources[(int)p]))
            {
                foreach (string name in new[] { "Grid", "Volume", "Hotkeys", "Account" })
                {
                    var entry = compose.RightBarView.Find(name);
                    if (entry) entry.gameObject.SetActive(true);
                }
                compose.LeftBarView.Find("Top/SaveMode").gameObject.SetActive(true);
                return;
            }
            font = AdeEditorText.Font;
            checkmark = Resources.Load<Sprite>("AffdataEdit/Icons/Checkmark");
            if (Arcade.Audio.BassOutputSettings.IsWindows) AdeAudioOutputSettings.AddTo(compose);
            BuildSpectrumSetting(settings.Find("Wraper"));
            BuildHitTypeSetting(settings.Find("Wraper"));
            BuildDisplayDelaySetting();
            BuildPasteTimingSetting(settings.Find("Wraper"));
            BuildTapWidthSetting(settings.Find("Wraper"));
            BuildContextMenuSetting(settings.Find("Wraper"));
            BuildChartSortSetting(settings.Find("Wraper"));
            BuildDragEditingSettings(settings.Find("Wraper"));
            BuildHotkeyPresetSettings();
            foreach (Transform row in settings.Find("Wraper"))
                Register(row, row.name == "DarkUi" ? Page.Appearance : row.name == "Log" ? Page.About
                    : row.name == "KeepContextMenuOpen" || row.name == "VerticalContextMenu" ? Page.ContextMenu
                    : EditingRows.Contains(row.name) ? Page.Editing
                    : row.name == "ShowSpectrum" || row.name.StartsWith("Spectrum") ? Page.Spectrum
                    : row.name == "PlaybackSync" || row.name == "GlobalAudioOffset" || row.name == "DisplayDelay"
                        || row.name == "AutoOutputLatency" || row.name.StartsWith("WindowsAudio") ? Page.Audio : Page.General);
            foreach (Transform row in settings)
                if (row.name != "Wraper" && row.name != "Text" && row.name != "Version") Register(row, Page.About);
            foreach (Page page in SourcePages)
            {
                int i = (int)page;
                Transform content = AdeUiKit.Content(sources[i]);
                Transform wrapper = content.Find("Wraper");
                foreach (Transform row in wrapper ? wrapper : content) Register(row, (Page)i);
            }
            Build(template);
            darkToggle = rows.First(r => r.Row.name == "DarkUi").Row.GetComponentInChildren<Toggle>(true);
            foreach (var row in rows)
                checks.AddRange(row.Row.GetComponentsInChildren<Toggle>(true).Select(t => t.graphic as Image).Where(i => i));
            Ready = true;
            Instance = this;
            BindEntries();
        }

        private void Register(Transform row, Page page)
        {
            foreach (var input in row.GetComponentsInChildren<TMP_InputField>(true))
                AdeTextSelection.ConfigureSettings(input, page == Page.Grid
                    || input.contentType == TMP_InputField.ContentType.IntegerNumber
                    || input.contentType == TMP_InputField.ContentType.DecimalNumber
                    || input.characterValidation == TMP_InputField.CharacterValidation.Integer
                    || input.characterValidation == TMP_InputField.CharacterValidation.Decimal);
            rows.Add(new BorrowedRow { Row = row, Parent = row.parent, Index = row.GetSiblingIndex(), Page = page });
        }

        private void BuildTapWidthSetting(Transform parent)
        {
            var compose = ArcadeComposeManager.Instance;
            var template = compose.PlaybackSyncToggle.GetComponentInParent<HorizontalLayoutGroup>(true).transform;
            var row = AdeUiKit.CloneRow(template, parent, "EnableTapWidthEditing");
            row.GetComponentInChildren<TMP_Text>(true).text = "允许编辑地键宽度";
            var toggle = row.GetComponentInChildren<Toggle>(true);
            toggle.SetIsOnWithoutNotify(compose.ArcadePreference.EnableTapWidthEditing);
            toggle.onValueChanged.AddListener(value =>
            {
                compose.ArcadePreference.EnableTapWidthEditing = value;
                compose.SavePreferences();
                Editing.AdeValueEditor.Instance.UpdateFields();
            });
        }

        private void BuildPasteTimingSetting(Transform parent)
        {
            var compose = ArcadeComposeManager.Instance;
            var template = compose.PlaybackSyncToggle.GetComponentInParent<HorizontalLayoutGroup>(true).transform;
            var row = AdeUiKit.CloneRow(template, parent, "FixNotesTimeOnPaste");
            row.GetComponentInChildren<TMP_Text>(true).text = "在粘贴时修正时间";
            var toggle = row.GetComponentInChildren<Toggle>(true);
            toggle.SetIsOnWithoutNotify(compose.ArcadePreference.FixNotesTimeOnPaste);
            toggle.onValueChanged.AddListener(value =>
            {
                compose.ArcadePreference.FixNotesTimeOnPaste = value;
                compose.SavePreferences();
            });
        }

        // The automatic output-latency toggle shows the measured value above the combined
        // 全局音频偏移 / 画面延迟补偿 row. Both delay controls only move the picture; hit sounds
        // keep following the music.
        private void BuildDisplayDelaySetting()
        {
            var compose = ArcadeComposeManager.Instance;
            var offsetRow = compose.GlobalAudioOffsetInput.GetComponentInParent<HorizontalLayoutGroup>(true).transform;
            // This is only a staging container. Keep it outside the source list: rows are
            // registered later in this same frame, before deferred Destroy has run.
            var clone = AdeUiKit.CloneRow(offsetRow, null, "DisplayDelay");
            var caption = clone.GetComponentInChildren<TMP_Text>(true);
            var input = clone.GetComponentInChildren<TMP_InputField>(true);
            caption.text = "画面延迟补偿";
            caption.transform.SetParent(offsetRow, false);
            input.transform.SetParent(offsetRow, false);
            if (Application.isPlaying) Destroy(clone);
            else DestroyImmediate(clone);
            input.onEndEdit = new TMP_InputField.SubmitEvent();
            input.SetTextWithoutNotify(compose.ArcadePreference.DisplayDelay.ToString(System.Globalization.CultureInfo.InvariantCulture));
            input.onEndEdit.AddListener(text =>
            {
                if (!int.TryParse(text, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out int value))
                    value = compose.ArcadePreference.DisplayDelay;
                value = Mathf.Clamp(value, -500, 500);
                input.SetTextWithoutNotify(value.ToString(System.Globalization.CultureInfo.InvariantCulture));
                compose.ArcadePreference.DisplayDelay = value;
                compose.SavePreferences();
                compose.ApplyDisplayDelay();
            });
            var toggleTemplate = compose.PlaybackSyncToggle.GetComponentInParent<HorizontalLayoutGroup>(true).transform;
            var autoRow = AdeUiKit.CloneRow(toggleTemplate, offsetRow.parent, "AutoOutputLatency");
            autoRow.transform.SetSiblingIndex(offsetRow.GetSiblingIndex());
            autoRow.GetComponentInChildren<TMP_Text>(true).text =
                "自动补偿输出延迟（检测到 " + Arcade.Audio.BassAudio.OutputLatencyMs + " ms）";
            var toggle = autoRow.GetComponentInChildren<Toggle>(true);
            toggle.SetIsOnWithoutNotify(compose.ArcadePreference.AutoOutputLatency);
            toggle.onValueChanged.AddListener(value =>
            {
                compose.ArcadePreference.AutoOutputLatency = value;
                compose.SavePreferences();
                compose.ApplyDisplayDelay();
            });
        }

        private void BuildHitTypeSetting(Transform parent)
        {
            var compose = ArcadeComposeManager.Instance;
            var template = compose.PlaybackSyncToggle.GetComponentInParent<HorizontalLayoutGroup>(true).transform;
            var row = AdeUiKit.CloneRow(template, parent, "ShowJudgementDetails");
            row.GetComponentInChildren<TMP_Text>(true).text = "显示判定详情";
            var toggle = row.GetComponentInChildren<Toggle>(true);
            toggle.SetIsOnWithoutNotify(compose.ArcadePreference.ShowJudgementDetails);
            toggle.onValueChanged.AddListener(value =>
            {
                compose.ArcadePreference.ShowJudgementDetails = value;
                compose.SavePreferences();
            });
        }

        private void BuildSpectrumSetting(Transform parent)
        {
            var compose = ArcadeComposeManager.Instance;
            var template = compose.PlaybackSyncToggle.GetComponentInParent<HorizontalLayoutGroup>(true).transform;
            var row = AdeUiKit.CloneRow(template, parent, "ShowSpectrum");
            row.GetComponentInChildren<TMP_Text>(true).text = "显示频谱";
            var toggle = row.GetComponentInChildren<Toggle>(true);
            toggle.SetIsOnWithoutNotify(compose.ArcadePreference.ShowSpectrum);
            toggle.onValueChanged.AddListener(value =>
            {
                compose.ArcadePreference.ShowSpectrum = value;
                compose.SavePreferences();
            });
            var brightness = Instantiate(Dialog.AdeSoundDialogContent.Instance.ChartAudioInput, parent);
            brightness.name = "SpectrumBrightness";
            brightness.transform.Find("Text").GetComponent<TMP_Text>().text = "频谱亮度 (%)";
            brightness.SliderValueScale = 1;
            brightness.NumberFormat = "0";
            brightness.slider.Slider.minValue = 0;
            brightness.slider.Slider.maxValue = 100;
            brightness.slider.Slider.wholeNumbers = true;
            brightness.SetValueWithoutNotify(compose.ArcadePreference.SpectrumOpacity * 100);
            brightness.onValueEdited = value =>
            {
                if (float.IsNaN(value) || float.IsInfinity(value))
                {
                    brightness.SetValueWithoutNotify(compose.ArcadePreference.SpectrumOpacity * 100);
                    return;
                }
                value = Mathf.Round(Mathf.Clamp(value, 0, 100));
                compose.ArcadePreference.SpectrumOpacity = value / 100;
                brightness.SetValueWithoutNotify(value);
                compose.SavePreferences();
            };
            var logRow = AdeUiKit.CloneRow(template, parent, "SpectrumLogScale");
            logRow.GetComponentInChildren<TMP_Text>(true).text = "频谱对数频率轴";
            var logToggle = logRow.GetComponentInChildren<Toggle>(true);
            logToggle.SetIsOnWithoutNotify(compose.ArcadePreference.SpectrumLogScale);
            logToggle.onValueChanged.AddListener(value =>
            {
                compose.ArcadePreference.SpectrumLogScale = value;
                compose.SavePreferences();
                UpdateSpectrumTuningRows();
            });
            var modeTemplate = compose.ResolutionDropdown.GetComponentInParent<HorizontalLayoutGroup>(true).transform;
            var modeRow = AdeUiKit.CloneRow(modeTemplate, parent, "SpectrumIntensityMode");
            modeRow.GetComponentInChildren<TMP_Text>(true).text = "频谱强度模式";
            var mode = modeRow.GetComponentInChildren<TMP_Dropdown>(true);
            mode.onValueChanged = new TMP_Dropdown.DropdownEvent();
            mode.ClearOptions();
            mode.AddOptions(new List<string> { "dB", "Linear" });
            mode.interactable = true;
            mode.SetValueWithoutNotify(compose.ArcadePreference.SpectrumLinearAmplitude ? 1 : 0);
            mode.onValueChanged.AddListener(value =>
            {
                compose.ArcadePreference.SpectrumLinearAmplitude = value == 1;
                compose.SavePreferences();
                UpdateSpectrumTuningRows();
            });
            BuildSpectrumTuning(parent);
            gameObject.AddComponent<AdeAudioSpectrum>();
        }

        private void BuildContextMenuSetting(Transform parent)
        {
            var compose = ArcadeComposeManager.Instance;
            var template = compose.PlaybackSyncToggle.GetComponentInParent<HorizontalLayoutGroup>(true).transform;
            var row = AdeUiKit.CloneRow(template, parent, "KeepContextMenuOpen");
            row.GetComponentInChildren<TMP_Text>(true).text = "点击选取";
            var toggle = row.GetComponentInChildren<Toggle>(true);
            toggle.SetIsOnWithoutNotify(compose.ArcadePreference.KeepContextMenuOpen);
            toggle.onValueChanged.AddListener(value =>
            {
                if (!compose.ArcadePreference.VerticalContextMenu) return;
                compose.ArcadePreference.KeepContextMenuOpen = value;
                compose.SavePreferences();
            });

            var styleRow = AdeUiKit.CloneRow(template, parent, "VerticalContextMenu");
            styleRow.GetComponentInChildren<TMP_Text>(true).text = "使用全新的右键菜单";
            var styleToggle = styleRow.GetComponentInChildren<Toggle>(true);
            styleToggle.SetIsOnWithoutNotify(compose.ArcadePreference.VerticalContextMenu);
            row.transform.SetSiblingIndex(styleRow.transform.GetSiblingIndex());
            var rowState = row.gameObject.AddComponent<CanvasGroup>();
            void UpdateClickSelection(bool enabled)
            {
                toggle.interactable = enabled;
                toggle.SetIsOnWithoutNotify(enabled && compose.ArcadePreference.KeepContextMenuOpen);
                rowState.alpha = enabled ? 1f : 0.45f;
            }
            UpdateClickSelection(compose.ArcadePreference.VerticalContextMenu);
            styleToggle.onValueChanged.AddListener(value =>
            {
                compose.ArcadePreference.VerticalContextMenu = value;
                UpdateClickSelection(value);
                compose.SavePreferences();
            });
        }

        private void BuildChartSortSetting(Transform parent)
        {
            var compose = ArcadeComposeManager.Instance;
            var template = compose.ResolutionDropdown.GetComponentInParent<HorizontalLayoutGroup>(true).transform;
            var row = AdeUiKit.CloneRow(template, parent, "ChartSortMode");
            row.GetComponentInChildren<TMP_Text>(true).text = "谱面保存排序";
            var dropdown = row.GetComponentInChildren<TMP_Dropdown>(true);
            dropdown.onValueChanged = new TMP_Dropdown.DropdownEvent();
            dropdown.ClearOptions();
            dropdown.AddOptions(new List<string> { "按时间", "按类型" });
            dropdown.interactable = true;
            dropdown.SetValueWithoutNotify(compose.ArcadePreference.ChartSortMode == Gameplay.Chart.ChartSortMode.Timing ? 0 : 1);
            dropdown.onValueChanged.AddListener(value =>
            {
                compose.ArcadePreference.ChartSortMode = value == 0 ? Gameplay.Chart.ChartSortMode.Timing : Gameplay.Chart.ChartSortMode.Type;
                compose.SavePreferences();
            });
        }

        private void Build(AdeSingleDialog template)
        {
            dialog = AdeUiKit.CloneDialog(template, "SettingsCenter");
            dialog.Title.text = "设置";
            dialog.ButtonText.text = "关闭";
            AdeUiKit.SetOnClick(dialog.CompleteButton, dialog.Close);
            view = (RectTransform)dialog.View.transform;
            foreach (var layout in view.GetComponents<LayoutGroup>()) layout.enabled = false;
            foreach (var fitter in view.GetComponents<ContentSizeFitter>()) fitter.enabled = false;
            view.Find("Scroll View").gameObject.SetActive(false);
            var spacer = view.Find("ThemeTitleSpacer"); if (spacer) spacer.gameObject.SetActive(false);
            var buttons = view.Find("Buttons") as RectTransform;
            buttons.anchorMin = Vector2.zero; buttons.anchorMax = new Vector2(1, 0);
            buttons.pivot = new Vector2(.5f, 0); buttons.sizeDelta = new Vector2(0, 86); buttons.anchoredPosition = Vector2.zero;
            // Same 28-unit side margin as the body, so the Close button lines up with the page controls.
            var buttonLayout = buttons.GetComponent<HorizontalOrVerticalLayoutGroup>();
            if (buttonLayout)
                buttonLayout.padding = new RectOffset(28, 28, buttonLayout.padding.top, buttonLayout.padding.bottom);

            var body = Rect("SettingsBody", view);
            // 28-unit outer margin on every side: the sidebar lines up with the "设置" title, the page controls
            // end 28 units from the right edge (in line with the Close button), and the sidebar sits ~24 units
            // below the header line and above the button row. On the right the body runs 20 units further:
            // the 12-unit scrollbar sits 8 units from the window edge with an 8-unit gap to the controls.
            Fill(body); body.offsetMin = new Vector2(28, 88); body.offsetMax = new Vector2(-8, -98);
            var nav = Rect("Categories", body, typeof(Image), typeof(VerticalLayoutGroup));
            nav.anchorMin = Vector2.zero; nav.anchorMax = new Vector2(0, 1); nav.pivot = Vector2.zero;
            nav.sizeDelta = new Vector2(190, 0); sidebar = nav.GetComponent<Image>();
            var layoutGroup = nav.GetComponent<VerticalLayoutGroup>();
            layoutGroup.padding = new RectOffset(8, 8, 12, 12); layoutGroup.spacing = 6;
            layoutGroup.childControlWidth = layoutGroup.childControlHeight = true;
            layoutGroup.childForceExpandWidth = true; layoutGroup.childForceExpandHeight = false;
            var line = Rect("Separator", body, typeof(Image));
            line.anchorMin = Vector2.zero; line.anchorMax = new Vector2(0, 1); line.pivot = Vector2.zero;
            // Centred between the sidebar (190) and the page content (232 + 8 padding).
            line.sizeDelta = new Vector2(1, 0); line.anchoredPosition = new Vector2(215, 0);
            divider = line.GetComponent<Image>(); divider.raycastTarget = false;
            for (int i = 0; i < pages.Length; i++)
            {
                var page = (Page)i;
                navigation[i] = MakeButton(nav, Titles[i], () => Select(page));
                AddIcon(navigation[i], page);
                var scroll = Rect(Titles[i], body, typeof(ScrollRect));
                Fill(scroll); scroll.offsetMin = new Vector2(232, 0);
                var viewport = Rect("Viewport", scroll, typeof(RectMask2D), typeof(Image)); Fill(viewport);
                viewport.offsetMax = new Vector2(-20, 0);
                viewport.GetComponent<Image>().color = Color.clear;
                pages[i] = Rect("Content", viewport, typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
                var content = pages[i]; content.anchorMin = new Vector2(0, 1); content.anchorMax = Vector2.one;
                content.pivot = new Vector2(.5f, 1); content.sizeDelta = Vector2.zero;
                var layout = content.GetComponent<VerticalLayoutGroup>();
                // Top padding 12 matches the sidebar's, so the first row lines up with the first category.
                layout.padding = new RectOffset(8, 0, 12, 24); layout.spacing = 14;
                layout.childControlWidth = layout.childControlHeight = true;
                layout.childForceExpandWidth = true; layout.childForceExpandHeight = false;
                content.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
                var scroller = scroll.GetComponent<ScrollRect>(); scroller.content = content; scroller.viewport = viewport;
                scroller.horizontal = false; scroller.movementType = ScrollRect.MovementType.Clamped; scroller.scrollSensitivity = 35;
                var bar = Instantiate(view.Find("Scroll View").GetComponentInChildren<Scrollbar>(true), scroll, false);
                bar.onValueChanged = new Scrollbar.ScrollEvent();
                var barRect = (RectTransform)bar.transform;
                barRect.anchorMin = new Vector2(1, 0); barRect.anchorMax = Vector2.one; barRect.pivot = Vector2.one;
                barRect.sizeDelta = new Vector2(12, 0); barRect.anchoredPosition = Vector2.zero;
                scroller.verticalScrollbar = bar; scroller.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;
                scroll.gameObject.SetActive(false);
            }
            BuildAppearance();
            BuildStorage();
            BuildSpectrumReset();
            BuildGridEditor();
            BuildAbout();
            BuildHotkeyGroups();
            BuildContextMenuEditor();
            var accountActions = Rect("AccountActions", pages[(int)Page.Account], typeof(HorizontalLayoutGroup));
            var accountLayout = accountActions.GetComponent<HorizontalLayoutGroup>();
            accountLayout.childControlWidth = accountLayout.childControlHeight = true;
            accountLayout.childForceExpandWidth = accountLayout.childForceExpandHeight = false;
            accountLayout.childAlignment = TextAnchor.MiddleRight;
            accountAction = MakeButton(accountActions, "登录", () => ((AdeDualDialog)sources[(int)Page.Account]).RightButton.onClick.Invoke());
            var sourceImage = (Image)AdeAccountManager.Instance.SettingsDialog.RightButton.targetGraphic;
            accountAction.image.sprite = sourceImage.sprite;
            accountAction.image.type = Image.Type.Sliced;
            accountActionText = accountAction.GetComponentInChildren<TMP_Text>();
            accountActionText.alignment = TextAlignmentOptions.Center;
            accountActionText.rectTransform.offsetMin = new Vector2(12, 0);
            accountAction.GetComponent<LayoutElement>().preferredWidth = 180;
            dialog.OnOpen += Mount;
            dialog.OnClose += Release;
            AdeUiTheme.AddRoot(view);
            AdeUiTheme.OnSkinApplied();
        }

        public void Show(Page page)
        {
            if (!Ready) return;
            if (mounted) Select(page);
            else activePage = page;
            dialog.Open();
        }

        private void ToggleSettings()
        {
            if (mounted) dialog.SwitchOpenState();
            else
            {
                // Reopen on the category used last time.
                int last = ArcadeComposeManager.Instance.ArcadePreference.LastSettingsPage;
                Show(last >= 0 && last < Titles.Length ? (Page)last : Page.General);
            }
        }

        private void BindEntries()
        {
            var compose = ArcadeComposeManager.Instance;
            AdeUiKit.SetOnClick(compose.LeftBarView.Find("Bottom/Setting").GetComponent<Button>(), ToggleSettings);
            compose.LeftBarView.Find("Top/SaveMode").gameObject.SetActive(false);
            foreach (Page page in SourcePages)
            {
                string name = page == Page.Audio ? "Volume" : page.ToString();
                // Hide duplicate entries only after the settings hub is ready; keep fallback controls intact.
                compose.RightBarView.Find(name).gameObject.SetActive(false);
            }
        }

        private void Mount()
        {
            if (mounted) return;
            MountRows();
            darkToggle.transform.parent.SetAsFirstSibling();
            hotkeyPresetRow.SetAsFirstSibling();
            accountAction.transform.parent.SetAsLastSibling();
            contextEditorRoot.SetAsLastSibling();
            // Spectrum has its own page; keep the slow-audio cache entry on Audio.
            foreach (string name in new[] { "SpectrumLogScale", "SpectrumIntensityMode", "SpectrumBrightness", "ShowSpectrum" })
                rows.FirstOrDefault(r => r.Row.name == name)?.Row.SetAsFirstSibling();
            foreach (var entry in slowAudioCacheEntry) entry.SetAsLastSibling();
            // The reset button sits in its own right-aligned row; move the row, not the button.
            spectrumReset.transform.parent.SetAsLastSibling();
            mounted = true;
            Select(activePage, false);
        }

        private void MountRows()
        {
            foreach (var row in rows)
            {
                row.Row.SetParent(row.Host ? row.Host : pages[(int)row.Page], false);
                ArrangeRow(row.Row, row.Page);
            }
        }

        private void Select(Page page) => Select(page, true);
        private void Select(Page page, bool closePrevious)
        {
            if (closePrevious) sources[(int)activePage]?.OnClose?.Invoke();
            activePage = page;
            var compose = ArcadeComposeManager.Instance;
            if (compose.ArcadePreference.LastSettingsPage != (int)page)
            {
                compose.ArcadePreference.LastSettingsPage = (int)page;
                compose.SavePreferences();
            }
            for (int i = 0; i < pages.Length; i++) pages[i].parent.parent.gameObject.SetActive(i == (int)page);
            sources[(int)page]?.OnOpen?.Invoke();
            if (page == Page.Account) AdeAccountManager.Instance.PrepareSettingsView();
            Style();
            ScrollToTop(page).Forget();
        }

        private async UniTask ScrollToTop(Page page)
        {
            await UniTask.NextFrame(destroyCancellationToken);
            if (!mounted || activePage != page) return;
            Canvas.ForceUpdateCanvases();
            pages[(int)page].parent.parent.GetComponent<ScrollRect>().verticalNormalizedPosition = 1;
        }

        private void Release()
        {
            if (!mounted) return;
            sources[(int)activePage]?.OnClose?.Invoke();
            foreach (var row in rows.OrderBy(r => r.Index))
            {
                row.Row.SetParent(row.Parent, false);
                row.Row.SetSiblingIndex(row.Index);
            }
            mounted = false;
        }

        private void LateUpdate()
        {
            if (!Ready) return;
            var canvas = ArcadeComposeManager.Instance.EditorCanvas.rect;
            view.sizeDelta = new Vector2(Mathf.Min(1060, canvas.width - 100), Mathf.Min(760, canvas.height - 100));
            if (!dialog.View.activeSelf) return;
            var source = (AdeDualDialog)sources[(int)Page.Account];
            accountActionText.text = source.RightButtonText.text;
            accountActionText.color = AdeUiTheme.Current.AccentText;
            accountAction.interactable = source.RightButton.interactable;
            accountAction.image.sprite = source.RightButton.image.sprite;
            Style();
        }

        // The audio-buffer note reads as a caption (smaller, muted), like other explanatory text.
        private void StyleAudioHint()
        {
            var row = rows.FirstOrDefault(r => r.Row.name == "WindowsAudioHint");
            var hint = row?.Row.GetComponentInChildren<TMP_Text>(true);
            if (!hint) return;
            hint.fontSize = AdeEditorText.CaptionSize;
            hint.fontStyle = FontStyles.Normal;
            hint.textWrappingMode = TextWrappingModes.Normal;
            hint.color = AdeUiTheme.Current.Muted;
        }

        private void Style()
        {
            bool dark = AdeUiTheme.Dark;
            darkToggle?.SetIsOnWithoutNotify(dark);
            dragModeDropdown?.SetValueWithoutNotify((int)Editing.AdeDragEdit.Instance.Mode);
            StyleAppearance();
            StyleStorage();
            StyleAbout();
            StyleHotkeyGroups();
            StyleHotkeyPreset();
            StyleContextMenu();
            StyleAudioHint();
            StyleSpectrum();
            StyleGrid();
            sidebar.color = AdeUiTheme.Current.Chrome;
            sidebar.sprite = AdeUiTheme.RoundedSurface;
            sidebar.type = Image.Type.Sliced;
            divider.color = AdeUiTheme.Current.Border;
            for (int i = 0; i < navigation.Length; i++)
            {
                bool selected = i == (int)activePage;
                var colors = ColorBlock.defaultColorBlock;
                // Opaque chrome avoids a dark flash when RGB and alpha fade together.
                colors.normalColor = colors.selectedColor = selected
                    ? AdeUiTheme.Current.Selection : AdeUiTheme.Current.Chrome;
                colors.highlightedColor = Color.Lerp(colors.normalColor, AdeUiTheme.Current.Text, selected ? .04f : .06f);
                colors.pressedColor = Color.Lerp(colors.normalColor, AdeUiTheme.Current.Text, .1f);
                navigation[i].image.sprite = AdeUiTheme.RoundedSurface;
                navigation[i].image.type = Image.Type.Sliced;
                navigation[i].image.color = Color.white;
                navigation[i].colors = colors;
                var foreground = i == (int)activePage ? AdeUiTheme.Current.Text : AdeUiTheme.Current.Muted;
                navigation[i].GetComponentInChildren<TMP_Text>().color = foreground;
                icons[i].color = foreground;
            }
            foreach (var check in checks)
            {
                check.sprite = checkmark;
                check.color = dark ? Color.white : new Color32(50, 50, 50, 255);
            }
        }

        private void AddIcon(Button button, Page page)
        {
            var label = button.GetComponentInChildren<TMP_Text>();
            label.rectTransform.offsetMin = new Vector2(50, 0);
            var icon = Rect("Icon", button.transform);
            icon.anchorMin = icon.anchorMax = new Vector2(0, .5f);
            icon.anchoredPosition = new Vector2(26, 0); icon.sizeDelta = new Vector2(24, 24);
            if (page == Page.ContextMenu || page == Page.Editing || page == Page.Spectrum)
            {
                var image = icon.gameObject.AddComponent<Image>();
                image.sprite = Resources.Load<Sprite>(page == Page.ContextMenu ? "AffdataEdit/Icons/MouseRight"
                    : page == Page.Spectrum ? "AffdataEdit/Icons/Spectrum" : "AffdataEdit/Icons/Edit");
                image.preserveAspect = true;
                icons[(int)page] = image;
            }
            else if (page == Page.About)
            {
                var text = icon.gameObject.AddComponent<TextMeshProUGUI>(); text.font = font;
                AdeUiIcons.SetText(text, AdeUiIcons.Info);
                text.fontSize = 26; text.alignment = TextAlignmentOptions.Center;
                text.overflowMode = TextOverflowModes.Overflow;
                icons[(int)page] = text;
            }
            else
            {
                var compose = ArcadeComposeManager.Instance;
                Transform source = page == Page.General ? compose.LeftBarView.Find("Bottom/Setting/Image")
                    : page == Page.Storage ? compose.LeftBarView.Find("Top/Folder/Image")
                    : compose.RightBarView.Find((page == Page.Audio ? "Volume" : page == Page.Appearance ? "Skin" : page.ToString()) + "/Image");
                var image = icon.gameObject.AddComponent<Image>(); image.sprite = source.GetComponent<Image>().sprite;
                image.preserveAspect = true; icons[(int)page] = image;
            }
            icons[(int)page].raycastTarget = false;
        }

        private Button MakeButton(Transform parent, string title, UnityEngine.Events.UnityAction action)
        {
            var rect = Rect(title, parent, typeof(Image), typeof(Button), typeof(LayoutElement));
            var button = rect.GetComponent<Button>(); button.targetGraphic = rect.GetComponent<Image>(); button.onClick.AddListener(action);
            rect.GetComponent<LayoutElement>().preferredHeight = 48;
            var labelRect = Rect("Text", rect, typeof(TextMeshProUGUI)); Fill(labelRect);
            labelRect.offsetMin = new Vector2(20, 0); labelRect.offsetMax = new Vector2(-12, 0);
            var label = labelRect.GetComponent<TMP_Text>(); AdeEditorText.Configure(label, AdeEditorText.BodySize);
            label.alignment = TextAlignmentOptions.Left; label.text = title; label.raycastTarget = false;
            return button;
        }

        private static RectTransform Rect(string name, Transform parent, params System.Type[] components)
        {
            var go = new GameObject(name, typeof(RectTransform)); go.layer = parent.gameObject.layer;
            go.transform.SetParent(parent, false);
            foreach (var component in components) go.AddComponent(component);
            return (RectTransform)go.transform;
        }
        private static void Fill(RectTransform rect)
        { rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = rect.offsetMax = Vector2.zero; }
        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }
    }
}
