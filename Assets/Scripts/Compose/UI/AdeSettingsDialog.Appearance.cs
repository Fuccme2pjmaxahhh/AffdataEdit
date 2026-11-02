using Cysharp.Threading.Tasks;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Text = TMPro.TMP_Text;

namespace Arcade.Compose
{
    public sealed partial class AdeSettingsDialog
    {
        private readonly List<(Button button, TMP_Text label, Image[] preview, AdeUiTheme.Preset preset)> themeCards
            = new List<(Button, TMP_Text, Image[], AdeUiTheme.Preset)>();
        private readonly List<TMP_Text> appearanceLabels = new List<TMP_Text>();
        private readonly RectTransform[] themeSections = new RectTransform[2];
        private bool? displayedDark;

        private void BuildAppearance()
        {
            var page = pages[(int)Page.Appearance];
            AppearanceLabel(page, "编辑器配色", 30, 44);
            AppearanceLabel(page, "点击预览立即应用，深浅色分别记住选择。", 24, 38);
            for (int group = 0; group < 2; group++)
            {
                var section = Rect(group == 0 ? "DarkThemes" : "LightThemes", page, typeof(VerticalLayoutGroup));
                themeSections[group] = section;
                var sectionLayout = section.GetComponent<VerticalLayoutGroup>();
                sectionLayout.spacing = 14; sectionLayout.childControlWidth = sectionLayout.childControlHeight = true;
                sectionLayout.childForceExpandWidth = true; sectionLayout.childForceExpandHeight = false;
                RectTransform row = null; int count = 0;
                foreach (var preset in AdeUiTheme.Presets)
                {
                    if (preset.IsDark != (group == 0)) continue;
                    if (count++ % 2 == 0)
                    {
                        row = Rect("ThemeRow", section, typeof(HorizontalLayoutGroup), typeof(LayoutElement));
                        row.GetComponent<LayoutElement>().preferredHeight = 146;
                        var layout = row.GetComponent<HorizontalLayoutGroup>();
                        layout.spacing = 14; layout.childControlWidth = layout.childControlHeight = true;
                        layout.childForceExpandWidth = layout.childForceExpandHeight = true;
                    }
                    var button = MakeButton(row, preset.Name, () => AdeUiTheme.SetPreset(preset.Id));
                    button.name = preset.Id;
                    button.GetComponent<LayoutElement>().flexibleWidth = 1;
                    button.GetComponent<LayoutElement>().preferredWidth = 0;
                    var label = button.GetComponentInChildren<TMP_Text>();
                    label.fontSize = 24; label.rectTransform.anchorMax = new Vector2(1, 0);
                    label.rectTransform.offsetMin = new Vector2(12, 0); label.rectTransform.offsetMax = new Vector2(-12, 44);
                    var preview = new Image[8];
                    preview[0] = PreviewRect(button.transform, "Surface", 0, .30f, 1, 1);
                    preview[0].rectTransform.offsetMin = new Vector2(10, 4); preview[0].rectTransform.offsetMax = new Vector2(-10, -10);
                    preview[0].gameObject.AddComponent<Mask>().showMaskGraphic = true;
                    preview[1] = PreviewRect(preview[0].transform, "Sidebar", 0, 0, .24f, 1);
                    preview[2] = PreviewRect(preview[0].transform, "Title", .32f, .72f, .66f, .78f);
                    preview[3] = PreviewRect(preview[0].transform, "TextLine", .32f, .55f, .88f, .59f);
                    preview[4] = PreviewRect(preview[0].transform, "Input", .32f, .18f, .88f, .40f);
                    preview[5] = PreviewRect(preview[0].transform, "Action", .72f, .20f, .86f, .38f);
                    preview[6] = PreviewRect(preview[1].transform, "Selected", .12f, .59f, .88f, .73f);
                    preview[7] = PreviewRect(preview[1].transform, "Item", .18f, .40f, .72f, .44f);
                    themeCards.Add((button, label, preview, preset));
                }
            }
        }

        private Image PreviewRect(Transform parent, string name, float x1, float y1, float x2, float y2)
        {
            var rect = Rect(name, parent, typeof(Image)); rect.anchorMin = new Vector2(x1, y1); rect.anchorMax = new Vector2(x2, y2);
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            var image = rect.GetComponent<Image>(); image.raycastTarget = false; return image;
        }

        private void AppearanceLabel(Transform parent, string caption, int size, float height)
        {
            var rect = Rect("AppearanceLabel", parent, typeof(TextMeshProUGUI), typeof(LayoutElement));
            var text = rect.GetComponent<TMP_Text>(); AdeEditorText.Configure(text, size); text.text = caption;
            rect.GetComponent<LayoutElement>().preferredHeight = height; appearanceLabels.Add(text);
        }

        private void StyleAppearance()
        {
            if (displayedDark != AdeUiTheme.Dark)
            {
                displayedDark = AdeUiTheme.Dark;
                for (int i = 0; i < themeSections.Length; i++) themeSections[i].gameObject.SetActive((i == 0) == AdeUiTheme.Dark);
                if (mounted && activePage == Page.Appearance) ScrollToTop(Page.Appearance).Forget();
            }
            foreach (var label in appearanceLabels) label.color = AdeUiTheme.Current.Text;
            foreach (var card in themeCards)
            {
                bool selected = card.preset == AdeUiTheme.Current;
                var colors = ColorBlock.defaultColorBlock;
                colors.normalColor = colors.selectedColor = selected ? AdeUiTheme.Current.Selection : AdeUiTheme.Current.Input;
                colors.highlightedColor = Color.Lerp(colors.normalColor, AdeUiTheme.Current.Text, .04f);
                colors.pressedColor = Color.Lerp(colors.normalColor, AdeUiTheme.Current.Text, .08f);
                card.button.image.sprite = AdeUiTheme.RoundedSurface;
                card.button.image.type = Image.Type.Sliced;
                card.button.image.color = Color.white;
                card.button.colors = colors;
                card.label.color = AdeUiTheme.Current.Text;
                AdeUiIcons.SetText(card.label, (selected ? AdeUiIcons.Check + " " : "") + card.preset.Name);
                var p = card.preset;
                card.preview[0].sprite = AdeUiTheme.RoundedSurface;
                card.preview[0].type = Image.Type.Sliced;
                card.preview[0].color = p.Panel; card.preview[1].color = p.Chrome;
                card.preview[2].color = p.Text; card.preview[3].color = p.Muted;
                card.preview[4].color = p.Input; card.preview[5].color = p.WhiteCloseButton ? Color.white : p.Accent;
                card.preview[6].color = p.Selection; card.preview[7].color = p.Muted;
            }
        }
    }
}
