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
            var unsupported = AdeUiKit.CloneRow(template, wrapper, "UnimplementedNewHotkeys");
            var unsupportedButton = unsupported.GetComponentInChildren<Button>(true);
            unsupportedButton.interactable = true;
            unsupportedButton.GetComponentInChildren<TMP_Text>(true).text = "查看清单";
            AdeUiKit.SetOnClick(unsupportedButton, () => Dialog.AdeBasicSingleDialogContent.Instance.Show(
                AdeHotkeyPresets.Unsupported, "截图中尚未支持的热键"));
            foreach (var label in unsupported.GetComponentsInChildren<TMP_Text>(true))
                if (!label.transform.IsChildOf(unsupportedButton.transform))
                { label.text = "截图中尚未支持的热键"; break; }
        }

        private void StyleHotkeyPreset()
        {
            if (hotkeyPresetDropdown)
                hotkeyPresetDropdown.SetValueWithoutNotify(Array.IndexOf(AdeHotkeyPresets.Names, AdeInputManager.Instance.Presets.Current));
        }
    }
}
