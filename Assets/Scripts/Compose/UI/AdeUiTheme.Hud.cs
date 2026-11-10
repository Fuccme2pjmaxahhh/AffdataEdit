using Arcade.Gameplay;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Arcade.Compose
{
    public sealed partial class AdeUiTheme
    {
        private Material hudMaterial;

        private void StylePlaybackHud()
        {
            var compose = ArcadeComposeManager.Instance;
            if (!compose) return;
            var hud = darkPreset;
            Color surface = hud.Chrome, raised = hud.Panel;
            Color border = Color.Lerp(hud.Border, hud.Text, .18f);
            Color foreground = hud.Text, muted = hud.Muted;
            if (!Dark)
            {
                // A charcoal surface with the light theme's warm/cool undertone.
                // Keep luminance independent of its accent so white HUD text stays readable.
                Color chrome = Current.Chrome, text = Current.Text;
                float chromeMean = (chrome.r + chrome.g + chrome.b) / 3f;
                float textMean = (text.r + text.g + text.b) / 3f;
                surface = new Color(
                    .27f + (chrome.r - chromeMean) * .55f + (text.r - textMean) * .25f,
                    .28f + (chrome.g - chromeMean) * .55f + (text.g - textMean) * .25f,
                    .29f + (chrome.b - chromeMean) * .55f + (text.b - textMean) * .25f);
                raised = Color.Lerp(surface, chrome, .10f);
                border = Color.Lerp(surface, chrome, .28f);
                foreground = Color.white;
                muted = Color.Lerp(surface, Color.white, .78f);
            }
            if (!hudMaterial) hudMaterial = new Material(Resources.Load<Shader>("AffdataEdit/Theme/HudPalette"));
            hudMaterial.SetColor("_SurfaceColor", surface);
            hudMaterial.SetColor("_RaisedColor", raised);
            hudMaterial.SetColor("_BorderColor", border);
            hudMaterial.SetColor("_TextColor", foreground);
            var panel = ArcSkinManager.Instance.SongInfo;
            LayoutHudFields(panel.rectTransform);
            panel.sprite = darkSprites[11];
            panel.material = hudMaterial;
            panel.color = Color.white;
            compose.PauseButtonImage.material = hudMaterial;
            compose.PauseButtonImage.color = Color.white;
            compose.TimingSliderHandle.color = foreground;
            var timingSlider = compose.TimingSliderHandle.GetComponentInParent<Slider>();
            if (timingSlider && timingSlider.fillRect && timingSlider.fillRect.TryGetComponent<Image>(out var timingFill))
                timingFill.color = foreground;
            compose.SetPlaySprite(darkSprites[12]);
            compose.SetPlayPressedSprite(darkSprites[13]);
            compose.SetPauseSprite(darkSprites[14]);
            compose.SetPausePressedSprite(darkSprites[15]);
            AlignToolbarEdges(compose.LeftBarView, compose.PauseButtonImage.rectTransform, compose.BottomBarView);
            AlignToolbarEdges(compose.RightBarView, panel.rectTransform, compose.BottomBarView);
            var score = ArcScoreManager.Instance.ScoreText;
            score.color = foreground;
            score.fontSharedMaterial.SetColor("_OutlineColor", surface);
            foreach (var label in panel.GetComponentsInChildren<TMP_Text>(true))
            {
                if (label == score) continue;
                Color color = label.transform.IsChildOf(panel.transform.Find("Cover")) ? Color.white
                    : label.name == "Placeholder" ? muted : foreground;
                color.a = label.color.a;
                label.color = color;
            }
        }

        private static void LayoutHudFields(RectTransform panel)
        {
            // The player artwork is offset 30 units beyond the window. Keep editable
            // text inside that visible edge. AE's rating contains only "10", not "Future 10".
            var rating = (RectTransform)panel.Find("Difficult");
            rating.anchoredPosition = new Vector2(-116, -85);
            rating.sizeDelta = new Vector2(68, 40);
            var ratingInput = rating.GetComponent<TMP_InputField>();
            ratingInput.textComponent.alignment = TextAlignmentOptions.MidlineRight;
            if (ratingInput.placeholder is TMP_Text placeholder) placeholder.alignment = TextAlignmentOptions.MidlineRight;
            ((RectTransform)panel.Find("Difficulties")).anchoredPosition = new Vector2(-284, -89);
            var name = (RectTransform)panel.Find("Name");
            name.anchoredPosition = new Vector2(-245, -122);
            name.sizeDelta = new Vector2(197, 40);
            var composer = (RectTransform)panel.Find("Composer");
            composer.anchoredPosition = new Vector2(-245, -152);
            composer.sizeDelta = new Vector2(197, 40);
        }

        private static bool IsPlaybackHud(Graphic graphic)
        {
            var compose = ArcadeComposeManager.Instance;
            var skin = ArcSkinManager.Instance;
            var score = ArcScoreManager.Instance;
            return (compose && graphic == compose.PauseButtonImage)
                || (score && graphic == score.ScoreText)
                || (skin && skin.SongInfo && graphic.transform.IsChildOf(skin.SongInfo.transform));
        }

        private static void AlignToolbarEdges(RectTransform bar, RectTransform header, RectTransform footer)
        {
            var layout = bar.GetComponent<VerticalLayoutGroup>();
            var corners = new Vector3[4];
            header.GetWorldCorners(corners);
            float topEdge = bar.rect.yMax - bar.InverseTransformPoint(corners[0]).y;
            footer.GetWorldCorners(corners);
            float bottomEdge = bar.InverseTransformPoint(corners[1]).y - bar.rect.yMin;
            var buttons = bar.GetComponentsInChildren<Button>();
            // Tool backgrounds extend beyond their 60px hit rects. Align the full square.
            float topOutset = buttons.Length == 0 ? 0 : BackgroundOutset(buttons[0], true);
            float bottomOutset = buttons.Length == 0 ? 0 : BackgroundOutset(buttons[buttons.Length - 1], false);
            int top = Mathf.CeilToInt(topEdge + topOutset - .001f);
            int bottom = Mathf.CeilToInt(bottomEdge + bottomOutset - .001f);
            layout.padding = new RectOffset(layout.padding.left, layout.padding.right, top, bottom);
        }

        private static float BackgroundOutset(Button button, bool top)
        {
            var rect = (RectTransform)button.transform;
            var corners = new Vector3[4];
            button.targetGraphic.rectTransform.GetWorldCorners(corners);
            return Mathf.Max(0, top ? rect.InverseTransformPoint(corners[1]).y - rect.rect.yMax
                : rect.rect.yMin - rect.InverseTransformPoint(corners[0]).y);
        }
    }
}
