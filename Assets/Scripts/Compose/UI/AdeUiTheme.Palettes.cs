using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Text = TMPro.TMP_Text;

namespace Arcade.Compose
{
    public sealed partial class AdeUiTheme
    {
        public sealed class Preset
        {
            public readonly string Id, Name;
            public readonly bool IsDark;
            public readonly Color Chrome, Panel, Input, Border, Text, Muted, Accent, AccentText;
            public Color Selection => Color.Lerp(Panel, Accent, IsDark ? .22f : .15f);
            internal bool WhiteCloseButton => Id.StartsWith("claude-") || Id.StartsWith("codex-");
            public Preset(string id, string name, bool dark, uint chrome, uint panel, uint input, uint border,
                uint text, uint muted, uint accent, uint accentText)
            {
                Id = id; Name = name; IsDark = dark;
                Chrome = C(chrome); Panel = C(panel); Input = C(input); Border = C(border);
                Text = C(text); Muted = C(muted); Accent = C(accent); AccentText = C(accentText);
            }
        }

        // Reference palettes: github.com/primer/primitives, github.com/catppuccin/palette,
        // github.com/rose-pine/palette. Roles are adapted to editor controls and surfaces.
        public static readonly IReadOnlyList<Preset> Presets = new[]
        {
            // Desktop neutrals use the apps' bundled surface colors, adapted to this editor.
            new Preset("chatgpt-dark", "ChatGPT Desktop", true, 0x212121, 0x303030, 0x3A3A3A, 0x4F4F4F, 0xEDEDED, 0xCDCDCD, 0xEDEDED, 0x0D0D0D),
            new Preset("claude-dark", "Claude Desktop", true, 0x1F1E1D, 0x262624, 0x30302E, 0x494842, 0xFAF9F5, 0xC2C0B6, 0xD97757, 0x141413),
            new Preset("codex-dark", "Codex", true, 0x181818, 0x242424, 0x303030, 0x424242, 0xFFFFFF, 0xB4B4B4, 0x339CFF, 0x0D0D0D),
            new Preset("nord-dark", "Nord", true, 0x2E3440, 0x3B4252, 0x434C5E, 0x4C566A, 0xECEFF4, 0xD8DEE9, 0x88C0D0, 0x2E3440),
            new Preset("solarized-dark", "Solarized Dark", true, 0x002B36, 0x073642, 0x164653, 0x586E75, 0x93A1A1, 0x839496, 0x268BD2, 0x002B36),
            new Preset("dracula", "Dracula", true, 0x21222C, 0x282A36, 0x44475A, 0x6272A4, 0xF8F8F2, 0xBFC3D6, 0xBD93F9, 0x282A36),
            new Preset("chatgpt-light", "ChatGPT Desktop", false, 0xF9F9F9, 0xFCFCFC, 0xF3F3F3, 0xD9D9D9, 0x0D0D0D, 0x5D5D5D, 0x212121, 0xFFFFFF),
            new Preset("claude-light", "Claude Desktop", false, 0xF5F4ED, 0xFAF9F5, 0xFFFFFF, 0xD9D8D0, 0x141413, 0x73726C, 0xD97757, 0x141413),
            new Preset("codex-light", "Codex", false, 0xF5F5F5, 0xFFFFFF, 0xF0F0F0, 0xD9D9D9, 0x0D0D0D, 0x666666, 0x0285FF, 0x0D0D0D),
            new Preset("nord-light", "Nord Snow", false, 0xE5E9F0, 0xECEFF4, 0xD8DEE9, 0xAAB3C2, 0x2E3440, 0x4C566A, 0x5E81AC, 0xFFFFFF),
            new Preset("solarized-light", "Solarized Light", false, 0xEEE8D5, 0xFDF6E3, 0xEEE8D5, 0xC9C6B5, 0x586E75, 0x657B83, 0x268BD2, 0x002B36),
            new Preset("one-light", "One Light", false, 0xEAEAEB, 0xFAFAFA, 0xF0F0F0, 0xD0D0D1, 0x383A42, 0x696C77, 0x4078F2, 0xFFFFFF),
            new Preset("classic-dark", "Classic Dark", true, 0x181818, 0x1F1F1F, 0x313131, 0x454545, 0xCCCCCC, 0xA0A0A0, 0x007ACC, 0xFFFFFF),
            new Preset("github-dimmed", "GitHub Dimmed", true, 0x22272E, 0x2D333B, 0x373E47, 0x545D68, 0xCDD9E5, 0xADBAC7, 0x539BF5, 0x22272E),
            new Preset("catppuccin-mocha", "Catppuccin Mocha", true, 0x181825, 0x1E1E2E, 0x313244, 0x45475A, 0xCDD6F4, 0xA6ADC8, 0x89B4FA, 0x181825),
            new Preset("rose-pine-moon", "Rosé Pine Moon", true, 0x232136, 0x2A273F, 0x393552, 0x6E6A86, 0xE0DEF4, 0x908CAA, 0x9CCFD8, 0x232136),
            new Preset("classic-light", "Classic Light", false, 0xF3F3F3, 0xFFFFFF, 0xF5F5F5, 0xD0D0D0, 0x323232, 0x666666, 0x006DB7, 0xFFFFFF),
            new Preset("github-light", "GitHub Light", false, 0xF6F8FA, 0xFFFFFF, 0xF6F8FA, 0xD0D7DE, 0x1F2328, 0x59636E, 0x0969DA, 0xFFFFFF),
            new Preset("catppuccin-latte", "Catppuccin Latte", false, 0xE6E9EF, 0xEFF1F5, 0xCCD0DA, 0xBCC0CC, 0x4C4F69, 0x6C6F85, 0x1E66F5, 0xFFFFFF),
            new Preset("rose-pine-dawn", "Rosé Pine Dawn", false, 0xFAF4ED, 0xFFFAF3, 0xF2E9E1, 0xD8CEC5, 0x464261, 0x797593, 0x286983, 0xFFFFFF),
        };
        private const string DarkPresetKey = "AffdataEdit.EditorTheme.Dark", LightPresetKey = "AffdataEdit.EditorTheme.Light";
        private static Preset darkPreset = Presets[0], lightPreset = Presets[6];
        public static Preset Current => Dark ? darkPreset : lightPreset;
        internal static Color ContextMenuSelection => Dark
            ? Current.Selection : Color.Lerp(Current.Selection, Current.Text, .20f);
        public static int Revision { get; private set; }
        public static Color IconColor => Dark ? C(0xC5C5C5) : C(0x7F7F7F);

        private static void LoadPresets()
        {
            darkPreset = FindPreset(AdeSettings.GetString(DarkPresetKey), true);
            lightPreset = FindPreset(AdeSettings.GetString(LightPresetKey), false);
            Revision++;
        }
        private static Preset FindPreset(string id, bool dark)
        {
            foreach (var preset in Presets) if (preset.Id == id && preset.IsDark == dark) return preset;
            return Presets[dark ? 0 : 6];
        }
        public static void SetPreset(string id)
        {
            foreach (var preset in Presets)
            {
                if (preset.Id != id) continue;
                if (preset.IsDark) darkPreset = preset; else lightPreset = preset;
                Dark = preset.IsDark;
                AdeSettings.SetString(Dark ? DarkPresetKey : LightPresetKey, id);
                AdeSettings.SetInt(PreferenceKey, Dark ? 1 : 0);
                ApplySelectedPreset();
                return;
            }
        }
        private static void ApplySelectedPreset()
        {
            Revision++;
            AdeSettings.Save();
            if (!Instance) return;
            Instance.ApplySkinSprites();
            Instance.Sweep();
        }

        private static Color Resolve(Entry entry)
        {
            if (entry.Text) return Current.Text;
            if (entry.Sprite == null && Same(entry.Light, C(0x7F7F7F))) return IconColor;
            switch (entry.Sprite)
            {
                case "Top": case "Left": case "Right": case "Bottom": return Current.Chrome;
                case "ToolBackground": return entry.HiddenAtRest ? Current.Chrome : Current.Selection;
                case "UISprite": case "InputFieldBackground": case "Background": return Current.Input;
                case "Knob": case "Refresh": return Current.Text;
                case "UIMask": return Current.Panel;
                default: return Same(entry.Light, C(0xF5F5F5)) ? Current.Input : Current.Text;
            }
        }

        private struct PaintedGraphic
        {
            public Entry Entry;
            public string Sprite;
            public Color32 Color, Normal;
        }
        private readonly Dictionary<Graphic, PaintedGraphic> painted = new Dictionary<Graphic, PaintedGraphic>();
        private readonly List<Graphic> staleGraphics = new List<Graphic>();
    }
}
