using System.Threading;
using Cysharp.Threading.Tasks;
using Arcade.Aff;
using Arcade.Compose;
using Arcade.Gameplay;
using Arcade.Gameplay.Chart;
using System.Collections.Generic;
using UnityEngine;
using Arcade.Aff.Faults;
using System.Linq;
using UnityEngine.UI;
using Text = TMPro.TMP_Text;
using System.Globalization;

// The checks follow Arcade Alpha's fault detector, plus Play's ArcTap range check.
namespace Arcade.Aff.Faults
{
	public abstract class Fault
	{
		// One lane in world units; notes closer than this share a lane.
		protected const float LaneWidth = 4.25f;

		public Fault(string Reason)
		{
			this.Reason = Reason;
		}
		public List<ArcEvent> Faults = new List<ArcEvent>();
		// Pairs of an event and the earlier event it overlaps.
		public List<(ArcEvent Current, ArcEvent Other)> Overlaps = new List<(ArcEvent, ArcEvent)>();
		public string Reason;

		public int Count => Faults.Count + Overlaps.Count;

		public abstract void Check(ArcChart chart);

		protected static bool NoInput(IHasTimingGroup e) => e.TimingGroup != null && e.TimingGroup.NoInput;
	}

	public class ShortHoldFault : Fault
	{
		public ShortHoldFault() : base("Hold 持续时间 <= 0ms") { }
		public override void Check(ArcChart chart)
		{
			Faults.AddRange(chart.Holds.Where(h => h.EndTiming <= h.Timing));
		}
	}
	public class NegativeArcFault : Fault
	{
		public NegativeArcFault() : base("Arc 持续时间 < 0ms 且不是黑线") { }
		public override void Check(ArcChart chart)
		{
			Faults.AddRange(chart.Arcs.Where(a => a.EndTiming < a.Timing && !a.IsVoid));
		}
	}
	public class TapOverlapFault : Fault
	{
		public TapOverlapFault() : base("Tap 重叠") { }
		public override void Check(ArcChart chart)
		{
			var taps = chart.Taps.Where(t => !NoInput(t)).OrderBy(t => t.Timing).ToList();
			for (int i = 0; i < taps.Count - 1; ++i)
				for (int k = i + 1; k < taps.Count && taps[k].Timing == taps[i].Timing; ++k)
					if (taps[i].Timing == taps[k].Timing && Mathf.Approximately(taps[i].WorldX, taps[k].WorldX))
						Overlaps.Add((taps[k], taps[i]));
		}
	}
	public class TapTooCloseFault : Fault
	{
		public TapTooCloseFault() : base("Tap 与前一个 Tap 间隔过小（同一轨道 25ms 内）") { }
		public override void Check(ArcChart chart)
		{
			var taps = chart.Taps.Where(t => !NoInput(t)).OrderBy(t => t.Timing).ToList();
			for (int i = 0; i < taps.Count - 1; ++i)
				for (int k = i + 1; k < taps.Count && taps[k].Timing - taps[i].Timing < 25; ++k)
					if (Mathf.Abs(taps[i].WorldX - taps[k].WorldX) < LaneWidth && Mathf.Abs(taps[i].Timing - taps[k].Timing) < 25)
						Overlaps.Add((taps[k], taps[i]));
		}
	}
	public class HoldOverlapFault : Fault
	{
		public HoldOverlapFault() : base("Hold 重叠") { }
		public override void Check(ArcChart chart)
		{
			var holds = chart.Holds.Where(h => !NoInput(h)).OrderBy(h => h.Timing).ToList();
			for (int i = 0; i < holds.Count - 1; ++i)
				for (int k = i + 1; k < holds.Count && holds[k].Timing <= holds[i].EndTiming; ++k)
					if (holds[k].Timing <= holds[i].EndTiming && holds[k].Timing >= holds[i].Timing
						&& Mathf.Abs(holds[i].WorldX - holds[k].WorldX) < LaneWidth)
						Overlaps.Add((holds[k], holds[i]));
		}
	}
	public class ArcTapOverlapFault : Fault
	{
		private const float Epsilon = 0.1f;
		public ArcTapOverlapFault() : base("ArcTap 重叠") { }
		public override void Check(ArcChart chart)
		{
			var ats = new List<(ArcArcTap ArcTap, float X, float Y)>();
			foreach (var arc in chart.Arcs)
			{
				if (NoInput(arc)) continue;
				foreach (var at in arc.ArcTaps)
				{
					float t = arc.EndTiming == arc.Timing ? 0 : 1f * (at.Timing - arc.Timing) / (arc.EndTiming - arc.Timing);
					ats.Add((at, ArcAlgorithm.X(arc.XStart, arc.XEnd, t, arc.CurveType), ArcAlgorithm.Y(arc.YStart, arc.YEnd, t, arc.CurveType)));
				}
			}
			ats.Sort((a, b) => a.ArcTap.Timing.CompareTo(b.ArcTap.Timing));
			for (int i = 0; i < ats.Count - 1; ++i)
				for (int k = i + 1; k < ats.Count && ats[k].ArcTap.Timing == ats[i].ArcTap.Timing; ++k)
					if (ats[i].ArcTap.Timing == ats[k].ArcTap.Timing
						&& Vector2.Distance(new Vector2(ats[i].X, ats[i].Y), new Vector2(ats[k].X, ats[k].Y)) < Epsilon)
						Overlaps.Add((ats[k].ArcTap, ats[i].ArcTap));
		}
	}
	public class TimingOverlapFault : Fault
	{
		public TimingOverlapFault() : base("Timing 重叠（同一组内时间相同）") { }
		public override void Check(ArcChart chart)
		{
			foreach (var timings in new[] { chart.Timings }.Concat(chart.TimingGroups.Select(g => g.Timings)))
				for (int i = 0; i < timings.Count - 1; ++i)
					for (int k = i + 1; k < timings.Count; ++k)
						if (timings[i].Timing == timings[k].Timing)
							Overlaps.Add((timings[k], timings[i]));
		}
	}
	public class TapHoldOverlapFault : Fault
	{
		public TapHoldOverlapFault() : base("Hold 与 Tap 重叠") { }
		public override void Check(ArcChart chart)
		{
			var taps = chart.Taps.Where(t => !NoInput(t)).OrderBy(t => t.Timing).ToList();
			foreach (var h in chart.Holds)
			{
				if (NoInput(h)) continue;
				int lo = 0, hi = taps.Count;
				while (lo < hi)
				{
					int mid = lo + (hi - lo) / 2;
					if (taps[mid].Timing < h.Timing) lo = mid + 1; else hi = mid;
				}
				for (int i = lo; i < taps.Count && taps[i].Timing <= h.EndTiming; i++)
				{
					var t = taps[i];
					if (!NoInput(t) && t.Timing >= h.Timing && t.Timing <= h.EndTiming && Mathf.Abs(t.WorldX - h.WorldX) < LaneWidth)
						Overlaps.Add((t, h));
				}
			}
		}
	}
	public class ShortLongNoteFault : Fault
	{
		// One frame at 60 fps.
		private const float MinDuration = 16.6667f;
		public ShortLongNoteFault() : base("长条持续时间过短（< 16.67ms）") { }
		public override void Check(ArcChart chart)
		{
			Faults.AddRange(chart.Holds.Where(h => !NoInput(h) && h.EndTiming > h.Timing && h.EndTiming - h.Timing < MinDuration));
			Faults.AddRange(chart.Arcs.Where(a => !NoInput(a) && !a.IsVoid && a.EndTiming > a.Timing && a.EndTiming - a.Timing < MinDuration));
		}
	}
	public class CrossTimingFault : Fault
	{
		// The BPM-changing timings each Arc crosses.
		public readonly Dictionary<ArcEvent, List<ArcTiming>> Crossed = new Dictionary<ArcEvent, List<ArcTiming>>();
		public CrossTimingFault() : base("Arc 跨越变速的 Timing") { }
		public override void Check(ArcChart chart)
		{
			foreach (var a in chart.Arcs)
			{
				float bpm = ArcTimingManager.Instance.CalculateBpmByTiming(a.Timing, a.TimingGroup);
				var crossed = (a.TimingGroup?.Timings ?? chart.Timings)
					.Where(t => t.Timing > a.Timing && t.Timing < a.EndTiming && !Mathf.Approximately(t.Bpm, bpm)).ToList();
				if (crossed.Count == 0) continue;
				Faults.Add(a);
				Crossed[a] = crossed;
			}
		}
	}
	public class ZeroTimeArcTapFault : Fault
	{
		public ZeroTimeArcTapFault() : base("ArcTap 位于持续时间为 0 的 Arc 上") { }
		public override void Check(ArcChart chart)
		{
			Faults.AddRange(chart.Arcs.Where(a => a.EndTiming == a.Timing && a.ArcTaps.Count != 0));
		}
	}
	public class ArcTapOutsideArcFault : Fault
	{
		public ArcTapOutsideArcFault() : base("ArcTap 超出所在 Arc 的时间范围") { }
		public override void Check(ArcChart chart)
		{
			foreach (var a in chart.Arcs)
			{
				int start = Mathf.Min(a.Timing, a.EndTiming), end = Mathf.Max(a.Timing, a.EndTiming);
				Faults.AddRange(a.ArcTaps.Where(at => at.Timing < start || at.Timing > end));
			}
		}
	}
	public class MissingSfxFault : Fault
	{
		public MissingSfxFault() : base("Arc 引用的音效文件不存在") { }
		public override void Check(ArcChart chart)
		{
			var loaded = ArcEffectManager.Instance.SpecialEffectAudios;
			Faults.AddRange(chart.Arcs.Where(a => a.Effect != null && a.Effect.EndsWith("_wav") && !loaded.ContainsKey(a.Effect)));
		}
	}
}



namespace Arcade.Compose
{
    public sealed partial class AdeFaultDetector : MonoBehaviour
    {
        public Text Status;
        [SerializeField] private AdeRightSideCard card;
        [SerializeField] private RectTransform content;
        [SerializeField] private Button issueTemplate;
        [SerializeField] private GameObject badge;
        [SerializeField] private Text badgeCount;
        private readonly List<Issue> issues = new List<Issue>();
        private readonly List<Button> rows = new List<Button>();
        private ArcChart chart;
        private CancellationTokenSource checking, rendering;
        private float nextCheck;
        private bool dirty;
        private int revision, themeRevision = -1;

        private sealed class Issue
        {
            public string Reason;
            public ArcEvent Event, Related;
        }

        private void Start()
        {
            if (!card) return;
            ArcGameplayManager.Instance.OnChartLoad.AddListener(Schedule);
            Command.AdeCommandManager.Instance.onCommandExecuted += OnCommand;
            card.Opened += Render;
            card.Closed += StopRendering;
            Schedule();
        }

        private void OnDestroy()
        {
            CancelCheck();
            StopRendering();
            if (ArcGameplayManager.Instance) ArcGameplayManager.Instance.OnChartLoad.RemoveListener(Schedule);
            if (Command.AdeCommandManager.Instance) Command.AdeCommandManager.Instance.onCommandExecuted -= OnCommand;
            if (card) card.Opened -= Render;
            if (card) card.Closed -= StopRendering;
        }

        private void OnCommand(Command.ICommand command, bool undo) => Schedule();

        private void Schedule()
        {
            revision++;
            CancelCheck();
            chart = ArcGameplayManager.Instance.Chart;
            nextCheck = Time.unscaledTime + .6f;
            dirty = true;
            if (chart == null)
            {
                issues.Clear();
                UpdateSummary();
                if (card.IsOpen) Render();
            }
        }

        private void Update()
        {
            if (!card) return;
            if (chart != ArcGameplayManager.Instance.Chart) Schedule();
            bool canCheck = !AdeOperationManager.Instance.HasOngoingOperation
                && !AdeProjectManager.Instance.IsLoading && !ArcGameplayManager.Instance.IsPlaying;
            if (checking != null && !canCheck)
            {
                CancelCheck();
                dirty = true; nextCheck = Time.unscaledTime + .6f;
            }
            if (dirty && checking == null && canCheck && Time.unscaledTime >= nextCheck)
                {
                checking = CancellationTokenSource.CreateLinkedTokenSource(destroyCancellationToken);
                CheckAsync(revision, checking).Forget();
            }
            if (themeRevision != AdeUiTheme.Revision)
            {
                themeRevision = AdeUiTheme.Revision;
                foreach (var row in rows) StyleRow(row);
            }
        }

        private async UniTask CheckAsync(int version, CancellationTokenSource cancellation)
        {
            try
            {
                dirty = false;
                if (chart == null) return;
                Status.text = "正在检查…";
                var next = new List<Issue>();
                Fault[] checks = {
                    new ShortHoldFault(), new TapOverlapFault(), new TapTooCloseFault(), new HoldOverlapFault(),
                    new ArcTapOverlapFault(), new TimingOverlapFault(), new TapHoldOverlapFault(), new NegativeArcFault(),
                    new ShortLongNoteFault(), new CrossTimingFault(), new ZeroTimeArcTapFault(), new ArcTapOutsideArcFault(),
                    new MissingSfxFault() };
                foreach (var check in checks)
                {
                    check.Check(chart);
                    foreach (var overlap in check.Overlaps)
                        next.Add(new Issue { Reason = check.Reason, Event = overlap.Current, Related = overlap.Other });
                    foreach (var item in check.Faults) next.Add(new Issue { Reason = check.Reason, Event = item });
                    await UniTask.NextFrame(cancellation.Token);
                    if (version != revision || chart != ArcGameplayManager.Instance.Chart) return;
                }
                issues.Clear();
                issues.AddRange(next.OrderBy(i => i.Event.Timing));
                UpdateSummary();
                if (card.IsOpen) Render();
            }
            finally
            {
                if (checking == cancellation) checking = null;
                cancellation.Dispose();
            }
        }

        private void CancelCheck()
        {
            checking?.Cancel();
            checking = null;
        }

        private void UpdateSummary()
        {
            Status.text = chart == null ? "打开谱面后自动检查" : issues.Count == 0 ? "没有发现问题" : $"发现 {issues.Count} 个问题";
            badge.SetActive(issues.Count > 0);
            badgeCount.text = issues.Count.ToString(CultureInfo.InvariantCulture);
            float diameter = Mathf.Max(26, badgeCount.GetPreferredValues(badgeCount.text).x + 12);
            var rect = (RectTransform)badge.transform;
            rect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, diameter);
            rect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, diameter);
        }

        // Kept for existing toolbar bindings; checking is scheduled automatically.
        public void OnInvoke() { if (card) card.Toggle(); }
    }
}
