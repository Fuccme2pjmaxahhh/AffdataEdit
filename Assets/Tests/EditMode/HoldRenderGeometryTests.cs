using System;
using System.Diagnostics;
using System.Reflection;
using Arcade.Gameplay;
using NUnit.Framework;
using UnityEngine;

public class HoldRenderGeometryTests
{
    private static readonly Func<float, float, bool, bool, bool, bool, Vector2> Span =
        (Func<float, float, bool, bool, bool, bool, Vector2>)Delegate.CreateDelegate(
            typeof(Func<float, float, bool, bool, bool, bool, Vector2>),
            typeof(ArcHoldNoteManager).GetMethod("CalculateRenderSpan", BindingFlags.Static | BindingFlags.NonPublic));

    // Distances are measured from the judgement line, positive into the visible track.
    [TestCase(500, 1500, false, false, false, false, 500, 1500)]
    [TestCase(1500, 500, false, false, false, false, 500, 1500)]
    [TestCase(-1500, -500, false, false, false, false, -1500, -500)]
    [TestCase(-500, 1500, false, false, false, false, -500, 1500)]
    [TestCase(1500, 500, true, false, false, false, 0, 500)]
    [TestCase(-1500, -500, true, false, false, false, -500, 0)]
    [TestCase(500, -1500, true, false, false, false, -1500, 0)]
    [TestCase(-500, 1500, true, false, false, false, 0, 1500)]
    [TestCase(500, 1500, false, true, false, false, 500, 1500)]
    [TestCase(-500, 1500, false, true, false, false, 0, 1500)]
    [TestCase(-1500, -500, false, true, false, false, -500, 0)]
    [TestCase(1500, 500, true, false, true, false, 0, 0)]
    [TestCase(1500, 500, false, true, true, false, 0, 0)]
    [TestCase(-500, 1500, true, true, true, true, -500, 1500)]
    [TestCase(-200000, 200000, false, false, false, false, -100000, 100000)]
    public void SpanPreservesDirectionChangesAndClipsOnlyTheEffectiveHead(float head, float tail,
        bool judging, bool noInput, bool ended, bool editing, float near, float far)
    {
        Assert.AreEqual(new Vector2(near, far), Span(head, tail, judging, noInput, ended, editing));
    }

    [Test]
    public void ZeroBpmSpanIsEmptyAndOffscreenSpansStayCulled()
    {
        Assert.AreEqual(Vector2.zero, Span(0, 0, false, false, false, false));
        var behind = Span(-200000, -150000, false, false, false, false);
        var distant = Span(150000, 200000, false, false, false, false);
        Assert.Greater(behind.x, behind.y);
        Assert.Greater(distant.x, distant.y);
    }

    private static Vector2 PreviousSpan(float head, float tail, bool judging, bool noInput, bool ended, bool editing)
    {
        if (!editing && (judging || noInput)) head = 0;
        if (tail < head) { float swap = head; head = tail; tail = swap; }
        if (head > 100000 || tail < -100000) return new Vector2(1, 0);
        if (tail > 100000) tail = 100000;
        if (head < -100000) head = -100000;
        return new Vector2(head, tail);
    }

    [Test]
    public void RepeatedGeometryUpdatesAllocateNoManagedMemory()
    {
        const int count = 1000000;
        var previous = (Func<float, float, bool, bool, bool, bool, Vector2>)PreviousSpan;
        var oldTimes = new double[9]; var newTimes = new double[9];
        float sink = 0;
        for (int i = 0; i < 10000; i++) sink += Span(i % 2000 - 1000, 2000, (i & 1) == 0, false, false, false).y;
        long allocations = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < count; i++) sink += Span(i % 2000 - 1000, 2000, (i & 1) == 0, false, false, false).y;
        Assert.AreEqual(0, GC.GetAllocatedBytesForCurrentThread() - allocations);
        for (int round = 0; round < oldTimes.Length; round++)
        {
            for (int which = 0; which < 2; which++)
            {
                bool current = (round + which) % 2 == 0;
                var method = current ? Span : previous;
                long start = Stopwatch.GetTimestamp();
                for (int i = 0; i < count; i++) sink += method(i % 2000 - 1000, 2000, (i & 1) == 0, false, false, false).y;
                double ms = (Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency;
                if (current) newTimes[round] = ms; else oldTimes[round] = ms;
            }
        }
        Array.Sort(oldTimes); Array.Sort(newTimes);
        UnityEngine.Debug.Log($"HOLD_GEOMETRY_BENCH count={count} previousMs={oldTimes[4]:F3} currentMs={newTimes[4]:F3} allocatedBytes=0 sink={sink}");
    }
}
