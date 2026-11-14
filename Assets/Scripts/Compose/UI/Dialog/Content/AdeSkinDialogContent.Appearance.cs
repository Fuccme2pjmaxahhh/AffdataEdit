using System;
using System.Collections.Generic;
using Arcade.Gameplay;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Arcade.Compose.Dialog
{
    public partial class AdeSkinDialogContent
    {
        private AdeSingleDialog appearanceDialog;
        private readonly List<Action> appearanceRefresh = new List<Action>();
        private readonly List<TMP_Text> appearanceLabels = new List<TMP_Text>();
        private TMP_Text appearanceHint;

        private void RefreshAppearanceDialog()
        {
            foreach (var label in appearanceLabels) if (label) label.color = AdeUiTheme.Current.Text;
            if (appearanceHint) appearanceHint.color = AdeUiTheme.Current.Muted;
        }

        private void BuildAppearanceEntry()
        {
            var source = resourceActions ? resourceActions.GetComponentInChildren<Button>(true) : null;
            if (!source) return;
            var button = Instantiate(source, presetPage.transform.parent, false);
            button.name = "SceneAppearance";
            button.transform.SetSiblingIndex(Mathf.Min(presetPage.transform.GetSiblingIndex(), customPage.transform.GetSiblingIndex()));
            var size = button.GetComponent<LayoutElement>() ?? button.gameObject.AddComponent<LayoutElement>();
            size.minHeight = size.preferredHeight = 44;
            size.flexibleWidth = 1;
            button.GetComponentInChildren<TMP_Text>(true).text = "场景外观";
            AdeUiKit.SetOnClick(button, OpenAppearance);
            AdeUiTheme.UseControlSurface(button);
        }

        public void SelectPreset(string name)
        {
            if (!AppearancePresetCatalog.TryGet(name, out var preset)) return;
            CurrentBackgroundOption?.SetSelected(false);
            preference.Customize = false;
            SetPresetPreference(preset);
            VerifySkinOptions();
            Initialize();
            SavePreferences();
        }

        private void SetPresetPreference(AppearancePreset preset)
        {
            preference.SelectedPreset = preset.Name;
            preference.SelectedBackground = preset.Background;
            preference.IsExternalBackground = false;
            preference.SkinSide = (Side)Mathf.Clamp(preset.Side, 0, 4);
            preference.SelectedTheme = AdeSkinHost.Instance.skinData.DefaultThemeData;
            preference.SelectedNote = AdeSkinHost.Instance.skinData.DefaultNoteData;
            preference.Appearance = new AppearanceSelection
            {
                Track = preset.Track, CriticalLine = preset.CriticalLine,
                SingleLine = preset.SingleLine, Diamond = preset.Diamond,
                Combo = string.IsNullOrEmpty(preset.ComboColor)
                    ? preset.Side == 2 ? "colorless" : preset.Side == 1 || preset.Side == 4 ? "conflict" : "light"
                    : preset.ComboColor
            };
            preference.PresetNote = preset.Note;
            preference.NoteSide = null;
            preference.HitEffect = preset.HitEffect;
            preference.Particle = preset.Particle;
        }

        private void ApplyAppearance() => ApplyAppearance(preference.SkinSide);

        private void ApplyAppearance(Side side)
        {
            ArcSkinManager.Instance.ApplyAppearance(preference.Appearance, side);
            if (preference.PresetNote >= 0)
                ArcSkinManager.Instance.ApplyPresetExtras(preference.PresetNote, preference.HitEffect, preference.Particle, side);
            if (!string.IsNullOrEmpty(preference.Appearance.Track))
                ArcTapNoteManager.Instance.SetConnectionLineColor(side == Side.Conflict || side == Side.DarkLephon
                    ? Affdata.Core.Chart.ConnectionLineStyle.Conflict : Affdata.Core.Chart.ConnectionLineStyle.Light);
        }

        private void OpenAppearance()
        {
            if (!appearanceDialog) BuildAppearanceDialog();
            if (!appearanceDialog) return;
            RefreshAppearanceDialog();
            foreach (var refresh in appearanceRefresh) refresh();
            AdeUiKit.SetMaxHeight(appearanceDialog, Mathf.Max(180, ArcadeComposeManager.Instance.EditorCanvas.rect.height - 220));
            appearanceDialog.Open();
        }

        private void BuildAppearanceDialog()
        {
            AdeSingleDialog template = null;
            foreach (var candidate in FindObjectsByType<AdeSingleDialog>(FindObjectsInactive.Include))
                if (candidate.name == "Setting") { template = candidate; break; }
            if (!template) return;
            appearanceDialog = AdeUiKit.CloneDialog(template, "SceneAppearanceDialog");
            appearanceDialog.Title.text = "场景外观";
            appearanceDialog.ButtonText.text = "关闭";
            AdeUiKit.SetOnClick(appearanceDialog.CompleteButton, appearanceDialog.Close);
            var content = AdeUiKit.Content(appearanceDialog);
            var layout = content.GetComponent<VerticalLayoutGroup>() ?? content.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(28, 28, 16, 20);
            layout.spacing = 12;
            layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            var hint = NewRect("Description", content, typeof(TextMeshProUGUI), typeof(LayoutElement));
            var text = hint.GetComponent<TMP_Text>();
            appearanceHint = text;
            AdeEditorText.Configure(text, AdeEditorText.CaptionSize);
            text.text = "独立搭配轨道与装饰；选择完整预设可一次应用整套外观。";
            text.color = AdeUiTheme.Current.Muted;
            text.textWrappingMode = TextWrappingModes.Normal;
            text.raycastTarget = false;
            hint.GetComponent<LayoutElement>().preferredHeight = 44;
            AddAppearanceRow(content, "轨道", ArcAppearanceCatalog.Tracks, () => preference.Appearance.Track, SelectTrackAppearance);
            AddAppearanceRow(content, "判定线", ArcAppearanceCatalog.CriticalLines, () => preference.Appearance.CriticalLine, v => preference.Appearance.CriticalLine = v);
            AddAppearanceRow(content, "两侧装饰／墙", ArcAppearanceCatalog.SingleLines, () => preference.Appearance.SingleLine, v => preference.Appearance.SingleLine = v);
            AddAppearanceRow(content, "菱形装饰", ArcAppearanceCatalog.Diamonds, () => preference.Appearance.Diamond, v => preference.Appearance.Diamond = v);
            AddAppearanceRow(content, "Combo 颜色", ArcAppearanceCatalog.ComboColors, () => preference.Appearance.Combo, v => preference.Appearance.Combo = v);
            // The source dialog supplies the standard frame, Close/Escape handling and animation.
            AdeUiTheme.Refresh();
        }

        private void SelectTrackAppearance(string id)
        {
            preference.Appearance.Track = id;
            int side = ArcAppearanceCatalog.GetTrackSide(id);
            if (side < 0) return;
            if (preference.SkinSide == Side.Lephon) side = (int)Side.Lephon;
            // A track changes the environment's side; the chosen note skin remains independent.
            if (!preference.NoteSide.HasValue) preference.NoteSide = (int)preference.SkinSide;
            preference.SkinSide = (Side)side;
            SideDropdown.SetValueWithoutNotify(side);
            ApplyThemeSideSkin();
            ArcTapNoteManager.Instance.SetConnectionLineColor(side == 1 || side == 4
                ? Affdata.Core.Chart.ConnectionLineStyle.Conflict : Affdata.Core.Chart.ConnectionLineStyle.Light);
        }

        private void AddAppearanceRow(Transform content, string title, IReadOnlyList<AppearanceOption> options, Func<string> get, Action<string> set)
        {
            var row = NewRect(title, content, typeof(HorizontalLayoutGroup), typeof(LayoutElement));
            row.GetComponent<LayoutElement>().preferredHeight = 48;
            var layout = row.GetComponent<HorizontalLayoutGroup>();
            layout.spacing = 16;
            layout.childAlignment = TextAnchor.MiddleLeft;
            layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;
            var label = NewRect("Text", row, typeof(TextMeshProUGUI), typeof(LayoutElement));
            AdeEditorText.Configure(label.GetComponent<TMP_Text>(), AdeEditorText.BodySize);
            label.GetComponent<TMP_Text>().text = title;
            label.GetComponent<TMP_Text>().color = AdeUiTheme.Current.Text;
            appearanceLabels.Add(label.GetComponent<TMP_Text>());
            label.GetComponent<TMP_Text>().raycastTarget = false;
            label.GetComponent<LayoutElement>().preferredWidth = 156;
            var dropdown = Instantiate(ThemeDropdown, row, false);
            dropdown.name = "Value";
            dropdown.onValueChanged = new TMP_Dropdown.DropdownEvent();
            dropdown.interactable = true;
            dropdown.ClearOptions();
            var names = new List<string> { "跟随主题" };
            foreach (var option in options) names.Add(option.Name);
            dropdown.AddOptions(names);
            var size = dropdown.GetComponent<LayoutElement>() ?? dropdown.gameObject.AddComponent<LayoutElement>();
            size.minHeight = size.preferredHeight = 44;
            size.minWidth = 180;
            size.flexibleWidth = 1;
            appearanceRefresh.Add(() =>
            {
                int index = 0;
                for (int i = 0; i < options.Count; i++) if (options[i].Id == get()) { index = i + 1; break; }
                dropdown.SetValueWithoutNotify(index);
            });
            dropdown.onValueChanged.AddListener(index =>
            {
                CurrentBackgroundOption?.SetSelected(false);
                set(index == 0 ? null : options[index - 1].Id);
                preference.Customize = true;
                CurrentBackgroundOption?.SetSelected(true);
                UpdateTabs();
                ApplyAppearance();
                SavePreferences();
            });
        }

        private static RectTransform NewRect(string name, Transform parent, params Type[] components)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = parent.gameObject.layer;
            go.transform.SetParent(parent, false);
            foreach (var component in components) go.AddComponent(component);
            return (RectTransform)go.transform;
        }
    }
}
