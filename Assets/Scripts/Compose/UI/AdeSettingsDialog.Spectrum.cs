using System;
using System.Collections.Generic;
using Arcade.Compose.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Arcade.Compose
{
    // Settings > 频谱: user-adjustable spectrum look per intensity mode, defaulting to the built-in values.
    public sealed partial class AdeSettingsDialog
    {
        private readonly List<(AdeNumberInputWithSlider Input, Func<SpectrumTuning, float> Get)> spectrumSliders
            = new List<(AdeNumberInputWithSlider, Func<SpectrumTuning, float>)>();
        private readonly List<GameObject> spectrumDbRows = new List<GameObject>();
        private readonly List<GameObject> spectrumLinearRows = new List<GameObject>();
        private readonly List<GameObject> spectrumLogRows = new List<GameObject>();
        private Button spectrumReset;
        // Slider (180) + gap (16) + number box, the control column of the slider rows (see ArrangeRow).
        private const float SpectrumControlWidth = 180 + 16 + AudioInputWidth;

        private static SpectrumTuning Tuning
        {
            get
            {
                var preference = ArcadeComposeManager.Instance.ArcadePreference;
                return preference.SpectrumTuning ??= new SpectrumTuning();
            }
        }

        // Rows are created in the original settings list (and borrowed like the other rows), in page order.
        private void BuildSpectrumTuning(Transform parent)
        {
            AddSpectrumSlider(parent, "SpectrumDbFloor", "dB 下限 (dB)", -100f, -20f, "0",
                t => t.DbFloor, (t, v) => t.DbFloor = v, spectrumDbRows);
            AddSpectrumSlider(parent, "SpectrumDbCeiling", "dB 上限 (dB)", -60f, 0f, "0",
                t => t.DbCeiling, (t, v) => t.DbCeiling = v, spectrumDbRows);
            AddSpectrumSlider(parent, "SpectrumDbBandShare", "频段相对占比 (%)", 0f, 100f, "0",
                t => t.DbBandShare, (t, v) => t.DbBandShare = v, spectrumDbRows);
            AddSpectrumSlider(parent, "SpectrumDbContrast", "对比度曲线", 0.5f, 4f, "0.00",
                t => t.DbContrast, (t, v) => t.DbContrast = v, spectrumDbRows);
            AddSpectrumSlider(parent, "SpectrumDbGate", "噪声门限 (dB)", -80f, -40f, "0",
                t => t.DbGate, (t, v) => t.DbGate = v, spectrumDbRows);
            AddSpectrumSlider(parent, "SpectrumLinearGain", "线性增益", 0.1f, 4f, "0.00",
                t => t.LinearGain, (t, v) => t.LinearGain = v, spectrumLinearRows);
            AddSpectrumSlider(parent, "SpectrumLinearContrast", "线性对比度", 0.3f, 3f, "0.00",
                t => t.LinearContrast, (t, v) => t.LinearContrast = v, spectrumLinearRows);
            AddSpectrumSlider(parent, "SpectrumLinearBoost", "线性亮度倍率", 0.5f, 4f, "0.00",
                t => t.LinearBoost, (t, v) => t.LinearBoost = v, spectrumLinearRows);
            AddSpectrumSlider(parent, "SpectrumLogMinFrequency", "对数轴最低频率 (Hz)", 20f, 4000f, "0",
                t => t.LogMinFrequency, (t, v) => t.LogMinFrequency = v, spectrumLogRows);
            AddSpectrumSlider(parent, "SpectrumLogMaxFrequency", "对数轴最高频率 (Hz)", 1000f, 22000f, "0",
                t => t.LogMaxFrequency, (t, v) => t.LogMaxFrequency = v, spectrumLogRows);
            UpdateSpectrumTuningRows();
        }

        private void AddSpectrumSlider(Transform parent, string name, string label, float min, float max, string format,
            Func<SpectrumTuning, float> get, Action<SpectrumTuning, float> set, List<GameObject> group)
        {
            var input = Instantiate(Dialog.AdeSoundDialogContent.Instance.ChartAudioInput, parent);
            input.name = name;
            input.transform.Find("Text").GetComponent<TMP_Text>().text = label;
            bool whole = format == "0";
            input.SliderValueScale = 1;
            input.NumberFormat = format;
            input.slider.Slider.minValue = min;
            input.slider.Slider.maxValue = max;
            input.slider.Slider.wholeNumbers = whole;
            input.SetValueWithoutNotify(get(Tuning));
            input.onValueEdited = value =>
            {
                if (float.IsNaN(value) || float.IsInfinity(value))
                {
                    input.SetValueWithoutNotify(get(Tuning));
                    return;
                }
                value = Mathf.Clamp(whole ? Mathf.Round(value) : value, min, max);
                set(Tuning, value);
                input.SetValueWithoutNotify(value);
                ArcadeComposeManager.Instance.SavePreferences();
            };
            spectrumSliders.Add((input, get));
            group?.Add(input.gameObject);
        }

        // Only the current intensity mode's parameters (and the log range while the log axis is on) are shown.
        private void UpdateSpectrumTuningRows()
        {
            var preference = ArcadeComposeManager.Instance.ArcadePreference;
            foreach (var row in spectrumDbRows) row.SetActive(!preference.SpectrumLinearAmplitude);
            foreach (var row in spectrumLinearRows) row.SetActive(preference.SpectrumLinearAmplitude);
            foreach (var row in spectrumLogRows) row.SetActive(preference.SpectrumLogScale);
        }

        // Built with the page; moved to the end of the Spectrum page on mount. A right-aligned button as wide as
        // the slider + number column above it, with centred text (not a full-width bar).
        private void BuildSpectrumReset()
        {
            var row = Rect("SpectrumResetRow", pages[(int)Page.Spectrum], typeof(HorizontalLayoutGroup));
            var layout = row.GetComponent<HorizontalLayoutGroup>();
            layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandWidth = layout.childForceExpandHeight = false;
            layout.childAlignment = TextAnchor.MiddleRight;
            layout.padding = new RectOffset(0, 0, 8, 0);
            spectrumReset = MakeButton(row, "恢复当前模式默认值", ResetSpectrumTuning);
            var size = spectrumReset.GetComponent<LayoutElement>();
            size.preferredWidth = SpectrumControlWidth;
            size.preferredHeight = ControlHeight;
            var caption = spectrumReset.GetComponentInChildren<TMP_Text>();
            caption.alignment = TextAlignmentOptions.Center;
            caption.rectTransform.offsetMin = caption.rectTransform.offsetMax = Vector2.zero;
            // Same bordered surface as the other panel buttons; theme sweeps keep it styled.
            AdeUiTheme.UseControlSurface(spectrumReset);
        }

        // Resets only what is shown: the current intensity mode's parameters, plus the log range while the log
        // axis is on. The other mode keeps its values.
        private void ResetSpectrumTuning()
        {
            var preference = ArcadeComposeManager.Instance.ArcadePreference;
            var t = Tuning;
            var d = new SpectrumTuning();
            if (preference.SpectrumLinearAmplitude)
            {
                t.LinearGain = d.LinearGain; t.LinearContrast = d.LinearContrast; t.LinearBoost = d.LinearBoost;
            }
            else
            {
                t.DbFloor = d.DbFloor; t.DbCeiling = d.DbCeiling; t.DbBandShare = d.DbBandShare;
                t.DbContrast = d.DbContrast; t.DbGate = d.DbGate;
            }
            if (preference.SpectrumLogScale)
            {
                t.LogMinFrequency = d.LogMinFrequency; t.LogMaxFrequency = d.LogMaxFrequency;
            }
            foreach (var (input, get) in spectrumSliders) input.SetValueWithoutNotify(get(t));
            ArcadeComposeManager.Instance.SavePreferences();
        }

        private void StyleSpectrum()
        {
            if (!spectrumReset) return;
            spectrumReset.GetComponentInChildren<TMP_Text>().color = AdeUiTheme.Current.Text;
        }
    }
}
