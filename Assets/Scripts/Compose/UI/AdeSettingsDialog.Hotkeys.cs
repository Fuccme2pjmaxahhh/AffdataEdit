using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Arcade.Compose
{
    public sealed partial class AdeSettingsDialog
    {
        private TMP_Dropdown hotkeyPresetDropdown;
        private Transform hotkeyPresetRow;

        private void BuildHotkeyPresetSettings()
        {
            var input = AdeInputManager.Instance;
            var content = input.Dialog;
            var buttons = new List<AdeHotkeyRebindingButton>(content.RebindingButtons);
            var template = buttons[0].GetComponentInParent<HorizontalLayoutGroup>(true).transform;
            var wrapper = template.parent;
            var dropdownTemplate = ArcadeComposeManager.Instance.ResolutionDropdown.GetComponentInParent<HorizontalLayoutGroup>(true).transform;
            var presetContent = AdeUiKit.Content(content.Dialog);
            var row = AdeUiKit.CloneRow(dropdownTemplate, presetContent.Find("Wraper") ?? presetContent, "HotkeyPreset");
            hotkeyPresetRow = row.transform;
            row.transform.SetAsFirstSibling();
            row.GetComponentInChildren<TMP_Text>(true).text = "热键预设";
            hotkeyPresetDropdown = row.GetComponentInChildren<TMP_Dropdown>(true);
            hotkeyPresetDropdown.onValueChanged = new TMP_Dropdown.DropdownEvent();
            hotkeyPresetDropdown.ClearOptions();
            hotkeyPresetDropdown.AddOptions(new List<string>(AdeHotkeyPresets.Names));
            hotkeyPresetDropdown.interactable = true;
            hotkeyPresetDropdown.onValueChanged.AddListener(index =>
            {
                input.SetHotkeyRebindingButton(null);
                input.Presets.Select(AdeHotkeyPresets.Names[index]);
                foreach (var binding in content.RebindingButtons) input.UpdateTextForHotkeyButton(binding);
            });
            foreach (var definition in AdeHotkeyPresets.ExtraActions)
            {
                var keyRow = AdeUiKit.CloneRow(template, wrapper, definition.Action);
                var button = keyRow.GetComponentInChildren<Button>(true);
                var binding = button.gameObject.AddComponent<AdeHotkeyRebindingButton>();
                binding.Text = button.GetComponentInChildren<TMP_Text>(true);
                binding.HotkeyName = definition.Action;
                foreach (var label in keyRow.GetComponentsInChildren<TMP_Text>(true))
                    if (label != binding.Text) { label.text = definition.Label; break; }
                AdeUiKit.SetOnClick(button, binding.StartRebinding);
                buttons.Add(binding);
            }
            content.RebindingButtons = buttons.ToArray();
            var gestures = AdeUiKit.CreateLabelRow(wrapper, template.GetComponentInChildren<TMP_Text>(true).transform,
                "鼠标组合操作（固定）\n中键拖动：按当前模式编辑时间／位置；混选时整体移动，单 ArcTap 连同短宿主移动且不重复计算\n位置模式 Alt＋中键：整体移动 Arc、Trace、Slide、单 ArcTap；按下时选择最近可见端点为原点，Slide 只移动 X\nCtrl＋左键：多选／切换选择；再次点已选 Arc 可选整链\nShift＋左键：时间范围选择\n时间拖动按下即吸附最近网格线；松开中键提交，Esc 取消；Q 播放／暂停时继续拖动\n自由相机：中键转向，中键＋滚轮调速，Shift＋WASD 加速");
            gestures.transform.parent.name = "MouseGestureHelp";
        }

        private void StyleHotkeyPreset()
        {
            if (hotkeyPresetDropdown)
                hotkeyPresetDropdown.SetValueWithoutNotify(Array.IndexOf(AdeHotkeyPresets.Names, AdeInputManager.Instance.Presets.Current));
        }
    }
}
