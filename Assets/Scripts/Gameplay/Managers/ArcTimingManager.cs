using System.Collections.Generic;
using System.Linq;
using Affdata.Core.Chart;
using Arcade.Compose;
using Arcade.Compose.Editing;
using Arcade.Compose.UI;
using Arcade.Gameplay.Chart;
using UnityEngine;

namespace Arcade.Gameplay
{
	public class ArcTimingManager : MonoBehaviour
	{
		public static ArcTimingManager Instance { get; private set; }

		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
		private static void ResetStaticState() => Instance = null;
		private void Awake()
		{
			Instance = this;
		}
		private void Start()
		{
			phaseShaderId = Shader.PropertyToID("_Phase");
		}

		private int settingVelocity = 30;
		public float BaseBpm = 100;
		public Transform BeatlineLayer;
		public GameObject BeatlinePrefab;

		public const int LostDelay = 120;
		public float BeatlineEnwidenRatio = 0;

		// Note: This should be ordered!
		[HideInInspector]
		private List<ArcTiming> timings = new List<ArcTiming>();
		[HideInInspector]
		[System.NonSerialized]
		public List<ArcTimingGroup> timingGroups = new List<ArcTimingGroup>();
		public SpriteRenderer[] TrackComponentRenderers;
		private List<int> beatlineTimings = new List<int>();
		private SpriteRenderer beatlineStyle;
		private float earliestRenderTime = 0;
		private float latestRenderTime = 0;
		private int phaseShaderId = 0;
		private MaterialPropertyBlock phaseBlock;
		private float phase = 0;
		// Render ranges depend only on these inputs, so a paused editor recomputes nothing.
		// Every timing edit goes through this manager and bumps the version.
		private int renderRangeVersion;
		private (int timing, float velocity, float baseBpm, int begin, int end, int version) renderRangeInputs = (int.MinValue, 0, 0, 0, 0, -1);
		private float[] timingPositions = new float[0];
        private sealed class RenderCurve
        {
            public List<ArcTiming> Timings;
            public float[] Steps;
            public float Earliest, Latest;
            public bool Updated;
        }

        // Only Timing/Bpm affect render ranges. Share work between identical curves,
        // without changing their float integration order or merging tempo segments.
        private sealed class RenderCurveComparer : IEqualityComparer<List<ArcTiming>>
        {
            public bool Equals(List<ArcTiming> a, List<ArcTiming> b)
            {
                if (ReferenceEquals(a, b)) return true;
                if (a == null || b == null || a.Count != b.Count) return false;
                for (int i = 0; i < a.Count; i++)
                    if (a[i].Timing != b[i].Timing || !a[i].Bpm.Equals(b[i].Bpm)) return false;
                return true;
            }
            public int GetHashCode(List<ArcTiming> events)
            {
                unchecked
                {
                    int hash = events.Count;
                    foreach (var item in events) hash = (hash * 31 + item.Timing) * 31 + item.Bpm.GetHashCode();
                    return hash;
                }
            }
        }
        private readonly Dictionary<List<ArcTiming>, RenderCurve> renderCurves =
            new Dictionary<List<ArcTiming>, RenderCurve>(new RenderCurveComparer());
        private RenderCurve[] groupRenderCurves = new RenderCurve[0];
        private (int version, float velocity, float baseBpm) renderCurveInputs = (-1, 0, 0);

        private void PrepareRenderCurves()
        {
            var inputs = (renderRangeVersion, Velocity, BaseBpm);
            if (inputs != renderCurveInputs)
            {
                renderCurveInputs = inputs;
                renderCurves.Clear();
                groupRenderCurves = new RenderCurve[timingGroups.Count + 1];
                for (int group = 0; group < groupRenderCurves.Length; group++)
                {
                    var events = GetTiming(group == 0 ? null : timingGroups[group - 1]);
                    if (!renderCurves.TryGetValue(events, out var curve))
                    {
                        curve = new RenderCurve { Timings = events, Steps = new float[System.Math.Max(0, events.Count - 1)] };
                        for (int i = 0; i < curve.Steps.Length; i++)
                            curve.Steps[i] = (events[i + 1].Timing - events[i].Timing) * events[i].Bpm / BaseBpm * Velocity;
                        renderCurves.Add(events, curve);
                    }
                    groupRenderCurves[group] = curve;
                }
            }
            foreach (var curve in renderCurves.Values) curve.Updated = false;
        }

		public float CurrentSpeed { get; set; }
		public List<ArcTiming> Timings { get => timings; }
		private float Velocity
		{
			// Alpha: displayed speed * 30; SettingVelocity stores displayed speed * 30.
			get => settingVelocity;
		}

		public int SettingVelocity
		{
			get => settingVelocity;
			set
			{
				settingVelocity = value;
				ArcArcManager.Instance.Rebuild();
				ArcadeComposeManager.Instance.VelocitySlider.SetValueWithoutNotify(((float)value) / 30);
			}
		}

		private void Update()
		{
			if (Timings == null) return;
			if (Timings.Count == 0) return;
			UpdateChartSpeedStatus();
			var gameplay = ArcGameplayManager.Instance;
			var inputs = (gameplay.ChartTiming, Velocity, BaseBpm, gameplay.AllBeginChartTiming, gameplay.AllEndChartTiming, renderRangeVersion);
			if (inputs != renderRangeInputs)
			{
				renderRangeInputs = inputs;
                PrepareRenderCurves();
                UpdateRenderRange(null, groupRenderCurves[0]);
                for (int i = 0; i < timingGroups.Count; i++)
                    UpdateRenderRange(timingGroups[i], groupRenderCurves[i + 1]);
			}
			UpdateTrackSpeed();
		}

		public void Clean()
		{
			CurrentSpeed = 0;
			Timings.Clear();
			timingGroups.Clear();
            renderCurves.Clear();
            groupRenderCurves = new RenderCurve[0];
			renderRangeVersion++;
			AdeTimingEditor.Instance.SetCurrentTimingGroup(null);
			SetTrackPhase(0);
		}
		public void Load(List<ArcTiming> arcTimings, List<ArcTimingGroup> arcTimingGroups)
		{
			// Note: We replaced the inplace sort by sort to another list and reassign
			// just because we do not have stable inplace sort now in dot net
			timings = arcTimings.OrderBy((timing) => timing.Timing).ToList();
			ArcGameplayManager.Instance.Chart.Timings = timings;
			timingGroups = arcTimingGroups;
			foreach (var timingGroup in timingGroups)
			{
				timingGroup.Timings = timingGroup.Timings.OrderBy((timing) => timing.Timing).ToList();
			}
			OnTimingGroupChange();
			OnTimingChange(null);
		}

		public List<ArcTiming> GetTiming(ArcTimingGroup timingGroup)
		{
			if (timingGroup == null)
			{
				return Timings;
			}
			return timingGroup.Timings;
		}

		// Timing lists are stably sorted. Equal timestamps use the last event,
		// matching the previous FindLastIndex/Last queries, including before the first event.
		internal static int FindTimingIndex(List<ArcTiming> events, int timing)
		{
			int low = 0, high = events.Count;
			while (low < high)
			{
				int middle = low + (high - low) / 2;
				if (events[middle].Timing <= timing) low = middle + 1;
				else high = middle;
			}
			return low - 1;
		}
		public void CalculateBeatlineTimes()
		{
			beatlineTimings.Clear();
			var sharedTimings = new BeatLineTiming[Timings.Count];
			for (int i = 0; i < Timings.Count; i++)
			{
				ArcTiming timing = Timings[i];
				sharedTimings[i] = new BeatLineTiming(timing.Timing, timing.Bpm, timing.BeatsPerLine);
			}
			beatlineTimings.AddRange(BeatLineGenerator.Generate(sharedTimings, ArcGameplayManager.Instance.Length));
		}

		public float CalculatePositionByTiming(int Timing, ArcTimingGroup timingGroup)
		{
			return CalculatePositionByTimingAndStart(ArcGameplayManager.Instance.ChartTiming, Timing, timingGroup);
		}
		public float CalculatePositionByTimingAndStart(int pivotTiming, int targetTiming, ArcTimingGroup timingGroup)
		{
			var Timings = GetTiming(timingGroup);
			if (Timings.Count == 0)
			{
				return 0;
			}
			bool reversed = pivotTiming > targetTiming;
			int startTiming = reversed ? targetTiming : pivotTiming;
			int endTiming = reversed ? pivotTiming : targetTiming;
			int startTimingId = FindTimingIndex(Timings, startTiming);
			int endTimingId = FindTimingIndex(Timings, endTiming);
			if (startTimingId == -1)
			{
				startTimingId = 0;
			}
			if (endTimingId == -1)
			{
				endTimingId = 0;
			}
			float result = 0;
			for (int i = startTimingId; i <= endTimingId; i++)
			{
				int segmentStartTiming = i == startTimingId ? startTiming : Timings[i].Timing;
				int segmentEndTiming = i == endTimingId ? endTiming : Timings[i + 1].Timing;
				result += (segmentEndTiming - segmentStartTiming) * Timings[i].Bpm / BaseBpm * Velocity;
			}
			float newresult = reversed ? -result : result;

			return newresult;
		}

		// Distance per millisecond of the timing in effect at `timing`.
		public float CalculateVelocityAtTiming(int timing, ArcTimingGroup timingGroup)
		{
			var Timings = GetTiming(timingGroup);
			if (Timings.Count == 0)
			{
				return 0;
			}
			int index = Mathf.Max(0, FindTimingIndex(Timings, timing));
			return Timings[index].Bpm / BaseBpm * Velocity;
		}

		public int CalculateChartTimingByPosition(float position, ArcTimingGroup timingGroup)
		{
			var Timings = GetTiming(timingGroup);
			if (Timings.Count == 0)
			{
				return 0;
			}
			int currentTiming = ArcGameplayManager.Instance.ChartTiming;
			if (position < 0)
			{
				return currentTiming;
			}
			int currentTimingId = FindTimingIndex(Timings, currentTiming);
			int allEndTime = ArcGameplayManager.Instance.AllEndChartTiming;
			float positionRemain = position;
			for (int i = currentTimingId; i < Timings.Count; i++)
			{
				int startTiming = i == currentTimingId ? currentTiming : Timings[i].Timing;
				int endTiming = i + 1 == Timings.Count ? allEndTime : Timings[i + 1].Timing;
				float bpm = i == -1 ? Timings[0].Bpm : Timings[i].Bpm;
				float delta = (endTiming - startTiming) * bpm / BaseBpm * Velocity;
				if (delta < positionRemain)
				{
					positionRemain -= delta;
					continue;
				}
				if (delta == 0)
				{
					return startTiming;
				}
				return Mathf.RoundToInt(Mathf.Lerp(startTiming, endTiming, positionRemain / delta));
			}
			return allEndTime;
		}

        // Keep the inverse near the edited endpoint when reversed tempo segments share a position.
        internal bool TryCalculateChartTimingByPosition(float position, ArcTimingGroup group, int referenceTiming, out int result)
        {
            result = referenceTiming;
            var events = GetTiming(group);
            if (events.Count == 0 || BaseBpm == 0 || float.IsNaN(position) || float.IsInfinity(position)) return false;
            double startPosition = CalculatePositionByTiming(events[0].Timing, group);
            double best = double.PositiveInfinity;
            for (int i = 0; i < events.Count; i++)
            {
                int start = events[i].Timing;
                int lower = i == 0 ? int.MinValue : start;
                int upper = i + 1 < events.Count ? events[i + 1].Timing : int.MaxValue;
                double velocity = (double)events[i].Bpm / BaseBpm * settingVelocity;
                double candidate = velocity == 0 ? System.Math.Max(lower, System.Math.Min(upper, referenceTiming))
                    : start + (position - startPosition) / velocity;
                // At a stop, use the same float integration as rendering; accumulated
                // segment rounding can otherwise hide an exactly matching plane.
                bool matchesStop = velocity == 0 && System.Math.Abs(position - CalculatePositionByTiming((int)candidate, group)) < .001;
                if ((velocity != 0 || matchesStop)
                    && !double.IsNaN(candidate) && !double.IsInfinity(candidate) && candidate >= lower && candidate <= upper)
                {
                    double distance = System.Math.Abs(candidate - referenceTiming);
                    if (distance < best) { best = distance; result = (int)System.Math.Round(candidate); }
                }
                if (i + 1 < events.Count) startPosition += ((long)upper - start) * velocity;
            }
            return !double.IsPositiveInfinity(best);
        }

		public int CalculateZeroLengthVoidArcDisappearTime(int ChartTiming, ArcTimingGroup timingGroup)
		{
			var Timings = GetTiming(timingGroup);
			if (Timings.Count == 0)
			{
				return ChartTiming;
			}
			int startTimingId = FindTimingIndex(Timings, ChartTiming);
			if (startTimingId == -1)
			{
				startTimingId = 0;
			}
			float distance = 0;
			float direction;
			if (Timings[0].Bpm == 0)
			{
				direction = Mathf.Sign(CalculatePositionByTimingAndStart(0, ChartTiming, timingGroup));
			}
			else
			{
				direction = Mathf.Sign(Timings[0].Bpm);
			}
			if (direction == 0)
			{
				direction = 1;
			}
			for (int i = startTimingId; i < Timings.Count; i++)
			{
				var currentTiming = i == startTimingId ? ChartTiming : Timings[i].Timing;
				var currentBpm = Timings[i].Bpm;
				if (distance < 0)
				{
					return currentTiming;
				}
				if (i == Timings.Count - 1)
				{
					if (currentBpm * direction > 0)
					{
						return Mathf.CeilToInt(currentTiming + distance / (currentBpm / BaseBpm * Velocity * direction));
					}
				}
				else
				{
					var nextTiming = Timings[i + 1].Timing;
					if (nextTiming == currentTiming)
					{
						continue;
					}
					if (currentBpm * direction > 0)
					{
						int resultTiming = Mathf.CeilToInt(currentTiming + distance / (currentBpm / BaseBpm * Velocity * direction));
						if (resultTiming < nextTiming)
						{
							return resultTiming;
						}
					}
					distance -= (nextTiming - currentTiming) * Timings[i].Bpm / BaseBpm * Velocity * direction;
				}
			}
			return int.MaxValue;
		}
		public float CalculateBpmByTiming(int ChartTiming, ArcTimingGroup timingGroup)
		{
			var Timings = GetTiming(timingGroup);
			if (Timings.Count == 0)
			{
				return 0;
			}
			if (ChartTiming < Timings[0].Timing)
			{
				return Timings[0].Bpm;
			}

			return Timings[FindTimingIndex(Timings, ChartTiming)].Bpm;
		}

		private void UpdateChartSpeedStatus()
		{
			int currentTiming = ArcGameplayManager.Instance.ChartTiming;
			if (Timings.Count == 0)
			{
				CurrentSpeed = 0;
				return;
			}
			int currentTimingId = FindTimingIndex(Timings, currentTiming);
			if (currentTimingId == -1)
			{
				currentTimingId = 0;
			}
			CurrentSpeed = Timings[currentTimingId].Bpm / BaseBpm;
		}
		private void UpdateRenderRange(ArcTimingGroup timingGroup, RenderCurve curve)
		{
            if (curve.Updated)
            {
                StoreRenderRange(timingGroup, curve.Earliest, curve.Latest);
                return;
            }
            var Timings = curve.Timings;
			int nearPosition = -100000;
			int farPosition = 100000;
			float earliestRenderTime, latestRenderTime;
			if (Timings.Count == 0)
			{
				earliestRenderTime = float.NegativeInfinity;
				latestRenderTime = float.PositiveInfinity;
			}
			else
			{
				int currentTiming = ArcGameplayManager.Instance.ChartTiming;
				int currentTimingId = FindTimingIndex(Timings, currentTiming);
				// One buffer for every group; each pass writes all entries it reads.
				if (timingPositions.Length < Timings.Count) timingPositions = new float[Mathf.NextPowerOfTwo(Timings.Count)];
				float[] TimingPosition = timingPositions;
				for (int i = currentTimingId; i + 1 < Timings.Count; i++)
				{
					int startTiming = i == currentTimingId ? currentTiming : Timings[i].Timing;
					int endTiming = Timings[i + 1].Timing;
					float startPosition = i == currentTimingId ? 0 : TimingPosition[i];
					float bpm = i == -1 ? Timings[0].Bpm : Timings[i].Bpm;
					TimingPosition[i + 1] = startPosition + (i == currentTimingId
                        ? (endTiming - startTiming) * bpm / BaseBpm * Velocity : curve.Steps[i]);
				}
				for (int i = currentTimingId; i >= 0; i--)
				{
					int startTiming = i == currentTimingId ? currentTiming : Timings[i + 1].Timing;
					int endTiming = Timings[i].Timing;
					float startPosition = i == currentTimingId ? 0 : TimingPosition[i + 1];
                    // Preserve the legacy unchecked subtraction even for a 2^31 ms span.
					TimingPosition[i] = startPosition + (i == currentTimingId
                        ? (endTiming - startTiming) * Timings[i].Bpm / BaseBpm * Velocity
                        : endTiming - startTiming == int.MinValue ? curve.Steps[i] : -curve.Steps[i]);
				}
				earliestRenderTime = float.PositiveInfinity;
				latestRenderTime = float.NegativeInfinity;
				int allBeginTime = ArcGameplayManager.Instance.AllBeginChartTiming;
				float allBeginPosition = TimingPosition[0] + (allBeginTime - Timings[0].Timing) * Timings[0].Bpm / BaseBpm * Velocity;
				int allEndTime = ArcGameplayManager.Instance.AllEndChartTiming;
				float allEndPosition = TimingPosition[Timings.Count - 1] + (allEndTime - Timings[Timings.Count - 1].Timing) * Timings[Timings.Count - 1].Bpm / BaseBpm * Velocity;

				for (int i = -1; i < Timings.Count; i++)
				{
					int startTime = i == -1 ? allBeginTime : Timings[i].Timing;
					int finishTime = i + 1 == Timings.Count ? allEndTime : Timings[i + 1].Timing;
					float startPosition = i == -1 ? allBeginPosition : TimingPosition[i];
					float finishPosition = i + 1 == Timings.Count ? allEndPosition : TimingPosition[i + 1];
					if (finishTime < startTime)
					{
						continue;
					}
					if (startPosition > farPosition && finishPosition > farPosition)
					{
						continue;
					}
					if (startPosition < nearPosition && finishPosition < nearPosition)
					{
						continue;
					}
					float nearTime;
					float farTime;
                    if ((startPosition >= nearPosition && startPosition <= farPosition
                        && finishPosition >= nearPosition && finishPosition <= farPosition)
                        || Mathf.Approximately(startPosition, finishPosition))
					{
						nearTime = startTime;
						farTime = finishTime;
					}
					else
					{
						nearTime = Mathf.Lerp(startTime, finishTime, Mathf.InverseLerp(startPosition, finishPosition, nearPosition));
						farTime = Mathf.Lerp(startTime, finishTime, Mathf.InverseLerp(startPosition, finishPosition, farPosition));
					}
					earliestRenderTime = Mathf.Min(earliestRenderTime, Mathf.Min(nearTime, farTime));
					latestRenderTime = Mathf.Max(latestRenderTime, Mathf.Max(nearTime, farTime));
				}

				if (Timings[0].Bpm == 0)
				{
					if (allBeginPosition <= farPosition && allBeginPosition >= nearPosition)
					{
						earliestRenderTime = Mathf.Min(earliestRenderTime, Mathf.Min(allBeginTime, float.NegativeInfinity));
						latestRenderTime = Mathf.Max(latestRenderTime, Mathf.Max(allBeginTime, float.NegativeInfinity));
					}
				}
				else
				{
					float beforeAllBeginTargetPosition = Timings[0].Bpm > 0 ? nearPosition : farPosition;
					float beforeAllBeginTime = allBeginTime + (beforeAllBeginTargetPosition - allBeginPosition) / (Timings[0].Bpm / BaseBpm * Velocity);
					if (beforeAllBeginTime <= allBeginTime)
					{
						earliestRenderTime = Mathf.Min(earliestRenderTime, Mathf.Min(allBeginTime, beforeAllBeginTime));
						latestRenderTime = Mathf.Max(latestRenderTime, Mathf.Max(allBeginTime, beforeAllBeginTime));
					}
				}

				if (Timings[Timings.Count - 1].Bpm == 0)
				{
					if (allEndPosition <= farPosition && allEndPosition >= nearPosition)
					{
						earliestRenderTime = Mathf.Min(earliestRenderTime, Mathf.Min(allEndTime, float.PositiveInfinity));
						latestRenderTime = Mathf.Max(latestRenderTime, Mathf.Max(allEndTime, float.PositiveInfinity));
					}
				}
				else
				{
					float afterAllEndTargetPosition = Timings[Timings.Count - 1].Bpm > 0 ? farPosition : nearPosition;
					float afterAllEndTime = allEndTime + (afterAllEndTargetPosition - allEndPosition) / (Timings[Timings.Count - 1].Bpm / BaseBpm * Velocity);
					if (afterAllEndTime >= allEndTime)
					{
						earliestRenderTime = Mathf.Min(earliestRenderTime, Mathf.Min(allEndTime, afterAllEndTime));
						latestRenderTime = Mathf.Max(latestRenderTime, Mathf.Max(allEndTime, afterAllEndTime));
					}
				}

				earliestRenderTime = Mathf.Min(earliestRenderTime, currentTiming);
				latestRenderTime = Mathf.Max(latestRenderTime, currentTiming);
			}
            curve.Earliest = earliestRenderTime;
            curve.Latest = latestRenderTime;
            curve.Updated = true;
            StoreRenderRange(timingGroup, earliestRenderTime, latestRenderTime);
        }

        private void StoreRenderRange(ArcTimingGroup timingGroup, float earliestRenderTime, float latestRenderTime)
        {
            if (timingGroup == null)
            {
                this.earliestRenderTime = earliestRenderTime;
				this.latestRenderTime = latestRenderTime;
			}
			else
			{
				timingGroup.earliestRenderTime = earliestRenderTime;
				timingGroup.latestRenderTime = latestRenderTime;
			}
		}
		internal void SubmitBeatlines(ArcNoteRenderer renderer, Camera camera)
		{
			if (!isActiveAndEnabled || !BeatlineLayer.gameObject.activeInHierarchy
				|| (camera.cullingMask & (1 << BeatlinePrefab.layer)) == 0) return;
			if (!beatlineStyle) beatlineStyle = BeatlinePrefab.GetComponent<SpriteRenderer>();
			if (!beatlineStyle.sprite) return;
			Matrix4x4 parent = BeatlineLayer.localToWorldMatrix;
			Quaternion rotation = BeatlinePrefab.transform.localRotation;
			Matrix4x4 flip = Matrix4x4.Scale(new Vector3(beatlineStyle.flipX ? -1 : 1, beatlineStyle.flipY ? -1 : 1, 1));
			foreach (int t in beatlineTimings)
			{
				if (!ShouldTryRender(t, null, 0, false))
				{
					continue;
				}
				float pos = CalculatePositionByTiming(t, null);
				if (pos > 100000 || pos < -100000)
				{
					continue;
				}
				float z = pos / 1000f;
				Matrix4x4 matrix = parent * Matrix4x4.TRS(new Vector3(0, 0, -z), rotation,
					new Vector3(1700 * (1 + BeatlineEnwidenRatio * 0.5f), 20 + z, 1)) * flip;
				renderer.SubmitSprite(beatlineStyle.sprite, matrix, beatlineStyle.color,
					beatlineStyle.sortingLayerName, beatlineStyle.sortingOrder, beatlineStyle.sharedMaterial.renderQueue);
			}
		}
		private void UpdateTrackSpeed()
		{
			phase += Velocity / 172.25f * 10.5f * (ArcGameplayManager.Instance.IsPlaying ? CurrentSpeed : 0) * Time.deltaTime;
			phase -= Mathf.Floor(phase);
			SetTrackPhase(phase);
		}
		// A property block keeps the scrolling phase off the shared track material assets.
		private void SetTrackPhase(float value)
		{
			phaseBlock ??= new MaterialPropertyBlock();
			foreach (var renderer in TrackComponentRenderers)
			{
				renderer.GetPropertyBlock(phaseBlock);
				phaseBlock.SetFloat(phaseShaderId, value);
				renderer.SetPropertyBlock(phaseBlock);
			}
		}

		public void ReOrderTimingGroup(ArcTimingGroup timingGroup)
		{
			var Timings = GetTiming(timingGroup);
			var timings = Timings.OrderBy((timing) => timing.Timing).ToList();
			Timings.Clear();
			Timings.AddRange(timings);
		}
		public void AddTiming(ArcTiming newTiming, ArcTimingGroup timingGroup)
		{
			var Timings = GetTiming(timingGroup);
			Timings.Add(newTiming);
			ReOrderTimingGroup(timingGroup);
			OnTimingChange(timingGroup);
		}
		public void RemoveTiming(ArcTiming timing, ArcTimingGroup timingGroup)
		{
			GetTiming(timingGroup).Remove(timing);
			OnTimingChange(timingGroup);
		}
		public void UpdateTimingGroup(ArcTimingGroup timingGroup)
		{
			ReOrderTimingGroup(timingGroup);
			OnTimingChange(timingGroup);
		}
		public void OnTimingChange(ArcTimingGroup timingGroup)
		{
			renderRangeVersion++;
			ArcScoreManager.Invalidate();
			if (timingGroup == null)
			{
				CalculateBeatlineTimes();
			}
			AdeTimingEditor.Instance.ForceUpdateTimingGroup(timingGroup);
		}
		public void AddTimingGroup(ArcTimingGroup timingGroup)
		{
			if (timingGroup.Id > timingGroups.Count + 1)
			{
				timingGroup.Id = timingGroups.Count + 1;
			}
			if (timingGroup.Id < 1)
			{
				timingGroup.Id = 1;
			}
			int pos = timingGroup.Id - 1;
			timingGroups.Insert(pos, timingGroup);
			for (int i = pos + 1; i < timingGroups.Count; i++)
			{
				timingGroups[i].Id = i + 1;
			}
			OnTimingGroupChange();
		}
		public void RemoveTimingGroup(ArcTimingGroup timingGroup)
		{
			int pos = timingGroup.Id - 1;
			timingGroups.Remove(timingGroup);
			for (int i = pos; i < timingGroups.Count; i++)
			{
				timingGroups[i].Id = i + 1;
			}
			OnTimingGroupChange();
		}
		public void OnTimingGroupChange()
		{
			renderRangeVersion++;
			AdeTimingEditor.Instance.ForceUpdateAll();
		}
		// Note: this is a function used to optimize rendering by avoid not needed position calculation
		// Invoker should manually check position again after this check passed
        internal void GetRenderRange(ArcTimingGroup group, out float earliest, out float latest)
        {
            earliest = group == null ? earliestRenderTime : group.earliestRenderTime;
            latest = group == null ? latestRenderTime : group.latestRenderTime;
        }
		public bool ShouldTryRender(int Timing, ArcTimingGroup timingGroup, int duration = 0, bool note = true)
		{
			float earliestRenderTime;
			float latestRenderTime;
			if (timingGroup == null)
			{
				earliestRenderTime = this.earliestRenderTime;
				latestRenderTime = this.latestRenderTime;
			}
			else
			{
				earliestRenderTime = timingGroup.earliestRenderTime;
				latestRenderTime = timingGroup.latestRenderTime;
			}
			if (Timing + duration >= earliestRenderTime && Timing <= latestRenderTime)
			{
				if (note && Timing + duration + LostDelay < ArcGameplayManager.Instance.ChartTiming)
				{
					return false;
				}
				return true;
			}
			return false;
		}
	}
}
