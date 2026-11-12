using System.Collections.Generic;
using System.Reflection;
using Arcade.Gameplay;
using Arcade.Gameplay.Chart;
using NUnit.Framework;

public class ArcGroupAppearanceTests
{
    private const BindingFlags StaticMembers = BindingFlags.Static | BindingFlags.NonPublic;
    private static readonly MethodInfo Calculate = typeof(ArcArcManager).GetMethod("CalculateArcGroups", StaticMembers);
    private static readonly MethodInfo Alpha = typeof(ArcArcManager).GetMethod("NonJudgingArcAlpha", StaticMembers);
    private static readonly FieldInfo Begin = typeof(ArcArc).GetField("RenderingGroupBeginTiming", BindingFlags.Instance | BindingFlags.NonPublic);

    private static ArcArc Arc(int start, int end, float from, float to)
        => new ArcArc {
            Timing = start, EndTiming = end, XStart = from, XEnd = to, YStart = .01f, YEnd = .01f,
            CurveType = ArcCurveType.S, LineType = ArcLineType.FalseNotVoid,
            TimingGroup = new ArcTimingGroup { NoInput = true }
        };

    private static void Group(params ArcArc[] arcs) => Calculate.Invoke(null, new object[] { new List<ArcArc>(arcs) });
    private static int GroupBegin(ArcArc arc) => (int)Begin.GetValue(arc);
    private static float At(ArcArc arc, int now) => (float)Alpha.Invoke(null, new object[] { arc, now });

    [Test]
    public void MergingBranchesRetainsEachBranchAppearanceOriginWithoutSplittingJudgementGroup()
    {
        var early = Arc(100, 300, 0, .5f);
        var lead = Arc(200, 201, 1, 1);
        var branch = Arc(201, 300, 1, .5f);
        var shared = Arc(300, 500, .5f, .8f);
        Group(early, lead, branch, shared);
        AssertBranchOrigins(early, lead, branch, shared);
        // Editing insertion order must not change the timing-sorted connection result.
        Group(shared, branch, lead, early);
        AssertBranchOrigins(early, lead, branch, shared);
        Assert.That(At(branch, 200), Is.EqualTo(1));
        Assert.That(At(branch, 201), Is.EqualTo(.65f));
    }

    private static void AssertBranchOrigins(ArcArc early, ArcArc lead, ArcArc branch, ArcArc shared)
    {
        Assert.That(early.ArcGroup, Is.SameAs(branch.ArcGroup));
        Assert.That(early.ArcGroup, Is.SameAs(shared.ArcGroup));
        Assert.That(early.ArcGroup.Count, Is.EqualTo(4));
        Assert.That(GroupBegin(early), Is.EqualTo(100));
        Assert.That(GroupBegin(lead), Is.EqualTo(200));
        Assert.That(GroupBegin(branch), Is.EqualTo(200));
        Assert.That(GroupBegin(shared), Is.EqualTo(100));
    }

    [Test]
    public void DirectChainDimsTogetherAfterItsStartWhileAnIsolatedArcRemainsUndimmed()
    {
        var first = Arc(100, 200, 0, .5f);
        var second = Arc(200, 300, .5f, 1);
        var isolated = Arc(400, 500, 2, 2);
        Group(second, isolated, first);
        Assert.That(GroupBegin(second), Is.EqualTo(100));
        Assert.That(At(first, 100), Is.EqualTo(1));
        Assert.That(At(second, 100), Is.EqualTo(1));
        Assert.That(At(first, 101), Is.EqualTo(.65f));
        Assert.That(At(second, 101), Is.EqualTo(.65f));
        Assert.That(At(isolated, 1000), Is.EqualTo(1));
    }

    [Test]
    public void IsolatedReverseDurationCachesItsEarlierEndpoint()
    {
        var reverse = Arc(200, 100, 0, 0);
        Group(reverse);
        Assert.That(GroupBegin(reverse), Is.EqualTo(100));
        Assert.That(At(reverse, 500), Is.EqualTo(1));
    }

    [TestCase(127708, 1f)]
    [TestCase(128456, 1f)]
    [TestCase(128457, .65f)]
    public void OneMillisecondLeadKeepsTheBlueBranchBrightUntilTheActualBoundary(int now, float expected)
    {
        // Minimal reproduction of two branches converging after a one-millisecond blue lead.
        var earlierRed = Arc(126958, 128458, -.5f, 1.5f);
        earlierRed.Color = 1;
        var blueLead = Arc(128456, 128457, -.5f, -.5f);
        var blue = Arc(128457, 129957, -.5f, .5f);
        blue.CurveType = ArcCurveType.Si;
        var red = Arc(128457, 129957, 1.5f, .5f);
        red.Color = 1;
        var shared = Arc(129957, 132582, .5f, .5f);
        Group(earlierRed, blueLead, blue, red, shared);
        Assert.That(blue.ArcGroup, Is.SameAs(earlierRed.ArcGroup));
        Assert.That(GroupBegin(earlierRed), Is.EqualTo(126958));
        Assert.That(GroupBegin(blue), Is.EqualTo(128456));
        Assert.That(At(blue, now), Is.EqualTo(expected));
    }
}
