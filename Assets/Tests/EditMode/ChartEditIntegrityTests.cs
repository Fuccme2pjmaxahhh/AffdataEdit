using System;
using System.IO;
using System.Linq;
using Arcade.Aff;
using Arcade.Compose;
using Arcade.Compose.Command;
using Arcade.Compose.Editing;
using Arcade.Gameplay;
using Arcade.Gameplay.Chart;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

public class ChartEditIntegrityTests
{
    [TestCase(false)]
    [TestCase(true)]
    public void UndoRestoresWhichBpmWinsAtTheSameMillisecond(bool delete)
    {
        var owner = new GameObject("Timing undo");
        owner.SetActive(false);
        var timings = typeof(ArcTimingManager).GetProperty("Instance");
        var editor = typeof(AdeTimingEditor).GetProperty("Instance");
        var originalTimings = timings.GetValue(null);
        var originalEditor = editor.GetValue(null);
        try
        {
            timings.SetValue(null, owner.AddComponent<ArcTimingManager>());
            editor.SetValue(null, owner.AddComponent<AdeTimingEditor>());
            var a = new ArcTiming { Timing = 100, Bpm = 120 };
            var b = new ArcTiming { Timing = 100, Bpm = 180 };
            var group = new ArcTimingGroup();
            group.Timings.AddRange(new[] { a, b });
            var moved = (ArcTiming)a.Clone();
            moved.Timing = 200;
            ICommand command = delete ? (ICommand)new RemoveTimingEvent(group, a) : new EditTimingEvent(group, a, moved);
            for (int i = 0; i < 2; i++)
            {
                command.Do();
                command.Undo();
                CollectionAssert.AreEqual(new[] { a, b }, group.Timings);
                Assert.That(ArcTimingManager.Instance.CalculateBpmByTiming(100, group), Is.EqualTo(180));
            }
        }
        finally
        {
            timings.SetValue(null, originalTimings);
            editor.SetValue(null, originalEditor);
            Object.DestroyImmediate(owner);
        }
    }

    [Test]
    public void HiddenTimingPanelDoesNotBuildRowsWhenAChartLoads()
    {
        var owner = new GameObject("Hidden Timing");
        owner.SetActive(false);
        try
        {
            var editor = owner.AddComponent<AdeTimingEditor>();
            editor.View = owner;
            // No dropdown or prefab is needed until the panel is shown.
            Assert.DoesNotThrow(editor.UpdateTiming);
        }
        finally { Object.DestroyImmediate(owner); }
    }

    [TestCase("NaN")]
    [TestCase("Infinity")]
    public void NonFiniteNumbersDoNotEnterTheChart(string invalid)
    {
        var raw = AffParser.Parse($"TimingPointDensityFactor:{invalid}\n-\ntiming(0,{invalid},4);timing(1,-120,0);");
        Assert.That(raw.TimingPointDensityFactor, Is.EqualTo(1));
        Assert.That(raw.items.OfType<RawAffTiming>().Single().Bpm, Is.EqualTo(-120));
        Assert.That(raw.warning, Is.Not.Empty);
        var timings = new[] { new ArcTiming { Bpm = float.PositiveInfinity }, new ArcTiming { Bpm = 180 } };
        Assert.That(AdeProjectManager.ResolveBaseBpm(float.NaN, timings), Is.EqualTo(180));
    }

    [TestCase("trackdisplay", SceneControlType.TrackDisplay)]
    [TestCase("hidegroup", SceneControlType.HideGroup)]
    [TestCase("enwidencamera", SceneControlType.EnwidenCamera)]
    [TestCase("enwidenlanes", SceneControlType.EnwidenLanes)]
    public void SceneControlNumericDurationSurvivesEditingAndSaving(string type, SceneControlType expected)
    {
        foreach (string duration in new[] { "2", "0.001" })
        {
            var chart = AffParser.Parse($"-\nscenecontrol(0,{type},{duration},1);");
            var control = new ArcSceneControl((RawAffSceneControl)chart.items.Single(), null);
            Assert.That(control.Type, Is.EqualTo(expected));
            var item = (RawAffSceneControl)((ArcSceneControl)control.Clone()).IntoRawItem();
            var reopened = AffParser.Parse("-\n" + ArcaeaFileFormat.FormatItem(item));
            var saved = (RawAffSceneControl)reopened.items.Single();
            Assert.That(((RawAffFloat)saved.Params[0]).data, Is.EqualTo(duration == "2" ? 2f : .001f));
        }
    }

    [Test]
    public void CameraAndMeasureKeepTheValuesEnteredInTheEditor()
    {
        var chart = AffParser.Parse("-\ntiming(0,125,0.125);camera(0,0.001,-0.000125,0,0,0,0,l,100);");
        using (var bytes = new MemoryStream())
        {
            ArcaeaFileFormat.DumpToStream(bytes, chart);
            var reopened = AffParser.Parse(System.Text.Encoding.UTF8.GetString(bytes.ToArray()));
            Assert.That(reopened.items.OfType<RawAffTiming>().Single().BeatsPerLine, Is.EqualTo(.125f));
            var camera = reopened.items.OfType<RawAffCamera>().Single();
            Assert.That(camera.MoveX, Is.EqualTo(.001f));
            Assert.That(camera.MoveY, Is.EqualTo(-.000125f));
        }
    }
}
