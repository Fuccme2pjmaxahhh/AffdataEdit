using System;
using System.Collections;
using System.Reflection;
using Arcade.Compose;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

public class AdeSettingsRowLifecycleTests
{
    private const BindingFlags Members = BindingFlags.Instance | BindingFlags.NonPublic;
    private GameObject root;
    private ArcadeComposeManager compose, previousCompose;
    private AdeSettingsDialog settings;
    private RectTransform source, destination, offset;
    private TMP_InputField delay;

    private static FieldInfo ComposeInstance => typeof(ArcadeComposeManager)
        .GetField("<Instance>k__BackingField", BindingFlags.Static | BindingFlags.NonPublic);

    [SetUp]
    public void SetUp()
    {
        root = new GameObject("Settings row lifecycle");
        root.SetActive(false);
        compose = root.AddComponent<ArcadeComposeManager>();
        previousCompose = (ArcadeComposeManager)ComposeInstance.GetValue(null);
        ComposeInstance.SetValue(null, compose);
        settings = root.AddComponent<AdeSettingsDialog>();
        source = Rect("Source", root.transform);
        destination = Rect("Audio page", root.transform);
        offset = Rect("GlobalAudioOffset", source, typeof(HorizontalLayoutGroup));
        Label("Text", offset, "Global offset");
        var inputObject = Rect("Input", offset, typeof(Image), typeof(TMP_InputField));
        compose.GlobalAudioOffsetInput = inputObject.GetComponent<TMP_InputField>();
        compose.GlobalAudioOffsetInput.textViewport = Rect("Viewport", inputObject);
        compose.GlobalAudioOffsetInput.textComponent = Label("Value", compose.GlobalAudioOffsetInput.textViewport, "0");
        compose.GlobalAudioOffsetInput.SetTextWithoutNotify("23");
        compose.ArcadePreference.DisplayDelay = 41;
        var toggleRow = Rect("PlaybackSync", source, typeof(HorizontalLayoutGroup));
        Label("Text", toggleRow, "Playback sync");
        compose.PlaybackSyncToggle = Rect("Toggle", toggleRow, typeof(Toggle)).GetComponent<Toggle>();
        Call("BuildDisplayDelaySetting");
        var fields = offset.GetComponentsInChildren<TMP_InputField>(true);
        Assert.That(fields.Length, Is.EqualTo(2));
        delay = fields[0] == compose.GlobalAudioOffsetInput ? fields[1] : fields[0];
        foreach (Transform row in source) Call("Register", row, AdeSettingsDialog.Page.Audio);
        var pages = (RectTransform[])typeof(AdeSettingsDialog).GetField("pages", Members).GetValue(settings);
        pages[(int)AdeSettingsDialog.Page.Audio] = destination;
    }

    [TearDown]
    public void TearDown()
    {
        Object.DestroyImmediate(root);
        ComposeInstance.SetValue(null, previousCompose);
    }

    [Test]
    public void InvalidIntegerSettingsRestoreTheEffectiveValues()
    {
        compose.UndoBufferSizeInput = delay;
        compose.ArcadePreference.UndoBufferSize = 200;
        compose.ArcadePreference.GlobalAudioOffset = -23;
        foreach (string invalid in new[] { "", "-", "1234567890123" })
        {
            compose.SetUndoBufferSize(invalid);
            compose.SetGlobalAudioOffset(invalid);
            Assert.AreEqual("200", delay.text);
            Assert.AreEqual("-23", compose.GlobalAudioOffsetInput.text);
        }

        var obs = root.AddComponent<Arcade.Compose.Feature.AdeObsManager>();
        obs.ObsServerPortInput = delay;
        var preference = new Arcade.Compose.Feature.OBSPreferences();
        obs.GetType().GetField("preference", Members).SetValue(obs, preference);
        obs.OnOBSServerPortChange("65535");
        foreach (string invalid in new[] { "0", "-1", "65536", "" })
        {
            obs.OnOBSServerPortChange(invalid);
            Assert.AreEqual(65535, preference.port);
            Assert.AreEqual("65535", delay.text);
        }

        var gameplayInstance = typeof(Arcade.Gameplay.ArcGameplayManager).GetProperty("Instance");
        var previousGame = gameplayInstance.GetValue(null);
        try
        {
            var game = root.AddComponent<Arcade.Gameplay.ArcGameplayManager>();
            gameplayInstance.SetValue(null, game);
            game.ChartAudioOffset = -34;
            var project = root.AddComponent<AdeProjectManager>();
            project.AudioOffset = delay;
            delay.SetTextWithoutNotify("-");
            project.OnAudioOffsetEdited();
            Assert.AreEqual(-34, game.ChartAudioOffset);
            Assert.AreEqual("-34", delay.text);
        }
        finally { gameplayInstance.SetValue(null, previousGame); }
    }

    [Test]
    public void GridRangeExpansionIncludesDecimalEndpointAndRejectsWholeInvalidAxis()
    {
        var parse = typeof(AdeGridManager).GetMethod("Parse", BindingFlags.Static | BindingFlags.NonPublic);
        IList Parse(string value) => (IList)parse.Invoke(null, new object[] { value });
        foreach (var value in new[] { "0>1/0", "0>1/-1", "0>1/1e-18", "0;bad;1", "NaN", "Infinity",
            "1>0/1", "1>2/1e-20", "0>4096/1", "0>4095/1;2" })
            Assert.IsNull(Parse(value), value);
        Assert.AreEqual(4096, Parse("0>4095/1").Count);
        Assert.IsEmpty(Parse(""));
        var positions = Parse("0>1/0.1;2;");
        Assert.AreEqual(12, positions.Count);
        Assert.AreEqual(1f, positions[10]);
        Assert.AreEqual(2f, positions[11]);
    }

    [Test]
    public void PartialGridDraftAndInvalidStoredAxisPreserveOtherCoordinates()
    {
        const BindingFlags globals = BindingFlags.Static | BindingFlags.NonPublic;
        var file = typeof(AdeSettings).GetField("file", globals);
        var canSave = typeof(AdeSettings).GetField("canSave", globals);
        var instance = typeof(AdeGridManager).GetProperty("Instance");
        var oldFile = file.GetValue(null);
        var oldCanSave = canSave.GetValue(null);
        var oldGrid = instance.GetValue(null);
        try
        {
            var isolated = new AdeSettingsFile(System.IO.Path.Combine(System.IO.Path.GetTempPath(), Guid.NewGuid() + ".json"));
            isolated.Load();
            file.SetValue(null, isolated);
            canSave.SetValue(null, false);
            AdeSettings.SetString("AdeOBSDialog", "{\"ip\":\"example.invalid\",\"port\":70000}");
            var obs = root.AddComponent<Arcade.Compose.Feature.AdeObsManager>();
            obs.GetType().GetMethod("LoadPreferences", Members).Invoke(obs, null);
            var obsSettings = (Arcade.Compose.Feature.OBSPreferences)obs.GetType().GetField("preference", Members).GetValue(obs);
            Assert.AreEqual(4455, obsSettings.port);
            Assert.AreEqual("example.invalid", obsSettings.ip);
            AdeSettings.SetString("AdeGridManager", "{\"X\":\"0>1/0\",\"Y\":\"0.7\"}");
            var grid = root.AddComponent<AdeGridManager>();
            instance.SetValue(null, grid);
            grid.VerticalXInputField = delay;
            grid.VerticalYInputField = compose.GlobalAudioOffsetInput;
            grid.GetType().GetMethod("LoadCustomGrids", Members).Invoke(grid, null);
            Assert.AreEqual(new CustomGrid().X, delay.text);
            CollectionAssert.AreEqual(new[] { .7f }, grid.VerticalYPositions);
            var original = grid.VerticalXPositions;
            grid.DensityInputField = delay;
            grid.BeatlineDensity = 4;
            delay.SetTextWithoutNotify("NaN");
            grid.OnDensityChange();
            Assert.AreEqual("4", delay.text);
            delay.SetTextWithoutNotify("0;bad");
            grid.OnVerticalXChange();
            Assert.AreSame(original, grid.VerticalXPositions);
            Assert.AreEqual(new CustomGrid().X, delay.text);

            var axisType = typeof(AdeSettingsDialog).GetNestedType("GridAxisEditor", BindingFlags.NonPublic);
            object axis = Activator.CreateInstance(axisType, true);
            axisType.GetField("IsX").SetValue(axis, true);
            foreach (string name in new[] { "Start", "End", "Step", "Extra" })
                axisType.GetField(name).SetValue(axis, Object.Instantiate(delay, source));
            TMP_InputField Field(string name) => (TMP_InputField)axisType.GetField(name).GetValue(axis);
            axisType.GetField("Summary").SetValue(axis, Label("Summary", source, ""));
            Field("Start").SetTextWithoutNotify("0");
            Field("End").SetTextWithoutNotify("");
            Field("Step").SetTextWithoutNotify("1/4");
            Field("Extra").SetTextWithoutNotify("");
            Call("ApplyGridAxis", axis);
            Assert.AreSame(original, grid.VerticalXPositions);
            Assert.AreEqual("0", Field("Start").text);
            Field("End").SetTextWithoutNotify("1");
            Field("Extra").SetTextWithoutNotify("oops");
            Call("ApplyGridAxis", axis);
            Assert.AreSame(original, grid.VerticalXPositions);
            Field("Extra").SetTextWithoutNotify("1.5");
            Call("ApplyGridAxis", axis);
            CollectionAssert.AreEqual(new[] { 0f, .25f, .5f, .75f, 1f, 1.5f }, grid.VerticalXPositions);
            CollectionAssert.AreEqual(new[] { .7f }, grid.VerticalYPositions);
        }
        finally
        {
            file.SetValue(null, oldFile);
            canSave.SetValue(null, oldCanSave);
            instance.SetValue(null, oldGrid);
        }
    }

    [Test]
    public void TemporaryDisplayDelayRowIsNeverRegisteredForMounting()
    {
        Assert.That(source.childCount, Is.EqualTo(3));
        Assert.That(source.Find("DisplayDelay"), Is.Null,
            "A staging row queued for deferred destruction must not enter the settings source list.");
        var rows = (IList)typeof(AdeSettingsDialog).GetField("rows", Members).GetValue(settings);
        Assert.That(rows.Count, Is.EqualTo(3));
        foreach (var row in rows)
        {
            var transform = (Transform)row.GetType().GetField("Row").GetValue(row);
            Assert.That((bool)transform, Is.True);
            Assert.That(transform.name, Is.Not.EqualTo("DisplayDelay"));
        }
        Call("MountRows");
        Assert.That(destination.childCount, Is.EqualTo(3));
        Assert.That(delay.transform.parent, Is.SameAs(offset));
    }

    [Test]
    public void RepeatedMountAndCloseReturnAllControlsWithoutStaleReferences()
    {
        var order = new Transform[source.childCount];
        for (int i = 0; i < order.Length; i++) order[i] = source.GetChild(i);
        for (int cycle = 0; cycle < 3; cycle++)
        {
            Call("MountRows");
            typeof(AdeSettingsDialog).GetField("mounted", Members).SetValue(settings, true);
            Assert.That(source.childCount, Is.Zero);
            Assert.That(destination.childCount, Is.EqualTo(order.Length));
            Assert.That(compose.GlobalAudioOffsetInput.text, Is.EqualTo("23"));
            Assert.That(delay.text, Is.EqualTo("41"));
            Call("Release");
            Assert.That(destination.childCount, Is.Zero);
            for (int i = 0; i < order.Length; i++) Assert.That(source.GetChild(i), Is.SameAs(order[i]));
        }
    }

    private object Call(string method, params object[] arguments)
        => typeof(AdeSettingsDialog).GetMethod(method, Members).Invoke(settings, arguments);

    private static RectTransform Rect(string name, Transform parent, params Type[] components)
    {
        var node = new GameObject(name, typeof(RectTransform));
        node.transform.SetParent(parent, false);
        foreach (var component in components) node.AddComponent(component);
        return (RectTransform)node.transform;
    }

    private static TextMeshProUGUI Label(string name, Transform parent, string text)
    {
        var label = Rect(name, parent, typeof(TextMeshProUGUI)).GetComponent<TextMeshProUGUI>();
        label.font = Resources.Load<TMP_FontAsset>("AffdataEdit/Fonts/EditorUI SDF");
        label.text = text;
        return label;
    }
}

public class AdeSettingsDeferredRowTests
{
    [UnityEngine.TestTools.UnityTest]
    public IEnumerator DeferredDestructionBeforeFirstOpenLeavesAllBorrowedRowsAlive()
    {
        // Exercise the player's deferred Destroy boundary, not only EditMode's immediate cleanup.
        yield return new UnityEngine.TestTools.EnterPlayMode();
        var fixture = new AdeSettingsRowLifecycleTests();
        try
        {
            fixture.SetUp();
            yield return null;
            fixture.RepeatedMountAndCloseReturnAllControlsWithoutStaleReferences();
        }
        finally { fixture.TearDown(); }
        yield return new UnityEngine.TestTools.ExitPlayMode();
    }
}
