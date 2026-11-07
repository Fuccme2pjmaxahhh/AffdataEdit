using System.Collections.Generic;
using System.Linq;
using Arcade.Compose;
using NUnit.Framework;
using UnityEngine.InputSystem;

public class AdeHotkeyPresetTests
{
    private AdeInputControl controls;
    private Dictionary<string, string> settings;
    private InputActionMap Map => controls.ArcadeHotkey.Get();
    private AdeHotkeyPresets CreatePresets() => new AdeHotkeyPresets(Map,
        key => settings.TryGetValue(key, out string value) ? value : "", (key, value) => settings[key] = value);

    [SetUp] public void SetUp() { controls = new AdeInputControl(); settings = new Dictionary<string, string>(); }
    [TearDown] public void TearDown()
    {
        controls.Disable();
        UnityEngine.Object.DestroyImmediate(controls.asset);
    }

    [Test] public void FreshInstallUsesLegacyAndNewRemovesConflicts()
    {
        var presets = CreatePresets(); presets.Load();
        Assert.AreEqual("Legacy", presets.Current);
        Assert.AreEqual("<Keyboard>/f1", AdeHotkeyPresets.Binding.Read(Map.FindAction("Reselect Timing")).Key);
        presets.Select("New");
        Assert.AreEqual("<Keyboard>/f1", AdeHotkeyPresets.Binding.Read(Map.FindAction("Arc Toggle Void")).Key);
        Assert.IsEmpty(AdeHotkeyPresets.Binding.Read(Map.FindAction("Reselect Timing")).Key);
        Assert.IsEmpty(AdeHotkeyPresets.Binding.Read(Map.FindAction("Play Or Pause")).Key);
        Assert.AreEqual("<Keyboard>/space", AdeHotkeyPresets.Binding.Read(Map.FindAction("Preview When Holding")).Key);
        Assert.IsFalse(AdeHotkeyPresets.Binding.Read(Map.FindAction("Preview When Holding")).Ctrl);
        var active = Map.actions.Select(a => AdeHotkeyPresets.Binding.Read(a).Signature).Where(s => s.Length > 0).ToArray();
        Assert.AreEqual(active.Length, active.Distinct().Count(), "New must not leave conflicting old bindings active");
    }

    [Test] public void OldUserRebindingMigratesToCustomWithoutLosingIt()
    {
        Map.FindAction("Save").ApplyBindingOverride(1, "<Keyboard>/f10");
        settings["hotkeyRebinding"] = Map.SaveBindingOverridesAsJson();
        var presets = CreatePresets(); presets.Load();
        Assert.AreEqual("Custom", presets.Current);
        presets.Select("New"); presets.Select("Legacy"); presets.Select("Custom");
        Assert.AreEqual("<Keyboard>/f10", AdeHotkeyPresets.Binding.Read(Map.FindAction("Save")).Key);
    }

    [Test] public void EditingNewPreservesOneCustomAcrossPresetSwitchAndRestart()
    {
        var presets = CreatePresets(); presets.Load(); presets.Select("New");
        Map.FindAction("Arc Toggle Void").ApplyBindingOverride(1, "<Keyboard>/f9");
        presets.CaptureEdit();
        Assert.AreEqual("Custom", presets.Current);
        presets.Select("Legacy");
        presets = CreatePresets(); presets.Load();
        Assert.AreEqual("Legacy", presets.Current);
        presets.Select("Custom");
        Assert.AreEqual("<Keyboard>/f9", AdeHotkeyPresets.Binding.Read(Map.FindAction("Arc Toggle Void")).Key);
        Assert.IsEmpty(AdeHotkeyPresets.Binding.Read(Map.FindAction("Reselect Timing")).Key);
    }
}
