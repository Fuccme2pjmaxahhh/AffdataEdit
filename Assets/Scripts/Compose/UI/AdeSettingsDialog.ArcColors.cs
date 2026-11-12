using Arcade.Compose.Operation;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Arcade.Compose
{
    public sealed partial class AdeSettingsDialog
    {
        private RectTransform arcColorSettings;
        private TMP_Text arcColorHeading, arcColorHint;
        private readonly Toggle[] arcColorToggles = new Toggle[5];

        private void BuildArcColorSettings()
        {
            arcColorSettings = Rect("ArcColorCycle", pages[(int)Page.Editing], typeof(VerticalLayoutGroup));
            var layout = arcColorSettings.GetComponent<VerticalLayoutGroup>();
            layout.spacing = 8;
            layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandWidth = true; layout.childForceExpandHeight = false;
            arcColorHeading = Rect("Heading", arcColorSettings, typeof(TextMeshProUGUI)).GetComponent<TMP_Text>();
            AdeEditorText.Configure(arcColorHeading, AdeEditorText.BodySize);
            arcColorHeading.text = "热键轮换创建 Arc 的颜色";
            arcColorHint = Rect("Hint", arcColorSettings, typeof(TextMeshProUGUI)).GetComponent<TMP_Text>();
            AdeEditorText.Configure(arcColorHint, AdeEditorText.CaptionSize);
            arcColorHint.text = "仅在勾选的颜色中轮换，至少保留一种。";
            arcColorHint.textWrappingMode = TextWrappingModes.Normal;
            var choices = Rect("Colors", arcColorSettings, typeof(HorizontalLayoutGroup));
            var choicesLayout = choices.GetComponent<HorizontalLayoutGroup>();
            choicesLayout.spacing = 8;
            choicesLayout.childControlWidth = choicesLayout.childControlHeight = true;
            choicesLayout.childForceExpandWidth = true; choicesLayout.childForceExpandHeight = false;
            var preference = ArcadeComposeManager.Instance.ArcadePreference;
            for (int color = 0; color < arcColorToggles.Length; color++)
            {
                int bit = 1 << color;
                var chip = Rect(AdeClickToCreate.ArcColorName(color), choices, typeof(Image), typeof(Toggle), typeof(LayoutElement));
                var size = chip.GetComponent<LayoutElement>();
                size.preferredHeight = 44; size.flexibleWidth = 1;
                var toggle = chip.GetComponent<Toggle>();
                toggle.targetGraphic = chip.GetComponent<Image>();
                var check = Rect("Check", chip, typeof(Image));
                check.anchorMin = check.anchorMax = new Vector2(0, .5f);
                check.anchoredPosition = new Vector2(22, 0); check.sizeDelta = new Vector2(20, 20);
                toggle.graphic = check.GetComponent<Image>();
                toggle.graphic.raycastTarget = false;
                var caption = Rect("Text", chip, typeof(TextMeshProUGUI)); Fill(caption);
                caption.offsetMin = new Vector2(42, 0); caption.offsetMax = new Vector2(-8, 0);
                var text = caption.GetComponent<TMP_Text>();
                AdeEditorText.Configure(text, AdeEditorText.BodySize);
                text.text = AdeClickToCreate.ArcColorName(color);
                toggle.SetIsOnWithoutNotify((preference.GetArcColorCycleMask() & bit) != 0);
                toggle.onValueChanged.AddListener(enabled =>
                {
                    int mask = preference.GetArcColorCycleMask();
                    int next = enabled ? mask | bit : mask & ~bit;
                    if (next == 0)
                    {
                        toggle.SetIsOnWithoutNotify(true);
                        AdeToast.Instance?.Show("至少保留一种轮换颜色");
                        return;
                    }
                    preference.ArcColorCycleMask = next;
                    preference.ArcColorCycleCustomized = true;
                    ArcadeComposeManager.Instance.SavePreferences();
                });
                arcColorToggles[color] = toggle;
            }
        }

        private void StyleArcColorSettings()
        {
            arcColorHeading.color = AdeUiTheme.Current.Text;
            arcColorHint.color = AdeUiTheme.Current.Muted;
            int mask = ArcadeComposeManager.Instance.ArcadePreference.GetArcColorCycleMask();
            for (int color = 0; color < arcColorToggles.Length; color++)
            {
                var toggle = arcColorToggles[color];
                toggle.SetIsOnWithoutNotify((mask & (1 << color)) != 0);
                var image = (Image)toggle.targetGraphic;
                image.sprite = AdeUiTheme.RoundedSurface; image.type = Image.Type.Sliced;
                image.color = AdeUiTheme.Current.Input;
                var check = (Image)toggle.graphic;
                check.sprite = checkmark; check.color = AdeUiTheme.Current.Text;
                toggle.GetComponentInChildren<TMP_Text>().color = AdeUiTheme.Current.Text;
            }
        }
    }
}
