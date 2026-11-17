using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using Arcade.Aff;
using Arcade.Gameplay;
using Arcade.Gameplay.Chart;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

public class DenseJudgementTests
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static readonly Func<ArcLongNote, int, double> Count =
        (Func<ArcLongNote, int, double>)Delegate.CreateDelegate(typeof(Func<ArcLongNote, int, double>),
            typeof(ArcLongNote).GetMethod("CountJudgementsAtOrBefore", Private));
    private static readonly Func<ArcLongNote, int, int, bool> HasTick =
        (Func<ArcLongNote, int, int, bool>)Delegate.CreateDelegate(typeof(Func<ArcLongNote, int, int, bool>),
            typeof(ArcLongNote).GetMethod("HasJudgementBetween", Private));
    private GameObject owner;
    private ArcGameplayManager game, previousGame;
    private ArcTimingManager timing, previousTiming;
    private ArcScoreManager score, previousScore;

    [SetUp]
    public void SetUp()
    {
        previousGame = ArcGameplayManager.Instance;
        previousTiming = ArcTimingManager.Instance;
        previousScore = ArcScoreManager.Instance;
        owner = new GameObject("Dense judgement tests"); owner.SetActive(false);
        game = owner.AddComponent<ArcGameplayManager>();
        timing = owner.AddComponent<ArcTimingManager>();
        score = owner.AddComponent<ArcScoreManager>();
        SetInstance(typeof(ArcGameplayManager), game);
        SetInstance(typeof(ArcTimingManager), timing);
        SetInstance(typeof(ArcScoreManager), score);
        game.Chart = new ArcChart(new RawAffChart()) { TimingPointDensityFactor = 1 };
        timing.Timings.Add(new ArcTiming { Bpm = 100 });
    }

    private static void SetInstance(Type type, object value) => type.GetProperty("Instance").SetValue(null, value);

    [TearDown]
    public void TearDown()
    {
        Object.DestroyImmediate(owner);
        SetInstance(typeof(ArcGameplayManager), previousGame);
        SetInstance(typeof(ArcTimingManager), previousTiming);
        SetInstance(typeof(ArcScoreManager), previousScore);
    }

    private void Calculate(ArcLongNote note, float bpm, float density = 1)
    {
        timing.Timings[0].Bpm = bpm;
        game.Chart.TimingPointDensityFactor = density;
        if (note is ArcHold hold) hold.CalculateJudgeTimings();
        else ((ArcArc)note).CalculateJudgeTimings();
    }

    // The previous materialized formula is the behavioral oracle, including
    // truncation toward zero, repeated timestamps and the midpoint fallback.
    private static List<int> Reference(ArcLongNote note, float bpm, float density)
    {
        var ticks = new List<int>();
        var arc = note as ArcArc;
        if (arc != null && (arc.IsVoid || arc.EndTiming == arc.Timing)) return ticks;
        double absoluteBpm = Math.Abs((double)bpm);
        if (arc != null && bpm == 0) { ticks.Add(note.Timing); return ticks; }
        double interval = 60000f / absoluteBpm / (absoluteBpm >= 255 ? 1 : 2) / density;
        int total = (int)((note.EndTiming - note.Timing) / interval);
        int first = arc == null || arc.RenderHead ? 1 : 0;
        if (first >= total)
        {
            ticks.Add((int)(note.Timing + (note.EndTiming - note.Timing) * .5f));
            return ticks;
        }
        for (int i = first; i < total; i++)
        {
            int t = (int)(note.Timing + i * interval);
            if (t < note.EndTiming) ticks.Add(t);
        }
        return ticks;
    }

    private static int UpperBound(List<int> ticks, int time)
    {
        int low = 0, high = ticks.Count;
        while (low < high)
        {
            int middle = low + (high - low) / 2;
            if (ticks[middle] <= time) low = middle + 1;
            else high = middle;
        }
        return low;
    }

    [TestCase(0f, 1f, -999, 4500)]
    [TestCase(-254f, 1f, -999, 4500)]
    [TestCase(255f, 1f, 101, 4500)]
    [TestCase(60000f, 1f, 0, 4096)]
    [TestCase(60000f, 1f, 0, 4097)]
    [TestCase(600000f, 1f, 0, 1000)]
    [TestCase(-600000f, .75f, -1001, 2001)]
    [TestCase(730001f, 1.33f, 2147480000, 3000)]
    [TestCase(730001f, 1.33f, -2147480000, 3000)]
    [TestCase(6000000f, .1f, -11, 70)]
    [TestCase(600000000f, 1f, -1, 1)]
    public void HoldAndArcTicksCountsAndEffectsMatchPreviousFormula(float bpm, float density, int start, int duration)
    {
        foreach (var note in new ArcLongNote[]
        {
            new ArcHold { Timing = start, EndTiming = start + duration },
            new ArcArc { Timing = start, EndTiming = start + duration, RenderHead = true, LineType = ArcLineType.FalseNotVoid },
            new ArcArc { Timing = start, EndTiming = start + duration, RenderHead = false, LineType = ArcLineType.FalseNotVoid }
        })
        {
            var expected = Reference(note, bpm, density);
            Calculate(note, bpm, density);
            Assert.AreEqual(expected.Count, note.JudgementCount);
            Assert.AreEqual(expected.Count == 0 ? start : expected[0], note.FirstJudgementTiming);
            if (note.JudgeTimings.Count > 0) Assert.That(note.JudgeTimings, Is.EqualTo(expected));
            for (int i = -1; i <= 1001; i++)
            {
                int t = start + (int)((long)duration * i / 1000);
                Assert.AreEqual(t < start ? 0 : UpperBound(expected, t), Count(note, t), $"{note.GetType().Name} at {t}");
                Assert.AreEqual(UpperBound(expected, t) > UpperBound(expected, t - 1), HasTick(note, t - 1, t));
                Assert.IsFalse(HasTick(note, t, t));
                Assert.IsFalse(HasTick(note, t, t - 1));
            }
        }
    }

    private object Field(string name) => typeof(ArcScoreManager).GetField(name, Private).GetValue(score);
    private object Call(string name, params object[] arguments) => typeof(ArcScoreManager).GetMethod(name, Private).Invoke(score, arguments);

    [Test]
    public void DenseScoreKeepsAllTicksWithoutFlatteningAndDropsReferencesAfterRebuild()
    {
        var hold = new ArcHold { Timing = -100, EndTiming = 1000 };
        var arc = new ArcArc { Timing = 100, EndTiming = 900, RenderHead = false, LineType = ArcLineType.FalseNotVoid };
        Calculate(hold, -600000); Calculate(arc, 600000);
        game.Chart.Holds.Add(hold); game.Chart.Arcs.Add(arc);
        game.Chart.Taps.Add(new ArcTap { Timing = 123 });
        arc.ArcTaps.Add(new ArcArcTap { Timing = 345 });
        var expected = Reference(hold, 600000, 1);
        expected.AddRange(Reference(arc, 600000, 1)); expected.Add(123); expected.Add(345); expected.Sort();
        Call("RebuildScoreCache", game.Chart);
        Assert.AreEqual(2, ((IList)Field("judgeTimings")).Count);
        Assert.AreEqual(2, ((IList)Field("denseNotes")).Count);
        for (int t = -200; t < 1200; t += 7)
        {
            int count = UpperBound(expected, t);
            Assert.AreEqual(count, (double)Call("CalculateCombo", t));
            Assert.AreEqual((int)Math.Round(count * (10000000d / expected.Count) + count), (int)Call("CalculateScore", t));
            Assert.AreEqual(count, (int)Field("combo"));
        }
        hold.TimingGroup = new ArcTimingGroup { NoInput = true };
        arc.LineType = ArcLineType.Designant;
        Call("RebuildScoreCache", game.Chart);
        Assert.AreEqual(0, ((IList)Field("denseNotes")).Count);
        Assert.AreEqual(1, (double)Call("CalculateCombo", 2000));
        Call("RebuildScoreCache", (object)null);
        Assert.AreEqual(0, ((IList)Field("denseNotes")).Count);
        Assert.AreEqual(0, ((IList)Field("judgeTimings")).Count);
    }

    [Test]
    public void RecalculationClearsDenseStateForNormalVoidAndZeroLengthNotes()
    {
        var hold = new ArcHold { Timing = 100, EndTiming = 10000 };
        hold.JudgeTimings.Capacity = 20000;
        Calculate(hold, 600000);
        Assert.AreEqual(0, hold.JudgeTimings.Capacity);
        Calculate(hold, 100);
        Assert.That(hold.JudgeTimings, Is.EqualTo(Reference(hold, 100, 1)));
        Assert.AreEqual(hold.JudgeTimings.Count, hold.JudgementCount);
        var arc = new ArcArc { Timing = 0, EndTiming = 10000, LineType = ArcLineType.FalseNotVoid };
        Calculate(arc, 600000);
        arc.LineType = ArcLineType.TrueIsVoid;
        Calculate(arc, 600000);
        Assert.AreEqual(0, arc.JudgementCount);
        arc.LineType = ArcLineType.FalseNotVoid; arc.EndTiming = arc.Timing;
        Calculate(arc, 600000);
        Assert.AreEqual(0, arc.JudgementCount);
        arc.EndTiming = 10000; Calculate(arc, 0);
        Assert.AreEqual(1, arc.JudgementCount); Assert.AreEqual(0, arc.FirstJudgementTiming);
    }

    [TestCase(6e10f)]
    [TestCase(float.MaxValue)]
    public void HugeFiniteBpmKeepsCountsFiniteAndDisplayedIntegersInRange(float bpm)
    {
        var hold = new ArcHold { Timing = 0, EndTiming = 10000 };
        Calculate(hold, bpm);
        game.Chart.Holds.Add(hold); Call("RebuildScoreCache", game.Chart);
        Assert.AreEqual(0, hold.JudgeTimings.Capacity);
        Assert.Greater(hold.JudgementCount, int.MaxValue);
        Assert.IsFalse(double.IsInfinity(hold.JudgementCount));
        double previous = 0;
        for (int t = -1; t <= 10001; t += 17)
        {
            double current = Count(hold, t);
            Assert.That(current, Is.InRange(previous, hold.JudgementCount));
            previous = current;
        }
        Assert.AreEqual(hold.JudgementCount, Count(hold, hold.EndTiming));
        Assert.AreEqual(int.MaxValue, (int)Call("CalculateScore", hold.EndTiming));
        Assert.AreEqual(int.MaxValue, (int)Field("combo"));
    }

    [Test]
    public void MillionTickConstructionAndRepeatedQueriesUseBoundedMemory()
    {
        long canaryStart = GC.GetAllocatedBytesForCurrentThread();
        GC.KeepAlive(new byte[8192]);
        bool allocationCounterSupported = GC.GetAllocatedBytesForCurrentThread() > canaryStart;
        var hold = new ArcHold { Timing = 0, EndTiming = 10000 };
        Calculate(hold, 6000000); // warm up before measuring
        long allocations = GC.GetAllocatedBytesForCurrentThread();
        long start = Stopwatch.GetTimestamp();
        Calculate(hold, 6000000);
        long buildBytes = GC.GetAllocatedBytesForCurrentThread() - allocations;
        double buildMs = (Stopwatch.GetTimestamp() - start) * 1000d / Stopwatch.Frequency;
        Assert.AreEqual(999999, hold.JudgementCount);
        Assert.AreEqual(0, hold.JudgeTimings.Capacity);
        if (allocationCounterSupported) Assert.Less(buildBytes, 1024);
        double sink = 0;
        for (int i = 0; i < 1000; i++) sink += Count(hold, i);
        allocations = GC.GetAllocatedBytesForCurrentThread(); start = Stopwatch.GetTimestamp();
        for (int i = 0; i < 100000; i++)
        {
            sink += Count(hold, i % 10000);
            if (HasTick(hold, i % 10000, i % 10000 + 1)) sink++;
        }
        long queryBytes = GC.GetAllocatedBytesForCurrentThread() - allocations;
        double queryMs = (Stopwatch.GetTimestamp() - start) * 1000d / Stopwatch.Frequency;
        if (allocationCounterSupported) Assert.AreEqual(0, queryBytes);
        start = Stopwatch.GetTimestamp();
        var previousTicks = Reference(hold, 6000000, 1);
        var previousScoreCache = new List<int>(previousTicks);
        double previousMs = (Stopwatch.GetTimestamp() - start) * 1000d / Stopwatch.Frequency;
        Assert.AreEqual(previousTicks.Count, hold.JudgementCount);
        game.Chart.Holds.Add(hold); Call("RebuildScoreCache", game.Chart);
        Assert.AreEqual(0, ((List<int>)Field("judgeTimings")).Capacity);
        long previousArrayBytes = 4L * (previousTicks.Capacity + previousScoreCache.Capacity);
        UnityEngine.Debug.Log($"DENSE_JUDGEMENT_BENCH ticks={hold.JudgementCount} buildMs={buildMs:F4} previousBuildAndCopyMs={previousMs:F3} previousTickArrayBytes={previousArrayBytes} currentTickArrayBytes=0 allocationCounterSupported={allocationCounterSupported} buildBytes={buildBytes} queries=100000 countAndEffectMs={queryMs:F3} queryBytes={queryBytes} sink={sink}");
    }
}
