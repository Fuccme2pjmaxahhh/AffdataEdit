using UnityEngine;
using UnityEngine.EventSystems;
using Arcade.Compose.Command;
using UnityEngine.Events;
using UnityEngine.UI;

namespace Arcade.Compose
{
    public sealed partial class AdeChartToolsPanel
    {
        // The scene owns the static view; runtime only binds actions and populates chart data.
        private void BindView()
        {
            for (int i = 0; i < tabs.Length; i++)
            {
                var page = (Page)i;
                AdeUiKit.SetOnClick(tabs[i], () => Show(page));
                AdeUiKit.SetOnClick(entries[i], () => Toggle(page));
            }
            Bind(panel, "工具页签/Close", Collapse);
            Bind(emptyState, "打开谱面/打开谱面", () => AdeProjectManager.Instance.OpenProject());
            bottomGroup.onValueChanged.AddListener(SelectGroup);
            groupScroll.onValueChanged.AddListener(_ => ScrollGroups());
            Bind(groupCount.transform.parent, "+ 新建", AddGroup);
            Bind(deleteConfirmation, "确认操作/取消", () => deleteConfirmation.gameObject.SetActive(false));
            Bind(deleteConfirmation, "确认操作/删除组及内容", ConfirmDeleteGroup);
            for (int i = 0; i < groupTabs.Length; i++)
            {
                int tab = i;
                AdeUiKit.SetOnClick(groupTabs[i], () => ShowGroupTab(tab));
            }
            noInput.onValueChanged.AddListener(_ => ApplyGroup());
            fadingHolds.onValueChanged.AddListener(_ => ApplyGroup());
            noClip.onValueChanged.AddListener(_ => ApplyGroup());
            angleX.onEndEdit.AddListener(_ => ApplyGroup());
            angleY.onEndEdit.AddListener(_ => ApplyGroup());
            attributes.onEndEdit.AddListener(_ => ApplyAttributes());
            BindDisclosure(attributes.transform.parent, "属性文本");
            Bind(timingScroll.transform.parent, "Timing操作/+ 当前时间", AddTiming);
            foreach (var page in controlPages)
            {
                page.Group.gameObject.SetActive(false);
                page.GroupInput = Instantiate(page.Time, page.Group.transform.parent, false);
                page.GroupInput.name = "TimingGroupId";
                page.GroupInput.onValueChanged = new TMPro.TMP_InputField.OnChangeEvent();
                page.GroupInput.onEndEdit = new TMPro.TMP_InputField.SubmitEvent();
                page.GroupInput.contentType = TMPro.TMP_InputField.ContentType.Standard;
                page.GroupInput.characterLimit = 0;
                page.GroupInput.onEndEdit.AddListener(_ =>
                {
                    if (!TryReadGroupInput(page, out var selectedGroup)) return;
                    page.Group.value = Mathf.Max(0, groups.IndexOf(selectedGroup));
                });
                if (page.GroupInput.placeholder is TMPro.TMP_Text groupHint) groupHint.text = "组号，0 为默认组";
                AdeTextSelection.Configure(page.GroupInput);
                page.Scope.onValueChanged.AddListener(_ => RefreshControls(page));
                page.Scroll.onValueChanged.AddListener(_ => ScrollControls(page));
                Bind(page.Scroll.transform.parent, "事件操作/+ 当前时间", () => AddControl(page));
                AdeUiKit.SetOnClick(page.Duplicate, () => DuplicateControl(page));
                AdeUiKit.SetOnClick(page.Delete, () => DeleteControl(page));
                Bind(page.Time.transform.parent, "当前", () => page.Time.text = Gameplay.ArcGameplayManager.Instance.ChartTiming.ToString());
                if (!page.Camera) page.Type.onValueChanged.AddListener(_ => UpdateSceneType(page, true));
                var current = page.Time.transform.parent.Find("当前").GetComponent<Button>();
                current.GetComponentInChildren<TMPro.TMP_Text>().gameObject.SetActive(false);
                var currentIcon = Rect("Icon", current.transform, typeof(Image));
                currentIcon.anchorMin = currentIcon.anchorMax = new Vector2(.5f, .5f);
                currentIcon.sizeDelta = new Vector2(24, 24);
                var image = currentIcon.GetComponent<Image>();
                image.sprite = Resources.Load<Sprite>("AffdataEdit/Icons/Realtime");
                image.color = AdeUiTheme.IconColor;
                image.raycastTarget = false;
                if (page.Camera) BindCamera(page);
                else Bind(page.Form.transform, "应用参数/应用参数", () => ApplyControl(page, false));
                Bind(page.Statement.transform.parent, "语句操作/应用语句", () => ApplyControl(page, true));
                BindDisclosure(page.Statement.transform.parent, "AFF 语句");
            }
            BindCollapsibles();
        }

        private bool TryReadGroupInput(ControlPage page, out Gameplay.Chart.ArcTimingGroup timingGroup)
        {
            string text = AdeTextSelection.NormalizeForCommit(page.GroupInput);
            if (Editing.AdeTimingEditor.TryResolveGroupId(text, groups, out timingGroup)) return true;
            Message("请输入已存在的时间组编号，0 为默认组", true);
            var selected = page.Selected as Gameplay.Chart.IHasTimingGroup;
            page.GroupInput.SetTextWithoutNotify((selected?.TimingGroup?.Id ?? 0).ToString());
            return false;
        }

        private void BindCamera(ControlPage page)
        {
            page.Form.transform.Find("应用参数").gameObject.SetActive(false);
            page.Form.transform.GetChild(0).gameObject.SetActive(false);
            // Keep the two vector rows, with a compact draggable label beside each field.
            for (int i = 0; i < page.Vectors.Length; i++)
            {
                var field = page.Vectors[i];
                var row = field.transform.parent;
                if (i % 3 == 0)
                    row.parent.GetChild(row.GetSiblingIndex() - 1).gameObject.SetActive(false);
                int index = field.transform.GetSiblingIndex();
                var cell = Rect("Axis", row, typeof(HorizontalLayoutGroup));
                cell.SetSiblingIndex(index);
                Size(cell, -1, ControlHeight);
                var layout = cell.GetComponent<HorizontalLayoutGroup>();
                layout.childControlWidth = layout.childControlHeight = true;
                layout.childForceExpandWidth = false;
                layout.childForceExpandHeight = true;
                var label = Label(cell, "XYZ"[i % 3].ToString());
                Size(label.transform, 24, ControlHeight);
                field.transform.SetParent(cell, false);
                Size(field.transform, -1, ControlHeight);
                BindCameraNumber(page, field, label);
            }
            BindCameraNumber(page, page.Time, page.Time.transform.parent.GetComponentInChildren<TMPro.TMP_Text>());
            BindCameraNumber(page, page.Duration, page.Duration.transform.parent.GetComponentInChildren<TMPro.TMP_Text>());
            page.Type.onValueChanged.AddListener(_ => ApplyCamera(page));
            page.Group.onValueChanged.AddListener(_ => ApplyCamera(page));
        }

        private void BindCameraNumber(ControlPage page, TMPro.TMP_InputField field, TMPro.TMP_Text label)
        {
            field.onValueChanged.AddListener(text =>
            {
                bool valid = field == page.Time || field == page.Duration
                    ? ChartControlText.Integer(field.text, out _) : ChartControlText.Float(field.text, out _);
                if (valid) ApplyCamera(page);
            });
            field.onEndEdit.AddListener(_ => { ApplyCamera(page); FillControl(page, false); });
            label.raycastTarget = true;
            string caption = label.text;
            var trigger = label.gameObject.AddComponent<EventTrigger>();
            void On(EventTriggerType type, UnityAction<BaseEventData> action)
            {
                var entry = new EventTrigger.Entry { eventID = type };
                entry.callback.AddListener(action);
                trigger.triggers.Add(entry);
            }
            double start = 0;
            float startX = 0;
            On(EventTriggerType.PointerEnter, _ => label.text = "↔");
            On(EventTriggerType.PointerExit, _ => label.text = caption);
            On(EventTriggerType.BeginDrag, data =>
            {
                var pointer = (PointerEventData)data;
                if (pointer.button != PointerEventData.InputButton.Left) return;
                ApplyCamera(page);
                ChartControlText.Float(field.text, out float value);
                start = value;
                startX = pointer.position.x;
            });
            On(EventTriggerType.Drag, data =>
            {
                var pointer = (PointerEventData)data;
                if (pointer.button != PointerEventData.InputButton.Left || page.Selected == null) return;
                bool integer = field == page.Time || field == page.Duration;
                double value = start + (pointer.position.x - startX) * (integer ? 10d : .1d);
                double min = field == page.Duration ? 0 : integer ? int.MinValue : -1000000;
                double max = integer ? int.MaxValue : 1000000;
                value = System.Math.Max(min, System.Math.Min(max, value));
                field.SetTextWithoutNotify(integer ? ((int)value).ToString() : ChartControlText.Number((float)value));
                ApplyCamera(page);
            });
        }

        private void ApplyCamera(ControlPage page)
        {
            if (!(page.Selected is Gameplay.Chart.ArcCamera camera)) return;
            var next = (Gameplay.Chart.ArcCamera)camera.Clone();
            next.Timing = ChartControlText.Integer(page.Time.text, out int time) ? time : camera.Timing;
            next.Duration = ChartControlText.Integer(page.Duration.text, out int duration) ? duration : camera.Duration;
            next.Duration = (int)System.Math.Max(0, System.Math.Min(next.Duration, (long)int.MaxValue - next.Timing));
            for (int i = 0; i < page.Vectors.Length; i++)
            {
                var field = page.Vectors[i];
                float fallback = i < 3 ? camera.Move[i] : camera.Rotate[i - 3];
                float value = ChartControlText.Float(field.text, out float parsed) ? parsed : fallback;
                value = Mathf.Clamp(value, -1000000, 1000000);
                if (i < 3) next.Move[i] = value;
                else next.Rotate[i - 3] = value;
            }
            next.CameraType = (Gameplay.Chart.CameraEaseType)page.Type.value;
            if (!TryReadGroupInput(page, out var timingGroup)) return;
            next.TimingGroup = timingGroup;
            if (ChartControlText.Format(next) == ChartControlText.Format(camera) && next.TimingGroup == camera.TimingGroup) return;
            AdeCommandManager.Instance.Add(new ChartControlCommand(camera, camera, next));
            page.Statement.SetTextWithoutNotify(ChartControlText.Format(camera));
            ClearMessage();
        }

        private static void Bind(Transform root, string path, UnityAction action)
            => AdeUiKit.SetOnClick(root.Find(path).GetComponent<Button>(), action);

        private static void BindDisclosure(Transform content, string title)
        {
            var button = content.parent.GetChild(content.GetSiblingIndex() - 1).GetComponent<Button>();
            var label = button.GetComponentInChildren<TMPro.TMP_Text>();
            AdeUiKit.SetOnClick(button, () =>
            {
                bool show = !content.gameObject.activeSelf;
                content.gameObject.SetActive(show);
                AdeUiIcons.SetText(label, (show ? AdeUiIcons.Collapse : AdeUiIcons.Expand) + "  " + title);
            });
        }
    }
}
