using System;
using System.Collections.Generic;
using System.Reflection;
using Arcade.Aff;
using Arcade.Compose;
using Arcade.Gameplay.Chart;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

public class TimingEditorInputTests
{
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    private GameObject root;
    private static void Set(object owner, string name, object value) => owner.GetType().GetField(name, Hidden).SetValue(owner, value);
    private static object Get(object owner, string name) => owner.GetType().GetField(name, Hidden).GetValue(owner);
    private RectTransform Rect(string name, Transform parent)
    {
        var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        rect.SetParent(parent, false); return rect;
    }
    [SetUp]
    public void SetUp() { root = new GameObject("Timing input regression"); root.SetActive(false); }
    [TearDown]
    public void TearDown() => Object.DestroyImmediate(root);

    [TestCase(1)]
    [TestCase(2)]
    [TestCase(3)]
    [TestCase(4)]
    [TestCase(3007)]
    public void CompactGroupHeightDoesNotTriggerAnEndlessRefresh(int count)
    {
        var panel = root.AddComponent<AdeChartToolsPanel>();
        var viewport = Rect("Viewport", root.transform); viewport.sizeDelta = new Vector2(480, 600);
        var page = Rect("Groups", viewport);
        ((RectTransform[])Get(panel, "pages"))[0] = page;
        var scroll = Rect("Group list", page).gameObject.AddComponent<ScrollRect>();
        var size = scroll.gameObject.AddComponent<LayoutElement>();
        float expected = Mathf.Min(count, 4) * 48 - 4;
        size.minHeight = size.preferredHeight = expected;
        Set(panel, "groupScroll", scroll);
        Set(panel, "expanded", true);
        Set(panel, "lastChart", new ArcChart(new RawAffChart()));
        var groups = (List<ArcTimingGroup>)Get(panel, "groups");
        for (int i = 0; i < count; i++) groups.Add(i == 0 ? null : new ArcTimingGroup { Id = i });
        var resize = typeof(AdeChartToolsPanel).GetMethod("ResizeGroupList", Hidden);
        for (int frame = 0; frame < 3; frame++)
        {
            Set(panel, "dirty", false);
            Set(panel, "groupLayoutDirty", true);
            resize.Invoke(panel, null);
            Assert.AreEqual(expected, size.preferredHeight);
            Assert.IsFalse((bool)Get(panel, "dirty"), "Layout must not invalidate chart data on every frame.");
        }
    }

    [TestCase(0)]
    [TestCase(1)]
    [TestCase(2)]
    public void RefreshPreservesTypingUntilCommitOrRebind(int focused)
    {
        var rowType = typeof(AdeChartToolsPanel).GetNestedType("TimingRow", BindingFlags.NonPublic);
        object row = Activator.CreateInstance(rowType, true);
        Set(row, "Timing", new ArcTiming { Timing = 1234, Bpm = 145.5f, BeatsPerLine = 3.5f });
        string[] names = { "Time", "Bpm", "Beats" }, expected = { "1234", "145.5", "3.5" };
        var fields = new TMP_InputField[3];
        for (int i = 0; i < fields.Length; i++)
        {
            fields[i] = Rect(names[i], root.transform).gameObject.AddComponent<TMP_InputField>();
            fields[i].SetTextWithoutNotify("pending input");
            Set(row, names[i], fields[i]);
        }
        typeof(TMP_InputField).GetField("m_AllowInput", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(fields[focused], true);
        var fill = typeof(AdeChartToolsPanel).GetMethod("SetTimingFields", BindingFlags.Static | BindingFlags.NonPublic);
        fill.Invoke(null, new[] { row, (object)true });
        for (int i = 0; i < fields.Length; i++) Assert.AreEqual(i == focused ? "pending input" : expected[i], fields[i].text);
        fill.Invoke(null, new[] { row, (object)false });
        for (int i = 0; i < fields.Length; i++) Assert.AreEqual(expected[i], fields[i].text);
        typeof(TMP_InputField).GetField("m_AllowInput", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(fields[focused], false);
    }
}
