using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Arcade.Aff;
using Arcade.Compose.Command;
using Arcade.Compose.Editing;
using Arcade.Compose.MarkingMenu;
using Arcade.Compose.Operation;
using Arcade.Gameplay.Chart;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using Text = TMPro.TMP_Text;
using InputField = TMPro.TMP_InputField;
using Dropdown = TMPro.TMP_Dropdown;

namespace Arcade.Compose.UI
{
    // Extra rows in the existing ValueEdit window. Time, time group and moving controls remain owned by AdeValueEditor.
    public sealed class AdeSlideEditor : MonoBehaviour, INoteSelectEvent
    {
        private readonly List<GameObject> rows = new List<GameObject>();
        private InputField startRange, endRange;
        private Text rangeHelp;
        private static bool LowPrecision => ArcadeComposeManager.Instance && ArcadeComposeManager.Instance.ArcadePreference.LowPrecisionArcAndSlide;
        private Button moveStart, moveEnd;
        private Dropdown leftCurve, rightCurve, height;
        private AdeSlidePreview preview;
        private MarkingMenuItem menu;
        private bool ready;
        private static readonly string[] CurveNames = { "S", "Si", "So", "B" };

        private void Start()
        {
            var editor = AdeValueEditor.Instance;
            if (!editor) return;
            startRange = RangeRow(editor.StartPos, "开始范围", true);
            endRange = RangeRow(editor.EndPos, "结束范围", false);
            leftCurve = CurveRow(editor.CurveType, "左侧曲线", true);
            rightCurve = CurveRow(editor.CurveType, "右侧曲线", false);
            height = HeightRow(editor.CurveType);
            var shape = new GameObject("Slide shape preview", typeof(RectTransform), typeof(LayoutElement), typeof(AdeSlidePreview));
            shape.transform.SetParent(editor.Timing.parent, false);
            shape.GetComponent<LayoutElement>().preferredHeight = 112;
            shape.GetComponent<LayoutElement>().preferredWidth = editor.StartPos.rect.width;
            preview = shape.GetComponent<AdeSlidePreview>();
            preview.OnEdited = value => Apply(note =>
            {
                note.StartCenter = value.StartCenter; note.StartWidth = value.StartWidth;
                note.EndCenter = value.EndCenter; note.EndWidth = value.EndWidth;
            });
            rows.Add(shape);
            var help = rangeHelp = Instantiate(editor.StartPos.GetComponentsInChildren<Text>(true)[0], editor.Timing.parent);
            help.name = "Slide range help";
            help.fontSize = 14; help.alignment = TMPro.TextAlignmentOptions.Left;
            help.gameObject.AddComponent<LayoutElement>().preferredHeight = 42;
            rows.Add(help.gameObject);
            // Place additional properties before the shared timing-group row.
            foreach (var row in rows) row.transform.SetSiblingIndex(editor.TimingGroup.GetSiblingIndex());
            var create = AdeClickToCreate.Instance;
            if (create.ClickToCreateItems.Length > 0)
            {
                menu = Instantiate(create.ClickToCreateItems[0], create.ClickToCreateItems[0].transform.parent);
                menu.StartupText = "Slide"; menu.HasSubMenu = false; menu.SubItems = new MarkingMenuItem[0];
                menu.OnConfirmed = new UnityEvent(); menu.OnConfirmed.AddListener(() =>
                {
                    create.Enable = true; create.SetClickToCreateMode(ClickToCreateMode.Slide);
                });
                menu.OnHangOver = new UnityEvent(); menu.gameObject.SetActive(false);
                create.ClickToCreateItems = create.ClickToCreateItems.Concat(new[] { menu }).ToArray();
            }
            AdeSelectionManager.Instance.NoteEventListeners.Add(this);
            AdeCommandManager.Instance.onCommandExecuted += OnCommand;
            ready = true; Refresh();
            AdeUiTheme.Refresh();
        }

        private InputField RangeRow(RectTransform template, string label, bool start)
        {
            var row = Instantiate(template, template.parent); row.name = label;
            row.GetComponentsInChildren<Text>(true)[0].text = label;
            var move = row.GetComponentInChildren<Button>(true);
            move.onClick = new Button.ButtonClickedEvent();
            move.onClick.AddListener(() =>
            {
                if (start) AdeValueEditor.Instance.OnReselectStartCoordinate();
                else AdeValueEditor.Instance.OnReselectEndCoordinate();
            });
            if (start) moveStart = move; else moveEnd = move;
            var input = row.GetComponentInChildren<InputField>(true);
            input.onEndEdit = new InputField.SubmitEvent(); input.onValueChanged = new InputField.OnChangeEvent();
            input.contentType = InputField.ContentType.Standard;
            AdeTextSelection.Configure(input);
            input.onEndEdit.AddListener(text =>
            {
                text = AdeTextSelection.NormalizeForCommit(input);
                var parts = text.Split(','); float center, width;
                if (parts.Length != 2 || !float.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out center)
                    || !float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out width))
                { Invalid(); return; }
                Apply(note =>
                {
                    if (start) { note.StartCenter = AffCoordinate.Round(center, LowPrecision); note.StartWidth = AffCoordinate.Round(width, LowPrecision); }
                    else { note.EndCenter = AffCoordinate.Round(center, LowPrecision); note.EndWidth = AffCoordinate.Round(width, LowPrecision); }
                });
            });
            rows.Add(row.gameObject); return input;
        }
        private Dropdown CurveRow(RectTransform template, string label, bool left)
        {
            var row = Instantiate(template, template.parent); row.name = label;
            row.GetComponentsInChildren<Text>(true)[0].text = label;
            var dropdown = row.GetComponentInChildren<Dropdown>(true);
            dropdown.onValueChanged = new Dropdown.DropdownEvent(); dropdown.ClearOptions();
            dropdown.AddOptions(CurveNames.ToList());
            dropdown.onValueChanged.AddListener(value => Apply(note =>
            {
                if (left) note.LeftCurve = value; else note.RightCurve = value;
            }));
            rows.Add(row.gameObject); return dropdown;
        }
        private Dropdown HeightRow(RectTransform template)
        {
            var row = Instantiate(template, template.parent); row.name = "Slide 高度";
            row.GetComponentsInChildren<Text>(true)[0].text = "高度";
            var dropdown = row.GetComponentInChildren<Dropdown>(true);
            dropdown.onValueChanged = new Dropdown.DropdownEvent(); dropdown.ClearOptions();
            dropdown.AddOptions(new List<string> { "地面", "普通天空", "高空（enwiden）" });
            dropdown.onValueChanged.AddListener(value => Apply(note => note.Height = (SlideHeight)value));
            rows.Add(row.gameObject); return dropdown;
        }
        private void Apply(Action<ArcSlide> edit)
        {
            var selected = AdeSelectionManager.Instance.SelectedNotes;
            if (selected.Count == 0 || selected.Any(n => !(n is ArcSlide))) return;
            var commands = new List<ICommand>();
            foreach (var note in selected)
            {
                var value = (ArcSlide)note.Clone(); edit(value);
                if (!value.IsValid) { Invalid(); return; }
                commands.Add(new EditArcEventCommand(note, value));
            }
            AdeCommandManager.Instance.Add(new BatchCommand(commands.ToArray(), "修改 Slide 范围"));
        }
        private void Invalid()
        {
            AdeToast.Instance.Show("Slide 坐标须为有限数值、当前精度下宽度大于 0，左右边界不可交叉"); Refresh();
        }
        internal static Vector2 EditingRange(ArcSlide note, bool end)
        {
            float center = end ? note.EndCenter : note.StartCenter;
            return (end ? note.EndWidth : note.StartWidth) == .02f ? new Vector2(center, center) : note.Range(end ? 1 : 0);
        }

        internal static void SetRangeEndpoints(ArcSlide note, bool end, float left, float right)
        {
            float width = AffCoordinate.Round(right - left, LowPrecision);
            float center = (left + right) / 2;
            if (width == 0) { center = left; width = .02f; }
            center = AffCoordinate.Round(center, LowPrecision);
            if (end) { note.EndCenter = center; note.EndWidth = width; }
            else { note.StartCenter = center; note.StartWidth = width; }
        }
        public void Refresh()
        {
            if (!ready) return;
            var selected = AdeSelectionManager.Instance.SelectedNotes;
            bool active = selected.Count > 0 && selected.All(n => n is ArcSlide);
            foreach (var row in rows) row.SetActive(active);
            if (!active) return;
            rangeHelp.text = $"范围：中心,宽度；最多{(LowPrecision ? "两" : "三")}位小数\n竖线随 X 网格设置；拖动四角自动吸附";
            moveStart.gameObject.SetActive(selected.Count == 1);
            moveEnd.gameObject.SetActive(selected.Count == 1);
            var note = (ArcSlide)selected[0];
            height.SetValueWithoutNotify((int)note.Height);
            height.RefreshShownValue();
            if (selected.Cast<ArcSlide>().Any(n => n.Height != note.Height)) height.captionText.text = "混合";
            startRange.SetTextWithoutNotify(selected.Cast<ArcSlide>().All(n => n.StartCenter == note.StartCenter && n.StartWidth == note.StartWidth)
                ? Range(note.StartCenter, note.StartWidth) : "-,-");
            endRange.SetTextWithoutNotify(selected.Cast<ArcSlide>().All(n => n.EndCenter == note.EndCenter && n.EndWidth == note.EndWidth)
                ? Range(note.EndCenter, note.EndWidth) : "-,-");
            leftCurve.SetValueWithoutNotify(note.LeftCurve); rightCurve.SetValueWithoutNotify(note.RightCurve);
            leftCurve.RefreshShownValue(); rightCurve.RefreshShownValue();
            if (selected.Cast<ArcSlide>().Any(n => n.LeftCurve != note.LeftCurve)) leftCurve.captionText.text = "混合";
            if (selected.Cast<ArcSlide>().Any(n => n.RightCurve != note.RightCurve)) rightCurve.captionText.text = "混合";
            preview.gameObject.SetActive(selected.Count == 1);
            preview.Note = (ArcSlide)note.Clone(); preview.SetVerticesDirty();
        }
        private static string Range(float center, float width) => AffCoordinate.Format(center, LowPrecision) + "," + AffCoordinate.Format(width, LowPrecision);
        private void OnCommand(ICommand command, bool undo) => Refresh();
        public void OnNoteSelect(ArcNote note) => Refresh();
        public void OnNoteDeselect(ArcNote note) => Refresh();
        public void OnNoteDeselectAll() => Refresh();
        private void OnDestroy()
        {
            if (AdeSelectionManager.Instance) AdeSelectionManager.Instance.NoteEventListeners.Remove(this);
            if (AdeCommandManager.Instance) AdeCommandManager.Instance.onCommandExecuted -= OnCommand;
            foreach (var row in rows) if (row) Destroy(row);
            if (menu) Destroy(menu.gameObject);
        }
    }
}
