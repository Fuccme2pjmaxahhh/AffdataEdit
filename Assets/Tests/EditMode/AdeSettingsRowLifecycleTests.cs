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
