using Arcade.Compose;
using Arcade.Compose.Editing;
using Arcade.Gameplay.Chart;
using NUnit.Framework;
using UnityEngine;

public class AdeSelectionVolumeTests
{
    private static ArcArc Arc(float x0, float y0, float x1, float y1, ArcCurveType curve = ArcCurveType.S) => new ArcArc
    { Timing = 0, EndTiming = 1000, XStart = x0, YStart = y0, XEnd = x1, YEnd = y1, CurveType = curve };

    [Test] public void ArcCanIntersectInMiddleWithBothEndpointsOutside()
    {
        Assert.IsTrue(AdeSelectionVolume.Intersects(Arc(0, 0, 1, 1), 0, 1000, Rect.MinMaxRect(.4f, .4f, .6f, .6f)));
    }
    [Test] public void LooseBoundingBoxAndWrongTimeDoNotCauseFalseSelection()
    {
        var arc = Arc(0, 0, 1, 1);
        Assert.IsFalse(AdeSelectionVolume.Intersects(arc, 0, 1000, Rect.MinMaxRect(.1f, .8f, .2f, .9f)));
        Assert.IsFalse(AdeSelectionVolume.Intersects(arc, 0, 100, Rect.MinMaxRect(.4f, .4f, .6f, .6f)));
    }
    [Test] public void CurvedPathAndReversedTimeBoundsAreSupported()
    {
        var arc = Arc(0, 0, 1, 1, ArcCurveType.SiSo);
        Assert.IsTrue(AdeSelectionVolume.Intersects(arc, 600, 400, Rect.MinMaxRect(.65f, .25f, .75f, .35f)));
    }
    [Test] public void SlideUsesItsWidthAndItsActualHalfWidthCoordinateSystem()
    {
        var slide = new ArcSlide { Timing = 0, EndTiming = 1000, StartCenter = .5f, EndCenter = .5f,
            StartWidth = 1, EndWidth = 1, Height = Arcade.Aff.SlideHeight.Sky };
        Assert.IsTrue(AdeSelectionVolume.Intersects(slide, 0, 1000, Rect.MinMaxRect(.7f, .9f, .8f, 1.1f)));
        Assert.IsFalse(AdeSelectionVolume.Intersects(slide, 0, 1000, Rect.MinMaxRect(.8f, .9f, .9f, 1.1f)));
    }
    [TestCase(ArcCurveType.S, ArcCurveType.S)]
    [TestCase(ArcCurveType.B, ArcCurveType.B)]
    [TestCase(ArcCurveType.Si, ArcCurveType.So)]
    [TestCase(ArcCurveType.SiSi, ArcCurveType.SoSo)]
    [TestCase(ArcCurveType.SiSo, ArcCurveType.SoSi)]
    public void ReverseCurveMatchesRequestedMappingAndIsReversible(ArcCurveType original, ArcCurveType reversed)
    {
        Assert.AreEqual(reversed, AdeEditorHotkeyCommands.ReverseCurve(original));
        Assert.AreEqual(original, AdeEditorHotkeyCommands.ReverseCurve(reversed));
    }
}
