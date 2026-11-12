using System;
using System.Collections;
using System.Reflection;
using Arcade.Gameplay;
using Arcade.Gameplay.Chart;
using NUnit.Framework;
using UnityEngine;

public class ArcCapDisplayTests
{
    private static readonly Type VisualType = typeof(ArcArcManager).Assembly.GetType("Arcade.Gameplay.ArcArcVisual");
    private const BindingFlags Fields = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

    [TestCase(0, 25000, .75f, 1.197f)]
    [TestCase(0, 0, 1, .882f)]
    [TestCase(0, 150000, 0, 2.142f)]
    [TestCase(1500, 25000, .75f, 1.197f)]
    [TestCase(1500, -25000, 1, .882f)]
    public void SolidCapUsesSignedDistanceInItsCurrentStage(int now, float position, float alpha, float width)
    {
        using (var fixture = new CapFixture(now))
        {
            fixture.Note.Position = position;
            fixture.Set("arcCapOrder", -99);
            fixture.Invoke("UpdateArcCap");
            Assert.That(fixture.Get<bool>("arcCapEnable"), Is.True);
            Assert.That(fixture.Get<Color>("arcCapColor").a, Is.EqualTo(alpha).Within(.00001f));
            Assert.That(fixture.Get<Vector3>("arcCapScale").x * .84f, Is.EqualTo(width).Within(.00001f));
        }
    }

    [Test]
    public void FutureCapAlreadyAcrossThePlaneIsHidden()
    {
        using (var fixture = new CapFixture(0))
        {
            fixture.Note.Position = -25000;
            fixture.Set("arcCapEnable", true);
            fixture.Invoke("UpdateArcCap");
            Assert.That(fixture.Get<bool>("arcCapEnable"), Is.False);
        }
    }

    [TestCase(0, false)]
    [TestCase(1, false)]
    [TestCase(0, true)]
    [TestCase(1, true)]
    public void StationarySingleTapCarriersNeverShowCaps(int duration, bool trace)
    {
        using (var fixture = new CapFixture(0))
        {
            fixture.Note.EndTiming = fixture.Note.Timing + duration;
            fixture.Note.LineType = trace ? ArcLineType.TrueIsVoid : ArcLineType.FalseNotVoid;
            fixture.Note.ArcTaps.Add(new ArcArcTap { Arc = fixture.Note });
            fixture.Set("arcCapEnable", true);
            if (trace) fixture.Invoke("UpdateTraceCap", 0, null);
            else fixture.Invoke("UpdateArcCap");
            Assert.That(fixture.Get<bool>("arcCapEnable"), Is.False);
        }
    }

    [TestCase(false)]
    [TestCase(true)]
    public void TraceCapKeepsItsExistingOpacityAndUsesItsTypeTint(bool designant)
    {
        using (var fixture = new CapFixture(1500))
        {
            fixture.Note.LineType = designant ? ArcLineType.Designant : ArcLineType.TrueIsVoid;
            fixture.Set("traceCrossing", true);
            fixture.Set("RootZ", 0f);
            fixture.Set("arcCapOrder", -99);
            fixture.Invoke("UpdateTraceCap", 1500, fixture.Timing);
            Assert.That(fixture.Get<bool>("arcCapEnable"), Is.True);
            Color color = fixture.Get<Color>("arcCapColor");
            Color expected = designant ? fixture.Manager.ArcDesignant : Color.white;
            Assert.That(color.r, Is.EqualTo(expected.r));
            Assert.That(color.g, Is.EqualTo(expected.g));
            Assert.That(color.b, Is.EqualTo(expected.b));
            Assert.That(color.a, Is.EqualTo(127f / 255f).Within(.00001f));
            Assert.That(fixture.Get<Vector3>("arcCapScale").x * .84f, Is.EqualTo(.5292f).Within(.00001f));
        }
    }

    [Test]
    public void HiddenArcUpdatesOnlyItsParticlePosition()
    {
        using (var fixture = new CapFixture(1500))
        {
            fixture.SetupEffectPlane();
            fixture.Note.TimingGroup = new ArcTimingGroup { GroupHide = true };
            fixture.Note.Position = -50000;
            fixture.AddSegment(new Vector3(1, 2, 0), new Vector3(5, 6, -100));
            fixture.Set("arcCapPosition", new Vector3(8, 9, 0));
            fixture.Set("arcCapEnable", true);
            fixture.Set("headEnable", true);
            fixture.Set("heightIndicatorEnable", true);
            fixture.Invoke("UpdateJudgePosition");
            Assert.That(fixture.JudgePosition, Is.EqualTo(new Vector3(3, 4, 0)));
            Assert.That(fixture.Get<Vector3>("arcCapPosition"), Is.EqualTo(new Vector3(8, 9, 0)));
            Assert.That(fixture.Get<bool>("arcCapEnable"), Is.True);
            Assert.That(fixture.Get<bool>("headEnable"), Is.True);
            Assert.That(fixture.Get<bool>("heightIndicatorEnable"), Is.True);
            Assert.That(fixture.Manager.ArcJudgePosLeft, Is.Zero);
            Assert.That(fixture.Manager.ArcJudgePosRight, Is.Zero);
        }
    }

    [Test]
    public void HeadlessArcCanUpdateItsParticleAtTheExactStart()
    {
        using (var fixture = new CapFixture(1000))
        {
            fixture.SetupEffectPlane();
            fixture.Note.RenderHead = false;
            fixture.Note.XStart = .25f;
            fixture.Note.YStart = .5f;
            fixture.Invoke("UpdateJudgePosition");
            Assert.That(fixture.JudgePosition, Is.EqualTo(new Vector3(
                ArcAlgorithm.ArcXToWorld(.25f), ArcAlgorithm.ArcYToWorld(.5f), 0)));
        }
    }

    [Test]
    public void HiddenArcDoesNotSubmitGeometry()
    {
        using (var fixture = new CapFixture(1500))
        {
            fixture.Note.TimingGroup = new ArcTimingGroup { GroupHide = true };
            fixture.Set("enable", true);
            Assert.DoesNotThrow(() => fixture.Invoke("Submit", new object[] { null }));
        }
    }

    private sealed class CapFixture : IDisposable
    {
        internal readonly ArcArc Note = new ArcArc {
            Timing = 1000, EndTiming = 2000, RenderHead = true, LineType = ArcLineType.FalseNotVoid
        };
        internal readonly ArcArcManager Manager;
        internal readonly ArcTimingManager Timing;
        private readonly object visual = Activator.CreateInstance(VisualType, true);
        private readonly GameObject root;
        private readonly Texture2D texture;
        private readonly Sprite sprite;
        private readonly ArcGameplayManager previousGameplay;
        private readonly ArcArcManager previousArcs;
        private readonly ArcTimingManager previousTiming;
        private readonly ArcEffectManager previousEffects;

        internal CapFixture(int now)
        {
            previousGameplay = ArcGameplayManager.Instance;
            previousArcs = ArcArcManager.Instance;
            previousTiming = ArcTimingManager.Instance;
            previousEffects = ArcEffectManager.Instance;
            root = new GameObject("Cap display test");
            root.SetActive(false);
            var gameplay = root.AddComponent<ArcGameplayManager>();
            typeof(ArcGameplayManager).GetField("audioTiming", Fields).SetValue(gameplay, now / 1000f);
            SetInstance(typeof(ArcGameplayManager), gameplay);
            Timing = root.AddComponent<ArcTimingManager>();
            Timing.Timings.Add(new ArcTiming { Timing = 0, Bpm = 100 });
            SetInstance(typeof(ArcTimingManager), Timing);
            Manager = root.AddComponent<ArcArcManager>();
            Manager.ArcLayer = root.transform;
            Manager.ArcNotePrefab = root;
            Manager.ArcDesignant = new Color32(240, 41, 97, 255);
            SetInstance(typeof(ArcArcManager), Manager);
            var skin = root.AddComponent<ArcArcRenderer>();
            skin.ArcCapRenderer = root.AddComponent<SpriteRenderer>();
            texture = new Texture2D(84, 84);
            sprite = Sprite.Create(texture, new Rect(0, 0, 84, 84), new Vector2(.5f, .5f), 100);
            skin.ArcCapRenderer.sprite = sprite;
            Set("arc", Note);
        }

        internal T Get<T>(string name) => (T)VisualType.GetField(name, Fields).GetValue(visual);
        internal Vector3 JudgePosition => (Vector3)VisualType.GetProperty("JudgePosition", Fields).GetValue(visual);
        internal void Set(string name, object value) => VisualType.GetField(name, Fields).SetValue(visual, value);
        internal void Invoke(string name, params object[] args) => VisualType.GetMethod(name, Fields).Invoke(visual, args);
        internal void SetupEffectPlane()
        {
            var effects = root.AddComponent<ArcEffectManager>();
            effects.EffectPlane = root.AddComponent<ArcEffectPlane>();
            effects.EffectPlane.ReferenceCamera = root.AddComponent<Camera>();
            SetInstance(typeof(ArcEffectManager), effects);
        }
        internal void AddSegment(Vector3 from, Vector3 to)
        {
            Type type = VisualType.Assembly.GetType("Arcade.Gameplay.ArcSegmentData");
            object segment = Activator.CreateInstance(type, true);
            type.GetMethod("BuildSegment").Invoke(segment, new object[] { from, to, .9f, 1000f, 2000f, 0f, 0f });
            ((IList)VisualType.GetField("segments", Fields).GetValue(visual)).Add(segment);
        }
        private static void SetInstance(Type type, object value) => type.GetProperty("Instance").SetValue(null, value);

        public void Dispose()
        {
            SetInstance(typeof(ArcGameplayManager), previousGameplay);
            SetInstance(typeof(ArcArcManager), previousArcs);
            SetInstance(typeof(ArcTimingManager), previousTiming);
            SetInstance(typeof(ArcEffectManager), previousEffects);
            UnityEngine.Object.DestroyImmediate(root);
            UnityEngine.Object.DestroyImmediate(sprite);
            UnityEngine.Object.DestroyImmediate(texture);
        }
    }
}
