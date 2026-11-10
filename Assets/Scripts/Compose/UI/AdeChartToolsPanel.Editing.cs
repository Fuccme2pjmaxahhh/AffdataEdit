using TMPro;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Arcade.Aff;
using Arcade.Compose.Command;
using Arcade.Compose.Editing;
using Arcade.Gameplay;
using Arcade.Gameplay.Chart;
using UnityEngine;
using UnityEngine.UI;

namespace Arcade.Compose
{
    public sealed partial class AdeChartToolsPanel
    {
        private sealed class TimingRow
        {
            public RectTransform Root;
            public ArcTiming Timing;
            public TMP_InputField Time, Bpm, Beats;
            public Button Remove;
        }

        private bool Loaded()
        {
            if (ArcGameplayManager.Instance.Chart != null) return true;
            Message("请先打开谱面。", true);
            return false;
        }

        private void AddGroup()
        {
            if (!Loaded()) return;
            AdeTimingEditor.Instance.AddTimingGroup();
            dirty = true;
            Message("已添加时间组，使用 base BPM 初始化一条 Timing。可撤销。");
        }

        private int GroupEventCount(ArcTimingGroup group)
        {
            var chart = ArcGameplayManager.Instance.Chart;
            if (chart == null) return 0;
            return chart.Taps.Count(x => x.TimingGroup == group) + chart.Holds.Count(x => x.TimingGroup == group)
                + chart.Arcs.Count(x => x.TimingGroup == group) + chart.Slides.Count(x => x.TimingGroup == group)
                + chart.Cameras.Count(x => x.TimingGroup == group) + chart.SceneControl.Count(x => x.TimingGroup == group);
        }

        private void DuplicateGroup(int index)
        {
            if (!Loaded() || index < 0 || index >= groups.Count) return;
            var source = groups[index];
            var copy = new ArcTimingGroup
            {
                Id = ArcTimingManager.Instance.timingGroups.Count + 1,
                Timings = ArcTimingManager.Instance.GetTiming(source).Select(t => (ArcTiming)t.Clone()).ToList()
            };
            copy.ApplyAttributes(source?.Attributes ?? "");
            AdeCommandManager.Instance.Add(new AddTimingGroup(copy));
            AdeTimingEditor.Instance.SetCurrentTimingGroup(copy);
            dirty = true;
            Message("已复制时间组的 Timing，未复制组内音符及其他事件。可撤销。");
        }

        private void DeleteGroup(int index)
        {
            if (!Loaded() || index <= 0 || index >= groups.Count) return;
            SelectGroup(index);
            Refresh();
            pendingDelete = lastGroup;
            deletePrompt.text = $"删除 {GroupName(pendingDelete)} 及组内 {GroupEventCount(pendingDelete)} 个音符 / 事件？\nTiming 也会删除，操作可撤销。";
            deleteConfirmation.gameObject.SetActive(true);
        }

        private void ConfirmDeleteGroup()
        {
            if (pendingDelete == null || !groups.Contains(pendingDelete)) return;
            AdeCommandManager.Instance.Add(new RemoveTimingGroup(pendingDelete));
            deleteConfirmation.gameObject.SetActive(false);
            pendingDelete = null;
            Message("时间组及组内内容已删除。可撤销。");
        }

        private void RefreshGroups()
        {
            if (!groupTopSpacer)
            {
                groupTopSpacer = Rect("Top spacer", groupList);
                groupTopSpacer.SetAsFirstSibling();
                groupBottomSpacer = Rect("Bottom spacer", groupList);
            }
            while (groupRows.Count < Mathf.Min(groups.Count, GroupRowCount))
            {
                int slot = groupRows.Count;
                var button = Quiet(Button(groupList, "", () => SelectGroup(groupFirst + slot)));
                button.GetComponentInChildren<TMP_Text>().alignment = TextAlignmentOptions.Left;
                button.GetComponentInChildren<TMP_Text>().fontSize = 24;
                var layout = button.gameObject.AddComponent<HorizontalLayoutGroup>();
                layout.padding = new RectOffset(12, 12, 0, 0); layout.spacing = 8;
                layout.childControlWidth = layout.childControlHeight = true;
                layout.childForceExpandWidth = layout.childForceExpandHeight = false;
                layout.childAlignment = TextAnchor.MiddleLeft;
                Size(button.GetComponentInChildren<TMP_Text>().transform, -1, ControlHeight);
                var detail = MutedLabel(button.transform, "", ControlHeight, 18);
                detail.alignment = TextAlignmentOptions.Right;
                detail.textWrappingMode = TextWrappingModes.NoWrap;
                Size(detail.transform, 104, ControlHeight);
                groupCopies.Add(IconButton(button.transform, "复制时间组", "Copy", () => DuplicateGroup(groupFirst + slot)));
                groupDeletes.Add(IconButton(button.transform, "删除时间组", "Trash", () => DeleteGroup(groupFirst + slot)));
                groupDetails.Add(detail);
                groupRows.Add(button);
                groupBottomSpacer.SetAsLastSibling();
            }
            groupFirst = -1;
            ScrollGroups();
            bool loaded = lastChart != null;
            bool editable = loaded && lastGroup != null;
            groupCount.text = "Timing  ·  " + groups.Count;
            Size(groupScroll.transform, -1, Mathf.Min(groups.Count, 4) * (ControlHeight + 4) - 4);
            groupProperties.gameObject.SetActive(editable);
            defaultGroupHint.gameObject.SetActive(!editable);
            noInput.SetIsOnWithoutNotify(lastGroup?.NoInput ?? false);
            fadingHolds.SetIsOnWithoutNotify(lastGroup?.FadingHolds ?? false);
            noClip.SetIsOnWithoutNotify(lastGroup?.NoClip ?? false);
            angleX.SetTextWithoutNotify(((lastGroup?.AngleX ?? 0) / 10f).ToString("0.#", CultureInfo.InvariantCulture));
            angleY.SetTextWithoutNotify(((lastGroup?.AngleY ?? 0) / 10f).ToString("0.#", CultureInfo.InvariantCulture));
            attributes.SetTextWithoutNotify(lastGroup?.Attributes ?? "");
            if (pendingDelete != lastGroup) { pendingDelete = null; deleteConfirmation.gameObject.SetActive(false); }
            RefreshTimings();
        }

        // Binds the rows to the groups at the current scroll position; event counts are only taken for them.
        private void ScrollGroups()
        {
            const float pitch = ControlHeight + 4;
            int first = Mathf.Clamp(Mathf.FloorToInt(groupList.anchoredPosition.y / pitch), 0, Mathf.Max(0, groups.Count - groupRows.Count));
            if (first == groupFirst) return;
            groupFirst = first;
            int after = groups.Count - first - groupRows.Count;
            SetSpacer(groupTopSpacer, first * pitch - 4);
            SetSpacer(groupBottomSpacer, after * pitch - 4);
            bool loaded = lastChart != null;
            for (int i = 0; i < groupRows.Count; i++)
            {
                int index = first + i;
                groupRows[i].gameObject.SetActive(index < groups.Count);
                if (index >= groups.Count) continue;
                var group = groups[index];
                groupRows[i].GetComponentInChildren<TMP_Text>().text = GroupName(group);
                groupDetails[i].text = group == null ? "基础" : GroupEventCount(group) + " 个事件";
                groupRows[i].interactable = loaded;
                groupCopies[i].interactable = loaded;
                groupDeletes[i].interactable = loaded && group != null;
                Style(groupCopies[i]); Style(groupDeletes[i]);
                Highlight(groupRows[i], group == lastGroup);
            }
        }

        // The layout's 4px spacing already separates a spacer from its neighbouring row.
        private static void SetSpacer(RectTransform spacer, float height)
        {
            spacer.gameObject.SetActive(height > 0);
            if (height > 0) Size(spacer, -1, height);
        }

        private void ApplyGroup()
        {
            if (!Loaded() || lastGroup == null) return;
            if (!ChartControlText.Float(angleX.text, out float x) || !ChartControlText.Float(angleY.text, out float y))
            {
                angleX.SetTextWithoutNotify((lastGroup.AngleX / 10f).ToString("0.#", CultureInfo.InvariantCulture));
                angleY.SetTextWithoutNotify((lastGroup.AngleY / 10f).ToString("0.#", CultureInfo.InvariantCulture));
                return;
            }
            int ax = Mathf.RoundToInt((x % 360 + 360) % 360 * 10) % 3600;
            int ay = Mathf.RoundToInt((y % 360 + 360) % 360 * 10) % 3600;
            var tokens = lastGroup.Attributes.Split('_').Where(t => t.Length > 0 && t != "noinput" && t != "fadingholds"
                && t != "noclip" && !t.StartsWith("anglex", StringComparison.Ordinal) && !t.StartsWith("angley", StringComparison.Ordinal)).ToList();
            if (noInput.isOn) tokens.Add("noinput");
            if (fadingHolds.isOn) tokens.Add("fadingholds");
            if (noClip.isOn) tokens.Add("noclip");
            if (ax != 0) tokens.Add("anglex" + ax);
            if (ay != 0) tokens.Add("angley" + ay);
            SetAttributes(string.Join("_", tokens));
        }

        private void ApplyAttributes()
        {
            if (!Loaded() || lastGroup == null) return;
            SetAttributes(attributes.text.Trim());
        }

        private void SetAttributes(string text)
        {
            if (!ChartControlText.ValidateAttributes(text, out string error))
            { attributes.SetTextWithoutNotify(lastGroup.Attributes); Message(error, true); return; }
            if (text == lastGroup.Attributes) return;
            AdeCommandManager.Instance.Add(new EditTimingGroupAttributes(lastGroup, text));
            dirty = true;
            ClearMessage();
        }

        private void AddTiming()
        {
            if (!Loaded()) return;
            var timing = new ArcTiming { Timing = ArcGameplayManager.Instance.ChartTiming,
                Bpm = ArcTimingManager.Instance.CalculateBpmByTiming(ArcGameplayManager.Instance.ChartTiming, lastGroup), BeatsPerLine = 4 };
            AdeCommandManager.Instance.Add(new AddTimingEvent(lastGroup, timing));
            Message("Timing 已添加。可撤销。");
        }

        private void RefreshTimings()
        {
            var timings = ArcTimingManager.Instance.GetTiming(lastGroup);
            int count = lastChart == null ? 0 : timings.Count;
            while (timingRows.Count < count)
            {
                var row = new TimingRow();
                row.Root = Instantiate(timingRowTemplate, timingList, false);
                row.Root.name = "Timing";
                row.Root.gameObject.SetActive(true);
                row.Time = row.Root.Find("Time").GetComponent<TMP_InputField>();
                row.Bpm = row.Root.Find("BPM").GetComponent<TMP_InputField>();
                row.Beats = row.Root.Find("Beats").GetComponent<TMP_InputField>();
                foreach (var field in new[] { row.Time, row.Bpm, row.Beats })
                { controls.Add(field); labels.Add(field.textComponent); }
                row.Time.onEndEdit.AddListener(_ => ApplyTiming(row, row.Time));
                row.Bpm.onEndEdit.AddListener(_ => ApplyTiming(row, row.Bpm));
                row.Beats.onEndEdit.AddListener(_ => ApplyTiming(row, row.Beats));
                row.Remove = Quiet(row.Root.Find("Remove").GetComponent<Button>());
                controls.Add(row.Remove);
                row.Remove.onClick.AddListener(() =>
                {
                    if (ArcTimingManager.Instance.GetTiming(lastGroup).Count > 1)
                        AdeCommandManager.Instance.Add(new RemoveTimingEvent(lastGroup, row.Timing));
                });
                timingRows.Add(row);
            }
            for (int i = 0; i < timingRows.Count; i++)
            {
                var row = timingRows[i]; row.Root.gameObject.SetActive(i < count);
                if (i >= count) continue;
                row.Timing = timings[i];
                SetTimingFields(row);
                row.Remove.interactable = count > 1;
                Style(row.Remove);
            }
            timingLayoutDirty = true;
        }

        private void ResizeTimingList()
        {
            if (!expanded || activePage != Page.Groups || activeGroupTab != 0 || lastChart == null) return;
            float height = ((RectTransform)pages[0].parent).rect.height;
            if (!timingLayoutDirty && Mathf.Approximately(height, timingViewportHeight)) return;
            timingLayoutDirty = false;
            timingViewportHeight = height;
            LayoutRebuilder.ForceRebuildLayoutImmediate(pages[0]);
            float otherHeight = LayoutUtility.GetPreferredHeight(pages[0])
                - LayoutUtility.GetPreferredHeight((RectTransform)timingScroll.transform);
            int count = ArcTimingManager.Instance.GetTiming(lastGroup).Count;
            float contentHeight = Mathf.Max(1, count) * (ControlHeight + 8) - 8;
            Size(timingScroll.transform, -1, Mathf.Min(contentHeight, Mathf.Max(ControlHeight, height - otherHeight)));
            LayoutRebuilder.ForceRebuildLayoutImmediate(pages[0]);
        }

        private static void SetTimingFields(TimingRow row)
        {
            row.Time.SetTextWithoutNotify(row.Timing.Timing.ToString(CultureInfo.InvariantCulture));
            row.Bpm.SetTextWithoutNotify(row.Timing.Bpm.ToString("0.###", CultureInfo.InvariantCulture));
            row.Beats.SetTextWithoutNotify(ChartControlText.Number(row.Timing.BeatsPerLine));
        }

        private void ApplyTiming(TimingRow row, TMP_InputField field)
        {
            if (lastChart == null || lastChart != ArcGameplayManager.Instance.Chart || row.Timing == null
                || !ArcTimingManager.Instance.GetTiming(lastGroup).Contains(row.Timing)) return;
            var next = (ArcTiming)row.Timing.Clone();
            if (field == row.Time && ChartControlText.Integer(field.text, out int time)) next.Timing = time;
            else if (field == row.Bpm && ChartControlText.Float(field.text, out float bpm) && Mathf.Abs(bpm) <= 1000000)
                next.Bpm = (float)Math.Round(bpm, 3, MidpointRounding.AwayFromZero);
            else if (field == row.Beats && ChartControlText.Float(field.text, out float beats) && beats >= 0)
                next.BeatsPerLine = beats;
            else { SetTimingFields(row); return; }
            if (next.Timing == row.Timing.Timing && next.Bpm == row.Timing.Bpm && next.BeatsPerLine == row.Timing.BeatsPerLine)
            { SetTimingFields(row); return; }
            AdeCommandManager.Instance.Add(new EditTimingEvent(lastGroup, row.Timing, next));
            SetTimingFields(row);
            ClearMessage();
        }

        private void RefreshControls(ControlPage page)
        {
            int revision = page.Camera ? ArcCameraManager.Instance.Revision : ArcSceneControlManager.Instance.Revision;
            if (page.EventsRevision != revision || page.EventsChart != lastChart
                || page.EventsGroup != lastGroup || page.EventsScope != page.Scope.value)
            {
                page.Events.Clear();
                if (lastChart != null)
                {
                    IEnumerable<ArcEvent> events = page.Camera ? ArcCameraManager.Instance.Cameras.Cast<ArcEvent>()
                        : ArcSceneControlManager.Instance.OrderedSceneControls;
                    foreach (var e in events)
                        if (page.Scope.value == 1 || ((IHasTimingGroup)e).TimingGroup == lastGroup) page.Events.Add(e);
                }
                page.EventsRevision = revision;
                page.EventsChart = lastChart;
                page.EventsGroup = lastGroup;
                page.EventsScope = page.Scope.value;
                if (!page.Events.Contains(page.Selected)) page.Selected = page.Events.FirstOrDefault();
            }
            if (!page.TopSpacer)
            {
                page.TopSpacer = Rect("Top spacer", page.List);
                page.TopSpacer.SetAsFirstSibling();
                page.BottomSpacer = Rect("Bottom spacer", page.List);
            }
            while (page.Rows.Count < Mathf.Min(page.Events.Count, 6))
            {
                int index = page.Rows.Count;
                var button = Quiet(Button(page.List, "", () =>
                {
                    page.Selected = page.Events[page.First + index];
                    var selectedGroup = ((IHasTimingGroup)page.Selected).TimingGroup;
                    AdeTimingEditor.Instance.SetCurrentTimingGroup(selectedGroup);
                    RevealCurrentGroup();
                    FillControl(page); HighlightControlRows(page); ClearMessage();
                }));
                button.GetComponentInChildren<TMP_Text>().alignment = TextAlignmentOptions.Left;
                button.GetComponentInChildren<TMP_Text>().fontSize = 20;
                Size(button.GetComponentInChildren<TMP_Text>().transform, 100, ControlHeight);
                var layout = button.gameObject.AddComponent<HorizontalLayoutGroup>();
                layout.padding = new RectOffset(8, 8, 0, 0); layout.spacing = 8;
                layout.childControlWidth = layout.childControlHeight = true;
                layout.childForceExpandWidth = layout.childForceExpandHeight = false;
                layout.childAlignment = TextAnchor.MiddleLeft;
                var description = Label(button.transform, "", ControlHeight, 20);
                description.textWrappingMode = TextWrappingModes.NoWrap;
                description.overflowMode = TextOverflowModes.Ellipsis;
                page.Descriptions.Add(description);
                var group = MutedLabel(button.transform, "", ControlHeight, 18);
                group.alignment = TextAlignmentOptions.Right;
                group.textWrappingMode = TextWrappingModes.NoWrap;
                Size(group.transform, 72, ControlHeight);
                page.Groups.Add(group);
                page.Rows.Add(button);
                page.BottomSpacer.SetAsLastSibling();
            }
            page.Scroll.gameObject.SetActive(page.Events.Count > 0);
            Size(page.Scroll.transform, -1, Mathf.Clamp(page.Events.Count, 1, 4) * (ControlHeight + 4) - 4);
            page.Summary.text = page.Events.Count == 0 ? "当前范围暂无事件，点击「+ 当前时间」添加。" : $"{page.Events.Count} 个事件";
            page.First = -1;
            ScrollControls(page);
            FillControl(page);
        }

        private void ScrollControls(ControlPage page)
        {
            if (!page.TopSpacer) return;
            const float pitch = ControlHeight + 4;
            int first = Mathf.Clamp(Mathf.FloorToInt(page.List.anchoredPosition.y / pitch),
                0, Mathf.Max(0, page.Events.Count - page.Rows.Count));
            if (first == page.First) return;
            page.First = first;
            SetSpacer(page.TopSpacer, first * pitch - 4);
            SetSpacer(page.BottomSpacer, (page.Events.Count - first - page.Rows.Count) * pitch - 4);
            for (int i = 0; i < page.Rows.Count; i++)
            {
                int index = first + i;
                page.Rows[i].gameObject.SetActive(index < page.Events.Count);
                if (index >= page.Events.Count) continue;
                var e = page.Events[index];
                string name = e is ArcCamera c ? $"{c.CameraType.ToString().ToLowerInvariant()} · {c.Duration} ms" : ((RawAffSceneControl)((ArcSceneControl)e).IntoRawItem()).Type;
                page.Rows[i].GetComponentInChildren<TMP_Text>().text = $"{e.Timing} ms";
                page.Descriptions[i].text = name;
                page.Groups[i].text = GroupName(((IHasTimingGroup)e).TimingGroup);
            }
            HighlightControlRows(page);
        }

        private void HighlightControlRows(ControlPage page)
        {
            for (int i = 0; i < page.Rows.Count; i++)
                if (page.Rows[i].gameObject.activeSelf)
                    Highlight(page.Rows[i], page.Selected == page.Events[page.First + i]);
        }

        private void FillControl(ControlPage page, bool preserveInput = true)
        {
            bool selected = page.Selected != null;
            page.Form.gameObject.SetActive(selected);
            page.Delete.interactable = page.Duplicate.interactable = selected;
            if (!selected) { page.Statement.SetTextWithoutNotify(""); return; }
            if (!preserveInput || !page.Time.isFocused) page.Time.SetTextWithoutNotify(page.Selected.Timing.ToString());
            page.Group.SetValueWithoutNotify(Mathf.Max(0, groups.IndexOf(((IHasTimingGroup)page.Selected).TimingGroup)));
            page.Group.RefreshShownValue();
            if (!preserveInput || !page.GroupInput.isFocused)
                page.GroupInput.SetTextWithoutNotify((((IHasTimingGroup)page.Selected).TimingGroup?.Id ?? 0).ToString());
            if (page.Selected is ArcCamera camera)
            {
                for (int i = 0; i < 3; i++)
                {
                    if (!preserveInput || !page.Vectors[i].isFocused) page.Vectors[i].SetTextWithoutNotify(ChartControlText.Number(camera.Move[i]));
                    if (!preserveInput || !page.Vectors[3 + i].isFocused) page.Vectors[3 + i].SetTextWithoutNotify(ChartControlText.Number(camera.Rotate[i]));
                }
                page.Type.SetValueWithoutNotify((int)camera.CameraType); page.Type.RefreshShownValue();
                if (!preserveInput || !page.Duration.isFocused) page.Duration.SetTextWithoutNotify(camera.Duration.ToString());
            }
            else
            {
                var raw = (RawAffSceneControl)((ArcSceneControl)page.Selected).IntoRawItem();
                int index = Array.IndexOf(ChartControlText.SceneTypes, raw.Type);
                page.Type.SetValueWithoutNotify(index < 0 ? ChartControlText.SceneTypes.Length : index); page.Type.RefreshShownValue();
                page.CustomType.SetTextWithoutNotify(raw.Type);
                page.Parameters.SetTextWithoutNotify(string.Join(",", raw.Params.Select(x => x is RawAffInt i ? i.data.ToString()
                    : x is RawAffFloat f ? ChartControlText.Number(f.data) : ((RawAffWord)x).data)));
                UpdateSceneType(page, false);
            }
            page.Statement.SetTextWithoutNotify(ChartControlText.Format(page.Selected));
        }

        private void UpdateSceneType(ControlPage page, bool resetParameters)
        {
            bool custom = page.Type.value == ChartControlText.SceneTypes.Length;
            page.CustomType.transform.parent.gameObject.SetActive(custom);
            string type = custom ? page.CustomType.text : ChartControlText.SceneTypes[page.Type.value];
            bool noArgs = type == "trackhide" || type == "trackshow";
            page.Parameters.interactable = !noArgs;
            page.Parameters.transform.parent.gameObject.SetActive(!noArgs);
            page.Hint.text = custom ? "扩展 SC 按原文保存；预览效果取决于当前支持。"
                : noArgs ? "此类型不需要参数。" : type == "hidegroup" ? "参数：0.00,开关（0 显示 / 1 隐藏）"
                : type == "enwidencamera" || type == "enwidenlanes" ? "参数：时长（ms）,开关（0 / 1）"
                : "参数：时长（秒）,整数值";
            if (resetParameters && !custom) page.Parameters.text = noArgs ? "" : type == "hidegroup" ? "0.00,1"
                : type == "enwidencamera" || type == "enwidenlanes" ? "1000.00,1" : type == "redline" ? "1.00,0"
                : type == "trackdisplay" ? "0.50,128" : "0.50,255";
        }

        private void AddControl(ControlPage page)
        {
            if (!Loaded()) return;
            int time = ArcGameplayManager.Instance.ChartTiming;
            ArcEvent value = page.Camera ? (ArcEvent)new ArcCamera { Timing = time, TimingGroup = lastGroup, Duration = 1000 }
                : new ArcSceneControl(new RawAffSceneControl { Timing = time, Type = "trackhide", Params = new List<IRawAffValue>() }, lastGroup);
            AdeCommandManager.Instance.Add(new ChartControlCommand(value, null, value));
            page.Selected = value; dirty = true;
            Message(page.Camera ? "事件已添加。参数修改会自动应用。" : "事件已添加。修改参数后点击应用。");
        }

        private void DuplicateControl(ControlPage page)
        {
            if (!Loaded() || page.Selected == null) return;
            var value = page.Selected.Clone(); value.Timing = ArcGameplayManager.Instance.ChartTiming;
            AdeCommandManager.Instance.Add(new ChartControlCommand(value, null, value));
            page.Selected = value; dirty = true;
            Message("已复制到当前时间。可撤销。");
        }

        private void DeleteControl(ControlPage page)
        {
            if (!Loaded() || page.Selected == null) return;
            AdeCommandManager.Instance.Add(new ChartControlCommand(page.Selected, page.Selected, null));
            Message("事件已删除。可撤销。");
        }

        private void ApplyControl(ControlPage page, bool statement)
        {
            if (!Loaded() || page.Selected == null) return;
            string text = page.Statement.text;
            if (!statement)
            {
                if (page.Camera)
                    text = $"camera({page.Time.text},{string.Join(",", page.Vectors.Select(x => x.text))},{ChartControlText.CameraTypes[page.Type.value]},{page.Duration.text});";
                else
                {
                    string type = page.Type.value < ChartControlText.SceneTypes.Length ? ChartControlText.SceneTypes[page.Type.value] : page.CustomType.text.Trim();
                    string args = page.Parameters.interactable ? page.Parameters.text.Trim() : "";
                    text = $"scenecontrol({page.Time.text},{type}{(args.Length == 0 ? "" : "," + args)});";
                }
            }
            if (!TryReadGroupInput(page, out var timingGroup)) return;
            if (!ChartControlText.TryParse(text, page.Camera, timingGroup, out var next, out string error))
            { Message(error, true); return; }
            AdeCommandManager.Instance.Add(new ChartControlCommand(page.Selected, page.Selected, next));
            Message("修改已应用。可撤销。");
        }
    }
}
