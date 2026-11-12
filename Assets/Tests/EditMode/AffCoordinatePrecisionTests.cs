using System.Globalization;
using System.IO;
using System.Text;
using Arcade.Aff;
using Arcade.Compose;
using Arcade.Gameplay.Chart;
using Newtonsoft.Json;
using NUnit.Framework;
using System.Reflection;
using Arcade.Compose.Command;
using Arcade.Compose.Editing;
using Arcade.Compose.UI;
using UnityEngine;

public class AffCoordinatePrecisionTests
{
    private GameObject gameRoot;
    private Arcade.Gameplay.ArcGameplayManager previousGame;

    [SetUp]
    public void SetUp()
    {
        previousGame = Arcade.Gameplay.ArcGameplayManager.Instance;
        gameRoot = new GameObject("Chart serialization clock"); gameRoot.SetActive(false);
        typeof(Arcade.Gameplay.ArcGameplayManager).GetProperty("Instance")
            .SetValue(null, gameRoot.AddComponent<Arcade.Gameplay.ArcGameplayManager>());
    }

    [TearDown]
    public void TearDown()
    {
        Object.DestroyImmediate(gameRoot);
        typeof(Arcade.Gameplay.ArcGameplayManager).GetProperty("Instance").SetValue(null, previousGame);
    }

    [TestCase(.125f, false, "0.125")]
    [TestCase(.125f, true, "0.13")]
    [TestCase(-.125f, true, "-0.13")]
    [TestCase(.5f, false, "0.500")]
    [TestCase(.5f, true, "0.50")]
    [TestCase(1f, false, "1.000")]
    [TestCase(0f, true, "0.00")]
    [TestCase(-.0004f, false, "0.000")]
    [TestCase(-.004f, true, "0.00")]
    [TestCase(1.625f, true, "1.63")]
    public void CoordinatesKeepTheSelectedDecimalPlaces(float value, bool lowPrecision, string expected)
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
            Assert.AreEqual(expected, AffCoordinate.Format(value, lowPrecision));
            Assert.AreEqual(float.Parse(expected, CultureInfo.InvariantCulture), AffCoordinate.Round(value, lowPrecision));
        }
        finally { CultureInfo.CurrentCulture = previous; }
    }

    [Test]
    public void HighPrecisionIsTheDefaultAndLowPrecisionSurvivesSettingsReload()
    {
        var preference = JsonConvert.DeserializeObject<ArcadePreference>("{}");
        Assert.IsFalse(preference.LowPrecisionArcAndSlide);
        preference.LowPrecisionArcAndSlide = true;
        preference = JsonConvert.DeserializeObject<ArcadePreference>(JsonConvert.SerializeObject(preference));
        Assert.IsTrue(preference.LowPrecisionArcAndSlide);
    }

    [TestCase(false, ChartSortMode.Timing)]
    [TestCase(true, ChartSortMode.Timing)]
    [TestCase(false, ChartSortMode.Type)]
    [TestCase(true, ChartSortMode.Type)]
    public void SavingArcsSlidesAndTimingGroupsUsesTheSamePrecision(bool lowPrecision, ChartSortMode sort)
    {
        const string notes = "arc(1000,2000,0.125,0.500,s,0.500,1.000,0,none,false);\n"
            + "slide(1000,2000,0.125,0.500,1.625,0.050,0,0,1);\n";
        var chart = new ArcChart(AffParser.Parse("AudioOffset:0\n-\ntiming(0,120,4);\n" + notes + "timinggroup(){\n" + notes + "};\n"));
        using (var stream = new MemoryStream())
        {
            chart.Serialize(stream, sort, lowPrecision);
            string text = Encoding.UTF8.GetString(stream.ToArray());
            string start = lowPrecision ? "0.13" : "0.125";
            string end = lowPrecision ? "1.63" : "1.625";
            string half = lowPrecision ? "0.50" : "0.500";
            string one = lowPrecision ? "1.00" : "1.000";
            string width = lowPrecision ? "0.05" : "0.050";
            StringAssert.Contains($"arc(1000,2000,{start},{half},s,{half},{one},0,none,false)", text);
            StringAssert.Contains($"slide(1000,2000,{start},{half},{end},{width},0,0,1)", text);
            var raw = AffParser.Parse(text);
            Assert.That(raw.error, Is.Empty, text);
            Assert.That(raw.recoverableError, Is.Empty, text);
            var loaded = new ArcChart(raw);
            Assert.AreEqual(2, loaded.Arcs.Count);
            Assert.AreEqual(2, loaded.Slides.Count);
            foreach (var arc in loaded.Arcs) Assert.AreEqual(lowPrecision ? .13f : .125f, arc.XStart);
            foreach (var slide in loaded.Slides) Assert.AreEqual(lowPrecision ? .13f : .125f, slide.StartCenter);
            Assert.AreEqual(.125f, chart.Arcs[0].XStart, "Saving must not mutate the open chart.");
            Assert.AreEqual(.125f, chart.Slides[0].StartCenter);
        }
    }

    [Test]
    public void LowPrecisionDoesNotSaveAZeroWidthSlide()
    {
        var slide = new RawAffSlide { Timing = 0, EndTiming = 1000, StartCenter = .5f, EndCenter = .5f, StartWidth = .004f, EndWidth = .004f };
        Assert.DoesNotThrow(() => ArcaeaFileFormat.FormatItem(slide));
        Assert.Throws<InvalidDataException>(() => ArcaeaFileFormat.FormatItem(slide, true));
        Assert.AreEqual(.004f, slide.StartWidth);
    }

    [Test]
    public void TapWidthsStillKeepADecimalPointToDistinguishTheMineMarker()
    {
        var tap = new RawAffTap { Timing = 1000, Track = 2, Width = 4 };
        StringAssert.Contains(",4.00)", ArcaeaFileFormat.FormatItem(tap, true));
    }

    [TestCase(false, .125f, "0.125,0.500")]
    [TestCase(true, .13f, "0.13,0.50")]
    public void ArcInputAndSlideEditingUseTheSelectedPrecision(bool lowPrecision, float expectedX, string expectedRange)
    {
        var previousCompose = ArcadeComposeManager.Instance;
        var previousSelection = AdeSelectionManager.Instance;
        var previousCommands = AdeCommandManager.Instance;
        var root = new GameObject("Coordinate editing precision"); root.SetActive(false);
        const BindingFlags hidden = BindingFlags.Instance | BindingFlags.NonPublic;
        try
        {
            var compose = root.AddComponent<ArcadeComposeManager>();
            compose.ArcadePreference.LowPrecisionArcAndSlide = lowPrecision;
            typeof(ArcadeComposeManager).GetProperty("Instance").SetValue(null, compose);
            typeof(AdeSelectionManager).GetProperty("Instance").SetValue(null, root.AddComponent<AdeSelectionManager>());
            typeof(AdeCommandManager).GetProperty("Instance").SetValue(null, root.AddComponent<AdeCommandManager>());
            var editor = root.AddComponent<AdeValueEditor>();
            object[] input = { "0.125,0.500", Vector2.zero };
            Assert.IsNull(typeof(AdeValueEditor).GetMethod("ParseCoord", hidden).Invoke(editor, input));
            Assert.AreEqual(new Vector2(expectedX, .5f), (Vector2)input[1]);
            Assert.AreEqual(expectedRange, typeof(AdeSlideEditor).GetMethod("Range", BindingFlags.Static | BindingFlags.NonPublic)
                .Invoke(null, new object[] { .125f, .5f }));
            var slide = new ArcSlide();
            typeof(AdeSlideEditor).GetMethod("SetRangeEndpoints", BindingFlags.Static | BindingFlags.NonPublic)
                .Invoke(null, new object[] { slide, false, 0f, .25f });
            Assert.AreEqual(expectedX, slide.StartCenter);
        }
        finally
        {
            Object.DestroyImmediate(root);
            typeof(ArcadeComposeManager).GetProperty("Instance").SetValue(null, previousCompose);
            typeof(AdeSelectionManager).GetProperty("Instance").SetValue(null, previousSelection);
            typeof(AdeCommandManager).GetProperty("Instance").SetValue(null, previousCommands);
        }
    }
}
