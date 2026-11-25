using System.Reflection;
using Arcade.Gameplay;
using Arcade.Compose;
using Arcade.Gameplay.Chart;
using NUnit.Framework;
using UnityEngine;

public class ArcTimingVelocityTests
{
    [TestCase(100)]
    [TestCase(200)]
    [TestCase(300)]
    public void UnconfiguredChartUsesItsOwnBpmAsSpeedReference(float bpm)
    {
        var go = new GameObject("Automatic base BPM test");
        go.SetActive(false);
        try
        {
            var manager = go.AddComponent<ArcTimingManager>();
            typeof(ArcTimingManager).GetField("settingVelocity", BindingFlags.NonPublic | BindingFlags.Instance)
                .SetValue(manager, 30); // Test automatic BPM at a fixed 1.0 scroll speed.
            manager.Timings.Add(new ArcTiming { Timing = 0, Bpm = bpm });
            manager.BaseBpm = AdeProjectManager.ResolveBaseBpm(0, manager.Timings);
            Assert.That(manager.CalculatePositionByTimingAndStart(0, 1000, null), Is.EqualTo(30000).Within(0.01f));
        }
        finally { Object.DestroyImmediate(go); }
    }

    [Test]
    public void AutomaticBaseBpmSkipsStopsAndReverseTimingsAndPreservesExplicitValues()
    {
        var timings = new[] {
            new ArcTiming { Timing = 300, Bpm = 240 },
            new ArcTiming { Timing = 0, Bpm = 0 },
            new ArcTiming { Timing = 100, Bpm = -120 },
            new ArcTiming { Timing = 200, Bpm = 180 }
        };
        Assert.That(AdeProjectManager.ResolveBaseBpm(0, timings), Is.EqualTo(180));
        Assert.That(AdeProjectManager.ResolveBaseBpm(150, timings), Is.EqualTo(150));
        Assert.That(AdeProjectManager.ResolveBaseBpm(0, null), Is.EqualTo(100));
    }

    [TestCase(30, 100, 30000)]
    [TestCase(31, 100, 31000)]
    [TestCase(32, 100, 32000)]
    [TestCase(195, 100, 195000)]
    [TestCase(30, 200, 60000)]
    [TestCase(30, -100, -30000)]
    public void OneSecondTravelMatchesAlpha(int setting, float bpm, float expected)
    {
        var go = new GameObject("Timing velocity test");
        go.SetActive(false);
        try
        {
            var manager = go.AddComponent<ArcTimingManager>();
            // Avoid the UI/rebuild side effects of the public setting setter.
            typeof(ArcTimingManager).GetField("settingVelocity", BindingFlags.NonPublic | BindingFlags.Instance)
                .SetValue(manager, setting);
            manager.BaseBpm = 100;
            manager.Timings.Add(new ArcTiming { Timing = 0, Bpm = bpm });
            Assert.That(manager.CalculatePositionByTimingAndStart(0, 1000, null), Is.EqualTo(expected).Within(0.01f));
            Assert.That(manager.CalculatePositionByTimingAndStart(1000, 0, null), Is.EqualTo(-expected).Within(0.01f));
        }
        finally
        {
            Object.DestroyImmediate(go);
        }
    }
}
