using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Arcade.Compose
{
    public sealed partial class AdeUiTheme
    {
        private void StyleDropdown(TMP_Dropdown dropdown)
        {
            if (!dropdown.template || !dropdown.captionText || !dropdown.itemText) return;
            var caption = dropdown.captionText;
            caption.alignment = TextAlignmentOptions.Left;
            caption.textWrappingMode = TextWrappingModes.NoWrap;
            caption.overflowMode = TextOverflowModes.Ellipsis;
            caption.rectTransform.anchorMin = Vector2.zero;
            caption.rectTransform.anchorMax = Vector2.one;
            caption.rectTransform.offsetMin = new Vector2(12, 0);
            caption.rectTransform.offsetMax = new Vector2(-36, 0);
            var arrow = dropdown.transform.Find("Arrow") as RectTransform;
            if (arrow)
            {
                arrow.anchorMin = arrow.anchorMax = new Vector2(1, .5f);
                arrow.pivot = new Vector2(.5f, .5f);
                arrow.anchoredPosition = new Vector2(-20, 0);
                arrow.sizeDelta = new Vector2(16, 16);
                var image = arrow.GetComponent<Image>();
                if (image) image.color = Current.Muted;
            }
            var template = dropdown.template;
            var background = template.GetComponent<Image>();
            if (background)
            {
                background.sprite = Shape("ThemeDropdownList", ControlRadius, Current.Input, Current.Border);
                background.type = UnityEngine.UI.Image.Type.Sliced;
                background.color = Color.white;
            }
            template.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, 228);
            var scroll = template.GetComponent<ScrollRect>();
            if (scroll)
            {
                var input = UnityEngine.EventSystems.EventSystem.current
                    ? UnityEngine.EventSystems.EventSystem.current.currentInputModule as UnityEngine.InputSystem.UI.InputSystemUIInputModule : null;
                float deltaPerTick = input ? Mathf.Abs(input.scrollDeltaPerTick) : 1;
                // Move one 44-unit option per wheel tick, including the UI module's multiplier.
                scroll.scrollSensitivity = deltaPerTick > 0 ? 44 / deltaPerTick : 44;
                AdeSmoothScrollInstaller.Ensure(scroll);
            }
            if (scroll && scroll.verticalScrollbar)
            {
                var bar = scroll.verticalScrollbar;
                var rect = (RectTransform)bar.transform;
                rect.anchorMin = new Vector2(1, 0); rect.anchorMax = Vector2.one;
                rect.pivot = Vector2.one;
                rect.sizeDelta = new Vector2(6, -12);
                rect.anchoredPosition = new Vector2(-6, -6);
                var track = bar.GetComponent<Image>();
                if (track) track.color = Color.clear;
                if (bar.targetGraphic is Image handle)
                {
                    handle.sprite = RoundedSurface;
                    handle.type = UnityEngine.UI.Image.Type.Sliced;
                    handle.color = Current.Border;
                }
                var area = (RectTransform)bar.handleRect.parent;
                area.offsetMin = area.offsetMax = Vector2.zero;
                bar.handleRect.sizeDelta = Vector2.zero;
                if (scroll.viewport) scroll.viewport.offsetMax = new Vector2(-18, scroll.viewport.offsetMax.y);
            }
            var item = dropdown.itemText.GetComponentInParent<Toggle>(true);
            if (!item) return;
            if (dropdown.itemImage == item.targetGraphic) dropdown.itemImage = null;
            if (!item.GetComponent<AdeDropdownPointerFocus>())
                item.gameObject.AddComponent<AdeDropdownPointerFocus>();
            var itemRect = (RectTransform)item.transform;
            itemRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, 44);
            var content = (RectTransform)itemRect.parent;
            content.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, 48);
            dropdown.itemText.fontSize = AdeEditorText.BodySize;
            dropdown.itemText.alignment = TextAlignmentOptions.Left;
            dropdown.itemText.textWrappingMode = TextWrappingModes.NoWrap;
            dropdown.itemText.overflowMode = TextOverflowModes.Ellipsis;
            dropdown.itemText.rectTransform.offsetMin = new Vector2(30, 0);
            dropdown.itemText.rectTransform.offsetMax = new Vector2(-10, 0);
            if (item.targetGraphic is Image fill)
            {
                fill.sprite = RoundedSurface;
                fill.type = UnityEngine.UI.Image.Type.Sliced;
                fill.color = Color.white;
                var colors = ColorBlock.defaultColorBlock;
                colors.normalColor = Color.clear;
                colors.highlightedColor = colors.selectedColor = Current.Selection;
                colors.pressedColor = Color.Lerp(Current.Selection, Current.Text, .08f);
                item.colors = colors;
            }
            if (item.graphic) item.graphic.color = Current.Text;
        }
    }
}
