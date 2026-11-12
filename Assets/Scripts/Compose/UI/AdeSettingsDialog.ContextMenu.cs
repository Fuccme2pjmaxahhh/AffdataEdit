using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Arcade.Compose
{
    public sealed partial class AdeSettingsDialog
    {
        private RectTransform contextEditorRoot, contextItemRows;
        private ContextMenuState contextState;
        private bool contextVertical;
        private readonly List<Image> contextChecks = new List<Image>();
        private readonly List<Button> contextOrderButtons = new List<Button>();
        private Button contextReset;

        private void BuildContextMenuEditor()
        {
            contextEditorRoot = Rect("ContextMenuItems", pages[(int)Page.ContextMenu], typeof(VerticalLayoutGroup));
            var layout = contextEditorRoot.GetComponent<VerticalLayoutGroup>();
            layout.spacing = 12;
            layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            contextVertical = ArcadeComposeManager.Instance.ArcadePreference.VerticalContextMenu;
            ContextDropdown("配置样式", new List<string> { "Legacy", "NEW" }, contextVertical ? 1 : 0, value =>
            {
                contextVertical = value == 1;
                RebuildContextMenuRows();
            });
            ContextDropdown("菜单状态", new List<string> { "点立得", "无点立得", "无点立得 · 已选中", "点立得 · 已选中" }, 0, value =>
            {
                contextState = (ContextMenuState)value;
                RebuildContextMenuRows();
            });
            var hint = Rect("Hint", contextEditorRoot, typeof(TextMeshProUGUI));
            var text = hint.GetComponent<TMP_Text>();
            AdeEditorText.Configure(text, AdeEditorText.CaptionSize);
            text.text = "勾选显示项目，用上移／下移调整顺序。Legacy 列表按顺时针排列（超过四项从左侧开始，四项及以下从右侧开始）；NEW 从上到下排列。两种样式分别保存顺序。奇数菜单不留空槽。";
            text.textWrappingMode = TextWrappingModes.Normal;
            text.raycastTarget = false;
            contextItemRows = Rect("Items", contextEditorRoot, typeof(VerticalLayoutGroup));
            var rowsLayout = contextItemRows.GetComponent<VerticalLayoutGroup>();
            rowsLayout.spacing = 6;
            rowsLayout.childControlWidth = rowsLayout.childControlHeight = true;
            rowsLayout.childForceExpandWidth = true;
            rowsLayout.childForceExpandHeight = false;
            contextReset = MakeButton(contextEditorRoot, "恢复当前状态默认", () =>
            {
                AdeMarkingMenuManager.Instance.Customization.States.RemoveAll(s => s != null && s.State == contextState);
                ArcadeComposeManager.Instance.SavePreferences();
                RebuildContextMenuRows();
            });
            contextReset.GetComponentInChildren<TMP_Text>().alignment = TextAlignmentOptions.Center;
            RebuildContextMenuRows();
        }

        private void ContextDropdown(string title, List<string> options, int value, UnityEngine.Events.UnityAction<int> changed)
        {
            var template = ArcadeComposeManager.Instance.ResolutionDropdown.GetComponentInParent<HorizontalLayoutGroup>(true).transform;
            var row = AdeUiKit.CloneRow(template, contextEditorRoot, title);
            row.GetComponentInChildren<TMP_Text>(true).text = title;
            var dropdown = row.GetComponentInChildren<TMP_Dropdown>(true);
            dropdown.onValueChanged = new TMP_Dropdown.DropdownEvent();
            dropdown.ClearOptions();
            dropdown.AddOptions(options);
            dropdown.interactable = true;
            dropdown.SetValueWithoutNotify(value);
            dropdown.onValueChanged.AddListener(changed);
            ArrangeRow(row.transform, Page.ContextMenu);
        }

        private void RebuildContextMenuRows()
        {
            contextChecks.Clear();
            contextOrderButtons.Clear();
            foreach (Transform child in contextItemRows)
            {
                child.gameObject.SetActive(false);
                Destroy(child.gameObject);
            }
            var manager = AdeMarkingMenuManager.Instance;
            var options = manager.Customization.For(contextState);
            var choices = manager.GetMenuChoices(contextState, contextVertical);
            var template = ArcadeComposeManager.Instance.PlaybackSyncToggle.GetComponentInParent<HorizontalLayoutGroup>(true).transform;
            for (int i = 0; i < choices.Count; i++)
            {
                var choice = choices[i];
                int index = i;
                var row = AdeUiKit.CloneRow(template, contextItemRows, choice.Id);
                row.GetComponentInChildren<TMP_Text>(true).text = choice.Title;
                var toggle = row.GetComponentInChildren<Toggle>(true);
                toggle.transform.SetAsFirstSibling();
                toggle.onValueChanged = new Toggle.ToggleEvent();
                toggle.SetIsOnWithoutNotify(options.Hidden == null || !options.Hidden.Contains(choice.Id));
                toggle.onValueChanged.AddListener(enabled =>
                {
                    if (options.Hidden == null) options.Hidden = new List<string>();
                    options.Hidden.Remove(choice.Id);
                    if (!enabled) options.Hidden.Add(choice.Id);
                    ArcadeComposeManager.Instance.SavePreferences();
                });
                if (toggle.graphic is Image check) contextChecks.Add(check);
                ArrangeRow(row.transform, Page.ContextMenu);
                ContextMoveButton(row.transform, "上移", index > 0, () => MoveContextItem(index, -1));
                ContextMoveButton(row.transform, "下移", index < choices.Count - 1, () => MoveContextItem(index, 1));
            }
            AdeUiTheme.Refresh();
        }

        private void ContextMoveButton(Transform row, string title, bool enabled, UnityEngine.Events.UnityAction action)
        {
            var button = MakeButton(row, title, action);
            contextOrderButtons.Add(button);
            button.interactable = enabled;
            var size = button.GetComponent<LayoutElement>();
            size.minWidth = size.preferredWidth = 64;
            size.minHeight = size.preferredHeight = 40;
            var label = button.GetComponentInChildren<TMP_Text>();
            label.alignment = TextAlignmentOptions.Center;
            label.rectTransform.offsetMin = new Vector2(4, 0);
            label.rectTransform.offsetMax = new Vector2(-4, 0);
        }

        private void MoveContextItem(int index, int delta)
        {
            var manager = AdeMarkingMenuManager.Instance;
            var order = manager.GetMenuChoices(contextState, contextVertical).Select(c => c.Id).ToList();
            string id = order[index];
            order.RemoveAt(index);
            order.Insert(index + delta, id);
            var options = manager.Customization.For(contextState);
            if (contextVertical) options.VerticalOrder = order;
            else options.CircularOrder = order;
            ArcadeComposeManager.Instance.SavePreferences();
            RebuildContextMenuRows();
        }

        private void StyleContextMenu()
        {
            if (contextReset) StyleContextButton(contextReset);
            foreach (var button in contextOrderButtons) StyleContextButton(button);
            foreach (var check in contextChecks)
            {
                check.sprite = checkmark;
                check.color = AdeUiTheme.Dark ? Color.white : new Color32(50, 50, 50, 255);
            }
        }

        private static void StyleContextButton(Button button)
        {
            var colors = ColorBlock.defaultColorBlock;
            colors.normalColor = colors.selectedColor = AdeUiTheme.Current.Input;
            colors.highlightedColor = Color.Lerp(colors.normalColor, AdeUiTheme.Current.Text, .08f);
            colors.pressedColor = Color.Lerp(colors.normalColor, AdeUiTheme.Current.Text, .14f);
            colors.disabledColor = AdeUiTheme.Current.Panel;
            button.image.sprite = AdeUiTheme.RoundedSurface;
            button.image.type = Image.Type.Sliced;
            button.image.color = Color.white;
            button.colors = colors;
            button.GetComponentInChildren<TMP_Text>().color = button.interactable ? AdeUiTheme.Current.Text : AdeUiTheme.Current.Muted;
        }
    }
}
