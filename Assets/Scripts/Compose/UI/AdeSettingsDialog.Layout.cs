using TMPro;
using Arcade.Compose.UI;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Arcade.Compose
{
    public sealed partial class AdeSettingsDialog
    {
        private readonly List<Image> hotkeyGroups = new List<Image>();
        private readonly List<TMP_Text> hotkeyHeadings = new List<TMP_Text>();
        private readonly List<Image> fixedHotkeys = new List<Image>();
        private TMP_Text hotkeyGestureHelp;
        private const float HotkeyWidth = 208, ControlHeight = 44, AudioInputWidth = 88;

        private void BuildHotkeyGroups()
        {
            Transform section = null;
            foreach (var row in rows)
            {
                if (row.Page != Page.Hotkeys || row.Row.name == "Reset" || row.Row == hotkeyPresetRow) continue;
                var body = row.Row.GetComponentInChildren<TMP_Text>(true);
                if (!row.Row.GetComponentInChildren<Selectable>(true) && body
                    && (row.Row.name == "MouseGestureHelp" || body.text.Contains("\n")))
                {
                    // This is flowing body text, not a fixed-height section heading.
                    hotkeyGestureHelp = body;
                    var bodySize = body.GetComponent<LayoutElement>();
                    if (bodySize) { bodySize.minHeight = bodySize.preferredHeight = -1; bodySize.flexibleHeight = 0; }
                    row.Host = null;
                    section = null;
                    continue;
                }
                if (!row.Row.GetComponentInChildren<Selectable>(true)
                    && row.Row.GetComponentsInChildren<TMP_Text>(true).Length == 1)
                {
                    var rect = Rect(row.Row.name + "Group", pages[(int)Page.Hotkeys], typeof(Image), typeof(VerticalLayoutGroup));
                    var layout = rect.GetComponent<VerticalLayoutGroup>();
                    layout.padding = new RectOffset(20, 20, 16, 16);
                    layout.spacing = 8;
                    layout.childControlWidth = layout.childControlHeight = true;
                    layout.childForceExpandWidth = true;
                    layout.childForceExpandHeight = false;
                    hotkeyGroups.Add(rect.GetComponent<Image>());
                    hotkeyHeadings.Add(row.Row.GetComponentInChildren<TMP_Text>(true));
                    section = rect;
                }
                row.Host = section;
                var key = row.Row.Find("Key")?.GetComponent<TMP_Text>();
                if (key) BuildFixedHotkey(row.Row, key);
            }
        }

        private void BuildFixedHotkey(Transform row, TMP_Text key)
        {
            var cap = Rect("FixedKeycap", row, typeof(Image), typeof(LayoutElement));
            fixedHotkeys.Add(cap.GetComponent<Image>());
            if (key.text == "Ctrl") key.text = AdeInputManager.PrimaryModifierLabel;
            key.transform.SetParent(cap, false);
            Fill(key.rectTransform);
            key.rectTransform.offsetMin = new Vector2(12, 0);
            key.rectTransform.offsetMax = new Vector2(-60, 0);
            key.fontSize = AdeEditorText.BodySize;
            key.alignment = TextAlignmentOptions.Center;
            key.textWrappingMode = TextWrappingModes.NoWrap;
            key.raycastTarget = false;
            var hint = Rect("FixedLabel", cap, typeof(TextMeshProUGUI));
            hint.anchorMin = new Vector2(1, 0); hint.anchorMax = Vector2.one;
            hint.pivot = new Vector2(1, .5f);
            hint.sizeDelta = new Vector2(52, 0); hint.anchoredPosition = new Vector2(-8, 0);
            var label = hint.GetComponent<TMP_Text>();
            AdeEditorText.Configure(label, AdeEditorText.CaptionSize);
            label.text = "固定";
            label.alignment = TextAlignmentOptions.Center;
            label.raycastTarget = false;
        }

        private void StyleHotkeyGroups()
        {
            if (hotkeyGestureHelp)
            {
                hotkeyGestureHelp.fontSize = AdeEditorText.CaptionSize;
                hotkeyGestureHelp.fontStyle = FontStyles.Normal;
                hotkeyGestureHelp.textWrappingMode = TextWrappingModes.Normal;
                hotkeyGestureHelp.color = AdeUiTheme.Current.Text;
            }
            foreach (var group in hotkeyGroups)
            {
                group.sprite = AdeUiTheme.RoundedSurface;
                group.type = Image.Type.Sliced;
                group.color = AdeUiTheme.Current.Chrome;
                group.raycastTarget = false;
            }
            foreach (var heading in hotkeyHeadings)
            {
                heading.fontSize = AdeEditorText.CaptionSize;
                heading.fontStyle = FontStyles.Bold;
                heading.color = AdeUiTheme.Current.Muted;
                var size = heading.transform.parent.GetComponent<LayoutElement>()
                    ?? heading.transform.parent.gameObject.AddComponent<LayoutElement>();
                size.preferredHeight = 34;
            }
            foreach (var key in fixedHotkeys)
            {
                key.sprite = AdeUiTheme.ReadOnlySurface;
                key.type = Image.Type.Sliced;
                key.color = Color.white;
                key.raycastTarget = false;
                foreach (var label in key.GetComponentsInChildren<TMP_Text>())
                    label.color = AdeUiTheme.Current.Muted;
            }
        }

        // Keep the original controls and listeners; only their layout changes in the hub.
        private static void ArrangeRow(Transform row, Page page)
        {
            var layout = row.GetComponent<HorizontalLayoutGroup>();
            if (!layout) return;
            layout.padding = new RectOffset();
            layout.spacing = 16;
            layout.childAlignment = TextAnchor.MiddleLeft;
            layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandWidth = layout.childForceExpandHeight = false;
            var fitter = row.GetComponent<ContentSizeFitter>();
            if (fitter) fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            bool hasControl = false;
            foreach (Transform child in row)
                if (child.GetComponentInChildren<Selectable>(true)) hasControl = true;
            bool staticBinding = page == Page.Hotkeys && !hasControl && row.childCount > 1;
            foreach (Transform child in row)
            {
                var size = child.GetComponent<LayoutElement>() ?? child.gameObject.AddComponent<LayoutElement>();
                if (page == Page.Hotkeys && (child.name == "Key" || child.name == "FixedKeycap"))
                {
                    size.minWidth = size.preferredWidth = row.name == "Reset" ? 280 : HotkeyWidth;
                    size.minHeight = size.preferredHeight = ControlHeight;
                    size.flexibleWidth = 0;
                    var button = child.GetComponentInChildren<Button>(true);
                    if (button)
                    {
                        Fill((RectTransform)button.transform);
                        var caption = button.GetComponentInChildren<TMP_Text>(true);
                        caption.fontSize = AdeEditorText.BodySize;
                        caption.enableAutoSizing = true;
                        caption.fontSizeMin = 16;
                        caption.fontSizeMax = AdeEditorText.BodySize;
                        caption.textWrappingMode = TextWrappingModes.NoWrap;
                        caption.rectTransform.offsetMin = new Vector2(12, 0);
                        caption.rectTransform.offsetMax = new Vector2(-12, 0);
                    }
                    continue;
                }
                var label = child.GetComponent<TMP_Text>();
                if (label)
                {
                    label.fontSize = AdeEditorText.BodySize;
                    label.alignment = TextAlignmentOptions.Left;
                    label.textWrappingMode = hasControl ? TextWrappingModes.NoWrap : TextWrappingModes.Normal;
                    size.minWidth = size.preferredWidth = 0;
                    size.flexibleWidth = 1;
                    continue;
                }
                var control = child.GetComponent<Selectable>();
                if (!control) continue;
                float width = control is Toggle ? 36 : control is Slider ? 180
                    : control is TMP_Dropdown ? 260 : (page == Page.Audio || page == Page.Spectrum) && control is TMP_InputField ? AudioInputWidth
                    : page == Page.Grid ? 360 : row.name == "Reset" ? 280 : 180;
                size.minWidth = size.preferredWidth = width;
                size.flexibleWidth = 0;
                size.minHeight = size.preferredHeight = control is Slider ? 24 : control is Toggle ? 36 : 44;
                foreach (var text in control.GetComponentsInChildren<TMP_Text>(true))
                    text.fontSize = AdeEditorText.BodySize;
                if (control is TMP_InputField input && (page == Page.Audio || page == Page.Spectrum))
                    input.textComponent.alignment = TextAlignmentOptions.Center;
            }
            if (hasControl || staticBinding)
            {
                var size = row.GetComponent<LayoutElement>() ?? row.gameObject.AddComponent<LayoutElement>();
                size.minHeight = size.preferredHeight = 48;
            }
        }
    }
}
