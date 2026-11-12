using System.Reflection;
using Arcade.Compose;
using Arcade.Compose.Operation;
using Newtonsoft.Json;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

public class ArcColorCycleTests
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private GameObject root;
    private ArcadeComposeManager compose, previousCompose;
    private AdeOperationManager operations, previousOperations;
    private AdeClickToCreate create;

    [SetUp]
    public void SetUp()
    {
        previousCompose = ArcadeComposeManager.Instance;
        previousOperations = AdeOperationManager.Instance;
        root = new GameObject("Arc color cycling"); root.SetActive(false);
        compose = root.AddComponent<ArcadeComposeManager>();
        operations = root.AddComponent<AdeOperationManager>();
        create = root.AddComponent<AdeClickToCreate>();
        typeof(ArcadeComposeManager).GetProperty("Instance").SetValue(null, compose);
        typeof(AdeOperationManager).GetProperty("Instance").SetValue(null, operations);
    }

    [TearDown]
    public void TearDown()
    {
        Object.DestroyImmediate(root);
        typeof(ArcadeComposeManager).GetProperty("Instance").SetValue(null, previousCompose);
        typeof(AdeOperationManager).GetProperty("Instance").SetValue(null, previousOperations);
    }

    [TestCase(31, 0, new[] { 1, 2, 3, 4, 0 })]
    [TestCase(15, 3, new[] { 0, 1, 2, 3, 0 })]
    [TestCase(3, 4, new[] { 0, 1, 0, 1 })]
    [TestCase(4, 0, new[] { 2, 2, 2 })]
    [TestCase(16, 0, new[] { 4, 4, 4 })]
    [TestCase(0, 4, new[] { 0, 1, 2, 3, 0 })]
    [TestCase(32, 4, new[] { 0, 1, 2, 3, 0 })]
    [TestCase(34, 4, new[] { 1, 1, 1 })]
    public void HotkeyCyclesOnlyEnabledColorsAndWraps(int mask, int start, int[] expected)
    {
        compose.ArcadePreference.ArcColorCycleMask = mask;
        compose.ArcadePreference.ArcColorCycleCustomized = true;
        create.SetArcColor(start); // Direct menu selection can still choose an excluded color.
        Assert.AreEqual(start, create.ArcColor);
        foreach (int color in expected)
        {
            create.SwitchColor();
            Assert.AreEqual(color, create.ArcColor);
        }
    }

    [Test]
    public void OngoingOperationStillBlocksColorCycling()
    {
        typeof(AdeOperationManager).GetField("ongoingOperation", Private).SetValue(operations, new AdeOngoingOperation());
        create.SwitchColor();
        Assert.AreEqual(0, create.ArcColor);
    }

    [Test]
    public void DefaultExcludesBlackAndCustomChoicesSurviveReload()
    {
        compose.ArcadePreference = JsonConvert.DeserializeObject<ArcadePreference>("{\"Velocity\":165}");
        Assert.AreEqual(15, compose.ArcadePreference.GetArcColorCycleMask());
        compose.ArcadePreference.ArcColorCycleMask = 5; // Blue and green.
        compose.ArcadePreference = JsonConvert.DeserializeObject<ArcadePreference>(JsonConvert.SerializeObject(compose.ArcadePreference));
        create.SwitchColor(); Assert.AreEqual(2, create.ArcColor);
        create.SwitchColor(); Assert.AreEqual(0, create.ArcColor);
    }

    [Test]
    public void PriorDefaultMigratesAndExplicitBlackOptInSurvivesReload()
    {
        var preference = JsonConvert.DeserializeObject<ArcadePreference>("{\"ArcColorCycleMask\":31}");
        Assert.AreEqual(15, preference.GetArcColorCycleMask());
        preference.ArcColorCycleCustomized = true;
        preference = JsonConvert.DeserializeObject<ArcadePreference>(JsonConvert.SerializeObject(preference));
        Assert.AreEqual(31, preference.GetArcColorCycleMask());
    }

    [Test]
    public void SettingsCannotUncheckTheLastColor()
    {
        compose.ArcadePreference.ArcColorCycleMask = 4;
        var settings = root.AddComponent<AdeSettingsDialog>();
        var page = new GameObject("Editing", typeof(RectTransform)); page.transform.SetParent(root.transform);
        var pages = (RectTransform[])typeof(AdeSettingsDialog).GetField("pages", Private).GetValue(settings);
        pages[(int)AdeSettingsDialog.Page.Editing] = (RectTransform)page.transform;
        typeof(AdeSettingsDialog).GetMethod("BuildArcColorSettings", Private).Invoke(settings, null);
        var toggles = page.GetComponentsInChildren<Toggle>(true);
        Assert.AreEqual(5, toggles.Length);
        Assert.IsTrue(toggles[2].isOn);
        toggles[2].isOn = false;
        Assert.IsTrue(toggles[2].isOn);
        Assert.AreEqual(4, compose.ArcadePreference.ArcColorCycleMask);
    }
}
