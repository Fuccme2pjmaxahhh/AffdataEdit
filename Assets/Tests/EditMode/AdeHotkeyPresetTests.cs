using System.Collections.Generic;
using System.Linq;
using Arcade.Compose;
using NUnit.Framework;
using UnityEngine.InputSystem;
using System.Reflection;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.LowLevel;
using TMPro;

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
        var reverse = AdeHotkeyPresets.Binding.Read(Map.FindAction("Reverse Arc"));
        Assert.That(reverse.Key, Is.EqualTo("<Keyboard>/n"));
        Assert.That(reverse.Ctrl, Is.True);
        Assert.That(reverse.Alt, Is.False);
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

    [Test] public void EscapeDuringRebindClearsBindingButClosingBindingDoesNot()
    {
        var root = new GameObject("Rebind cancellation check"); root.SetActive(false);
        var previousKeyboard = Keyboard.current;
        var keyboard = InputSystem.AddDevice<Keyboard>();
        try
        {
            // The inactive fixture bypasses Awake, including its macOS Cmd mapping.
            if (AdeInputManager.UsesCommand)
                for (int i = 0; i < Map.bindings.Count; i++)
                    if (Map.bindings[i].path == "<Keyboard>/ctrl")
                        Map.ChangeBinding(i).WithPath("<Keyboard>/*Meta");
            var input = root.AddComponent<AdeInputManager>(); input.Controls = controls;
            input.EventSystem = root.AddComponent<EventSystem>();
            var presets = CreatePresets(); presets.Load();
            typeof(AdeInputManager).GetProperty("Presets").SetValue(input, presets);
            var button = root.AddComponent<AdeHotkeyRebindingButton>(); button.HotkeyName = "Save";
            var label = new GameObject("Caption", typeof(RectTransform), typeof(TextMeshProUGUI)); label.transform.SetParent(root.transform);
            button.Text = label.GetComponent<TMP_Text>();
            string original = AdeHotkeyPresets.Binding.Read(Map.FindAction("Save")).Key;
            input.SetHotkeyRebindingButton(button); input.SetHotkeyRebindingButton(null);
            Assert.That(AdeHotkeyPresets.Binding.Read(Map.FindAction("Save")).Key, Is.EqualTo(original));
            input.SetHotkeyRebindingButton(button);
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.Escape)); InputSystem.Update();
            Assert.That(input.CurrentRebindingButton, Is.Null);
            Assert.That(AdeHotkeyPresets.Binding.Read(Map.FindAction("Save")).Key, Is.Empty);
            Assert.That(button.Text.text, Is.EqualTo("未绑定"));
            Assert.That(presets.Current, Is.EqualTo("Custom"));
            presets.Select("Legacy"); presets.Select("Custom");
            Assert.That(AdeHotkeyPresets.Binding.Read(Map.FindAction("Save")).Key, Is.Empty);
        }
        finally { InputSystem.RemoveDevice(keyboard); previousKeyboard?.MakeCurrent(); Object.DestroyImmediate(root); }
    }

    [Test] public void ChartNavigationCannotEnterHudMetadata()
    {
        var root = new GameObject("HUD navigation regression"); root.SetActive(false);
        try
        {
            var buttonObject = new GameObject("Chart button", typeof(RectTransform), typeof(UnityEngine.UI.Button));
            buttonObject.transform.SetParent(root.transform);
            var fieldObject = new GameObject("Composer", typeof(RectTransform), typeof(TMP_InputField));
            fieldObject.transform.SetParent(root.transform);
            var field = fieldObject.GetComponent<TMP_InputField>();
            fieldObject.transform.localPosition = Vector3.right * 200;
            root.SetActive(true);
            Assert.That(buttonObject.GetComponent<UnityEngine.UI.Button>().FindSelectableOnRight(), Is.EqualTo(field));
            typeof(AdeProjectManager).GetMethod("ConfigureHudInputNavigation", BindingFlags.Static | BindingFlags.NonPublic)
                .Invoke(null, new object[] { new[] { field } });
            Assert.That(field.navigation.mode, Is.EqualTo(UnityEngine.UI.Navigation.Mode.None));
            Assert.That(field.interactable, Is.True, "Mouse editing must remain available");
            Assert.That(buttonObject.GetComponent<UnityEngine.UI.Button>().FindSelectableOnRight(), Is.Not.EqualTo(field));
        }
        finally { Object.DestroyImmediate(root); }
    }

    [TestCase(PointerEventData.InputButton.Middle)]
    [TestCase(PointerEventData.InputButton.Right)]
    public void ReturningToChartReleasesTextSelection(PointerEventData.InputButton button)
    {
        var root = new GameObject("Chart pointer regression"); root.SetActive(false);
        var previousEvents = EventSystem.current;
        var eventObject = new GameObject("Pointer test event system", typeof(EventSystem));
        var events = eventObject.GetComponent<EventSystem>();
        // EditMode does not invoke EventSystem lifecycle methods automatically.
        typeof(EventSystem).GetMethod("OnEnable", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(events, null);
        EventSystem.current = events;
        try
        {
            var handler = root.AddComponent<AdeGameplayContentInputHandler>();
            var fieldObject = new GameObject("Composer", typeof(RectTransform), typeof(TMP_InputField));
            fieldObject.transform.SetParent(root.transform);
            root.SetActive(true);
            events.SetSelectedGameObject(fieldObject);
            handler.OnPointerDown(new PointerEventData(events) { button = button });
            Assert.That(events.currentSelectedGameObject, Is.Null);
        }
        finally
        {
            Object.DestroyImmediate(root);
            typeof(EventSystem).GetMethod("OnDisable", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(events, null);
            Object.DestroyImmediate(eventObject);
            if (previousEvents) EventSystem.current = previousEvents;
        }
    }

    [Test] public void MultilineGestureHelpIsNotAssignedFixedHeadingHeight()
    {
        var root = new GameObject("Hotkey help layout"); root.SetActive(false);
        try
        {
            var dialog = root.AddComponent<AdeSettingsDialog>();
            var template = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI)); template.transform.SetParent(root.transform);
            var text = AdeUiKit.CreateLabelRow(root.transform, template.transform, "鼠标组合操作\n第一条说明\n第二条说明\n第三条说明");
            text.transform.parent.name = "MouseGestureHelp";
            var type = typeof(AdeSettingsDialog);
            var rowType = type.GetNestedType("BorrowedRow", BindingFlags.NonPublic);
            var row = System.Activator.CreateInstance(rowType);
            rowType.GetField("Row").SetValue(row, text.transform.parent);
            rowType.GetField("Page").SetValue(row, AdeSettingsDialog.Page.Hotkeys);
            var rows = (System.Collections.IList)type.GetField("rows", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(dialog);
            rows.Add(row);
            type.GetMethod("BuildHotkeyGroups", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(dialog, null);
            var headings = (System.Collections.IList)type.GetField("hotkeyHeadings", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(dialog);
            Assert.That(headings.Count, Is.Zero);
            Assert.That(rowType.GetField("Host").GetValue(row), Is.Null);
            var size = text.transform.parent.GetComponent<UnityEngine.UI.LayoutElement>();
            Assert.That(size == null || size.preferredHeight < 0, Is.True);
        }
        finally { Object.DestroyImmediate(root); }
    }
}
