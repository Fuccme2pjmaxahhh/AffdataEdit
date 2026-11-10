using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using UnityEngine.InputSystem;

namespace Arcade.Compose
{
    // Presets describe bindings only. Editor commands are handled independently.
    public sealed class AdeHotkeyPresets
    {
        public const string SettingsKey = "AffdataEdit.HotkeyPresets";
        public static readonly string[] Names = { "Legacy", "New", "Custom" };
        public static readonly (string Action, string Label)[] ExtraActions =
        {
            ("Reverse Arc", "Arc / Slide 反转"), ("Preview Selected Code", "选中物件代码预览"),
            ("Select Spatial Range", "范围段落选择"), ("Arc Toggle Void", "Arc 音轨／黑线切换"),
            ("Arc Toggle Color", "Arc 蓝／红切换"), ("Arc Toggle Curve Pair", "Arc 缓动成对切换"),
            ("Arc Cycle Curve", "Arc 缓动轮换"), ("Arc Curve S", "Arc 缓动 S"),
            ("Arc Curve SiSi", "Arc 缓动 SISI")
        };
        public const string Unsupported = "尚未实现：值复制面板与数值复制、事件渲染器切换、中心点原点、Arc 分支、Arc 节点编辑；尚未接入截图热键：频谱、打开工程、单 ArcTap、直接指定 Arc 颜色。";

        public readonly struct Binding
        {
            public readonly string Key;
            public readonly bool Ctrl, Alt, Shift;
            public Binding(string key, bool ctrl = false, bool alt = false, bool shift = false)
            { Key = key; Ctrl = ctrl; Alt = alt; Shift = shift; }
            public string Interaction => $"HotKey(needModifier1={Ctrl.ToString().ToLowerInvariant()},needModifier2={Alt.ToString().ToLowerInvariant()},needModifier3={Shift.ToString().ToLowerInvariant()})";
            public string Signature => string.IsNullOrEmpty(Key) ? "" : $"{Key.ToLowerInvariant()}:{Ctrl}:{Alt}:{Shift}";
            public static Binding Read(InputAction action, bool defaults = false)
            {
                var key = action.bindings.First(b => b.name == "key");
                var composite = action.bindings.First(b => b.isComposite);
                string interaction = defaults ? composite.interactions : composite.effectiveInteractions;
                return new Binding(defaults ? key.path : key.effectivePath,
                    interaction.Contains("needModifier1=true"), interaction.Contains("needModifier2=true"),
                    interaction.Contains("needModifier3=true"));
            }
            public void Apply(InputAction action)
            {
                for (int i = 0; i < action.bindings.Count; i++)
                {
                    var binding = action.bindings[i];
                    if (binding.name == "key") action.ApplyBindingOverride(i, Key ?? "");
                    else if (binding.isComposite)
                        action.ApplyBindingOverride(i, new InputBinding { overridePath = binding.path, overrideInteractions = Interaction });
                }
            }
        }

        private sealed class State
        {
            public string Preset = "Legacy";
            public string CustomBindings;
        }
        private readonly InputActionMap map;
        private readonly Dictionary<string, Binding> legacy, modern;
        private readonly Func<string, string> read;
        private readonly Action<string, string> write;
        private State state;
        public string Current => state.Preset;

        public AdeHotkeyPresets(InputActionMap map) : this(map,
            key => AdeSettings.GetString(key), (key, value) => AdeSettings.SetString(key, value)) { }

        public AdeHotkeyPresets(InputActionMap map, Func<string, string> read, Action<string, string> write)
        {
            this.map = map;
            this.read = read;
            this.write = write;
            legacy = map.actions.ToDictionary(a => a.name, a => Binding.Read(a, true));
            modern = BuildNew(legacy);
        }

        public static Dictionary<string, Binding> BuildNew(IReadOnlyDictionary<string, Binding> defaults)
        {
            var result = defaults.ToDictionary(p => p.Key, p => p.Value);
            var changed = new Dictionary<string, Binding>
            {
                ["Timing Snap"] = new Binding("<Keyboard>/t", ctrl: true),
                ["Toggle Click To Create"] = new Binding("<Keyboard>/a"),
                ["Toggle Grid"] = new Binding("<Keyboard>/g"),
                ["Click To Create Idle"] = new Binding("<Keyboard>/1"),
                ["Click To Create Tap"] = new Binding("<Keyboard>/2"),
                ["Click To Create Hold"] = new Binding("<Keyboard>/3"),
                ["Click To Create Arc"] = new Binding("<Keyboard>/4"),
                ["Click To Create Arctap"] = new Binding("<Keyboard>/5"),
                ["Reverse Arc"] = new Binding("<Keyboard>/n", ctrl: true),
                ["Preview Selected Code"] = new Binding("<Keyboard>/tab"),
                ["Select Spatial Range"] = new Binding("<Keyboard>/f", ctrl: true),
                ["Arc Toggle Void"] = new Binding("<Keyboard>/f1"),
                ["Arc Toggle Color"] = new Binding("<Keyboard>/f2"),
                ["Arc Toggle Curve Pair"] = new Binding("<Keyboard>/f3"),
                ["Arc Cycle Curve"] = new Binding("<Keyboard>/f4"),
                ["Arc Curve S"] = new Binding("<Keyboard>/w"),
                ["Arc Curve SiSi"] = new Binding("<Keyboard>/w", alt: true)
            };
            var disabled = new HashSet<string>
            {
                "Play Or Pause", "VerticalMirror", "Click To Create Arc Void",
                "Click To Create Arc Color", "Click To Create Arc Curve Type"
            };
            var reserved = new HashSet<string>(changed.Values.Select(b => b.Signature));
            foreach (string name in result.Keys.ToArray())
                if (!changed.ContainsKey(name) && (disabled.Contains(name) || reserved.Contains(result[name].Signature)))
                    result[name] = new Binding("");
            foreach (var entry in changed) result[entry.Key] = entry.Value;
            return result;
        }

        public void Load()
        {
            map.RemoveAllBindingOverrides();
            string existing = read("hotkeyRebinding");
            if (!string.IsNullOrEmpty(existing)) map.LoadBindingOverridesFromJson(existing);
            string saved = read(SettingsKey);
            state = string.IsNullOrEmpty(saved) ? new State() : JsonConvert.DeserializeObject<State>(saved) ?? new State();
            if (string.IsNullOrEmpty(saved) && !Matches(legacy))
            {
                state.Preset = "Custom";
                state.CustomBindings = map.SaveBindingOverridesAsJson();
            }
            Select(state.Preset);
        }

        public void Select(string name)
        {
            if (!Names.Contains(name)) name = "Legacy";
            map.RemoveAllBindingOverrides();
            if (name == "Custom" && !string.IsNullOrEmpty(state.CustomBindings))
                map.LoadBindingOverridesFromJson(state.CustomBindings);
            else if (name == "New")
                foreach (var binding in modern) binding.Value.Apply(map.FindAction(binding.Key, true));
            state.Preset = name;
            Save();
        }

        public void CaptureEdit()
        {
            state.Preset = "Custom";
            state.CustomBindings = map.SaveBindingOverridesAsJson();
            Save();
        }

        private bool Matches(IReadOnlyDictionary<string, Binding> preset) => map.actions.All(a =>
            Binding.Read(a).Signature == preset[a.name].Signature);

        private void Save()
        {
            write(SettingsKey, JsonConvert.SerializeObject(state));
            write("hotkeyRebinding", map.SaveBindingOverridesAsJson());
        }
    }
}
