using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Arcade.Compose
{
    public sealed partial class AdeUiTheme
    {
        private static readonly Dictionary<Image, bool> toolbarToggleStates = new Dictionary<Image, bool>();
        private readonly List<Image> staleToolbarIcons = new List<Image>();
        // Follows the preset accent; neutral accents get a lighter wash so the icon stays readable.
        internal static Color ToolbarActiveColor
        {
            get
            {
                Color accent = Current.Accent;
                Color.RGBToHSV(accent, out _, out float saturation, out _);
                accent.a = saturation > .2f ? .4f : .22f;
                return accent;
            }
        }

        public static void SetToolbarToggleState(Image icon, bool active)
        {
            if (!icon) return;
            toolbarToggleStates[icon] = active;
            if (Instance) Instance.StyleToolbarToggle(icon, active);
        }

        private void ApplyToolbarToggleStates()
        {
            staleToolbarIcons.Clear();
            foreach (var state in toolbarToggleStates)
            {
                if (!state.Key) staleToolbarIcons.Add(state.Key);
                else StyleToolbarToggle(state.Key, state.Value);
            }
            foreach (var icon in staleToolbarIcons) toolbarToggleStates.Remove(icon);
        }

        private static ColorBlock ToolbarActiveStates()
        {
            Color tint = ToolbarActiveColor;
            Color fill = Color.Lerp(Current.Chrome, new Color(tint.r, tint.g, tint.b, 1f), tint.a);
            fill.a = 1f;
            var colors = ColorBlock.defaultColorBlock;
            colors.normalColor = colors.selectedColor = fill;
            colors.highlightedColor = Color.Lerp(fill, Current.Text, .08f);
            colors.pressedColor = Color.Lerp(fill, Current.Text, .16f);
            colors.disabledColor = Color.Lerp(fill, Current.Chrome, .5f);
            return colors;
        }

        private void StyleToolbarToggle(Image icon, bool active)
        {
            var button = icon.GetComponentInParent<Button>();
            if (!button || !button.image) return;
            // Older scene bindings point at the button background rather than its icon.
            if (icon == button.image) icon = button.transform.Find("Image").GetComponent<Image>();
            if (!button.GetComponent<AdeToolbarPointerFocus>())
                button.gameObject.AddComponent<AdeToolbarPointerFocus>();

            // Keep the original square tool sprite. The blue fill lives in the opaque tint states
            // (precomposited over Chrome), so toggling only crossfades dark<->blue and never passes
            // through the white default tint on a white sprite.
            button.image.color = Color.white;
            button.transition = Selectable.Transition.ColorTint;
            if (active) button.colors = ToolbarActiveStates();
            else SetStates(button, ToolbarEntry);
            Color foreground = active ? Color.Lerp(IconColor, Dark ? Color.white : Color.black, .25f) : IconColor;
            icon.color = button.IsInteractable() ? foreground : Color.Lerp(IconColor, Current.Chrome, .55f);
        }
    }
}
