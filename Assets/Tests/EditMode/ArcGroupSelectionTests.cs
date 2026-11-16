using System.Collections.Generic;
using System.Reflection;
using Arcade.Compose;
using Arcade.Compose.Operation;
using Arcade.Gameplay;
using Arcade.Gameplay.Chart;
using Newtonsoft.Json;
using NUnit.Framework;
using UnityEngine;

public class ArcGroupSelectionTests
{
    private GameObject root;
    private AdeSelectNoteOperation operation;
    private AdeSelectionManager selection;
    private ArcGameplayManager previousGame;
    private ArcArc first, second, outside;
    private static readonly MethodInfo Click = typeof(AdeSelectNoteOperation).GetMethod("SelectArc", BindingFlags.Instance | BindingFlags.NonPublic);

    [SetUp]
    public void SetUp()
    {
        previousGame = ArcGameplayManager.Instance;
        root = new GameObject("Arc selection tests"); root.SetActive(false);
        typeof(ArcGameplayManager).GetProperty("Instance").SetValue(null, root.AddComponent<ArcGameplayManager>());
        operation = root.AddComponent<AdeSelectNoteOperation>();
        selection = root.AddComponent<AdeSelectionManager>();
        first = new ArcArc(); second = new ArcArc(); outside = new ArcArc();
        first.ArcGroup = second.ArcGroup = new List<ArcArc> { first, second };
        selection.SelectNote(outside);
    }

    [TearDown]
    public void TearDown()
    {
        Object.DestroyImmediate(root);
        typeof(ArcGameplayManager).GetProperty("Instance").SetValue(null, previousGame);
    }

    private void Press(ArcArc arc, float time, bool legacy = false, float x = 0)
        => Click.Invoke(operation, new object[] { selection, arc, legacy, new Vector2(x, 0), time });

    [TestCase(0)] [TestCase(1)] [TestCase(2)]
    public void SingleClickOnlyTogglesTarget(int selected)
    {
        if (selected > 0) selection.SelectNote(first);
        if (selected > 1) selection.SelectNote(second);
        Press(first, 1);
        Assert.That(selection.SelectedNotes.Contains(first), Is.EqualTo(selected == 0));
        Assert.That(selection.SelectedNotes.Contains(second), Is.EqualTo(selected == 2));
        Assert.That(selection.SelectedNotes, Does.Contain(outside));
    }

    [TestCase(0)] [TestCase(1)] [TestCase(2)]
    public void DoubleClickUsesGroupStateBeforeFirstClick(int selected)
    {
        if (selected > 0) selection.SelectNote(first);
        if (selected > 1) selection.SelectNote(second);
        Press(first, 1); Press(first, 1.2f);
        Assert.That(selection.SelectedNotes.Contains(first), Is.EqualTo(selected != 2));
        Assert.That(selection.SelectedNotes.Contains(second), Is.EqualTo(selected != 2));
        Assert.That(selection.SelectedNotes, Does.Contain(outside));
    }

    [TestCase(1.5f, 0)] [TestCase(1.2f, 20)]
    public void SlowOrDistantClicksRemainSingle(float time, float x)
    {
        Press(first, 1); Press(first, time, x: x);
        Assert.That(selection.SelectedNotes, Is.EquivalentTo(new[] { outside }));
    }

    [Test]
    public void DifferentArcsDoNotFormDoubleClick()
    {
        selection.SelectNote(first); selection.SelectNote(second);
        Press(first, 1); Press(second, 1.2f);
        Assert.That(selection.SelectedNotes, Is.EquivalentTo(new[] { outside }));
    }

    [Test]
    public void ResetOrExternalSelectionChangeBreaksDoubleClick()
    {
        selection.SelectNote(first); selection.SelectNote(second);
        Press(first, 1); operation.Reset(); Press(first, 1.2f);
        Assert.That(selection.SelectedNotes, Does.Contain(second));
        Press(first, 2); selection.DeselectNote(outside); Press(first, 2.2f);
        Assert.That(selection.SelectedNotes, Does.Contain(second));
    }

    [Test]
    public void LegacyClickStillSelectsThenDeselectsGroup()
    {
        selection.SelectNote(first);
        Press(first, 1, true);
        Assert.That(selection.SelectedNotes, Does.Contain(second));
        Press(first, 2, true);
        Assert.That(selection.SelectedNotes, Is.EquivalentTo(new[] { outside }));
    }

    [Test]
    public void NewAndExistingSettingsDefaultToNewBehaviorAndLegacyChoicePersists()
    {
        Assert.IsFalse(new ArcadePreference().LegacyArcGroupSelection);
        var preference = JsonConvert.DeserializeObject<ArcadePreference>("{}");
        Assert.IsFalse(preference.LegacyArcGroupSelection);
        preference.LegacyArcGroupSelection = true;
        Assert.IsTrue(JsonConvert.DeserializeObject<ArcadePreference>(JsonConvert.SerializeObject(preference)).LegacyArcGroupSelection);
    }
}
