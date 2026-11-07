using Arcade.Aff;
using UnityEngine;

namespace Arcade.Gameplay.Chart
{
    public class ArcSlide : ArcLongNote, IIntoRawItem, IHasTimingGroup, ISetableTimingGroup
    {
        // Default preview span; authored coordinates are unrestricted.
        public const float MinX = -0.5f, MaxX = 1.5f;
        private float startCenter = 0.5f, startWidth = 0.333f, endCenter = 0.5f, endWidth = 0.333f;
        public float StartCenter { get => startCenter; set => startCenter = RawAffSlide.RoundCoordinate(value); }
        public float StartWidth { get => startWidth; set => startWidth = RawAffSlide.RoundCoordinate(value); }
        public float EndCenter { get => endCenter; set => endCenter = RawAffSlide.RoundCoordinate(value); }
        public float EndWidth { get => endWidth; set => endWidth = RawAffSlide.RoundCoordinate(value); }
        public int LeftCurve, RightCurve;
        public SlideHeight Height = SlideHeight.Sky;
        // Compatibility for callers using the former two-plane model.
        public bool IsFloor { get => Height == SlideHeight.Ground; set => Height = value ? SlideHeight.Ground : SlideHeight.Sky; }
        // Authored heights stay fixed, like Arc coordinates, regardless of enwiden progress.
        public float WorldHeight => ArcAlgorithm.ArcYToWorld(IsFloor ? -2f / 9f : Height == SlideHeight.HighSky ? 1.61f : 1f);
        public float WorldWidth => 8.5f;
        public float WorldX(float normalizedX) => WorldWidth * (0.5f - normalizedX);
        private ArcTimingGroup timingGroup;
        public ArcTimingGroup TimingGroup
        {
            get => timingGroup;
            set { if (timingGroup == value) return; timingGroup = value; InvalidateRenderIndex(); }
        }
        // Derived runtime identity; never copied or serialized as chart data.
        public ArcSlide GroupRoot { get; internal set; }
        public bool IsGroupHead = true, IsGroupTail = true;
        public Vector4 Overlap = new Vector4(0, 1, 0, 1);
        private bool selected;
        public override bool Selected { get => selected; set => selected = value; }
        public ArcSlide() { }
        public ArcSlide(RawAffSlide raw, ArcTimingGroup group)
        {
            Timing = raw.Timing; EndTiming = raw.EndTiming; TimingGroup = group;
            StartCenter = raw.StartCenter; StartWidth = raw.StartWidth;
            EndCenter = raw.EndCenter; EndWidth = raw.EndWidth;
            LeftCurve = raw.LeftCurve; RightCurve = raw.RightCurve; Height = raw.Height;
        }
        public Vector2 Range(float progress)
        {
            return new Vector2(Mathf.Lerp(StartCenter - StartWidth / 2, EndCenter - EndWidth / 2, Curve(LeftCurve, progress)),
                Mathf.Lerp(StartCenter + StartWidth / 2, EndCenter + EndWidth / 2, Curve(RightCurve, progress)));
        }
        public static float Curve(int kind, float t)
        {
            t = Mathf.Clamp01(t);
            return kind == 1 ? Mathf.Sin(t * Mathf.PI / 2) : kind == 2 ? 1 - Mathf.Cos(t * Mathf.PI / 2) : kind == 3 ? t * t * (3 - 2 * t) : t;
        }
        public bool IsValid => EndTiming > (long)Timing + 1 && (long)EndTiming - Timing <= int.MaxValue && LeftCurve >= 0 && LeftCurve <= 3 && RightCurve >= 0 && RightCurve <= 3
            && Height >= SlideHeight.Ground && Height <= SlideHeight.HighSky
            && ValidShape(StartCenter, StartWidth, EndCenter, EndWidth, LeftCurve, RightCurve);
        public static bool ValidRange(float center, float width) => !float.IsNaN(center) && !float.IsInfinity(center)
            && width > 0 && !float.IsInfinity(width);
        // The two easing functions can cross even when both endpoints are valid.
        // Test the analytic extrema of width, not just sampled mesh rows.
        public static bool ValidShape(float sc, float sw, float ec, float ew, int left, int right)
        {
            if (!ValidRange(sc, sw) || !ValidRange(ec, ew) || left < 0 || left > 3 || right < 0 || right > 3) return false;
            double dl = ec - ew / 2d - (sc - sw / 2d), dr = ec + ew / 2d - (sc + sw / 2d);
            if (left == right) return true;
            if (left == 3 || right == 3)
            {
                // Certify positivity using a derivative bound, including mixed B/sine edges.
                double slopeBound = System.Math.Abs(dl) * MaxSlope(left) + System.Math.Abs(dr) * MaxSlope(right);
                return PositiveWidth(0, 1, 0);
                double MaxSlope(int curve) => curve == 0 ? 1 : curve == 3 ? 1.5 : System.Math.PI / 2;
                double Ease(int curve, double t) => curve == 0 ? t : curve == 1 ? System.Math.Sin(t * System.Math.PI / 2)
                    : curve == 2 ? 1 - System.Math.Cos(t * System.Math.PI / 2) : t * t * (3 - 2 * t);
                bool PositiveWidth(double from, double to, int depth)
                {
                    double mid = (from + to) / 2;
                    double width = sw + dr * Ease(right, mid) - dl * Ease(left, mid);
                    if (width <= 0) return false;
                    if (width > slopeBound * (to - from) / 2) return true;
                    // Reject unresolved near-touching edges rather than accepting a crossing.
                    if (depth == 16) return false;
                    return PositiveWidth(from, mid, depth + 1) && PositiveWidth(mid, to, depth + 1);
                }
            }
            double a = 0, b = 0, c = 0, k = System.Math.PI / 2;
            Accumulate(right, dr); Accumulate(left, -dl);
            double radius = System.Math.Sqrt(a * a + b * b);
            if (radius == 0 || System.Math.Abs(c) > radius) return true;
            double phase = System.Math.Atan2(b, a), angle = System.Math.Asin(-c / radius);
            for (int n = -1; n <= 1; n++)
            {
                if (!Check(angle - phase + n * 2 * System.Math.PI)
                    || !Check(System.Math.PI - angle - phase + n * 2 * System.Math.PI)) return false;
            }
            return true;
            void Accumulate(int curve, double delta)
            {
                if (curve == 0) c += delta;
                else if (curve == 1) b += delta * k;
                else a += delta * k;
            }
            bool Check(double theta)
            {
                if (theta <= 0 || theta >= k) return true;
                double t = theta / k;
                double l = left == 0 ? t : left == 1 ? System.Math.Sin(theta) : 1 - System.Math.Cos(theta);
                double r = right == 0 ? t : right == 1 ? System.Math.Sin(theta) : 1 - System.Math.Cos(theta);
                return sw + dr * r - dl * l > 0;
            }
        }
        // SkyArea ticks use the start BPM, with a final tick for a remainder >= 2ms.
        // Count analytically so seeking and tempo edits need no per-frame tick allocations.
        public int CountJudgements(int timing, double bpm, double density = 1)
        {
            if (!IsValid || this.NoInput() || timing < Timing || double.IsNaN(bpm)
                || double.IsInfinity(bpm) || density <= 0 || double.IsNaN(density) || double.IsInfinity(density)) return 0;
            bpm = System.Math.Abs(bpm);
            double duration = (long)EndTiming - Timing;
            double elapsed = System.Math.Min((long)timing - Timing, duration);
            if (bpm == 0) return timing >= EndTiming ? 1 : 0;
            double interval = (bpm >= 249.99 ? 60000d : 30000d) / bpm / density;
            double fullTicks = System.Math.Floor(duration / interval + 1e-9);
            double count = System.Math.Min(fullTicks, System.Math.Floor(elapsed / interval + 1e-9));
            if (timing >= EndTiming && duration - fullTicks * interval >= 2d - 1e-9) count++;
            return (int)System.Math.Min(count, int.MaxValue);
        }

        public override ArcEvent Clone() => new ArcSlide((RawAffSlide)IntoRawItem(), TimingGroup);
        public override void Assign(ArcEvent values)
        {
            base.Assign(values);
            var n = (ArcSlide)values;
            StartCenter = n.StartCenter; StartWidth = n.StartWidth; EndCenter = n.EndCenter; EndWidth = n.EndWidth;
            LeftCurve = n.LeftCurve; RightCurve = n.RightCurve; Height = n.Height; TimingGroup = n.TimingGroup;
            Rebuild();
        }
        public IRawAffItem IntoRawItem() => new RawAffSlide { Timing = Timing, EndTiming = EndTiming,
            StartCenter = StartCenter, StartWidth = StartWidth, EndCenter = EndCenter, EndWidth = EndWidth,
            LeftCurve = LeftCurve, RightCurve = RightCurve, Height = Height };
        internal ArcSlideVisual Visual;
        public override void Instantiate()
        {
            if (Visual == null) Visual = new ArcSlideVisual(this);
        }
        protected override void ReleaseVisual()
        {
            Visual?.Dispose();
            Visual = null;
            base.ReleaseVisual();
        }
        public void Rebuild()
        {
            ArcScoreManager.Invalidate();
            Visual?.Invalidate();
            if (ArcSlideManager.Instance) ArcSlideManager.Instance.RebuildGroups();
        }
    }
}
