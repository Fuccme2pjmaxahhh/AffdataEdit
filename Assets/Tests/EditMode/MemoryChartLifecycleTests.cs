using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Arcade.Compose;
using Arcade.Compose.Command;
using Arcade.Gameplay;
using Arcade.Gameplay.Chart;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

public class MemoryChartLifecycleTests
{
    private const BindingFlags InstanceFields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private readonly List<GameObject> owners = new List<GameObject>();
    private readonly List<Action> restoreSingletons = new List<Action>();

    private T Inactive<T>() where T : Component
    {
        var owner = new GameObject(typeof(T).Name + " memory regression");
        owner.SetActive(false); owners.Add(owner);
        return owner.AddComponent<T>();
    }
    private void Singleton<T>(T instance) where T : Component
    {
        var field = typeof(T).GetField("<Instance>k__BackingField", BindingFlags.Static | BindingFlags.NonPublic);
        var previous = field.GetValue(null);
        field.SetValue(null, instance);
        restoreSingletons.Add(() => field.SetValue(null, previous));
    }
    private static object Get(object owner, string field) => owner.GetType().GetField(field, InstanceFields).GetValue(owner);
    private static void Set(object owner, string field, object value) => owner.GetType().GetField(field, InstanceFields).SetValue(owner, value);
    private static object Call(object owner, string method, params object[] arguments)
        => owner.GetType().GetMethod(method, InstanceFields).Invoke(owner, arguments);

    [TearDown]
    public void TearDown()
    {
        for (int i = owners.Count - 1; i >= 0; i--) Object.DestroyImmediate(owners[i]);
        for (int i = restoreSingletons.Count - 1; i >= 0; i--) restoreSingletons[i]();
        owners.Clear(); restoreSingletons.Clear();
    }

    [Test]
    public void InactiveDrawsReleaseNoteAndPropertyBlockReferencesAndUnloadDropsPeakArrays()
    {
        var renderer = Inactive<ArcNoteRenderer>();
        var drawType = typeof(ArcNoteRenderer).GetNestedType("Draw", BindingFlags.NonPublic);
        var itemType = typeof(ArcNoteRenderer).GetNestedType("Item", BindingFlags.NonPublic);
        var draw = Activator.CreateInstance(drawType, true);
        var item = Activator.CreateInstance(itemType);
        var note = new ArcSlide();
        Set(item, "Note", note); Set(draw, "Item", item);
        var texture = new Texture2D(2, 2);
        try
        {
            var properties = (MaterialPropertyBlock)Get(draw, "Properties");
            properties.SetTexture("_MainTex", texture);
            ((IList)Get(renderer, "draws")).Add(draw);
            Set(renderer, "activeDraws", 1);
            Call(renderer, "TrimInactiveDraws", 0);
            Assert.That(Get(Get(draw, "Item"), "Note"), Is.Null);
            Assert.That(properties.GetTexture("_MainTex"), Is.Null);

            var inputType = typeof(ArcNoteRenderer).GetNestedType("FrameInput", BindingFlags.NonPublic);
            Set(renderer, "frameInputs", Array.CreateInstance(inputType, 8192));
            Call(renderer, "ReleaseChartMemory");
            Assert.That(((Array)Get(renderer, "frameInputs")).Length, Is.Zero);
            Assert.That(((IList)Get(renderer, "draws")).Count, Is.Zero);
            Call(renderer, "ReleaseChartMemory"); // unload and scene destruction may both call it
        }
        finally { Object.DestroyImmediate(texture); }
    }

    [TestCase(false)]
    [TestCase(true)]
    public void NegativeDisplayDelayKeepsHeadSoundPendingAfterVisualJudgement(bool sky)
    {
        var game = Inactive<ArcGameplayManager>(); Singleton(game);
        Singleton(Inactive<ArcAudioManager>());
        Singleton(Inactive<ArcEffectManager>());
        game.DisplayDelay = -200;
        Set(game, "audioTiming", 1.201f);
        Assert.AreEqual(1001, game.SoundChartTiming);
        if (sky)
        {
            var manager = Inactive<ArcArcManager>(); Singleton(manager);
            var note = new ArcArc { Timing = 1000, EndTiming = 1100, Judged = true };
            manager.Arcs.Add(note);
            Call(manager, "JudgeArcs");
            Assert.IsTrue(note.AudioPlayed, "The music clock must still consume the pending head.");
        }
        else
        {
            var manager = Inactive<ArcHoldNoteManager>(); Singleton(manager);
            var note = new ArcHold { Timing = 1000, EndTiming = 1100, Judged = true };
            manager.Holds.Add(note);
            Call(manager, "JudgeHoldNotes");
            Assert.IsTrue(note.AudioPlayed, "The music clock must still consume the pending head.");
        }
    }

    [Test]
    public void ChartLoadDefersSlideMeshesAndReturnedVisualRebindsWithoutRetainingItsOldNote()
    {
        var manager = Inactive<ArcSlideManager>(); Singleton(manager);
        var first = new ArcSlide { Timing = 0, EndTiming = 1000 };
        var second = new ArcSlide { Timing = 1000, EndTiming = 2000 };
        manager.Load(new List<ArcSlide> { first, second });
        Assert.That(Get(first, "Visual"), Is.Null);
        Assert.That(Get(second, "Visual"), Is.Null);
        first.Instantiate();
        var visual = Get(first, "Visual");
        var mesh = (Mesh)Get(visual, "mesh");
        first.Destroy();
        Assert.That(Get(visual, "note"), Is.Null);
        second.Instantiate();
        Assert.That(Get(second, "Visual"), Is.SameAs(visual));
        Assert.That(Get(visual, "note"), Is.SameAs(second));
        manager.Clean();
        Assert.That(Get(second, "Visual"), Is.Null);
        Assert.That(mesh == null, Is.True);
    }

    [TestCase(1f, .25f)]
    [TestCase(.25f, 1f)]
    [TestCase(.75f, .5f)]
    public void SlideHeadSoundHistorySurvivesVisualDelayRebaseWithoutReplayingOrSkipping(float oldRate, float newRate)
    {
        var manager = Inactive<ArcSlideManager>();
        int oldVisual = 1000, oldSound = oldVisual + Mathf.RoundToInt(100 * oldRate);
        int newSound = oldSound + 5, newVisual = newSound - Mathf.RoundToInt(100 * newRate);
        Set(manager, "previousTime", oldVisual);
        Set(manager, "previousSoundTime", oldSound);
        Set(manager, "previousSoundPlaying", true);
        // Rate changes alter only the visual delay. A visual discontinuity may clear particles,
        // but must leave the continuously advancing music/head-sound cursor intact.
        Assert.That(newVisual, Is.Not.EqualTo(oldVisual + 5));
        Call(manager, "ResetVisualFeedback");
        Assert.That(Get(manager, "previousSoundTime"), Is.EqualTo(oldSound));
        Assert.That((bool)Call(manager, "CrossedSoundHead", oldSound - 10, newSound, true), Is.False);
        Assert.That((bool)Call(manager, "CrossedSoundHead", oldSound + 4, newSound, true), Is.True);
        Set(manager, "previousSoundTime", newSound);
        Assert.That((bool)Call(manager, "CrossedSoundHead", oldSound + 4, newSound + 5, true), Is.False);
        manager.ResetFeedback(); // explicit seek/rebuild starts a fresh cursor and stays silent
        Assert.That((bool)Call(manager, "CrossedSoundHead", newSound + 6, newSound + 10, true), Is.False);
    }

    private sealed class CountCommand : ICommand
    {
        public int Value;
        public string Name => "Memory regression edit";
        public void Do() => Value++;
        public void Undo() => Value--;
    }
    private AdeCommandManager History()
    {
        var manager = Inactive<AdeCommandManager>();
        manager.onCommandExecuted = (_, __) => { };
        Singleton(Inactive<AdeOperationManager>());
        return manager;
    }
    private static long HistoryBytes(AdeCommandManager manager)
        => (long)typeof(AdeCommandManager).GetProperty("HistoryMemoryBytes", InstanceFields).GetValue(manager);
    private static int HistoryCount(AdeCommandManager manager, string list)
        => ((ICollection)Get(manager, list)).Count;

    [Test]
    public void UndoMemoryAccountingSurvivesUndoRedoAndBranchingAndExplicitZeroDepth()
    {
        var manager = History();
        var first = new CountCommand(); var second = new CountCommand();
        manager.Add(first); manager.Add(second);
        long both = HistoryBytes(manager);
        manager.Undo();
        Assert.That(second.Value, Is.Zero);
        Assert.That(HistoryBytes(manager), Is.EqualTo(both));
        manager.Redo();
        Assert.That(second.Value, Is.EqualTo(1));
        manager.Undo(); manager.Add(new CountCommand());
        Assert.That(HistoryCount(manager, "redo"), Is.Zero);
        Assert.That(HistoryBytes(manager), Is.EqualTo(both));
        Assert.That(manager.bufferSize, Is.EqualTo(200));
        manager.SetBufferSize(0);
        Assert.That(HistoryBytes(manager), Is.Zero);
        Assert.That(HistoryCount(manager, "undo"), Is.Zero);
        manager.Clear();
    }

    [Test]
    public void UndoByteBudgetPreservesTheNewestLargeEditAndDropsItOnlyAfterANewerEdit()
    {
        var manager = History();
        // A large batch has a large estimated retained footprint without allocating a huge heap.
        var command = new CountCommand();
        var children = new ICommand[131072];
        for (int i = 0; i < children.Length; i++) children[i] = command;
        manager.Add(new BatchCommand(children, "Large import"));
        Assert.That(HistoryBytes(manager), Is.GreaterThan(128L * 1024 * 1024));
        Assert.That(HistoryCount(manager, "undo"), Is.EqualTo(1));
        manager.Undo(); Assert.That(command.Value, Is.Zero);
        manager.Redo(); Assert.That(command.Value, Is.EqualTo(children.Length));
        manager.Add(new CountCommand());
        Assert.That(HistoryCount(manager, "undo"), Is.EqualTo(1));
        Assert.That(HistoryBytes(manager), Is.LessThan(128L * 1024 * 1024));
        manager.Clear(); Assert.That(HistoryBytes(manager), Is.Zero);
    }
}
