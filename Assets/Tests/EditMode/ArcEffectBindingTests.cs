using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Arcade.Gameplay;
using Arcade.Gameplay.Chart;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

public class ArcEffectBindingTests
{
    private const BindingFlags Members = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private readonly List<GameObject> owners = new List<GameObject>();
    private readonly List<Action> restore = new List<Action>();
    private ArcEffectManager effects;
    private Type visualType;
    private MethodInfo setEffect;

    private T Inactive<T>() where T : Component
    {
        var owner = new GameObject(typeof(T).Name + " effect rule check");
        owner.SetActive(false);
        owners.Add(owner);
        return owner.AddComponent<T>();
    }

    private void Singleton<T>(T value) where T : Component
    {
        var field = typeof(T).GetField("<Instance>k__BackingField", BindingFlags.Static | BindingFlags.NonPublic);
        object previous = field.GetValue(null);
        field.SetValue(null, value);
        restore.Add(() => field.SetValue(null, previous));
    }

    [SetUp]
    public void SetUp()
    {
        effects = Inactive<ArcEffectManager>();
        Singleton(effects);
        var arcs = Inactive<ArcArcManager>();
        Singleton(arcs);
        var skin = Inactive<ArcArcRenderer>();
        var template = Inactive<ArcLongNoteEffect>();
        skin.JudgeEffect = template;
        effects.HoldNoteEffects[0] = template;
        // New anchors live beneath this fixture, so teardown also releases the registry entries.
        template.transform.SetParent(effects.transform, false);
        arcs.ArcNotePrefab = skin.gameObject;
        visualType = typeof(ArcArcManager).Assembly.GetType("Arcade.Gameplay.ArcArcVisual", true);
        setEffect = typeof(ArcEffectManager).GetMethod("SetArcEffect", Members);
    }

    [TearDown]
    public void TearDown()
    {
        for (int i = owners.Count - 1; i >= 0; i--)
            if (owners[i]) Object.DestroyImmediate(owners[i]);
        for (int i = restore.Count - 1; i >= 0; i--) restore[i]();
        owners.Clear();
        restore.Clear();
    }

    private object Visual(ArcArc note)
    {
        note.LineType = ArcLineType.FalseNotVoid;
        object visual = Activator.CreateInstance(visualType, true);
        visualType.GetField("arc", Members).SetValue(visual, note);
        return visual;
    }

    private void Report(object visual, bool enable) => setEffect.Invoke(effects, new[] { visual, (object)0, enable });
    private IDictionary States => (IDictionary)typeof(ArcEffectManager).GetField("arcEffects", Members).GetValue(effects);
    private ArcLongNoteEffect Anchor => (ArcLongNoteEffect)States[0].GetType().GetField("Effect", Members).GetValue(States[0]);
    private object Target => typeof(ArcLongNoteEffect).GetProperty("FollowTarget", Members).GetValue(Anchor);

    [Test]
    public void SharedColorFollowsLastReportWithoutAllocatingAnotherAnchor()
    {
        object first = Visual(new ArcArc());
        object second = Visual(new ArcArc());
        Report(first, true);
        ArcLongNoteEffect shared = Anchor;
        Report(second, true);
        Assert.That(Target, Is.SameAs(second));
        // Report order, rather than the order in which owners first became active, wins.
        Report(second, true);
        Report(first, true);
        Assert.That(Target, Is.SameAs(first));
        Report(second, false);
        Assert.That(Target, Is.SameAs(first), "Removing another owner must preserve this frame's selected hit point.");
        Report(second, true);
        Report(second, false);
        Assert.That(Target, Is.SameAs(first), "Ending the selected owner falls back to the surviving owner.");
        Assert.That(Anchor, Is.SameAs(shared));
        Assert.That(States.Count, Is.EqualTo(1));
        Report(first, false);
        Assert.That(Target, Is.Null);
    }

    [Test]
    public void NoInputTransitionUnregistersAnAlreadyActiveVisual()
    {
        var note = new ArcArc { TimingGroup = new ArcTimingGroup() };
        object visual = Visual(note);
        PropertyInfo enable = visualType.GetProperty("EnableEffect", Members);
        enable.SetValue(visual, true);
        Assert.That(Target, Is.SameAs(visual));
        note.TimingGroup.NoInput = true;
        enable.SetValue(visual, true);
        Assert.That(enable.GetValue(visual), Is.EqualTo(false));
        Assert.That(Target, Is.Null);
        Assert.That(((IList)States[0].GetType().GetField("Owners", Members).GetValue(States[0])).Count, Is.Zero);
    }

    [Test]
    public void SharedColorUsesTimingThenEndOrderForUnsortedReports()
    {
        object first = Visual(new ArcArc { Timing = 100, EndTiming = 900 });
        object second = Visual(new ArcArc { Timing = 200, EndTiming = 400 });
        object third = Visual(new ArcArc { Timing = 200, EndTiming = 600 });
        Report(third, true);
        Report(first, true);
        Report(second, true);
        Assert.That(Target, Is.SameAs(third));
        Report(third, false);
        Assert.That(Target, Is.SameAs(second));
        // The first active report of a new frame replaces the previous frame's selection.
        States[0].GetType().GetField("ReportFrame", Members).SetValue(States[0], int.MinValue);
        Report(first, true);
        Assert.That(Target, Is.SameAs(first));
    }

    [TestCase(true, 100, false)]
    [TestCase(true, 101, true)]
    [TestCase(false, 99, false)]
    [TestCase(false, 100, true)]
    [TestCase(false, 200, true)]
    [TestCase(false, 201, false)]
    public void HeadAndConnectedSegmentUseTheirOwnStartBoundary(bool head, int timing, bool expected)
    {
        var note = new ArcArc { Timing = 100, EndTiming = 200, RenderHead = head, Judging = true, LineType = ArcLineType.FalseNotVoid };
        var rule = typeof(ArcArcManager).GetMethod("ShouldEnableArcEffect", BindingFlags.Static | BindingFlags.NonPublic);
        Assert.That(rule.Invoke(null, new object[] { note, timing }), Is.EqualTo(expected));
        note.TimingGroup = new ArcTimingGroup { NoInput = true };
        Assert.That(rule.Invoke(null, new object[] { note, timing }), Is.EqualTo(false));
    }

    [TestCase(.005f, false)]
    [TestCase(.000005f, true)]
    public void NearbyButDifferentHeightsDoNotHideAnIndependentHead(float heightDifference, bool connected)
    {
        var first = new ArcArc { Timing = 1000, EndTiming = 2000, XEnd = .5f, YEnd = .5f };
        var next = new ArcArc { Timing = 2000, EndTiming = 3000, XStart = .5f, YStart = .5f + heightDifference };
        Group(first, next);
        Assert.That(next.RenderHead, Is.EqualTo(!connected));
        Assert.That(ReferenceEquals(first.ArcGroup, next.ArcGroup), Is.EqualTo(connected));
    }

    [Test]
    public void ShortArcsOnlyConnectForwardAndKeepTheFirstHead()
    {
        var first = new ArcArc { Timing = 1000, EndTiming = 1000 };
        var next = new ArcArc { Timing = 1000, EndTiming = 1005 };
        // Input order must not make the later ending arc close the first arc's head.
        Group(next, first);
        Assert.That(first.RenderHead, Is.True);
        Assert.That(next.RenderHead, Is.False);
        Assert.That(first.ArcGroup, Is.SameAs(next.ArcGroup));
    }

    private static void Group(params ArcArc[] notes)
        => typeof(ArcArcManager).GetMethod("CalculateArcGroups", BindingFlags.Static | BindingFlags.NonPublic)
            .Invoke(null, new object[] { new List<ArcArc>(notes) });

    public IEnumerator ExerciseHiddenSharedParticleLifecycle()
    {
        var game = Inactive<ArcGameplayManager>();
        Singleton(game);
        game.IsPlaying = true;
        var timing = Inactive<ArcTimingManager>();
        Singleton(timing);
        timing.Timings.Add(new ArcTiming { Timing = 0, Bpm = 100 });
        var arcs = ArcArcManager.Instance;
        arcs.ArcLayer = arcs.transform;
        arcs.ArcNotePrefab.GetComponent<ArcArcRenderer>().HeightIndicatorRenderer = Inactive<SpriteRenderer>();
        effects.EffectPlane = Inactive<ArcEffectPlane>();
        var camera = Inactive<Camera>();
        camera.transform.position = new Vector3(0, 0, -10);
        effects.EffectPlane.ReferenceCamera = camera;
        // Only the anchor and pooled particle lifecycle run; unrelated gameplay managers stay inactive.
        effects.enabled = false;
        effects.gameObject.SetActive(true);
        var group = new ArcTimingGroup { GroupHide = true, latestRenderTime = 10000 };
        group.Timings.Add(new ArcTiming { Timing = 0, Bpm = 100 });
        var note = new ArcArc { Timing = 1000, EndTiming = 2000, XStart = .2f, XEnd = .8f,
            LineType = ArcLineType.FalseNotVoid, CurveType = ArcCurveType.S,
            YStart = .5f, YEnd = .5f, Judging = true, RenderHead = true, TimingGroup = group };
        note.ArcGroup = new List<ArcArc> { note };
        arcs.Arcs.Add(note);
        var render = typeof(ArcArcManager).GetMethod("RenderArcs", Members);
        var clock = typeof(ArcGameplayManager).GetField("audioTiming", Members);
        clock.SetValue(game, 1.25f);
        render.Invoke(arcs, null);
        Assert.That(note.Enable, Is.True);
        yield return null;
        yield return null;
        ParticleSystem particle = (ParticleSystem)typeof(ArcLongNoteEffect).GetField("particles", Members).GetValue(Anchor);
        Assert.That(particle, Is.Not.Null);
        Assert.That(particle.isPlaying, Is.True);
        Assert.That(particle.particleCount, Is.GreaterThan(0));
        Vector3 firstPosition = Anchor.transform.position;
        clock.SetValue(game, 1.75f);
        render.Invoke(arcs, null);
        yield return null;
        yield return null;
        Assert.That(Anchor.transform.position.x, Is.Not.EqualTo(firstPosition.x));

        var second = new ArcArc { Timing = 1000, EndTiming = 2000, XStart = .1f, XEnd = .4f,
            LineType = ArcLineType.FalseNotVoid, CurveType = ArcCurveType.S,
            YStart = .75f, YEnd = .75f, Judging = true, RenderHead = true, TimingGroup = group };
        second.ArcGroup = new List<ArcArc> { second };
        arcs.Arcs.Add(second);
        render.Invoke(arcs, null);
        yield return null;
        yield return null;
        Assert.That(Anchor.GetType().GetProperty("FollowTarget", Members).GetValue(Anchor),
            Is.SameAs(typeof(ArcArc).GetField("arcRenderer", Members).GetValue(second)));
        Assert.That(effects.GetComponentsInChildren<ParticleSystem>(true).Length, Is.EqualTo(1),
            "Overlapping hidden arcs of one color must keep a single emitter.");
        Assert.That(note.Enable, Is.False, "Only the selected hidden arc should retain a visual.");
        Assert.That(second.Enable, Is.True);
        var renderer = Inactive<ArcNoteRenderer>();
        visualType.GetMethod("Submit", Members).Invoke(Target, new object[] { renderer });
        Assert.That(((IList)typeof(ArcNoteRenderer).GetField("items", Members).GetValue(renderer)).Count, Is.Zero);

        clock.SetValue(game, 2.5f);
        render.Invoke(arcs, null);
        Assert.That(note.Enable, Is.False);
        Assert.That(second.Enable, Is.False);
        Assert.That(Target, Is.Null);
        Assert.That(typeof(ArcLongNoteEffect).GetField("requested", Members).GetValue(Anchor), Is.EqualTo(false));
        float stopAt = (float)typeof(ArcLongNoteEffect).GetField("stopAt", Members).GetValue(Anchor);
        // EditMode's enumerator does not advance a WaitForSeconds instruction like a player coroutine.
        float timeout = Time.realtimeSinceStartup + 3;
        while (Time.time < stopAt + .02f && Time.realtimeSinceStartup < timeout) yield return null;
        yield return null;
        Assert.That(Time.time, Is.GreaterThanOrEqualTo(stopAt));
        Assert.That(particle.gameObject.activeSelf, Is.False,
            "Anchor enabled=" + Anchor.enabled + ", active=" + Anchor.Active
            + ", stopAt=" + typeof(ArcLongNoteEffect).GetField("stopAt", Members).GetValue(Anchor)
            + ", same particle=" + ReferenceEquals(particle, typeof(ArcLongNoteEffect).GetField("particles", Members).GetValue(Anchor)));
    }
}

public class HiddenArcParticleLifecycleTests
{
    [UnityEngine.TestTools.UnityTest]
    public IEnumerator HiddenArcsKeepOneMovingParticleWithoutSubmittingBodies()
    {
        yield return new UnityEngine.TestTools.EnterPlayMode();
        var fixture = new ArcEffectBindingTests();
        try
        {
            fixture.SetUp();
            yield return fixture.ExerciseHiddenSharedParticleLifecycle();
        }
        finally { fixture.TearDown(); }
        yield return new UnityEngine.TestTools.ExitPlayMode();
    }
}
