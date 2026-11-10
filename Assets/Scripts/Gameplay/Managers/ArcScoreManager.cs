using System;
using System.Collections.Generic;
using UnityEngine;
using Text = TMPro.TMP_Text;
using Arcade.Gameplay.Chart;
using System.Globalization;

namespace Arcade.Gameplay
{
	public class ArcScoreManager : MonoBehaviour
	{
		public static ArcScoreManager Instance { get; private set; }

		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
		private static void ResetStaticState() => Instance = null;
		private void Awake()
		{
			Instance = this;
            ArcBitmapText.Attach(ScoreText, true);
            ArcBitmapText.Attach(ComboText, false);
		}

		public Text ScoreText;
		public Text ComboText;

		private int combo;
		private readonly List<int> judgeTimings = new List<int>();
		private ArcChart cachedChart;
		private bool scoreDirty = true;
		private double singleScore;
		private int displayedScore = -1, displayedCombo = -1;

		internal static void Invalidate()
		{
			ArcEvent.InvalidateRenderIndex();
			if (Instance) Instance.scoreDirty = true;
		}

		// Score roll shared by AffdataPlay (ScoreDisplay) and Arcade Alpha (SpinCountNText):
		// a 500 ms linear roll while playing; seeking or a lower score shows it at once.
		private const float ScoreRollSeconds = 0.5f;
		private double rollStart, rollTarget, rollValue;
		private float rollElapsed = ScoreRollSeconds;

		private void Update()
		{

			var game = ArcGameplayManager.Instance;
			if (cachedChart != game.Chart || scoreDirty) RebuildScoreCache(game.Chart);
			int score;
			if (game.Chart == null) { SetRoll(0); combo = 0; score = 0; }
			else score = RollScore(CalculateScore(game.ChartTiming));
			if (displayedScore != score)
			{
				displayedScore = score;
				ScoreText.text = score.ToString("D8", CultureInfo.InvariantCulture);
			}
			int visibleCombo = combo < 2 ? 0 : combo;
			if (displayedCombo != visibleCombo)
			{
				displayedCombo = visibleCombo;
				ComboText.text = visibleCombo == 0 ? "" : visibleCombo.ToString(CultureInfo.InvariantCulture);
			}
		}

		private int RollScore(int target)
		{
			if (target != rollTarget)
			{
				if (!ArcGameplayManager.Instance.IsPlaying || target < rollValue) SetRoll(target);
				else
				{
					// Continue from the shown value so a new target mid-roll stays smooth.
					rollStart = rollValue;
					rollTarget = target;
					rollElapsed = 0;
				}
			}
			rollElapsed = Mathf.Min(ScoreRollSeconds, rollElapsed + Time.unscaledDeltaTime);
			rollValue = rollStart + (rollTarget - rollStart) * (rollElapsed / ScoreRollSeconds);
			return (int)Math.Round(rollValue);
		}

		private void SetRoll(double value)
		{
			rollStart = rollTarget = rollValue = value;
			rollElapsed = ScoreRollSeconds;
		}

		private void RebuildScoreCache(ArcChart chart)
		{
			cachedChart = chart;
			scoreDirty = false;
			judgeTimings.Clear();
			singleScore = 0;
			if (chart == null) return;
			foreach (var tap in chart.Taps)
				if (!tap.NoInput()) judgeTimings.Add(tap.Timing);
			foreach (var hold in chart.Holds)
			{
				if (hold.NoInput()) continue;
				foreach (int timing in hold.JudgeTimings) judgeTimings.Add(Math.Max(hold.Timing, timing));
			}
			foreach (var arc in chart.Arcs)
			{
				if (arc.NoInput() || arc.Designant) continue;
				foreach (int timing in arc.JudgeTimings) judgeTimings.Add(Math.Max(arc.Timing, timing));
				foreach (var tap in arc.ArcTaps) judgeTimings.Add(Math.Max(arc.Timing, tap.Timing));
				if (arc.IsVariousSizedArctap) judgeTimings.Add(arc.Timing);
			}
			judgeTimings.Sort();
			long total = judgeTimings.Count;
			foreach (var slide in chart.Slides) total += CountSlideJudgements(slide, slide.EndTiming);
			if (total > 0) singleScore = 10000000d / total;
		}

		private int CalculateCombo(int timing)
		{
			int low = 0, high = judgeTimings.Count;
			while (low < high)
			{
				int middle = low + (high - low) / 2;
				if (judgeTimings[middle] <= timing) low = middle + 1;
				else high = middle;
			}
			int count = low;
			foreach (var slide in cachedChart.Slides) count += CountSlideJudgements(slide, timing);
			return count;
		}
		private static int CountSlideJudgements(ArcSlide slide, int timing)
		{
			var root = slide.GroupRoot ?? slide;
			return slide.CountJudgements(timing,
				ArcTimingManager.Instance.CalculateBpmByTiming(root.Timing, root.TimingGroup),
				ArcGameplayManager.Instance.TimingPointDensityFactor);
		}
		private int CalculateScore(int timing)
		{
			double single = singleScore;
			if (single == 0)
			{
				combo = 0;
				return 0;
			}
			combo = CalculateCombo(timing);
			return (int)Math.Round((combo * single + combo));
		}
	}
}
