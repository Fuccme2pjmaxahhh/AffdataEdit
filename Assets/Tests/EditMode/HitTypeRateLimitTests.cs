using System;
using System.Reflection;
using Arcade.Gameplay;
using NUnit.Framework;

public class HitTypeRateLimitTests
{
    private object limiter;
    private MethodInfo allow;

    [SetUp]
    public void SetUp()
    {
        Type effects = typeof(ArcGameplayManager).Assembly.GetType("Arcade.Gameplay.ArcHitTypeEffects", true);
        Type type = effects.GetNestedType("PureHitLimiter", BindingFlags.NonPublic);
        limiter = Activator.CreateInstance(type, true);
        allow = type.GetMethod("Allow", BindingFlags.Instance | BindingFlags.Public);
    }

    private bool Allow(int frame, float x = 0, float y = 0)
        => (bool)allow.Invoke(limiter, new object[] { frame, x, y });

    private void Exhaust(int frame, float x = 0, float y = 0)
    {
        for (int i = 0; i < 10; i++) Assert.That(Allow(frame, x, y), Is.True, "Hit " + (i + 1));
        Assert.That(Allow(frame, x, y), Is.False);
    }

    [Test]
    public void SamePositionAllowsTenHitsAndResetsOnNextFrame()
    {
        Exhaust(100);
        Assert.That(Allow(100), Is.False);
        Exhaust(101);
    }

    [Test]
    public void NearbyPositionsShareTheHalfUnitCellIncludingNegativeCoordinates()
    {
        Exhaust(100);
        Assert.That(Allow(100, -.249f, .249f), Is.False);
        Assert.That(Allow(100, .249f, -.249f), Is.False);
        Assert.That(Allow(100, .25f), Is.True);
        Assert.That(Allow(100, -.251f), Is.True);
        Assert.That(Allow(100, 0, .25f), Is.True);
        Assert.That(Allow(100, 0, -.251f), Is.True);
    }

    [Test]
    public void PositionsThatHashToTheSameSlotKeepSeparateQuotas()
    {
        Exhaust(100);
        // Half-unit cells 0 and 1024 share a slot in the fixed 1024-entry table.
        Exhaust(100, 512);
        Assert.That(Allow(100, 1), Is.True);
    }

    [Test]
    public void SymmetricPositionsWithTheSameHashKeepSeparateQuotas()
    {
        // The signed cell pairs (1, 1)/(-1, -1) and (1, -1)/(-1, 1)
        // collide in the spatial hash, but each position still owns ten hits.
        Exhaust(100, .5f, .5f);
        Exhaust(100, -.5f, -.5f);
        Exhaust(100, .5f, -.5f);
        Exhaust(100, -.5f, .5f);
    }

    [Test]
    public void FullTableRejectsNewPositionsAndRecoversOnNextFrame()
    {
        for (int i = 0; i < 1024; i++) Assert.That(Allow(100, i * .5f), Is.True);
        Assert.That(Allow(100, 1024 * .5f), Is.False);
        Assert.That(Allow(100, 0), Is.False);
        Exhaust(101, 1024 * .5f);
        Assert.That(Allow(101), Is.True);
    }
}
