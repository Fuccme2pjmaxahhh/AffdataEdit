using System.Globalization;
using System.IO;
using System.Text;
using Arcade.Aff;
using Arcade.Gameplay.Chart;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using System.Text.RegularExpressions;

public class ArcSlideTests
{
    [TestCase("b")]
    [TestCase("3")]
    public void BezierSlideParsesAndMatchesArcHorizontalCurve(string curve)
    {
        var chart = Parse($"slide(0,1000,0.2,0.2,0.8,0.2,{curve},{curve});");
        Assert.That(chart.error, Is.Empty);
        var slide = new ArcChart(chart).Slides[0];
        Assert.That(slide.LeftCurve, Is.EqualTo(3));
        for (int i = 0; i <= 20; i++)
        {
            float t = i / 20f;
            var range = slide.Range(t);
            Assert.That((range.x + range.y) / 2,
                Is.EqualTo(Arcade.Gameplay.ArcAlgorithm.B(.2f, .8f, t)).Within(0.000001f));
        }
        var raw = (RawAffSlide)slide.IntoRawItem();
        Assert.That(raw.LeftCurve, Is.EqualTo(3));
        Assert.That(raw.RightCurve, Is.EqualTo(3));
    }

    [Test]
    public void MixedBezierEdgesRejectCrossing()
    {
        Assert.That(ArcSlide.ValidShape(.2f, .1f, .8f, .1f, 3, 3), Is.True);
        Assert.That(ArcSlide.ValidShape(.2f, .01f, .8f, .01f, 1, 3), Is.False);
        Assert.That(ArcSlide.ValidShape(.2f, .3f, .8f, .3f, 1, 3), Is.True);
    }

    [TestCase(1000, 990, 1010, true, true, true)]
    [TestCase(1000, 1000, 1010, true, true, false)]
    [TestCase(0, 0, 16, false, true, true)]
    [TestCase(1000, 990, 1010, false, false, false)]
    [TestCase(1000, 0, 2000, true, true, false)]
    [TestCase(1000, 1100, 900, true, true, false)]
    [TestCase(1000, 1100, 1116, false, true, false)]
    [TestCase(1000, int.MinValue, 1000, false, true, false)]
    public void SlideHeadAudioOnlyPlaysOnPlaybackCrossing(int start, int previous, int now,
        bool wasPlaying, bool playing, bool expected)
    {
        Assert.That(Arcade.Gameplay.ArcSlideManager.ShouldPlayHeadSound(start, previous, now, wasPlaying, playing),
            Is.EqualTo(expected));
    }

    private static RawAffChart Parse(string body)
    {
        string path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, "AudioOffset:0\n-\n" + body);
            return ArcaeaFileFormat.ParseFromPath(path);
        }
        finally { File.Delete(path); }
    }

    [Test]
    public void SlideRoundTripPreservesTimingGroupAndFractionalWidthsInAnyLocale()
    {
        var culture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
            var raw = Parse("timing(0,120,4);timinggroup(noinput){timing(0,240,4);slide(1000,2500,0.5,0.001,0.625,0.25,1,2);};");
            Assert.That(raw.error, Is.Empty);
            var chart = new ArcChart(raw);
            Assert.That(chart.Slides.Count, Is.EqualTo(1));
            Assert.That(chart.Slides[0].TimingGroup, Is.SameAs(chart.TimingGroups[0]));
            using (var stream = new MemoryStream())
            {
                ArcaeaFileFormat.DumpToStream(stream, raw);
                string text = Encoding.UTF8.GetString(stream.ToArray());
                // Parse adds its own header; retain only serialized body.
                var again = Parse(text.Substring(text.IndexOf("-\n", System.StringComparison.Ordinal) + 2));
                Assert.That(again.error, Is.Empty, text);
                var slide = new ArcChart(again).Slides[0];
                Assert.That(slide.StartWidth, Is.EqualTo(0.001f));
                Assert.That(slide.EndTiming, Is.EqualTo(2500));
                Assert.That(slide.LeftCurve, Is.EqualTo(1));
                Assert.That(slide.RightCurve, Is.EqualTo(2));
                Assert.That(slide.TimingGroup.NoInput, Is.True);
            }
        }
        finally { CultureInfo.CurrentCulture = culture; }
    }

    [TestCase("slide(0,500,0.1,0.2,0.9,0.2,1,2);")]
    [TestCase("slide(0,500,0.5,0.3,0.5,0.3,0,0,1);")]
    [TestCase("slide(0,1,0.5,0.3,0.5,0.3,0,0);")]
    [TestCase("slide(100,0,0.5,0.3,0.5,0.3,0,0);")]
    [TestCase("slide(0,500,0.1,0.5,0.5,0.3,0,0);")]
    [TestCase("slide(0,500,0.5,0,0.5,0.3,0,0);")]
    [TestCase("slide(0,500,0.5,0.3,0.5,0.3,3,0);")]
    [TestCase("slide(0,500,0.5,0.3,0.5,0.3,0);")]
    [TestCase("slide(-2147483648,2147483647,0.5,0.3,0.5,0.3,0,0);")]
    public void InvalidSlidesReportErrorsInsteadOfCreatingBrokenNotes(string text)
    {
        LogAssert.Expect(LogType.Error, new Regex("slide"));
        var chart = Parse(text);
        Assert.That(chart.error, Is.Not.Empty);
        Assert.That(new ArcChart(chart).Slides, Is.Empty);
    }

    [TestCase(true)]
    [TestCase(false)]
    public void FloorFlagRoundTripsAndSurvivesCloneAndAssign(bool floor)
    {
        var raw = Parse("slide(0,500,0.5,0.3,0.5,0.3,0,0," + (floor ? "true" : "false") + ");");
        Assert.That(raw.error, Is.Empty);
        var note = new ArcChart(raw).Slides[0];
        Assert.That(note.IsFloor, Is.EqualTo(floor));
        var target = new ArcSlide(); target.Assign(note.Clone());
        Assert.That(target.IsFloor, Is.EqualTo(floor));
        using (var stream = new MemoryStream())
        {
            ArcaeaFileFormat.DumpToStream(stream, raw);
            string text = Encoding.UTF8.GetString(stream.ToArray());
            var again = Parse(text.Substring(text.IndexOf("-\n", System.StringComparison.Ordinal) + 2));
            Assert.That(again.error, Is.Empty);
            Assert.That(new ArcChart(again).Slides[0].IsFloor, Is.EqualTo(floor));
        }
    }

    [Test]
    public void LegacySlidesStayInSkyAndMirroringPreservesHorizontalGeometry()
    {
        var note = new ArcChart(Parse("slide(0,500,0.5,0.3,0.5,0.3,0,0);")).Slides[0];
        Assert.That(note.IsFloor, Is.False);
        Assert.That(note.WorldHeight, Is.EqualTo(5.5f));
        note.IsFloor = true;
        Assert.That(note.WorldHeight, Is.EqualTo(0).Within(0.000001f));
        Assert.That(note.WorldX(.125f), Is.EqualTo(3.1875f));
        Assert.That(note.WorldX(.875f), Is.EqualTo(-3.1875f));
        var next = (ArcSlide)note.Clone(); next.Timing = 500; next.EndTiming = 1000; next.IsFloor = false;
        Arcade.Gameplay.ArcSlideManager.UpdateConnections(new[] { note, next });
        Assert.That(note.IsGroupTail, Is.True);
        Assert.That(next.IsGroupHead, Is.True);
    }

    [Test]
    public void SlideCoordinatesRoundToThreeDecimalsBeforeAndAfterSaving()
    {
        var raw = new RawAffChart();
        raw.items.Add(new RawAffSlide { Timing = 0, EndTiming = 500, StartCenter = -.1875f, StartWidth = 1f / 3,
            EndCenter = 1.98765f, EndWidth = .12345f, LeftCurve = 0, RightCurve = 0 });
        var original = new ArcChart(raw).Slides[0];
        Assert.That(original.StartCenter, Is.EqualTo(-.188f));
        Assert.That(original.EndCenter, Is.EqualTo(1.988f));
        using (var stream = new MemoryStream())
        {
            ArcaeaFileFormat.DumpToStream(stream, raw);
            string text = Encoding.UTF8.GetString(stream.ToArray());
            var parsed = Parse(text.Substring(text.IndexOf("-\n", System.StringComparison.Ordinal) + 2));
            Assert.That(parsed.error, Is.Empty, text);
            var slide = new ArcChart(parsed).Slides[0];
            Assert.That(slide.StartCenter, Is.EqualTo(original.StartCenter));
            Assert.That(slide.EndCenter, Is.EqualTo(original.EndCenter));
            Assert.That(slide.StartWidth, Is.EqualTo(.333f));
            Assert.That(slide.EndWidth, Is.EqualTo(.123f));
        }
    }

    [TestCase(-.188f, .625f)]
    [TestCase(-.063f, .875f)]
    [TestCase(1.688f, .625f)]
    [TestCase(-2f, 4f)]
    public void SlidesAcceptCoordinatesOutsideTheFormerBounds(float center, float width)
    {
        string range = center.ToString(CultureInfo.InvariantCulture) + "," + width.ToString(CultureInfo.InvariantCulture);
        var raw = Parse($"slide(0,500,{range},{range},0,0);");
        Assert.That(raw.error, Is.Empty);
        var note = new ArcChart(raw).Slides[0];
        Assert.That(note.IsValid, Is.True);
        note.StartCenter = -2.12345f;
        Assert.That(note.StartCenter, Is.EqualTo(-2.123f));
        Assert.That(((ArcSlide)note.Clone()).StartCenter, Is.EqualTo(note.StartCenter));
    }

    [Test]
    public void ConnectedSlidesJoinOnlyAtOverlappingBoundariesInTheSameTimingGroup()
    {
        var a = new ArcSlide { Timing = 0, EndTiming = 500 };
        var b = new ArcSlide { Timing = 500, EndTiming = 1000 };
        var c = new ArcSlide { Timing = 1000, EndTiming = 1500, TimingGroup = new ArcTimingGroup() };
        Arcade.Gameplay.ArcSlideManager.UpdateConnections(new[] { a, b, c });
        Assert.That(a.IsGroupHead, Is.True);
        Assert.That(a.IsGroupTail, Is.False);
        Assert.That(b.IsGroupHead, Is.False);
        Assert.That(b.IsGroupTail, Is.True);
        Assert.That(c.IsGroupHead, Is.True);
        Assert.That(a.Overlap.z, Is.EqualTo(b.Range(0).x));
        Arcade.Gameplay.ArcSlideManager.UpdateConnections(new[] { b, c });
        Assert.That(b.IsGroupHead, Is.True);
    }

    [TestCase(-2, true)]
    [TestCase(0, true)]
    [TestCase(2, true)]
    [TestCase(3, false)]
    public void GroupContinuityUsesTimeToleranceWithoutRequiringRangeOverlap(int offset, bool connected)
    {
        var a = new ArcSlide { Timing = 0, EndTiming = 500, StartCenter = .15f, EndCenter = .15f, StartWidth = .1f, EndWidth = .1f };
        var b = new ArcSlide { Timing = 500 + offset, EndTiming = 1000, StartCenter = .85f, EndCenter = .85f, StartWidth = .1f, EndWidth = .1f };
        Arcade.Gameplay.ArcSlideManager.UpdateConnections(new[] { b, a });
        Assert.That(ReferenceEquals(a.GroupRoot, b.GroupRoot), Is.EqualTo(connected));
        Assert.That(a.IsGroupTail, Is.EqualTo(!connected));
        Assert.That(b.IsGroupHead, Is.EqualTo(!connected));
        if (offset == 0) Assert.That(a.Overlap.z == 0 && a.Overlap.w == 0, Is.True);
        else Assert.That(a.Overlap.z == 0 && a.Overlap.w == 1, Is.True);
    }

    [Test]
    public void EditingMiddleSegmentRebuildsGroupsAndKeepsPlanesSeparate()
    {
        var a = new ArcSlide { Timing = 0, EndTiming = 500 };
        var b = new ArcSlide { Timing = 500, EndTiming = 1000 };
        var c = new ArcSlide { Timing = 1000, EndTiming = 1500 };
        var floor = new ArcSlide { Timing = 500, EndTiming = 1000, IsFloor = true };
        var notes = new[] { c, floor, b, a };
        Arcade.Gameplay.ArcSlideManager.UpdateConnections(notes);
        Assert.That(c.GroupRoot, Is.SameAs(a));
        Assert.That(floor.GroupRoot, Is.SameAs(floor));
        Assert.That(b.IsGroupHead || b.IsGroupTail, Is.False);
        b.EndTiming = 990;
        Arcade.Gameplay.ArcSlideManager.UpdateConnections(notes);
        Assert.That(c.GroupRoot, Is.SameAs(c));
        Assert.That(b.IsGroupTail, Is.True);
        b.EndTiming = 1000;
        Arcade.Gameplay.ArcSlideManager.UpdateConnections(notes);
        Assert.That(c.GroupRoot, Is.SameAs(a));
    }

    [Test]
    public void SlideJudgementsIncludeTailAndRewindWithoutCountingGroupHeadsTwice()
    {
        var a = new ArcSlide { Timing = 1000, EndTiming = 1500 };
        var b = new ArcSlide { Timing = 1500, EndTiming = 2000 };
        Arcade.Gameplay.ArcSlideManager.UpdateConnections(new[] { a, b });
        Assert.That(a.CountJudgements(1249, 120), Is.Zero);
        Assert.That(a.CountJudgements(1250, 120), Is.EqualTo(1));
        Assert.That(a.CountJudgements(1500, 120) + b.CountJudgements(1500, 120), Is.EqualTo(2));
        Assert.That(a.CountJudgements(2000, 120) + b.CountJudgements(2000, 120), Is.EqualTo(4));
        Assert.That(a.CountJudgements(1000, 120), Is.Zero);
        b.IsFloor = true;
        Assert.That(b.CountJudgements(2000, 120), Is.EqualTo(2));
        b.TimingGroup = new ArcTimingGroup { NoInput = true };
        Assert.That(b.CountJudgements(2000, 120), Is.Zero);
    }

    [TestCase(501, 120, 1, 2)]
    [TestCase(502, 120, 1, 3)]
    [TestCase(500, 240, 1, 4)]
    [TestCase(500, 300, 1, 3)]
    [TestCase(500, -120, 1, 2)]
    [TestCase(500, 0, 1, 1)]
    [TestCase(500, 120, 2, 4)]
    public void SlideJudgementDensityAndTailFollowSourceRules(int duration, double bpm, double density, int expected)
    {
        var slide = new ArcSlide { Timing = 0, EndTiming = duration };
        Assert.That(slide.CountJudgements(duration, bpm, density), Is.EqualTo(expected));
    }

    [Test]
    public void IndependentEdgesMatchSourceCurvesAndCloneRetainsProperties()
    {
        var group = new ArcTimingGroup();
        var note = new ArcSlide { Timing = 100, EndTiming = 600, StartCenter = .25f,
            StartWidth = .5f, EndCenter = .75f, EndWidth = .5f, LeftCurve = 1, RightCurve = 2, TimingGroup = group };
        Assert.That(note.Range(0).x, Is.EqualTo(0));
        Assert.That(note.Range(1).y, Is.EqualTo(1));
        Assert.That(note.Range(.5f).x, Is.EqualTo(.3535534f).Within(.00001));
        Assert.That(note.Range(.5f).y, Is.EqualTo(.6464466f).Within(.00001));
        var clone = (ArcSlide)note.Clone();
        clone.EndCenter = .5f;
        Assert.That(note.EndCenter, Is.EqualTo(.75f));
        note.Assign(clone);
        Assert.That(note.EndCenter, Is.EqualTo(.5f));
        Assert.That(note.TimingGroup, Is.SameAs(group));
        Assert.That(note.LeftCurve, Is.EqualTo(1));
        Assert.That(note.EndTiming, Is.EqualTo(600));
    }
}
