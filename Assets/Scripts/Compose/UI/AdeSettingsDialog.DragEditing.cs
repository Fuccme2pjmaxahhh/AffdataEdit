using System.Collections.Generic;
using Arcade.Compose.Editing;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Arcade.Compose
{
    public sealed partial class AdeSettingsDialog
    {
        private TMP_Dropdown dragModeDropdown;

        private void BuildDragEditingSettings(Transform parent)
        {
            var template = ArcadeComposeManager.Instance.ResolutionDropdown.GetComponentInParent<HorizontalLayoutGroup>(true).transform;
            var row = AdeUiKit.CloneRow(template, parent, "DragEditMode");
            row.GetComponentInChildren<TMP_Text>(true).text = "中键拖动模式";
            dragModeDropdown = row.GetComponentInChildren<TMP_Dropdown>(true);
            dragModeDropdown.onValueChanged = new TMP_Dropdown.DropdownEvent();
            dragModeDropdown.ClearOptions();
            dragModeDropdown.AddOptions(new List<string> { "关闭", "时间", "位置" });
            dragModeDropdown.interactable = true;
            dragModeDropdown.SetValueWithoutNotify((int)AdeDragEdit.Instance.Mode);
            dragModeDropdown.onValueChanged.AddListener(value => AdeDragEdit.Instance.SetMode((AdeDragEdit.DragMode)value));

            var content = AdeInputManager.Instance.Dialog;
            var buttons = new List<AdeHotkeyRebindingButton>(content.RebindingButtons);
            var keyTemplate = buttons[0].GetComponentInParent<HorizontalLayoutGroup>(true).transform;
            // Insert next to an existing editable shortcut, before any fixed-key rows.
            int index = keyTemplate.GetSiblingIndex() + 1;
            string[] actions = { "Drag Edit Time", "Drag Edit Position", "Drag Edit Off" };
            string[] labels = { "中键拖动：时间", "中键拖动：位置", "关闭中键拖动" };
            for (int i = 0; i < actions.Length; i++)
            {
                var keyRow = AdeUiKit.CloneRow(keyTemplate, keyTemplate.parent, actions[i]);
                keyRow.transform.SetSiblingIndex(index + i);
                var button = keyRow.GetComponentInChildren<Button>(true);
                var binding = button.gameObject.AddComponent<AdeHotkeyRebindingButton>();
                binding.Text = button.GetComponentInChildren<TMP_Text>(true);
                binding.HotkeyName = actions[i];
                foreach (var label in keyRow.GetComponentsInChildren<TMP_Text>(true))
                    if (label != binding.Text) { label.text = labels[i]; break; }
                AdeUiKit.SetOnClick(button, binding.StartRebinding);
                buttons.Add(binding);
            }
            // Place the two-click shortcut with the existing selection shortcuts.
            Transform rangeParent = keyTemplate.parent;
            int rangeIndex = rangeParent.childCount;
            foreach (var label in content.GetComponentsInChildren<TMP_Text>(true))
            {
                if (label.text != "段落选择") continue;
                var rangeRow = label.GetComponentInParent<HorizontalLayoutGroup>(true);
                if (!rangeRow) continue;
                rangeParent = rangeRow.transform.parent;
                rangeIndex = rangeRow.transform.GetSiblingIndex() + 1;
                break;
            }
            var timeRangeRow = AdeUiKit.CloneRow(keyTemplate, rangeParent, "Select Time Range");
            timeRangeRow.transform.SetSiblingIndex(rangeIndex);
            var timeRangeButton = timeRangeRow.GetComponentInChildren<Button>(true);
            var timeRangeBinding = timeRangeButton.gameObject.AddComponent<AdeHotkeyRebindingButton>();
            timeRangeBinding.Text = timeRangeButton.GetComponentInChildren<TMP_Text>(true);
            timeRangeBinding.HotkeyName = "Select Time Range";
            foreach (var label in timeRangeRow.GetComponentsInChildren<TMP_Text>(true))
                if (label != timeRangeBinding.Text) { label.text = "段落选择（两次点击）"; break; }
            AdeUiKit.SetOnClick(timeRangeButton, timeRangeBinding.StartRebinding);
            buttons.Add(timeRangeBinding);
            content.RebindingButtons = buttons.ToArray();
        }
    }
}
