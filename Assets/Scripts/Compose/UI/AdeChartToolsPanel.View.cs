using TMPro;
using System;
using System.Collections.Generic;
using System.Linq;
using Arcade.Compose.Editing;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace Arcade.Compose
{
    public sealed partial class AdeChartToolsPanel
    {
        [SerializeField] private List<Image> backgrounds = new List<Image>();
        [SerializeField] private List<Image> sections = new List<Image>();
        [SerializeField] private List<TMP_Text> labels = new List<TMP_Text>();
        [SerializeField] private List<TMP_Text> mutedLabels = new List<TMP_Text>();
        [SerializeField] private List<Selectable> controls = new List<Selectable>();
        [SerializeField] private List<Button> quietButtons = new List<Button>();
        [SerializeField] private Image closeIcon;
        private Color Foreground => AdeUiTheme.Current.Text;
        private Color Muted => AdeUiTheme.Current.Muted;
        private const float ControlHeight = 44;

        private TMP_Text MutedLabel(Transform parent, string text, float height = 34, int fontSize = 18)
        {
            var label = Label(parent, text, height, fontSize);
            mutedLabels.Add(label); label.color = Muted;
            return label;
        }

        private Button Quiet(Button button) { quietButtons.Add(button); Style(button); return button; }

        private RectTransform Rect(string name, Transform parent, params Type[] types)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = parent.gameObject.layer;
            go.transform.SetParent(parent, false);
            foreach (var type in types) go.AddComponent(type);
            return (RectTransform)go.transform;
        }
        private static void Fill(RectTransform rect)
        { rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.sizeDelta = Vector2.zero; rect.anchoredPosition = Vector2.zero; }
        private static void Size(Transform transform, float width, float height, float flexWidth = 0, float flexHeight = 0)
        {
            var size = transform.GetComponent<LayoutElement>() ?? transform.gameObject.AddComponent<LayoutElement>();
            size.layoutPriority = 2;
            // A flexible cell starts at zero; its text must not change the column width.
            size.minWidth = width < 0 ? 0 : width; size.preferredWidth = width < 0 ? 0 : width;
            size.minHeight = size.preferredHeight = height;
            size.flexibleWidth = width < 0 ? 1 : flexWidth; size.flexibleHeight = flexHeight;
        }
        private TMP_Text Label(Transform parent, string text, float height = 40, int fontSize = 20)
        {
            var rect = Rect(text.Length == 0 ? "Label" : text, parent, typeof(TextMeshProUGUI));
            var label = rect.GetComponent<TMP_Text>();
            label.font = font; label.fontSize = fontSize; label.text = text; label.color = Foreground;
            label.alignment = TextAlignmentOptions.Left; label.raycastTarget = false;
            label.richText = false; label.textWrappingMode = TextWrappingModes.Normal; label.overflowMode = TextOverflowModes.Truncate;
            Size(rect, -1, Mathf.Max(height, Mathf.Ceil(fontSize * 1.45f))); labels.Add(label);
            return label;
        }
        private Button Button(Transform parent, string text, UnityAction action, float width = -1)
        {
            var rect = Rect(text, parent, typeof(Image), typeof(Button));
            var button = rect.GetComponent<Button>(); button.targetGraphic = rect.GetComponent<Image>();
            var source = AdeTimingEditor.Instance.TimingPrefab.GetComponentInChildren<TMPro.TMP_InputField>(true);
            rect.GetComponent<Image>().sprite = ((Image)source.targetGraphic).sprite;
            rect.GetComponent<Image>().type = Image.Type.Sliced;
            button.onClick.AddListener(action); Size(rect, width, ControlHeight);
            var label = Label(rect, text); Fill(label.rectTransform);
            label.textWrappingMode = TextWrappingModes.NoWrap; label.overflowMode = TextOverflowModes.Ellipsis;
            label.alignment = TextAlignmentOptions.Center;
            label.rectTransform.offsetMin = new Vector2(8, 0); label.rectTransform.offsetMax = new Vector2(-8, 0);
            controls.Add(button); Style(button);
            return button;
        }
        private Button IconButton(Transform parent, string name, string resource, UnityAction action)
        {
            var button = Quiet(Button(parent, "", action, ControlHeight));
            button.name = name;
            var icon = Rect("Icon", button.transform, typeof(Image));
            icon.anchorMin = icon.anchorMax = new Vector2(.5f, .5f);
            icon.sizeDelta = new Vector2(24, 24);
            var image = icon.GetComponent<Image>();
            image.sprite = Resources.Load<Sprite>("AffdataEdit/Icons/" + resource);
            image.raycastTarget = false;
            Style(button);
            return button;
        }

        private void SetGroupOptions(TMP_Dropdown dropdown, int value)
        {
            if (dropdown.options != groupOptions) dropdown.options = groupOptions;
            dropdown.SetValueWithoutNotify(Mathf.Max(0, value)); dropdown.RefreshShownValue();
        }
        private void Style(Selectable control)
        {
            if (!control) return;
            // Inputs, dropdowns and checkboxes use the same templates and theme as SettingsDrawer.
            if (control is Toggle toggle) toggle.graphic.color = AdeUiTheme.Dark ? Color.white : Foreground;
            var button = control as Button;
            if (!button || !quietButtons.Contains(button)) return;
            ((Image)button.targetGraphic).sprite = AdeUiTheme.RoundedSurface;
            ((Image)button.targetGraphic).type = Image.Type.Sliced;
            control.transition = Selectable.Transition.ColorTint;
            var colors = ColorBlock.defaultColorBlock; colors.normalColor = colors.selectedColor = Color.clear;
            colors.fadeDuration = 0;
            colors.highlightedColor = AdeUiTheme.Current.Selection;
            colors.pressedColor = Color.Lerp(AdeUiTheme.Current.Selection, Foreground, .12f);
            colors.disabledColor = Color.clear;
            control.colors = colors;
            control.targetGraphic.color = Color.white;
            var label = button.GetComponentInChildren<TMP_Text>(true);
            if (label) label.color = button.interactable ? Foreground : Muted;
            var icon = button.transform.Find("Icon")?.GetComponent<Image>();
            if (icon)
                icon.color = button.interactable ? AdeUiTheme.IconColor : new Color(Muted.r, Muted.g, Muted.b, .35f);
        }
        private void Highlight(Button button, bool selected)
        {
            Style(button); var colors = button.colors;
            if (selected) colors.normalColor = colors.selectedColor = AdeUiTheme.Current.Selection;
            button.colors = colors;
            var label = button.GetComponentInChildren<TMP_Text>(true);
            if (label) label.color = button.interactable ? Foreground : Muted;
        }
        private void ApplyTheme()
        {
            themeRevision = AdeUiTheme.Revision;
            foreach (var background in backgrounds) if (background)
            {
                background.sprite = AdeUiTheme.PanelSurface;
                background.type = Image.Type.Sliced; background.color = Color.white;
            }
            foreach (var section in sections)
            {
                section.sprite = AdeUiTheme.ReadOnlySurface;
                section.type = Image.Type.Sliced; section.color = Color.white;
            }
            closeIcon.color = AdeUiTheme.IconColor;
            StyleScrollbars();
            labels.RemoveAll(x => !x); controls.RemoveAll(x => !x);
            foreach (var label in labels) label.color = Foreground;
            foreach (var label in mutedLabels) if (label) label.color = Muted;
            foreach (var control in controls) Style(control);
            AdeUiTheme.Refresh();
            UpdateHighlights();
            for (int i = 0; i < groupRows.Count; i++) if (groupRows[i].gameObject.activeSelf) Highlight(groupRows[i], groupFirst + i == groups.IndexOf(lastGroup));
            foreach (var page in controlPages)
                HighlightControlRows(page);
        }
    }
}
