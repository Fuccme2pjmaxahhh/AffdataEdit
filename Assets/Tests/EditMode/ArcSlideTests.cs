using System.Globalization;
using System.IO;
using System.Text;
using Arcade.Aff;
using Arcade.Gameplay.Chart;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using System.Text.RegularExpressions;
using System.Reflection;
using Arcade.Compose;
using Arcade.Compose.Editing;
using Arcade.Compose.UI;
using UnityEngine.EventSystems;

public class ArcSlideTests
{
    [Test]
    public void TapWidthDefaultsToOneAndSurvivesCloneAndAffRoundTrip()
    {
        Assert.That(new ArcadePreference().EnableTapWidthEditing, Is.False);
        var raw = Parse("(1000,2,0.500);\n(1500,3,1.750);");
        Assert.That(raw.error, Is.Empty);
        var chart = new ArcChart(raw);
        Assert.That(chart.Taps[0].Width, Is.EqualTo(.5f)); Assert.That(chart.Taps[1].Width, Is.EqualTo(1.75f));
        Assert.That(new ArcTap().Width, Is.EqualTo(1));
        var copied = (ArcTap)chart.Taps[1].Clone();
        Assert.That(copied.Width, Is.EqualTo(1.75f));
        Assert.That(copied.WorldX, Is.EqualTo(chart.Taps[1].WorldX));
        using (var stream = new MemoryStream())
        {
            ArcaeaFileFormat.DumpToStream(stream, raw);
            string saved = Encoding.UTF8.GetString(stream.ToArray()).Replace("\r\n", "\n");
            var again = Parse(saved.Substring(saved.IndexOf("-\n", System.StringComparison.Ordinal) + 2));
            Assert.That(again.error, Is.Empty);
            Assert.That(new ArcChart(again).Taps[1].Width, Is.EqualTo(1.75f));
        }
    }

    [TestCase(0, 0)]
    [TestCase(45, 33.14807f)]
    [TestCase(90, 90)]
    [TestCase(180, 180)]
    [TestCase(-45, -33.14807f)]
    public void LegacyRingUsesAlphaOptimizedAngles(float degree, float expected)
    {
        var method = typeof(AdeMarkingMenuManager).GetMethod("OptimizeRingDegree", BindingFlags.Static | BindingFlags.NonPublic);
        Assert.That((float)method.Invoke(null, new object[] { degree }), Is.EqualTo(expected).Within(.001));
    }

    [TestCase(0, "-0")]
    [TestCase(1, "-0")]
    [TestCase(2, "-0")]
    [TestCase(3, "123")]
    [TestCase(5, "123")]
    [TestCase(7, "0")]
    [TestCase(9, "456")]
    [TestCase(30, "456")]
    public void FirstClickSelectsNearestSmallNumericFragment(int index, string expected)
    {
        const string value = "-0.123,0.456";
        var method = typeof(AdeTextSelection).GetMethod("NearestFragment", BindingFlags.Static | BindingFlags.NonPublic);
        var range = (Vector2Int)method.Invoke(null, new object[] { value, index });
        Assert.That(value.Substring(range.x, range.y - range.x), Is.EqualTo(expected));
    }

    [Test]
    public void TimeDragSelectsNearestGridEvenOutsideNormalCaptureDistance()
    {
        var owner = new GameObject("Immediate time drag grid"); owner.SetActive(false);
        try
        {
            var grid = owner.AddComponent<AdeGridManager>(); grid.EnableBeatline = true;
            var type = typeof(AdeGridManager);
            var lineType = type.GetNestedType("VisibleBeatline", BindingFlags.NonPublic);
            var lines = (System.Collections.IList)type.GetField("visibleBeatlines", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(grid);
            foreach (var pair in new[] { new Vector2(100, -10), new Vector2(200, -30) })
            {
                var line = System.Activator.CreateInstance(lineType);
                lineType.GetField("Timing").SetValue(line, (int)pair.x); lineType.GetField("Position").SetValue(line, pair.y);
                lines.Add(line);
            }
            var snap = type.GetMethod("AttachDragTimingFromPos", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(snap.Invoke(grid, new object[] { -22f, 150 }), Is.EqualTo(200));
            grid.EnableBeatline = false;
            Assert.That(snap.Invoke(grid, new object[] { -22f, 150 }), Is.EqualTo(150));
        }
        finally { Object.DestroyImmediate(owner); }
    }

    [Test]
    public void PlayingDragCommandsKeepOtherNotesJudgedAndSupportUndo()
    {
        var owner = new GameObject("Live drag playback"); owner.SetActive(false);
        var property = typeof(Arcade.Gameplay.ArcGameplayManager).GetProperty("Instance");
        var previous = property.GetValue(null);
        try
        {
            var game = owner.AddComponent<Arcade.Gameplay.ArcGameplayManager>(); property.SetValue(null, game);
            var untouched = new ArcTap { Judged = true };
            game.Chart = new ArcChart(new RawAffChart()) { Taps = new System.Collections.Generic.List<ArcTap> { untouched } };
            game.IsPlaying = true;
            var slide = new ArcSlide { Timing = 1000, EndTiming = 2000 };
            var next = (ArcSlide)slide.Clone(); next.StartCenter = .7f;
            var command = new Arcade.Compose.Command.EditArcEventCommand(slide, next, true);
            command.Do();
            Assert.That(slide.StartCenter, Is.EqualTo(.7f)); Assert.That(untouched.Judged, Is.True); Assert.That(game.IsPlaying, Is.True);
            command.Undo();
            Assert.That(slide.StartCenter, Is.EqualTo(.5f)); Assert.That(untouched.Judged, Is.True); Assert.That(game.IsPlaying, Is.True);
        }
        finally { property.SetValue(null, previous); Object.DestroyImmediate(owner); }
    }

    [Test]
    public void CurveCycleSkipsCrossingShapeAndContinuesToNextValidCombination()
    {
        var slide = new ArcSlide { Timing = 0, EndTiming = 500, StartCenter = .5f, EndCenter = 0,
            StartWidth = .1f, EndWidth = .1f, LeftCurve = 2, RightCurve = 2 };
        var method = typeof(AdeEditorHotkeyCommands).GetMethod("TryEditValidSlideCurves", BindingFlags.Static | BindingFlags.NonPublic);
        Assert.That(method.Invoke(null, new object[] { "Arc Cycle Curve", slide }), Is.True);
        Assert.That(slide.LeftCurve, Is.EqualTo(3)); Assert.That(slide.RightCurve, Is.EqualTo(3));
        Assert.That(slide.IsValid, Is.True);
        slide.LeftCurve = 3; slide.RightCurve = 1;
        Assert.That(method.Invoke(null, new object[] { "Arc Cycle Curve", slide }), Is.False);
        Assert.That(slide.LeftCurve, Is.EqualTo(3)); Assert.That(slide.RightCurve, Is.EqualTo(1));
    }

    [Test]
    public void MovingSlideLightsNewTrianglesAtTheActualGlowTextureCoordinates()
    {
        var type = typeof(Arcade.Gameplay.ArcSlideJudgementGrid);
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var grid = new Arcade.Gameplay.ArcSlideJudgementGrid();
        var mesh = (Mesh)type.GetMethod("BuildMesh", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, null);
        try
        {
            type.GetField("mesh", flags).SetValue(grid, mesh);
            type.GetMethod("BuildArrays", flags).Invoke(grid, null);
            var update = type.GetMethod("UpdateOpacity", flags);
            grid.SetSkyRange(-.05f, .05f); update.Invoke(grid, new object[] { 12.3f });
            grid.SetSkyRange(.95f, 1.05f); update.Invoke(grid, new object[] { 12.31f });
            var opacity = (float[])type.GetField("opacity", flags).GetValue(grid);
            var vertices = mesh.vertices;
            int newLit = 0;
            for (int i = 0; i < opacity.Length; i++)
            {
                float x = (vertices[i * 3].x + vertices[i * 3 + 1].x + vertices[i * 3 + 2].x) / 3;
                float textureX = (x * 1.7f - .85f + 1) / 2;
                if (textureX >= .68f && textureX <= .72f && opacity[i] > .5f) newLit++;
            }
            Assert.That(newLit, Is.GreaterThan(0), "The new interval needs visible triangles immediately after it moves");
        }
        finally { grid.Dispose(); Object.DestroyImmediate(mesh); }
    }

    [Test]
    public void GroupMoveSlidesOnlyTranslateXAndPreserveHeightWidthAndCurves()
    {
        var source = new ArcSlide { Timing = 100, EndTiming = 500, StartCenter = .2f, EndCenter = .7f,
            StartWidth = .02f, EndWidth = .5f, LeftCurve = 2, RightCurve = 2, Height = SlideHeight.HighSky };
        var value = (ArcSlide)source.Clone();
        var move = typeof(AdeDragEdit).GetMethod("OffsetNote", BindingFlags.Static | BindingFlags.NonPublic);
        move.Invoke(null, new object[] { value, source, new Vector2(.25f, -.9f) });
        Assert.That(value.StartCenter, Is.EqualTo(.45f)); Assert.That(value.EndCenter, Is.EqualTo(.95f));
        float worldDelta = Arcade.Gameplay.ArcAlgorithm.ArcXToWorld(.75f) - Arcade.Gameplay.ArcAlgorithm.ArcXToWorld(.5f);
        Assert.That(value.WorldX(value.StartCenter) - source.WorldX(source.StartCenter), Is.EqualTo(worldDelta).Within(.00001));
        Assert.That(value.WorldX(value.EndCenter) - source.WorldX(source.EndCenter), Is.EqualTo(worldDelta).Within(.00001));
        Assert.That(value.Height, Is.EqualTo(source.Height)); Assert.That(value.StartWidth, Is.EqualTo(.02f));
        Assert.That(value.EndWidth, Is.EqualTo(.5f)); Assert.That(value.LeftCurve, Is.EqualTo(2));
        Assert.That(value.Timing, Is.EqualTo(100)); Assert.That(value.EndTiming, Is.EqualTo(500));
        move.Invoke(null, new object[] { value, source, Vector2.zero });
        Assert.That(value.StartCenter, Is.EqualTo(source.StartCenter));
    }

    [Test]
    public void GroupPositionMovePreservesShapesAndSingleArcTapHostWithoutAccumulatingOffset()
    {
        var source = new ArcArc { Timing = 100, EndTiming = 500, XStart = .1f, XEnd = .8f, YStart = .2f, YEnd = 1 };
        var single = new ArcArc { Timing = 200, EndTiming = 201, XStart = .5f, XEnd = .5f, YStart = 1, YEnd = 1, LineType = ArcLineType.TrueIsVoid };
        single.ArcTaps.Add(new ArcArcTap { Timing = 200 });
        var move = typeof(AdeDragEdit).GetMethod("OffsetNote", BindingFlags.Static | BindingFlags.NonPublic);
        foreach (var original in new[] { source, single })
        {
            var value = (ArcArc)original.Clone();
            for (int repeat = 0; repeat < 3; repeat++)
            {
                move.Invoke(null, new object[] { value, original, new Vector2(.25f, -.1f) });
                Assert.That(value.XStart, Is.EqualTo(original.XStart + .25f).Within(.000001));
                Assert.That(value.XEnd - value.XStart, Is.EqualTo(original.XEnd - original.XStart).Within(.000001));
                Assert.That(value.YStart, Is.EqualTo(original.YStart - .1f).Within(.000001));
                Assert.That(value.Timing, Is.EqualTo(original.Timing)); Assert.That(value.EndTiming, Is.EqualTo(original.EndTiming));
            }
            move.Invoke(null, new object[] { value, original, Vector2.zero });
            Assert.That(value.XStart, Is.EqualTo(original.XStart)); Assert.That(value.YEnd, Is.EqualTo(original.YEnd));
        }
    }

    [Test]
    public void EditingSlideShapeDoesNotResetPlaybackFeedbackClock()
    {
        var owner = new GameObject("Slide feedback editing test"); owner.SetActive(false);
        var property = typeof(Arcade.Gameplay.ArcSlideManager).GetProperty("Instance");
        var previous = property.GetValue(null);
        try
        {
            var manager = owner.AddComponent<Arcade.Gameplay.ArcSlideManager>(); property.SetValue(null, manager);
            var time = typeof(Arcade.Gameplay.ArcSlideManager).GetField("previousTime", BindingFlags.Instance | BindingFlags.NonPublic);
            var playing = typeof(Arcade.Gameplay.ArcSlideManager).GetField("previousPlaying", BindingFlags.Instance | BindingFlags.NonPublic);
            time.SetValue(manager, 1234); playing.SetValue(manager, true);
            new ArcSlide { Timing = 1000, EndTiming = 2000 }.Rebuild();
            Assert.That(time.GetValue(manager), Is.EqualTo(1234)); Assert.That(playing.GetValue(manager), Is.True);
        }
        finally { property.SetValue(null, previous); Object.DestroyImmediate(owner); }
    }

    [Test]
    public void HoldConversionPreservesWorldPositionAndGroundHeight()
    {
        var hold = new ArcHold { Timing = 100, EndTiming = 700, Track = 2 };
        Assert.That(Arcade.Compose.Command.ConvertArcToSlideCommand.TryCreate(hold, out var command), Is.True);
        var slides = (ArcSlide[])typeof(Arcade.Compose.Command.ConvertArcToSlideCommand).GetField("slides", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(command);
        var slide = slides[0];
        Assert.That(slide.WorldX(slide.StartCenter), Is.EqualTo(hold.WorldX).Within(.005));
        Assert.That(slide.Timing, Is.EqualTo(100)); Assert.That(slide.EndTiming, Is.EqualTo(700));
        Assert.That(slide.Height, Is.EqualTo(SlideHeight.Ground)); Assert.That(slide.StartWidth, Is.EqualTo(.5f));
    }

    [Test]
    public void SlidePickingRejectsEndpointsOutsideCameraOrRenderRange()
    {
        var owner = new GameObject("Slide picking camera");
        try
        {
            var camera = owner.AddComponent<Camera>();
            camera.transform.position = new Vector3(0, 0, 10); camera.transform.rotation = Quaternion.Euler(0, 180, 0);
            var visible = typeof(AdeDragEdit).GetMethod("IsVisibleSlideEndpoint", BindingFlags.Static | BindingFlags.NonPublic);
            bool Check(Vector3 point) => (bool)visible.Invoke(null, new object[] { camera, point });
            Assert.That(Check(new Vector3(0, 0, -10)), Is.True);
            Assert.That(Check(new Vector3(0, 0, -101)), Is.False);
            Assert.That(Check(new Vector3(0, 0, 11)), Is.False);
            Assert.That(Check(new Vector3(1000, 0, -10)), Is.False);
        }
        finally { Object.DestroyImmediate(owner); }
    }

    [TestCase(false)]
    [TestCase(true)]
    public void CollapsedPreviewCanReopenInEitherDirectionAcrossSeparateDrags(bool moveRight)
    {
        var owner = new GameObject("Collapsed slide check", typeof(RectTransform)); owner.SetActive(false);
        try
        {
            var preview = owner.AddComponent<AdeSlidePreview>();
            preview.rectTransform.sizeDelta = new Vector2(400, 112);
            preview.Note = new ArcSlide { Timing = 0, EndTiming = 1000, EndCenter = 1, EndWidth = .02f };
            var point = typeof(AdeSlidePreview).GetMethod("Point", BindingFlags.Instance | BindingFlags.NonPublic);
            Vector2 Position(float x) => (Vector2)point.Invoke(preview, new object[] { x, 1f });
            for (int repeat = 0; repeat < 3; repeat++)
            {
                var pointer = new PointerEventData(null) { position = Position(1) };
                preview.OnBeginDrag(pointer);
                pointer.position = Position(moveRight ? 1.3f : .7f); preview.OnDrag(pointer);
                var range = preview.Note.Range(1);
                Assert.That(moveRight ? range.x : range.y, Is.EqualTo(1).Within(.00001));
                pointer.position = Position(1); preview.OnDrag(pointer); preview.OnEndDrag(pointer);
                Assert.That(preview.Note.EndCenter, Is.EqualTo(1));
                Assert.That(preview.Note.EndWidth, Is.EqualTo(.02f));
            }
        }
        finally { Object.DestroyImmediate(owner); }
    }

    [Test]
    public void ParallelSlideChainsDoNotInterruptEachOther()
    {
        var a = new ArcSlide { Timing = 0, EndTiming = 500, StartCenter = 0, EndCenter = 0 };
        var b = new ArcSlide { Timing = 0, EndTiming = 500, StartCenter = 1, EndCenter = 1 };
        var c = new ArcSlide { Timing = 500, EndTiming = 1000, StartCenter = 0, EndCenter = 0 };
        var d = new ArcSlide { Timing = 500, EndTiming = 1000, StartCenter = 1, EndCenter = 1 };
        Arcade.Gameplay.ArcSlideManager.UpdateConnections(new[] { d, a, c, b });
        Assert.That(c.GroupRoot, Is.SameAs(a)); Assert.That(d.GroupRoot, Is.SameAs(b));
        Assert.That(a.IsGroupTail || b.IsGroupTail || c.IsGroupHead || d.IsGroupHead, Is.False);
    }

    [TestCase(-.2f, SlideHeight.Ground)]
    [TestCase(.3f, SlideHeight.Ground)]
    [TestCase(1f, SlideHeight.Sky)]
    [TestCase(1.5f, SlideHeight.HighSky)]
    public void SlideConversionChoosesNearestHeight(float y, SlideHeight expected)
    {
        var method = typeof(Arcade.Compose.Command.ConvertArcToSlideCommand).GetMethod("NearestHeight", BindingFlags.Static | BindingFlags.NonPublic);
        Assert.That(method.Invoke(null, new object[] { y }), Is.EqualTo(expected));
        Assert.That(new ArcSlide().StartWidth, Is.EqualTo(.5f));
    }

    [Test]
    public void CursorCoordinatesUseActualDragPoint()
    {
        var owner = new GameObject("Cursor point check"); owner.SetActive(false);
        try
        {
            var cursor = owner.AddComponent<AdeCursorManager>();
            var point = new Vector3(2, 5.5f, -10);
            typeof(AdeCursorManager).GetField("dragCursorPoint", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(cursor, point);
            Assert.That(cursor.AttachedWallPoint, Is.EqualTo(point));
            Assert.That(cursor.AttachedCoordinate.x, Is.EqualTo(Arcade.Gameplay.ArcAlgorithm.WorldXToArc(point.x)));
        }
        finally { Object.DestroyImmediate(owner); }
    }

    [Test]
    public void PasteTimeCorrectionUsesStrictTwoMillisecondThresholdAndWorksWithGridHidden()
    {
        var owner = new GameObject("Paste grid check"); owner.SetActive(false);
        try
        {
            var grid = owner.AddComponent<AdeGridManager>();
            var lines = new System.Collections.Generic.List<BeatlineProperty>
            {
                new BeatlineProperty { Timing = 100, ExactTiming = 100.4 },
                new BeatlineProperty { Timing = 500, ExactTiming = 500 }
            };
            typeof(AdeGridManager).GetField("beatlineTimings", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(grid, lines);
            var method = typeof(AdeGridManager).GetMethod("FixPasteTiming", BindingFlags.Instance | BindingFlags.NonPublic);
            int Snap(int time) => (int)method.Invoke(grid, new object[] { time });
            Assert.That(grid.EnableBeatline, Is.False);
            Assert.That(Snap(102), Is.EqualTo(100));
            Assert.That(Snap(98), Is.EqualTo(98));
            Assert.That(Snap(498), Is.EqualTo(498));
            Assert.That(Snap(499), Is.EqualTo(500));
            Assert.That(Newtonsoft.Json.JsonConvert.DeserializeObject<ArcadePreference>("{}").FixNotesTimeOnPaste, Is.True);
            var saved = Newtonsoft.Json.JsonConvert.SerializeObject(new ArcadePreference { FixNotesTimeOnPaste = false });
            Assert.That(Newtonsoft.Json.JsonConvert.DeserializeObject<ArcadePreference>(saved).FixNotesTimeOnPaste, Is.False);
        }
        finally { Object.DestroyImmediate(owner); }
    }

    [Test]
    public void PasteTimeCorrectionIncludesNestedTapsAndPreservesShortObjects()
    {
        var arc = new ArcArc { Timing = 101, EndTiming = 501 };
        var tap = new ArcArcTap { Timing = 101 };
        arc.ArcTaps.Add(tap);
        var hold = new ArcHold { Timing = 1001, EndTiming = 1002 };
        var slide = new ArcSlide { Timing = 1001, EndTiming = 1003 };
        var ground = new ArcTap { Timing = 101 };
        var notes = new System.Collections.Generic.List<ArcNote> { arc, tap, hold, slide, ground };
        var method = typeof(Arcade.Compose.Operation.AdeCopyOrCutPasteOperation).GetMethod("FixPastedNotesTime", BindingFlags.Static | BindingFlags.NonPublic);
        System.Func<int, int> snap = t => t == 101 ? 100 : t == 501 ? 500 : t >= 1001 ? 1000 : t;
        method.Invoke(null, new object[] { notes, snap });
        Assert.That(arc.Timing, Is.EqualTo(100)); Assert.That(arc.EndTiming, Is.EqualTo(500));
        Assert.That(tap.Timing, Is.EqualTo(100)); Assert.That(ground.Timing, Is.EqualTo(100));
        Assert.That(hold.Timing, Is.EqualTo(1001)); Assert.That(hold.EndTiming, Is.EqualTo(1002));
        Assert.That(slide.Timing, Is.EqualTo(1001)); Assert.That(slide.EndTiming, Is.EqualTo(1003));
        var single = new ArcArc { Timing = 101, EndTiming = 102 };
        single.ArcTaps.Add(new ArcArcTap { Timing = 101 });
        method.Invoke(null, new object[] { new System.Collections.Generic.List<ArcNote> { single }, snap });
        Assert.That(single.Timing, Is.EqualTo(100)); Assert.That(single.EndTiming, Is.EqualTo(101));
    }

    [TestCase(false, false)]
    [TestCase(false, true)]
    [TestCase(true, false)]
    [TestCase(true, true)]
    public void SlidePreviewCornersCanCoincideAndSeparateWithoutMovingTheOppositeCorner(bool end, bool right)
    {
        var owner = new GameObject("Slide preview check", typeof(RectTransform)); owner.SetActive(false);
        try
        {
            var preview = owner.AddComponent<AdeSlidePreview>();
            preview.rectTransform.sizeDelta = new Vector2(400, 112);
            preview.Note = new ArcSlide { Timing = 0, EndTiming = 1000, StartCenter = .5f, EndCenter = .5f, StartWidth = .4f, EndWidth = .4f };
            var point = typeof(AdeSlidePreview).GetMethod("Point", BindingFlags.Instance | BindingFlags.NonPublic);
            Vector2 Position(float x) => (Vector2)point.Invoke(preview, new object[] { x, end ? 1f : 0f });
            var pointer = new PointerEventData(null) { position = Position(right ? .7f : .3f) };
            preview.OnBeginDrag(pointer);
            pointer.position = Position(right ? .3f : .7f); preview.OnDrag(pointer);
            Assert.That(end ? preview.Note.EndCenter : preview.Note.StartCenter, Is.EqualTo(right ? .3f : .7f).Within(.00001f));
            Assert.That(end ? preview.Note.EndWidth : preview.Note.StartWidth, Is.EqualTo(.02f));
            pointer.position = Position(right ? .9f : .1f); preview.OnDrag(pointer);
            var range = preview.Note.Range(end ? 1 : 0);
            Assert.That(range.x, Is.EqualTo(right ? .3f : .1f).Within(.00001f));
            Assert.That(range.y, Is.EqualTo(right ? .9f : .7f).Within(.00001f));
            ArcSlide committed = null; preview.OnEdited = value => committed = value;
            preview.OnEndDrag(pointer);
            Assert.That(committed, Is.Not.Null);
            Assert.That(committed.IsValid, Is.True);
        }
        finally { Object.DestroyImmediate(owner); }
    }

    [Test]
    public void SlideEditingCurveShortcutsUseTheirOwnPairAndCycleRules()
    {
        var method = typeof(AdeEditorHotkeyCommands).GetMethod("EditSlideCurves", BindingFlags.Static | BindingFlags.NonPublic);
        int[] paired = { 3, 2, 1, 0 };
        for (int left = 0; left < 4; left++) for (int right = 0; right < 4; right++)
        {
            var slide = new ArcSlide { LeftCurve = left, RightCurve = right };
            method.Invoke(null, new object[] { "Arc Toggle Curve Pair", slide });
            Assert.That(slide.LeftCurve, Is.EqualTo(paired[left]));
            Assert.That(slide.RightCurve, Is.EqualTo(paired[right]));
            method.Invoke(null, new object[] { "Arc Toggle Curve Pair", slide });
            Assert.That(slide.LeftCurve, Is.EqualTo(left));
            Assert.That(slide.RightCurve, Is.EqualTo(right));
            if (left == right) continue;
            Assert.That(method.Invoke(null, new object[] { "Arc Cycle Curve", slide }), Is.False);
            Assert.That(slide.LeftCurve, Is.EqualTo(left));
            Assert.That(slide.RightCurve, Is.EqualTo(right));
        }
        var cycling = new ArcSlide();
        foreach (var pair in new[] { new Vector2Int(1, 1), new Vector2Int(2, 2), new Vector2Int(3, 3), new Vector2Int(0, 0), new Vector2Int(1, 1) })
        {
            Assert.That(method.Invoke(null, new object[] { "Arc Cycle Curve", cycling }), Is.True);
            Assert.That(cycling.LeftCurve, Is.EqualTo(pair.x));
            Assert.That(cycling.RightCurve, Is.EqualTo(pair.y));
        }
    }

    [TestCase(false, false)]
    [TestCase(false, true)]
    [TestCase(true, false)]
    [TestCase(true, true)]
    public void SlidePositionDragMovesOnlyChosenEdgeAndCanReopenCollapsedRange(bool end, bool right)
    {
        var owner = new GameObject("Slide drag check"); owner.SetActive(false);
        try
        {
            var drag = owner.AddComponent<AdeDragEdit>();
            var original = new ArcSlide { Timing = 0, EndTiming = 1000, StartCenter = .5f, EndCenter = .5f, StartWidth = .4f, EndWidth = .4f };
            var draft = (ArcSlide)original.Clone();
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            void Field(string name, object value) => typeof(AdeDragEdit).GetField(name, flags).SetValue(drag, value);
            Field("note", original); Field("draft", draft); Field("end", end); Field("right", right);
            Field("slideRange", original.Range(end ? 1 : 0));
            var apply = typeof(AdeDragEdit).GetMethod("ApplyCoordinate", flags);
            void Move(float x) => apply.Invoke(drag, new object[] { new Vector2(Arcade.Gameplay.ArcAlgorithm.WorldXToArc(original.WorldX(x)), 1) });
            Move(right ? .3f : .7f);
            Assert.That(end ? draft.EndCenter : draft.StartCenter, Is.EqualTo(right ? .3f : .7f).Within(.00001f));
            Assert.That(end ? draft.EndWidth : draft.StartWidth, Is.EqualTo(.02f));
            Move(right ? .9f : .1f);
            var range = draft.Range(end ? 1 : 0);
            Assert.That(range.x, Is.EqualTo(right ? .3f : .1f).Within(.00001f));
            Assert.That(range.y, Is.EqualTo(right ? .9f : .7f).Within(.00001f));
            Assert.That(draft.Range(end ? 0 : 1), Is.EqualTo(original.Range(end ? 0 : 1)));
            Assert.That(draft.Timing, Is.EqualTo(original.Timing));
            Assert.That(draft.EndTiming, Is.EqualTo(original.EndTiming));
            Assert.That(draft.IsValid, Is.True);
        }
        finally { Object.DestroyImmediate(owner); }
    }

    [Test]
    public void SlideEdgePickingUsesTheSameScreenDistanceAsZeroDurationArcs()
    {
        var owner = new GameObject("Slide endpoint camera");
        try
        {
            var camera = owner.AddComponent<Camera>();
            camera.pixelRect = new Rect(0, 0, 800, 600);
            var slide = new ArcSlide();
            var left = new Vector3(slide.WorldX(.2f), 0, 10);
            var right = new Vector3(slide.WorldX(.8f), 0, 10);
            var pick = typeof(AdeDragEdit).GetMethod("TryChooseScreenEndpoint", BindingFlags.Static | BindingFlags.NonPublic);
            foreach (bool chooseRight in new[] { false, true })
            {
                object[] args = { camera, (Vector2)camera.WorldToScreenPoint(chooseRight ? right : left), left, right, false };
                Assert.That(pick.Invoke(null, args), Is.True);
                Assert.That(args[4], Is.EqualTo(chooseRight));
            }
        }
        finally { Object.DestroyImmediate(owner); }
    }

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
    public void GroupContinuityUsesTimeToleranceWithRangeOverlap(int offset, bool connected)
    {
        var a = new ArcSlide { Timing = 0, EndTiming = 500, StartCenter = .15f, EndCenter = .15f, StartWidth = .1f, EndWidth = .1f };
        var b = new ArcSlide { Timing = 500 + offset, EndTiming = 1000, StartCenter = .15f, EndCenter = .15f, StartWidth = .1f, EndWidth = .1f };
        Arcade.Gameplay.ArcSlideManager.UpdateConnections(new[] { b, a });
        Assert.That(ReferenceEquals(a.GroupRoot, b.GroupRoot), Is.EqualTo(connected));
        Assert.That(a.IsGroupTail, Is.EqualTo(!connected));
        Assert.That(b.IsGroupHead, Is.EqualTo(!connected));
        if (connected) Assert.That(a.Overlap.z, Is.EqualTo(b.Range(0).x));
        else Assert.That(a.Overlap.z == 0 && a.Overlap.w == 1, Is.True);
    }

    [TestCase(.03f, false)] // Disjoint.
    [TestCase(.02f, false)] // Touching.
    [TestCase(.011f, false)] // 0.009 overlap.
    [TestCase(.01f, false)] // Exactly 0.01 overlap.
    [TestCase(.009f, true)] // 0.011 overlap.
    public void SlideContinuityRequiresOverlapStrictlyGreaterThanPointZeroOne(float center, bool connected)
    {
        var a = new ArcSlide { Timing = 0, EndTiming = 500,
            StartCenter = 0, EndCenter = 0, StartWidth = .02f, EndWidth = .02f };
        var b = new ArcSlide { Timing = 502, EndTiming = 1000,
            StartCenter = center, EndCenter = center, StartWidth = .02f, EndWidth = .02f };
        Arcade.Gameplay.ArcSlideManager.UpdateConnections(new[] { a, b });
        Assert.That(ReferenceEquals(a.GroupRoot, b.GroupRoot), Is.EqualTo(connected));
        Assert.That(a.IsGroupTail, Is.EqualTo(!connected));
        Assert.That(b.IsGroupHead, Is.EqualTo(!connected));
    }

    [TestCase(-2)]
    [TestCase(0)]
    [TestCase(2)]
    public void ToleratedSlideSeamsUseActualOverlapOutsideDefaultRange(int offset)
    {
        var a = new ArcSlide { Timing = 0, EndTiming = 500,
            StartCenter = -.5f, EndCenter = -.5f, StartWidth = 1, EndWidth = 1 };
        var b = new ArcSlide { Timing = 500 + offset, EndTiming = 1000,
            StartCenter = -.25f, EndCenter = -.25f, StartWidth = 1, EndWidth = 1 };
        Arcade.Gameplay.ArcSlideManager.UpdateConnections(new[] { a, b });
        Assert.That(b.GroupRoot, Is.SameAs(a));
        Assert.That(a.Overlap.z, Is.EqualTo(-.75f));
        Assert.That(a.Overlap.w, Is.EqualTo(0));
        Assert.That(b.Overlap.x, Is.EqualTo(a.Overlap.z));
        Assert.That(b.Overlap.y, Is.EqualTo(a.Overlap.w));
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
