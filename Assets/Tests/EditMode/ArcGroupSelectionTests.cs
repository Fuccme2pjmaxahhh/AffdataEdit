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
    public void SameCountSelectionReplacementBreaksDoubleClickButNoOpDoesNot()
    {
        selection.SelectNote(first); selection.SelectNote(second);
        Press(first, 1);
        selection.DeselectNote(outside); selection.SelectNote(new ArcTap());
        Press(first, 1.2f);
        Assert.That(selection.SelectedNotes, Does.Contain(second));
        operation.Reset();
        Press(first, 2); selection.SelectNote(second); Press(first, 2.2f);
        Assert.IsFalse(selection.SelectedNotes.Contains(second));
    }

    [Test]
    public void LargeSelectionDoubleClicksAllocateNoSnapshot()
    {
        long canaryStart = System.GC.GetAllocatedBytesForCurrentThread();
        System.GC.KeepAlive(new byte[8192]);
        bool allocationCounterSupported = System.GC.GetAllocatedBytesForCurrentThread() > canaryStart;
        const int count = 20000;
        selection.SelectedNotes.Capacity = count + 4;
        for (int i = 0; i < count; i++)
            selection.SelectedNotes.Add(new ArcTap { Selected = true });
        var click = (System.Action<AdeSelectionManager, ArcArc, bool, Vector2, float>)
            System.Delegate.CreateDelegate(typeof(System.Action<AdeSelectionManager, ArcArc, bool, Vector2, float>), operation, Click);
        for (int i = 0; i < 4; i++)
        {
            click(selection, first, false, Vector2.zero, 1);
            click(selection, first, false, Vector2.zero, 1.2f);
        }
        long allocations = System.GC.GetAllocatedBytesForCurrentThread();
        long start = System.Diagnostics.Stopwatch.GetTimestamp();
        for (int i = 0; i < 100; i++)
        {
            click(selection, first, false, Vector2.zero, 1);
            click(selection, first, false, Vector2.zero, 1.2f);
        }
        long bytes = System.GC.GetAllocatedBytesForCurrentThread() - allocations;
        double ms = (System.Diagnostics.Stopwatch.GetTimestamp() - start) * 1000d / System.Diagnostics.Stopwatch.Frequency;
        if (allocationCounterSupported) Assert.AreEqual(0, bytes);
        Assert.AreEqual(count + 1, selection.SelectedNotes.Count);
        Debug.Log($"ARC_SELECTION_BENCH selected={count} doubleClicks=100 ms={ms:F3} allocationCounterSupported={allocationCounterSupported} allocatedBytes={bytes}");
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

    [TestCase(.5f)] [TestCase(2f)]
    public void ScaledArcTapGeometrySelectsItsOwnerWithoutSelectingOrdinaryArcTaps(float width)
    {
        var renderer = root.AddComponent<ArcNoteRenderer>();
        var mesh = new Mesh
        {
            vertices = new[] { new Vector3(-width, -1, 0), new Vector3(width, -1, 0), new Vector3(0, 1, 0) },
            triangles = new[] { 0, 1, 2 }
        };
        try
        {
            var type = typeof(ArcNoteRenderer);
            const BindingFlags fields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            // BeginPicking advances the query before the first mesh-cache read.
            type.GetField("pickingQuery", fields).SetValue(renderer, 1);
            var itemType = type.GetNestedType("Item", BindingFlags.NonPublic);
            var item = System.Activator.CreateInstance(itemType);
            var converted = new ArcArcTap { Arc = first, IsConvertedVariousSizedArctap = true, Enable = true };
            itemType.GetField("Note").SetValue(item, converted);
            itemType.GetField("Mesh").SetValue(item, mesh);
            var items = (System.Collections.IList)type.GetField("items", fields).GetValue(renderer);
            items.Add(item);
            var inputType = type.GetNestedType("FrameInput", BindingFlags.NonPublic);
            var input = System.Activator.CreateInstance(inputType);
            var dataType = type.GetNestedType("NoteInstance", BindingFlags.NonPublic);
            inputType.GetField("Data").SetValue(input, dataType.GetMethod("Create").Invoke(null,
                new object[] { Matrix4x4.identity, 0, false }));
            SetSingleNativeElement(renderer, "nativeInputs", input);
            var geometryType = type.GetNestedType("PreparedGeometry", BindingFlags.NonPublic);
            var geometry = System.Activator.CreateInstance(geometryType);
            geometryType.GetField("Bounds").SetValue(geometry, mesh.bounds);
            geometryType.GetField("Visible").SetValue(geometry, true);
            SetSingleNativeElement(renderer, "preparedGeometry", geometry);
            var raycast = type.GetMethod("RaycastNote", fields);
            var ray = new Ray(new Vector3(0, 0, -1), Vector3.forward);
            foreach (bool collectAll in new[] { false, true })
            {
                var picked = (ArcNote)raycast.Invoke(renderer, new object[] { ray, 120f, false, collectAll, null });
                Assert.That(picked, Is.SameAs(first));
            }
            var ordinary = new ArcArcTap { Arc = first, Enable = true };
            itemType.GetField("Note").SetValue(item, ordinary); items[0] = item;
            Assert.That(raycast.Invoke(renderer, new object[] { ray, 120f, false, false, null }), Is.SameAs(ordinary));
            selection.SelectNote(ordinary);
            Press(first, 1); Press(first, 1.2f);
            Assert.That(selection.SelectedNotes, Does.Contain(first));
            Assert.That(selection.SelectedNotes, Does.Contain(second));
            Assert.That(selection.SelectedNotes, Does.Contain(ordinary));
            Assert.IsFalse(selection.SelectedNotes.Contains(converted));
        }
        finally
        {
            typeof(ArcNoteRenderer).GetMethod("DisposeFrameBuffers", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(renderer, null);
            Object.DestroyImmediate(mesh);
        }
    }

    private static void SetSingleNativeElement(ArcNoteRenderer renderer, string fieldName, object element)
    {
        var field = typeof(ArcNoteRenderer).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        var constructor = System.Array.Find(field.FieldType.GetConstructors(), c =>
        {
            var args = c.GetParameters();
            return args.Length == 3 && args[0].ParameterType == typeof(int) && args[1].ParameterType.IsEnum;
        });
        var parameters = constructor.GetParameters();
        var array = constructor.Invoke(new object[] { 1, System.Enum.Parse(parameters[1].ParameterType, "Persistent"),
            System.Enum.Parse(parameters[2].ParameterType, "ClearMemory") });
        field.SetValue(renderer, array);
        field.FieldType.GetProperty("Item").SetValue(array, element, new object[] { 0 });
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
