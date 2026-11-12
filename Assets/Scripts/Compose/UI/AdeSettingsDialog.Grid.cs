using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Arcade.Compose
{
    // Settings > 网格: a friendlier editor for the vertical grid lines. Each axis gets start / end / spacing
    // boxes, an "extra positions" box and a live summary; it writes the same syntax ("start>end/step;x;...")
    // into the original X / Y fields, which stay below as the advanced editor and are kept in sync.
    public sealed partial class AdeSettingsDialog
    {
        private sealed class GridAxisEditor
        {
            public bool IsX;
            public TMP_InputField Start, End, Step, Extra;
            public TMP_Text Summary;
            public TMP_InputField Raw => IsX ? AdeGridManager.Instance.VerticalXInputField : AdeGridManager.Instance.VerticalYInputField;
        }

        private readonly List<GridAxisEditor> gridAxes = new List<GridAxisEditor>();
        private readonly List<Image> gridGroups = new List<Image>();
        private readonly List<TMP_Text> gridHeadings = new List<TMP_Text>();
        private readonly List<TMP_Text> gridLabels = new List<TMP_Text>();
        private readonly List<TMP_Text> gridCaptions = new List<TMP_Text>();

        private void BuildGridEditor()
        {
            if (!AdeGridManager.Instance || !AdeGridManager.Instance.VerticalXInputField) return;
            var page = pages[(int)Page.Grid];
            BuildGridAxis(page, true, "X 轴竖线（轨道宽度 -0.5 ~ 1.5）");
            BuildGridAxis(page, false, "Y 轴横线（高度 0 ~ 1）");
            var advanced = Rect("GridAdvancedCaption", page, typeof(TextMeshProUGUI));
            var caption = advanced.GetComponent<TMP_Text>();
            AdeEditorText.Configure(caption, AdeEditorText.CaptionSize);
            caption.text = "高级：直接编辑原始写法 “起点>终点/间隔;额外位置;…”，与上方同步";
            caption.textWrappingMode = TextWrappingModes.Normal;
            gridCaptions.Add(caption);
            foreach (var axis in gridAxes) axis.Raw.onEndEdit.AddListener(_ => LoadGridAxis(axis));
            foreach (var axis in gridAxes) LoadGridAxis(axis);
        }

        private void BuildGridAxis(Transform page, bool isX, string title)
        {
            var group = Rect(isX ? "GridXGroup" : "GridYGroup", page, typeof(Image), typeof(VerticalLayoutGroup));
            var layout = group.GetComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(20, 20, 14, 16);
            layout.spacing = 10;
            layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            gridGroups.Add(group.GetComponent<Image>());
            var heading = GridText(group, "Heading", title, AdeEditorText.CaptionSize);
            heading.fontStyle = FontStyles.Bold;
            gridHeadings.Add(heading);

            var axis = new GridAxisEditor { IsX = isX };
            var range = GridRow(group, "Range");
            axis.Start = GridField(range, "起点", 96);
            axis.End = GridField(range, "终点", 96);
            axis.Step = GridField(range, "间隔", 96);
            var extraRow = GridRow(group, "Extra");
            axis.Extra = GridField(extraRow, "额外位置", -1);
            if (axis.Extra.placeholder is TMP_Text hint) hint.text = "例如 0.33, 0.67";
            axis.Summary = GridText(group, "Summary", "", AdeEditorText.CaptionSize);
            gridCaptions.Add(axis.Summary);
            foreach (var field in new[] { axis.Start, axis.End, axis.Step, axis.Extra })
                field.onEndEdit.AddListener(_ => ApplyGridAxis(axis));
            gridAxes.Add(axis);
        }

        private RectTransform GridRow(Transform parent, string name)
        {
            var row = Rect(name, parent, typeof(HorizontalLayoutGroup));
            var layout = row.GetComponent<HorizontalLayoutGroup>();
            layout.spacing = 12;
            layout.childAlignment = TextAnchor.MiddleLeft;
            layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandWidth = layout.childForceExpandHeight = false;
            return row;
        }

        private TMP_Text GridText(Transform parent, string name, string text, float size)
        {
            var rect = Rect(name, parent, typeof(TextMeshProUGUI));
            var label = rect.GetComponent<TMP_Text>();
            AdeEditorText.Configure(label, size);
            label.text = text;
            label.richText = false;
            label.textWrappingMode = TextWrappingModes.Normal;
            label.raycastTarget = false;
            return label;
        }

        // A label followed by a copy of the grid's own input box (same look); width -1 = fill the row.
        private TMP_InputField GridField(Transform row, string label, float width)
        {
            var text = GridText(row, label, label, AdeEditorText.BodySize);
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.gameObject.AddComponent<LayoutElement>().minWidth = 0;
            gridLabels.Add(text);
            var field = Instantiate(AdeGridManager.Instance.VerticalXInputField, row, false);
            field.name = label;
            // Drop the scene's listeners (they would write the X field) and start empty.
            field.onEndEdit = new TMP_InputField.SubmitEvent();
            field.onSubmit = new TMP_InputField.SubmitEvent();
            field.onValueChanged = new TMP_InputField.OnChangeEvent();
            field.onSelect = new TMP_InputField.SelectionEvent();
            field.onDeselect = new TMP_InputField.SelectionEvent();
            field.contentType = TMP_InputField.ContentType.Standard;
            field.SetTextWithoutNotify("");
            field.textComponent.alignment = width > 0 ? TextAlignmentOptions.Center : TextAlignmentOptions.Left;
            AdeTextSelection.ConfigureSettings(field, true);
            var size = field.GetComponent<LayoutElement>() ?? field.gameObject.AddComponent<LayoutElement>();
            size.minHeight = size.preferredHeight = ControlHeight;
            if (width > 0) { size.minWidth = size.preferredWidth = width; size.flexibleWidth = 0; }
            else { size.minWidth = 120; size.preferredWidth = -1; size.flexibleWidth = 1; }
            return field;
        }

        // Raw syntax -> boxes: the first "a>b/c" segment fills start/end/spacing, everything else is listed as extra.
        private void LoadGridAxis(GridAxisEditor axis)
        {
            string start = "", end = "", step = "";
            var extras = new List<string>();
            foreach (string part in (axis.Raw.text ?? "").Split(';').Select(p => p.Trim()).Where(p => p.Length > 0))
            {
                int arrow = part.IndexOf('>'), slash = part.IndexOf('/');
                if (start.Length == 0 && arrow > 0 && slash > arrow)
                {
                    start = part.Substring(0, arrow).Trim();
                    end = part.Substring(arrow + 1, slash - arrow - 1).Trim();
                    step = part.Substring(slash + 1).Trim();
                }
                else extras.Add(part);
            }
            axis.Start.SetTextWithoutNotify(start);
            axis.End.SetTextWithoutNotify(end);
            axis.Step.SetTextWithoutNotify(step);
            axis.Extra.SetTextWithoutNotify(string.Join(", ", extras));
            UpdateGridSummary(axis);
        }

        // Boxes -> raw syntax. Spacing also accepts a fraction such as 1/8; a range needs all three boxes.
        private void ApplyGridAxis(GridAxisEditor axis)
        {
            var parts = new List<string>();
            bool hasRange = TryNumber(axis.Start.text, out float start) & TryNumber(axis.End.text, out float end)
                & TryStep(axis.Step.text, out float step);
            if (hasRange)
            {
                if (end < start) (start, end) = (end, start);
                parts.Add(Format(start) + ">" + Format(end) + "/" + Format(step));
            }
            foreach (string token in (axis.Extra.text ?? "").Split(new[] { ',', ';', '，', '；', ' ' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string value = token.Trim();
                if (value.Contains('>') || TryNumber(value, out _)) parts.Add(value);
            }
            axis.Raw.SetTextWithoutNotify(string.Join(";", parts));
            if (axis.IsX) AdeGridManager.Instance.OnVerticalXChange();
            else AdeGridManager.Instance.OnVerticalYChange();
            LoadGridAxis(axis);
        }

        private void UpdateGridSummary(GridAxisEditor axis)
        {
            var positions = axis.IsX ? AdeGridManager.Instance.VerticalXPositions : AdeGridManager.Instance.VerticalYPositions;
            if (positions == null || positions.Count == 0)
            {
                axis.Summary.text = "没有网格线";
                return;
            }
            var shown = positions.Take(10).Select(p => Format(p));
            axis.Summary.text = "共 " + positions.Count + " 条：" + string.Join("  ", shown) + (positions.Count > 10 ? "  …" : "");
        }

        private static bool TryNumber(string text, out float value)
            => float.TryParse((text ?? "").Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out value)
               && !float.IsNaN(value) && !float.IsInfinity(value);

        private static bool TryStep(string text, out float value)
        {
            text = (text ?? "").Trim();
            int slash = text.IndexOf('/');
            if (slash > 0 && TryNumber(text.Substring(0, slash), out float a) && TryNumber(text.Substring(slash + 1), out float b) && b != 0)
                value = a / b;
            else if (!TryNumber(text, out value)) return false;
            return value > 0.0001f;
        }

        private static string Format(float value) => Math.Round(value, 6).ToString("0.######", CultureInfo.InvariantCulture);

        private void StyleGrid()
        {
            foreach (var group in gridGroups)
            {
                group.sprite = AdeUiTheme.RoundedSurface;
                group.type = Image.Type.Sliced;
                group.color = AdeUiTheme.Current.Chrome;
                group.raycastTarget = false;
            }
            foreach (var heading in gridHeadings) heading.color = AdeUiTheme.Current.Muted;
            foreach (var label in gridLabels) label.color = AdeUiTheme.Current.Text;
            foreach (var caption in gridCaptions) caption.color = AdeUiTheme.Current.Muted;
        }
    }
}
