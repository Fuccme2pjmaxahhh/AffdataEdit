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
            // The free-text rating ("Future 10+") sits on AA's difficulty cut in the
            // strip. ArcadeComposeManager places the difficulty buttons, cut and the
            // shared text right edge for editor and player positions.
            var rating = (RectTransform)panel.Find("Difficult");
            rating.anchoredPosition = new Vector2(rating.anchoredPosition.x, -86);
            rating.sizeDelta = new Vector2(112, 40);
            var ratingInput = rating.GetComponent<TMP_InputField>();
            ratingInput.characterLimit = 0;
            ratingInput.textComponent.alignment = TextAlignmentOptions.Midline;
            // Longer ratings shrink a little instead of running into the buttons.
            ratingInput.textComponent.enableAutoSizing = true;
            ratingInput.textComponent.fontSizeMin = 14;
            ratingInput.textComponent.fontSizeMax = 18;
            if (ratingInput.placeholder is TMP_Text placeholder) placeholder.alignment = TextAlignmentOptions.Midline;
            // AA's title/artist lines (-145/-173) are centred on the cover (-113 to -203).
            var name = (RectTransform)panel.Find("Name");
            name.anchoredPosition = new Vector2(-255, -125);
            name.sizeDelta = new Vector2(name.sizeDelta.x, 40);
            var composer = (RectTransform)panel.Find("Composer");
            composer.anchoredPosition = new Vector2(-255, -153);
            composer.sizeDelta = new Vector2(composer.sizeDelta.x, 40);
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
