using Affdata.Core.Geometry;
using Affdata.Core.Chart;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Arcade.Aff;
using Arcade.Util.Misc;
using UnityEngine;

namespace Arcade.Gameplay.Chart
{
	public enum ChartSortMode
	{
		Timing,
		Type
	}
	public class ArcChart
	{
		public int AudioOffset;
		public float TimingPointDensityFactor;
		public Dictionary<string, string> AdditionalMetadata = new Dictionary<string, string>();
		public List<ArcTap> Taps = new List<ArcTap>();
		public List<ArcHold> Holds = new List<ArcHold>();
		public List<ArcTiming> Timings = new List<ArcTiming>();
		public List<ArcSlide> Slides = new List<ArcSlide>();
		public List<ArcArc> Arcs = new List<ArcArc>();
		public List<ArcCamera> Cameras = new List<ArcCamera>();
		public List<ArcSceneControl> SceneControl = new List<ArcSceneControl>();
		public List<ArcTimingGroup> TimingGroups = new List<ArcTimingGroup>();
		public int LastEventTiming = 0;

		public ArcChart(RawAffChart raw)
		{
			AudioOffset = raw.AudioOffset;
			TimingPointDensityFactor = raw.TimingPointDensityFactor;
			AdditionalMetadata = raw.additionalMetadata;
			foreach (var item in raw.items)
			{
				addRawItem(item, null);
			}
		}

		void addRawItem(IRawAffItem item, ArcTimingGroup timingGroup)
		{
			if (item is RawAffTiming rawTiming)
			{
				if (timingGroup == null)
				{
					Timings.Add(new ArcTiming(rawTiming));
				}
				else
				{
					timingGroup.Timings.Add(new ArcTiming(rawTiming));
				}
			}
			else if (item is RawAffTap rawTap)
			{
				Taps.Add(new ArcTap(rawTap, timingGroup));
			}
			else if (item is RawAffHold rawHold)
			{
				Holds.Add(new ArcHold(rawHold, timingGroup));
			}
			else if (item is RawAffSlide rawSlide)
			{
				Slides.Add(new ArcSlide(rawSlide, timingGroup));
			}
			else if (item is RawAffArc rawArc)
			{
				Arcs.Add(new ArcArc(rawArc, timingGroup));
			}
			else if (item is RawAffCamera rawCamera)
			{
				Cameras.Add(new ArcCamera(rawCamera, timingGroup));
			}
			else if (item is RawAffSceneControl rawSceneControl)
			{
				SceneControl.Add(new ArcSceneControl(rawSceneControl, timingGroup));
			}
			else if (item is RawAffTimingGroup rawTimingGroup)
			{
				ArcTimingGroup arcTimingGroup = new ArcTimingGroup()
				{
					Id = TimingGroups.Count + 1,
				};
				arcTimingGroup.ApplyAttributes(rawTimingGroup.Attributes);
				TimingGroups.Add(arcTimingGroup);
				foreach (var nestedItem in rawTimingGroup.Items)
				{
					addRawItem(nestedItem, arcTimingGroup);
				}
			}
		}

		public void Serialize(Stream stream, ChartSortMode mode = ChartSortMode.Timing, bool lowPrecision = false)
			=> CaptureSerializer(mode, lowPrecision)(stream);

		private sealed class SaveGroup
		{
			public readonly string Attributes;
			private readonly List<(int Time, int Type, IRawAffNestableItem Item)> events = new List<(int, int, IRawAffNestableItem)>();
			public SaveGroup(string attributes) => Attributes = attributes;
			public void Add(int time, int type, IRawAffNestableItem item) => events.Add((time, type, item));
			public IEnumerable<IRawAffNestableItem> Sorted(ChartSortMode mode)
			{
				IEnumerable<(int Time, int Type, IRawAffNestableItem Item)> ordered = events;
				if (mode == ChartSortMode.Type) ordered = events.OrderBy(e => e.Type).ThenBy(e => e.Time);
				if (mode == ChartSortMode.Timing) ordered = events.OrderBy(e => e.Time).ThenBy(e => e.Type);
				return ordered.Select(e => e.Item);
			}
		}

		// Capture on the editor thread. Each writer owns a single detached AFF snapshot.
		internal Action<Stream> CaptureSerializer(ChartSortMode mode, bool lowPrecision)
		{
			var raw = new RawAffChart
			{
				AudioOffset = ArcGameplayManager.Instance.ChartAudioOffset,
				TimingPointDensityFactor = TimingPointDensityFactor,
				additionalMetadata = new Dictionary<string, string>(AdditionalMetadata),
			};
			var groups = new List<SaveGroup>(TimingGroups.Count + 1) { new SaveGroup(null) };
			var groupIndices = new Dictionary<ArcTimingGroup, int>(TimingGroups.Count);
			foreach (var group in TimingGroups)
			{
				var bucket = new SaveGroup(group.Attributes);
				groupIndices.Add(group, groups.Count);
				groups.Add(bucket);
				foreach (var timing in group.Timings)
					bucket.Add(timing.Timing, 1, (RawAffTiming)timing.IntoRawItem());
			}
			int GroupIndex(ArcTimingGroup group) => group == null ? 0 : groupIndices[group];
			void Capture<T>(List<T> notes, int type) where T : ArcEvent, IIntoRawItem
			{
				foreach (var note in notes)
					groups[GroupIndex((note as IHasTimingGroup)?.TimingGroup)].Add(note.Timing, type, (IRawAffNestableItem)note.IntoRawItem());
			}
			Capture(Timings, 1);
			Capture(Cameras, 5);
			Capture(SceneControl, 6);
			Capture(Taps, 2);
			Capture(Holds, 3);
			Capture(Arcs, 4);

			var slides = new (ArcSlide.SaveData Data, int Group)[Slides.Count];
			ArcTimingGroup previous = null;
			int groupIndex = 0;
			for (int i = 0; i < slides.Length; i++)
			{
				var note = Slides[i];
				if (previous != note.TimingGroup)
				{
					previous = note.TimingGroup;
					groupIndex = GroupIndex(previous);
				}
				slides[i] = (new ArcSlide.SaveData(note), groupIndex);
			}
			return stream =>
			{
				foreach (var slide in slides)
					groups[slide.Group].Add(slide.Data.Timing, 7, slide.Data.ToRaw());
				raw.items.AddRange(groups[0].Sorted(mode));
				for (int i = 1; i < groups.Count; i++)
					raw.items.Add(new RawAffTimingGroup { Attributes = groups[i].Attributes, Items = groups[i].Sorted(mode).ToList() });
				ArcaeaFileFormat.DumpToStream(stream, raw, lowPrecision);
			};
		}
	}
	public interface ISelectable
	{
		bool Selected { get; set; }
	}
	public interface IHasTimingGroup
	{
		ArcTimingGroup TimingGroup { get; }
	}
	public static class ArcChartExtension
	{
		public static bool NoInput(this IHasTimingGroup hasTimingGroup)
		{
			return hasTimingGroup.TimingGroup?.NoInput ?? false;
		}
		public static bool GroupHide(this IHasTimingGroup hasTimingGroup)
		{
			return hasTimingGroup.TimingGroup?.GroupHide ?? false;
		}
		public static Vector3 FallDirection(this IHasTimingGroup hasTimingGroup)
		{
			int angleX = hasTimingGroup.TimingGroup?.AngleX ?? 0;
			int angleY = hasTimingGroup.TimingGroup?.AngleY ?? 0;
			Vector3 direction = new Vector3(0, 0, 1);
			direction = Quaternion.AngleAxis((float)(angleX) / 10, new Vector3(1, 0, 0)) * direction;
			direction = Quaternion.AngleAxis((float)(angleY) / 10, new Vector3(0, -1, 0)) * direction;
			return direction;
		}
		public static bool FadingHolds(this IHasTimingGroup hasTimingGroup)
		{
			return hasTimingGroup.TimingGroup?.FadingHolds ?? false;
		}
	}
	public interface ISetableTimingGroup
	{
		ArcTimingGroup TimingGroup { set; }
	}
	public enum ArcLineType
	{
		TrueIsVoid,
		FalseNotVoid,
		Designant
	}

	public enum ArcCurveType
	{
		B = (int)Affdata.Core.Geometry.ArcCurveType.B,
		S = (int)Affdata.Core.Geometry.ArcCurveType.S,
		Si = (int)Affdata.Core.Geometry.ArcCurveType.Si,
		So = (int)Affdata.Core.Geometry.ArcCurveType.So,
		SiSi = (int)Affdata.Core.Geometry.ArcCurveType.SiSi,
		SiSo = (int)Affdata.Core.Geometry.ArcCurveType.SiSo,
		SoSi = (int)Affdata.Core.Geometry.ArcCurveType.SoSi,
		SoSo = (int)Affdata.Core.Geometry.ArcCurveType.SoSo,
	}
	public enum CameraEaseType
	{
		L,
		Qi,
		Qo,
		Reset,
		S
	}
	public enum SceneControlType
	{
		TrackHide,
		TrackShow,
		HideGroup,
		EnwidenCamera,
		EnwidenLanes,
		TrackDisplay,
		Unknown,
        ArcahvDistort,
        ArcahvDebris,
        Redline,
	}
	public abstract class ArcEvent
	{
		internal static long RenderIndexVersion { get; private set; }
        internal static void InvalidateRenderIndex() => RenderIndexVersion++;
        protected virtual void OnTimingChanged() => InvalidateRenderIndex();
        private int timing;
        public int Timing
        {
            get => timing;
            set { if (timing == value) return; timing = value; OnTimingChanged(); }
        }
		public virtual void Assign(ArcEvent newValues)
		{
			ArcScoreManager.Invalidate();
			Timing = newValues.Timing;
		}
		public abstract ArcEvent Clone();
	}
	public class ArcSceneControl : ArcEvent, IIntoRawItem, IHasTimingGroup, ISetableTimingGroup
	{

		public ArcSceneControl()
		{
		}
		public ArcSceneControl(RawAffSceneControl rawAffSceneControl, ArcTimingGroup timingGroup)
		{
			Timing = rawAffSceneControl.Timing;
			RawType = rawAffSceneControl.Type;
			RawParams = rawAffSceneControl.Params;
			if (RawType == "trackhide" && RawParams.Count == 0)
			{
				Type = SceneControlType.TrackHide;
			}
			else if (RawType == "trackshow" && RawParams.Count == 0)
			{
				Type = SceneControlType.TrackShow;
			}
			else if (RawParams.Count == 2 && RawParams[1] is RawAffInt argument
				&& (RawParams[0] is RawAffFloat || RawParams[0] is RawAffInt))
			{
				float seconds = RawParams[0] is RawAffFloat number ? number.data : ((RawAffInt)RawParams[0]).data;
				switch (RawType)
				{
					case "trackdisplay":
						Type = SceneControlType.TrackDisplay;
						TrackDisplayValue = argument.data;
						break;
					case "hidegroup": Type = SceneControlType.HideGroup; break;
					case "enwidencamera": Type = SceneControlType.EnwidenCamera; break;
					case "enwidenlanes": Type = SceneControlType.EnwidenLanes; break;
					case "arcahvdistort" when seconds >= 0: Type = SceneControlType.ArcahvDistort; break;
					case "arcahvdebris" when seconds >= 0: Type = SceneControlType.ArcahvDebris; break;
					case "redline" when seconds >= 0: Type = SceneControlType.Redline; break;
				}
				if (Type != SceneControlType.Unknown)
				{
					Duration = seconds;
					if (Type == SceneControlType.HideGroup || Type == SceneControlType.EnwidenCamera || Type == SceneControlType.EnwidenLanes)
						Enable = argument.data > 0;
					if (Type == SceneControlType.ArcahvDistort || Type == SceneControlType.ArcahvDebris || Type == SceneControlType.Redline)
						EffectValue = argument.data;
				}
			}
			TimingGroup = timingGroup;
		}
		public IRawAffItem IntoRawItem()
		{
			var item = new RawAffSceneControl()
			{
				Timing = Timing,
				Type = "unknown",
				Params = new List<IRawAffValue>(),
			};
			if (Type == SceneControlType.TrackHide)
			{
				item.Type = "trackhide";
			}
			else if (Type == SceneControlType.TrackShow)
			{
				item.Type = "trackshow";
			}
			else if (Type == SceneControlType.TrackDisplay)
			{
				item.Type = "trackdisplay";
				item.Params = new List<IRawAffValue>{
					new RawAffFloat{data=Duration},
					new RawAffInt{data=TrackDisplayValue},
				};
			}
			else if (Type == SceneControlType.HideGroup)
			{
				item.Type = "hidegroup";
				item.Params = new List<IRawAffValue>{
					new RawAffFloat{data=Duration},
					new RawAffInt{data=Enable?1:0},
				};
			}
			else if (Type == SceneControlType.EnwidenCamera)
			{
				item.Type = "enwidencamera";
				item.Params = new List<IRawAffValue>{
					new RawAffFloat{data=Duration},
					new RawAffInt{data=Enable?1:0},
				};
			}
			else if (Type == SceneControlType.EnwidenLanes)
			{
				item.Type = "enwidenlanes";
				item.Params = new List<IRawAffValue>{
					new RawAffFloat{data=Duration},
					new RawAffInt{data=Enable?1:0},
				};
			}
            else if (Type == SceneControlType.ArcahvDistort || Type == SceneControlType.ArcahvDebris || Type == SceneControlType.Redline)
            {
                item.Type = Type == SceneControlType.ArcahvDistort ? "arcahvdistort"
                    : Type == SceneControlType.ArcahvDebris ? "arcahvdebris" : "redline";
                item.Params = new List<IRawAffValue>
                {
                    new RawAffFloat { data = Duration }, new RawAffInt { data = EffectValue }
                };
            }

			else
			{
				item.Type = RawType;
				item.Params = CopyParams(RawParams);
			}
			return item;
		}
		public SceneControlType Type = SceneControlType.Unknown;
		public bool Enable;
		public float Duration;
		public int TrackDisplayValue;
        public int EffectValue;
		private ArcTimingGroup timingGroup;
        public ArcTimingGroup TimingGroup
        {
            get => timingGroup;
            set { if (timingGroup == value) return; timingGroup = value; InvalidateRenderIndex(); }
        }
		public string RawType;
		public List<IRawAffValue> RawParams;

		public override ArcEvent Clone()
		{
			var copy = new ArcSceneControl();
			copy.Assign(this);
			return copy;
		}
		public override void Assign(ArcEvent newValues)
		{
			base.Assign(newValues);
			var sc = (ArcSceneControl)newValues;
			Type = sc.Type;
			Enable = sc.Enable;
			Duration = sc.Duration;
			TrackDisplayValue = sc.TrackDisplayValue;
			EffectValue = sc.EffectValue;
			TimingGroup = sc.TimingGroup;
			RawType = sc.RawType;
			RawParams = CopyParams(sc.RawParams);
		}
		private static List<IRawAffValue> CopyParams(List<IRawAffValue> values)
		{
			return values?.Select(value => value is RawAffInt i ? (IRawAffValue)new RawAffInt { data = i.data }
				: value is RawAffFloat f ? new RawAffFloat { data = f.data }
				: (IRawAffValue)new RawAffWord { data = ((RawAffWord)value).data }).ToList();
		}
	}
	public abstract class ArcNote : ArcEvent, ISelectable
	{
		// This indicated if the sound effect or tap particle of the note is played
		public bool Judged;
		public float Position;

		protected bool enable;
		public virtual bool Enable
		{
			get
			{
				return enable;
			}
			set
			{
				enable = value;
			}
		}
		public abstract bool Selected { get; set; }

        // Notes own no GameObject. Subclasses release their pooled render state here.
        protected virtual void ReleaseVisual()
        {
            enable = false;
        }

		public abstract void Instantiate();
		public virtual void Destroy()
        {
            ReleaseVisual();
        }

	}
	public abstract class ArcLongNote : ArcNote
	{
		private int endTiming;
        public int EndTiming
        {
            get => endTiming;
            set { if (endTiming == value) return; endTiming = value; OnTimingChanged(); }
        }
		public override void Assign(ArcEvent newValues)
		{
			base.Assign(newValues);
			ArcLongNote n = newValues as ArcLongNote;
			EndTiming = n.EndTiming;
		}

		// This indicate if long note particle present
		public bool Judging;

		// This two should be replaced by judged
		public bool ShouldPlayAudio;
		public bool AudioPlayed;

		// The times where combo increase
		public List<int> JudgeTimings = new List<int>();
        internal ArcDenseJudgements DenseJudgements { get; private set; }
        public double JudgementCount => DenseJudgements?.Count ?? JudgeTimings.Count;
        public int FirstJudgementTiming => DenseJudgements != null ? (DenseJudgements.Count > 0 ? DenseJudgements.First : Timing)
            : JudgeTimings.Count > 0 ? JudgeTimings[0] : Timing;

        internal void ClearJudgeTimings()
        {
            JudgeTimings.Clear();
            DenseJudgements = null;
        }

        protected void BuildJudgeTimings(double interval, int firstIndex)
        {
            double total = Math.Floor(((long)EndTiming - Timing) / interval);
            if (firstIndex >= total || double.IsNaN(interval) || interval <= 0)
            {
                JudgeTimings.Add((int)(Timing + ((long)EndTiming - Timing) * .5f));
                return;
            }
            const int MaterializedLimit = 4096;
            if (total > MaterializedLimit)
            {
                DenseJudgements = new ArcDenseJudgements(Timing, EndTiming, interval, firstIndex, total);
                // Drop the old array when an edited note switches to a dense run.
                if (JudgeTimings.Capacity > 0) JudgeTimings.Capacity = 0;
                return;
            }
            for (int n = firstIndex; n < (int)total; n++)
            {
                int t = (int)(Timing + n * interval);
                if (t < EndTiming) JudgeTimings.Add(t);
            }
        }

        internal double CountJudgementsAtOrBefore(int timing)
        {
            if (timing < Timing) return 0;
            if (DenseJudgements != null) return DenseJudgements.CountAtOrBefore(timing);
            int low = 0, high = JudgeTimings.Count;
            while (low < high)
            {
                int middle = low + (high - low) / 2;
                if (JudgeTimings[middle] <= timing) low = middle + 1;
                else high = middle;
            }
            return low;
        }

        internal bool HasJudgementBetween(int previous, int current)
        {
            // Visual effects use raw tick times; score clamping to the note start
            // remains in CountJudgementsAtOrBefore / the normal score cache.
            if (DenseJudgements != null)
                return DenseJudgements.CountAtOrBefore(current) > DenseJudgements.CountAtOrBefore(previous);
            int low = 0, high = JudgeTimings.Count;
            while (low < high)
            {
                int middle = low + (high - low) / 2;
                if (JudgeTimings[middle] <= previous) low = middle + 1;
                else high = middle;
            }
            return low < JudgeTimings.Count && JudgeTimings[low] <= current;
        }


	}
	public class ArcTap : ArcNote, IIntoRawItem, IHasTimingGroup, ISetableTimingGroup
	{
        internal Matrix4x4 RenderMatrix;
        internal int ResidentIndex = -1, GpuPresentationFrame = -1;
		private int track;
        public int Track { get => track; set { track = value; FloatLane = null; ArcNoteRenderer.UpdateResidentTap(this); } }
        private float? floatLane;
        public float? FloatLane { get => floatLane; set { if (floatLane == value) return; floatLane = value; ArcNoteRenderer.UpdateResidentTap(this); } }
        private float width = 1;
        public float Width
        {
            get => width;
            set { if (width == value) return; width = value; ArcNoteRenderer.UpdateResidentTap(this); }
        }
        public bool IsMine;
        public float WorldX => LanePosition.WorldX(Track, FloatLane);
		private ArcTimingGroup timingGroup;
        public ArcTimingGroup TimingGroup
        {
            get => timingGroup;
            set { if (timingGroup == value) return; timingGroup = value; InvalidateRenderIndex(); }
        }

		internal readonly HashSet<ArcArcTap> ConnectedArcTaps = new HashSet<ArcArcTap>();

		public ArcTap()
		{
		}
		public ArcTap(RawAffTap rawAffTap, ArcTimingGroup timingGroup)
		{
			Timing = rawAffTap.Timing;
			Track = rawAffTap.Track;
            FloatLane = rawAffTap.FloatLane;
            Width = rawAffTap.Width;
            IsMine = rawAffTap.IsMine;
			TimingGroup = timingGroup;
		}

		public IRawAffItem IntoRawItem()
		{
			return new RawAffTap()
			{
				Timing = Timing,
				Track = Track,
                FloatLane = FloatLane,
                Width = Width,
                IsMine = IsMine,
			};
		}

		public float Alpha { get; set; }
		private bool selected;
        public override bool Selected
        {
            get => selected;
            set { if (selected == value) return; selected = value; ArcNoteRenderer.UpdateResidentTap(this); }
        }
		public override void Destroy()
		{
			base.Destroy();
            foreach (var arcTap in ConnectedArcTaps) arcTap.ForgetConnection(this);
            ConnectedArcTaps.Clear();
		}
		public override ArcEvent Clone()
		{
			return new ArcTap()
			{
				Timing = Timing,
				Track = Track,
                FloatLane = FloatLane,
                Width = Width,
                IsMine = IsMine,
				TimingGroup = TimingGroup
			};
		}
		public override void Assign(ArcEvent newValues)
		{
			base.Assign(newValues);
			ArcTap n = newValues as ArcTap;
			Track = n.Track;
            FloatLane = n.FloatLane;
            Width = n.Width;
            IsMine = n.IsMine;
			TimingGroup = n.TimingGroup;
		}
		public override void Instantiate()
		{
            // Ground notes are rendered and picked directly from their render matrices.
        }
		public void SetupArcTapConnection()
		{
			ArcScoreManager.Invalidate();
            foreach (var arcTap in ConnectedArcTaps) arcTap.ForgetConnection(this);
            ConnectedArcTaps.Clear();
			if (this.NoInput() || IsMine) return;
			foreach (var arc in ArcArcManager.Instance.Arcs)
			{
				if (arc.NoInput()) continue;
				if (arc.IsVariousSizedArctap)
				{
					var arctap = arc.ConvertedVariousSizedArctap;
					if (Mathf.Abs(arctap.Timing - Timing) <= 1)
					{
						arctap.SetupArcTapConnection();
					}
				}
				if (arc.ArcTaps == null) continue;
				foreach (var arctap in arc.ArcTaps)
				{
					if (Mathf.Abs(arctap.Timing - Timing) <= 1)
					{
						arctap.SetupArcTapConnection();
					}
				}
			}
		}
	}
	public class ArcHold : ArcLongNote, IIntoRawItem, IHasTimingGroup, ISetableTimingGroup
	{
        // Transient editor state: keep a moving endpoint visible across the judgement line.
        private bool isEditing;
        public bool IsEditing
        {
            get => isEditing;
            set
            {
                isEditing = value;
                Judged = false;
                Judging = false;
            }
        }

        internal Matrix4x4 RenderMatrix;
		private int track;
        public int Track { get => track; set { track = value; FloatLane = null; } }
        public float? FloatLane;
        public float Width = 1;
        public float WorldX => LanePosition.WorldX(Track, FloatLane);
		private ArcTimingGroup timingGroup;
        public ArcTimingGroup TimingGroup
        {
            get => timingGroup;
            set { if (timingGroup == value) return; timingGroup = value; InvalidateRenderIndex(); }
        }


		public override void Destroy()
		{
			base.Destroy();
            if (ArcEffectManager.Instance) ArcEffectManager.Instance.RemoveFloatHoldNoteEffect(this);
			ClearJudgeTimings();
		}
		public override ArcEvent Clone()
		{
			return new ArcHold()
			{
				Timing = Timing,
				EndTiming = EndTiming,
				Track = Track,
                FloatLane = FloatLane,
                Width = Width,
				TimingGroup = TimingGroup,
			};
		}
		public override void Assign(ArcEvent newValues)
		{
			base.Assign(newValues);
			ArcHold n = newValues as ArcHold;
			Track = n.Track;
            FloatLane = n.FloatLane;
            Width = n.Width;
			TimingGroup = n.TimingGroup;
			CalculateJudgeTimings();
		}
		public override void Instantiate()
		{
            CalculateJudgeTimings();
        }

        public void CalculateJudgeTimings()
        {
            ArcScoreManager.Invalidate();
            ClearJudgeTimings();
            double bpm = Math.Abs(ArcTimingManager.Instance.CalculateBpmByTiming(Timing, TimingGroup));
            double interval = 60000f / bpm / (bpm >= 255 ? 1 : 2) / ArcGameplayManager.Instance.TimingPointDensityFactor;
            BuildJudgeTimings(interval, 1);
        }
		public float From { get; set; } = 0;
		public float To { get; set; } = 1;
		public float Alpha { get; set; } = 1;
		public override bool Selected { get; set; }
		public bool Highlight { get; set; }

		public int FlashCount;


		public ArcHold()
		{
		}
		public ArcHold(RawAffHold rawAffHold, ArcTimingGroup timingGroup)
		{
			Timing = rawAffHold.Timing;
			EndTiming = rawAffHold.EndTiming;
			Track = rawAffHold.Track;
            FloatLane = rawAffHold.FloatLane;
            Width = rawAffHold.Width;
			TimingGroup = timingGroup;
		}
		public IRawAffItem IntoRawItem()
		{
			return new RawAffHold()
			{
				Timing = Timing,
				EndTiming = EndTiming,
				Track = Track,
                FloatLane = FloatLane,
                Width = Width,
			};
		}
	}
	public class ArcTiming : ArcEvent, IIntoRawItem
	{
		public float Bpm;
		public float BeatsPerLine;

		public ArcTiming()
		{
		}

		public ArcTiming(RawAffTiming rawAffTiming)
		{
			Timing = rawAffTiming.Timing;
			Bpm = rawAffTiming.Bpm;
			BeatsPerLine = rawAffTiming.BeatsPerLine;
		}
		public IRawAffItem IntoRawItem()
		{
			return new RawAffTiming()
			{
				Timing = Timing,
				Bpm = Bpm,
				BeatsPerLine = BeatsPerLine,
			};
		}

		public override ArcEvent Clone()
		{
			return new ArcTiming()
			{
				Timing = Timing,
				Bpm = Bpm,
				BeatsPerLine = BeatsPerLine
			};
		}
		public override void Assign(ArcEvent newValues)
		{
			base.Assign(newValues);
			ArcTiming n = newValues as ArcTiming;
			Bpm = n.Bpm;
			BeatsPerLine = n.BeatsPerLine;
		}
	}
	public class ArcArcTap : ArcNote, IHasTimingGroup
	{
		public ArcArc Arc;
		public ArcTimingGroup TimingGroup { get => Arc.TimingGroup; }

		public bool IsConvertedVariousSizedArctap = false;
        // Kept so the chart saves unchanged; drawing still uses the default size.
        public float Width = 1;
        public bool IsMine;

		// ArcNoteRenderer draws the model and shadow from these values, as in AffdataPlay.
		private float rootZ;
		internal Matrix4x4 GetParentMatrix(Matrix4x4 layer) => layer * Matrix4x4.Translate(new Vector3(0, 0, rootZ));
		internal Matrix4x4 ModelMatrix
		{
			get
			{
				Vector3 centre = Arc.IsSfx ? NoteMeshDefinitions.SfxArcTapCenter : NoteMeshDefinitions.ArcTapCenter;
				Vector3 position = localPosition + Vector3.up * (centre.y - NoteMeshDefinitions.ArcTapAnchorYOffset);
				return Matrix4x4.TRS(position, Quaternion.identity, new Vector3(localScale * BaseModelScale.x, BaseModelScale.y, BaseModelScale.z));
			}
		}
		internal Color ShadowColor => ArcSkinManager.Instance.IsDarkSide
			? new Color(1, 1, 1, currentAlpha * (70f / 255f))
			: new Color(0, 0, 0, currentAlpha * (100f / 255f));

		public override void Destroy()
		{
			RemoveArcTapConnection();
			base.Destroy();
			currentAlpha = 1;
			currentTintColor = Color.white;
		}
		public override ArcEvent Clone()
		{
			return new ArcArcTap()
			{
				Timing = Timing,
				Width = Width,
				IsMine = IsMine,
			};
		}
		public override void Assign(ArcEvent newValues)
		{
			base.Assign(newValues);
			var n = (ArcArcTap)newValues;
			Width = n.Width;
			IsMine = n.IsMine;
		}
		public void Instantiate(ArcArc arc)
		{
			Arc = arc;
            UpdatePosition();
            UpdateScale();
            UpdateColor();
            SetupArcTapConnection();
        }

		public void UpdatePosition()
		{
			ArcTimingManager timingManager = ArcTimingManager.Instance;
			float t = 1f * (Timing - Arc.Timing) / (Arc.EndTiming - Arc.Timing);
			Vector3 parentLocalPosition = new Vector3(0, 0, -timingManager.CalculatePositionByTiming(Arc.Timing, Arc.TimingGroup) / 1000f);
			Vector3 baseLocalPosition = new Vector3();
			if (IsConvertedVariousSizedArctap)
			{
				baseLocalPosition = new Vector3(ArcAlgorithm.ArcXToWorld((Arc.XStart + Arc.XEnd) / 2f), ArcAlgorithm.ArcYToWorld(Arc.YStart) - 0.5f, 0);
			}
			else
			{
				baseLocalPosition = new Vector3(ArcAlgorithm.ArcXToWorld(ArcAlgorithm.X(Arc.XStart, Arc.XEnd, t, Arc.CurveType)),
										ArcAlgorithm.ArcYToWorld(ArcAlgorithm.Y(Arc.YStart, Arc.YEnd, t, Arc.CurveType)) - 0.5f, 0);
			}
			Vector3 offsetLocalPosition = -timingManager.CalculatePositionByTiming(Timing, Arc.TimingGroup) / 1000f * this.FallDirection();
			Vector3 additionalLocalPosition = new Vector3(0, 0, -0.6f);
			rootZ = parentLocalPosition.z;
            LocalPosition = baseLocalPosition + offsetLocalPosition - parentLocalPosition + additionalLocalPosition;
		}

		public void UpdateScale()
		{
            // CalculateVelocityAtTiming's position-units/ms equal world-units/s here.
            shadowDepthScale = NoteMeshDefinitions.ArcTapShadowDepthScale(
                ArcTimingManager.Instance.CalculateVelocityAtTiming(Timing, Arc.TimingGroup));
			if (IsConvertedVariousSizedArctap)
			{
				LocalScale = Mathf.Abs(Arc.XEnd - Arc.XStart) * 2;
			}
			else
			{
				LocalScale = 1.0f;
			}
		}

		public void UpdateColor()
		{
			if (Arc.Designant && !Arc.IsSfx)
			{
				TintColor = ArcArcManager.Instance.ArcTapDesignant;
			}
			else
			{
				TintColor = Color.white;
			}
		}

		/// <summary>
		/// Please use the overload method.
		/// </summary>
		public override void Instantiate()
		{
			throw new NotImplementedException();
		}

        public void SetupArcTapConnection()
        {
            RemoveArcTapConnection();
            if (Arc == null || Arc.NoInput() || Arc.Designant || ((Arc.EndTiming - Arc.Timing) == 0 && !IsConvertedVariousSizedArctap)) return;
            foreach (var tap in ArcTapNoteManager.Instance.GetConnectionCandidates(Timing))
            {
                if (Mathf.Abs(tap.Timing - Timing) > 1 || tap.NoInput() || tap.IsMine) continue;
                connectedTaps.Add(tap);
                tap.ConnectedArcTaps.Add(this);
            }
        }

        internal Vector2 GetConnectionPosition()
        {
            if (IsConvertedVariousSizedArctap)
                return new Vector2(ArcAlgorithm.ArcXToWorld((Arc.XStart + Arc.XEnd) / 2f), ArcAlgorithm.ArcYToWorld(Arc.YStart) - 0.5f);
            float progress = (Timing - Arc.Timing) / (float)(Arc.EndTiming - Arc.Timing);
            return new Vector2(ArcAlgorithm.ArcXToWorld(ArcAlgorithm.X(Arc.XStart, Arc.XEnd, progress, Arc.CurveType)),
                ArcAlgorithm.ArcYToWorld(ArcAlgorithm.Y(Arc.YStart, Arc.YEnd, progress, Arc.CurveType)) - 0.5f);
        }

        internal void ForgetConnection(ArcTap tap)
        {
            connectedTaps.Remove(tap);
        }

        public void RemoveArcTapConnection()
        {
            foreach (var tap in connectedTaps) tap.ConnectedArcTaps.Remove(this);
            connectedTaps.Clear();
        }

		public void Relocate()
		{
			ArcScoreManager.Invalidate();
			ArcTimingManager timingManager = ArcTimingManager.Instance;
			float t = 1f * (Timing - Arc.Timing) / (Arc.EndTiming - Arc.Timing);
			LocalPosition = new Vector3(ArcAlgorithm.ArcXToWorld(ArcAlgorithm.X(Arc.XStart, Arc.XEnd, t, Arc.CurveType)),
									  ArcAlgorithm.ArcYToWorld(ArcAlgorithm.Y(Arc.YStart, Arc.YEnd, t, Arc.CurveType)) - 0.5f,
									  -timingManager.CalculatePositionByTimingAndStart(Arc.Timing, Timing, Arc.TimingGroup) / 1000f - 0.6f);
            UpdateScale();
			SetupArcTapConnection();
		}

		public Color TintColor
		{
			get => currentTintColor;
			set => currentTintColor = value;
		}

		public float Alpha
		{
			get => currentAlpha;
			set => currentAlpha = value;
		}
		public Vector3 LocalPosition
		{
			get => localPosition;
			set => localPosition = value;
		}

        private float shadowDepthScale = 1f;

        internal Matrix4x4 GetShadowMatrix(Bounds bounds, Quaternion rotation)
        {
            float xScale = localScale * NoteMeshDefinitions.ArcTapShadowWidth / bounds.size.x;
            float yScale = NoteMeshDefinitions.ArcTapShadowDepth * shadowDepthScale / bounds.size.y;
            Vector3 p = localPosition;
            p.x -= bounds.center.x * xScale;
            p.y = NoteMeshDefinitions.ArcTapShadowHeight;
            // Sprite local Y becomes world +Z. Keep its near edge at the ArcTap timing,
            // extending the far edge as speed changes instead of stretching around the centre.
            p.z -= NoteMeshDefinitions.ArcTapCenter.z + bounds.max.y * yScale;
            return Matrix4x4.TRS(p, rotation, new Vector3(xScale, yScale, 1));
        }

		internal static readonly Vector3 NormalArcTapScale = new Vector3(-4.4625f, 0.6f, -1.2f);
		internal static readonly Vector3 SfxArcTapScale = Vector3.one * NoteMeshDefinitions.SfxArcTapScale;

		public Vector3 BaseModelScale { get { return Arc.IsSfx ? SfxArcTapScale : NormalArcTapScale; } }

		public float LocalScale
		{
			get => localScale;
			set => localScale = value;
		}
		public override bool Selected
		{
			get => selected;
			set => selected = value;
		}

        private Vector3 localPosition;
        private float localScale = 1;
        private readonly HashSet<ArcTap> connectedTaps = new HashSet<ArcTap>();
		private bool selected;
		private Color currentTintColor = Color.white;
		private float currentAlpha = 1f;

		public ArcArcTap()
		{
		}
		public ArcArcTap(RawAffArctap arctap)
		{
			Timing = arctap.Timing;
			Width = arctap.Width;
			IsMine = arctap.IsMine;
		}
	}
	public class ArcArc : ArcLongNote, IIntoRawItem, IHasTimingGroup, ISetableTimingGroup
	{
        internal int? CreationPreviewSoundTiming;
        internal int RenderEndTiming = int.MinValue;
		private ArcTimingGroup timingGroup;
        public ArcTimingGroup TimingGroup
        {
            get => timingGroup;
            set { if (timingGroup == value) return; timingGroup = value; InvalidateRenderIndex(); }
        }
		public float XStart;
		public float XEnd;
		public ArcCurveType CurveType;
		public float YStart;
		public float YEnd;
		public int Color;
		public string Effect = "none";
		public ArcLineType LineType;
		// Written in place of the line type as a hex color; renders a trace in that color.
		public Color32? TraceColor;
		public float? Smoothness;
		public bool IsVoid
		{
			get
			{
				return LineType != ArcLineType.FalseNotVoid;
			}

		}

		public bool Designant
		{
			get => LineType == ArcLineType.Designant;
		}
		public float EffectiveSmoothness
		{
			get => Mathf.Max(Smoothness ?? 1.00f, 1.00f);
		}
		public List<ArcArcTap> ArcTaps = new List<ArcArcTap>();

		public ArcArcTap ConvertedVariousSizedArctap = null;

		// Arcade Alpha draws no body, head or height indicator for a <= 1 ms arc that only
		// carries one arctap at a fixed position; just the arctap is visible.
		public bool HiddenSegments => Math.Abs(EndTiming - Timing) <= 1 && ArcTaps.Count == 1
			&& new Vector2(XStart, YStart) == new Vector2(XEnd, YEnd);

		public bool IsVariousSizedArctap
		{
			get
			{
				if (Color == ArcColors.GrayId && Timing == EndTiming && Math.Abs(YStart - YEnd) < float.Epsilon && !IsVoid)
				{
					return true;
				}
				return false;
			}
		}

		public override ArcEvent Clone()
		{
			ArcArc arc = new ArcArc()
			{
				Timing = Timing,
				EndTiming = EndTiming,
				XStart = XStart,
				XEnd = XEnd,
				CurveType = CurveType,
				YStart = YStart,
				YEnd = YEnd,
				Color = Color,
				Effect = Effect,
				LineType = LineType,
				TraceColor = TraceColor,
				TimingGroup = TimingGroup,
				Smoothness = Smoothness
			};
			foreach (var arctap in ArcTaps) arc.ArcTaps.Add(arctap.Clone() as ArcArcTap);
			return arc;
		}
		public override void Assign(ArcEvent newValues)
		{
			base.Assign(newValues);
			ArcArc n = newValues as ArcArc;
			XStart = n.XStart;
			XEnd = n.XEnd;
			CurveType = n.CurveType;
			YStart = n.YStart;
			YEnd = n.YEnd;
			Color = n.Color;
			Effect = n.Effect;
			LineType = n.LineType;
			TraceColor = n.TraceColor;
            Smoothness = n.Smoothness;
			TimingGroup = n.TimingGroup;
		}

		public override bool Enable
		{
			get
			{
				return base.Enable;
			}
			set
			{
                if (value && arcRenderer == null)
                {
                    arcRenderer = ArcArcManager.Instance.RentVisual();
                    arcRenderer.Bind(this);
                    arcRenderer.Selected = selected;
                }
                if (enable != value)
                {
                    enable = value;
                    arcRenderer.Enable = value;
                    if (!value) ReleaseVisual();
                }
			}
		}
		public override bool Selected
		{
			get
			{
				return selected;
			}
			set
			{
				selected = value;
                if (arcRenderer != null) arcRenderer.Selected = value;
				if (ConvertedVariousSizedArctap != null)
				{
					ConvertedVariousSizedArctap.Selected = value;
				}
			}
		}
		public bool IsSfx
		{
			get
			{
				return Effect.EndsWith("_wav");
			}
		}
		public override void Destroy()
		{
			base.Destroy();
			arcRenderer = null;
			ArcGroup = null;
			DestroyArcTaps();
		}
		public override void Instantiate()
		{
			InstantiateArcTaps();
		}

        public void CalculateJudgeTimings()
        {
            ArcScoreManager.Invalidate();
            ClearJudgeTimings();
            if (IsVoid || EndTiming == Timing) return;
            double bpm = Math.Abs(ArcTimingManager.Instance.CalculateBpmByTiming(Timing, TimingGroup));
            if (bpm == 0) { JudgeTimings.Add(Timing); return; }
            double interval = 60000f / bpm / (bpm >= 255 ? 1 : 2) / ArcGameplayManager.Instance.TimingPointDensityFactor;
            BuildJudgeTimings(interval, RenderHead ? 1 : 0);
        }
		public void Rebuild()
		{
			ArcScoreManager.Invalidate();
			arcRenderer?.Build();
			DestroyArcTaps();
			InstantiateArcTaps();
		}
		public void InstantiateArcTaps()
		{
			foreach (var tap in ArcTaps)
			{
				tap.Instantiate(this);
			}
			if (IsVariousSizedArctap)
			{
				ConvertedVariousSizedArctap = new ArcArcTap
				{
					Timing = Timing,
					Arc = this,
					IsConvertedVariousSizedArctap = true,
				};
				ConvertedVariousSizedArctap.Instantiate(this);
				ConvertedVariousSizedArctap.Selected = Selected;
			}
		}
		public void DestroyArcTaps()
		{
			foreach (var at in ArcTaps)
			{
				at.Destroy();
			}
			ConvertedVariousSizedArctap?.Destroy();
			ConvertedVariousSizedArctap = null;
		}
		public void AddArcTap(ArcArcTap arctap)
		{
			ArcScoreManager.Invalidate();
			if (arctap.Timing > EndTiming || arctap.Timing < Timing)
			{
				throw new ArgumentOutOfRangeException("ArcTap 时间不在 Arc 范围内");
			}
			ArcTimingManager timingManager = ArcTimingManager.Instance;
			arctap.Instantiate(this);
			float t = 1f * (arctap.Timing - Timing) / (EndTiming - Timing);
			arctap.LocalPosition = new Vector3(ArcAlgorithm.ArcXToWorld(ArcAlgorithm.X(XStart, XEnd, t, CurveType)),
									  ArcAlgorithm.ArcYToWorld(ArcAlgorithm.Y(YStart, YEnd, t, CurveType)) - 0.5f,
									  -timingManager.CalculatePositionByTimingAndStart(Timing, arctap.Timing, TimingGroup) / 1000f - 0.6f);
			ArcTaps.Add(arctap);
		}
		public void RemoveArcTap(ArcArcTap arctap)
		{
			ArcScoreManager.Invalidate();
			arctap.Destroy();
			ArcTaps.Remove(arctap);
		}


        private bool selected;
        internal float RenderingAlpha = 225 / 255f;
        internal bool RenderingHighlight;
        internal int RenderingGroupBeginTiming;

        protected override void ReleaseVisual()
        {
            if (arcRenderer != null)
            {
                arcRenderer.Unbind();
                ArcArcManager.Instance.ReturnVisual(arcRenderer);
            }
            arcRenderer = null;
            base.ReleaseVisual();
        }

		public int FlashCount;
		public float EndPosition;
		public bool Flag;
		public bool RenderHead;
		public List<ArcArc> ArcGroup;
		internal ArcArcVisual arcRenderer;

		public ArcArc()
		{
		}
		public ArcArc(RawAffArc rawAffArc, ArcTimingGroup timingGroup)
		{
			Timing = rawAffArc.Timing;
			EndTiming = rawAffArc.EndTiming;
			XStart = rawAffArc.XStart;
			XEnd = rawAffArc.XEnd;
			CurveType = rawAffArc.CurveType;
			YStart = rawAffArc.YStart;
			YEnd = rawAffArc.YEnd;
			Color = rawAffArc.Color;
			Effect = rawAffArc.Effect;
			LineType = rawAffArc.LineType;
			TraceColor = rawAffArc.TraceColor;
			Smoothness = rawAffArc.Smoothness;
			if (rawAffArc.ArcTaps.Count > 0)
			{
				// Like Alpha, an arc carrying arctaps is a trace.
				if (LineType == ArcLineType.FalseNotVoid)
				{
					LineType = ArcLineType.TrueIsVoid;
				}
				foreach (var arctap in rawAffArc.ArcTaps)
				{
					ArcTaps.Add(new ArcArcTap(arctap));
				}
			}
			TimingGroup = timingGroup;
		}
		public IRawAffItem IntoRawItem()
		{
			return new RawAffArc()
			{
				Timing = Timing,
				EndTiming = EndTiming,
				XStart = XStart,
				XEnd = XEnd,
				CurveType = CurveType,
				YStart = YStart,
				YEnd = YEnd,
				Color = Color,
				Effect = Effect,
				LineType = LineType,
				TraceColor = TraceColor,
				Smoothness = Smoothness,
				ArcTaps = ArcTaps.Select((arctap) => new RawAffArctap() { Timing = arctap.Timing, Width = arctap.Width, IsMine = arctap.IsMine }).ToList(),
			};
		}

	}
	public class ArcCamera : ArcEvent, IIntoRawItem, IHasTimingGroup, ISetableTimingGroup
	{

		private ArcTimingGroup timingGroup;
        public ArcTimingGroup TimingGroup
        {
            get => timingGroup;
            set { if (timingGroup == value) return; timingGroup = value; InvalidateRenderIndex(); }
        }
		public Vector3 Move, Rotate;
		public CameraEaseType CameraType;
		public int Duration;

		public float Percent;

		public ArcCamera()
		{
		}

		public ArcCamera(RawAffCamera rawAffCamera, ArcTimingGroup timingGroup)
		{
			Timing = rawAffCamera.Timing;
			Move = new Vector3(rawAffCamera.MoveX, rawAffCamera.MoveY, rawAffCamera.MoveZ);
			Rotate = new Vector3(rawAffCamera.RotateX, rawAffCamera.RotateY, rawAffCamera.RotateZ);
			CameraType = rawAffCamera.CameraType;
			Duration = rawAffCamera.Duration;
			TimingGroup = timingGroup;
		}
		public IRawAffItem IntoRawItem()
		{
			return new RawAffCamera()
			{
				Timing = Timing,
				MoveX = Move.x,
				MoveY = Move.y,
				MoveZ = Move.z,
				RotateX = Rotate.x,
				RotateY = Rotate.y,
				RotateZ = Rotate.z,
				CameraType = CameraType,
				Duration = Duration,
			};
		}

		public override ArcEvent Clone()
		{
			return new ArcCamera()
			{
				Timing = Timing,
				Duration = Duration,
				CameraType = CameraType,
				Move = Move,
				Percent = Percent,
				Rotate = Rotate,
				TimingGroup = TimingGroup
			};
		}
		public override void Assign(ArcEvent newValues)
		{
			base.Assign(newValues);
			ArcCamera n = newValues as ArcCamera;
			Move = n.Move;
			Rotate = n.Rotate;
			CameraType = n.CameraType;
			Duration = n.Duration;
			TimingGroup = n.TimingGroup;
		}

		public void Update(int Timing)
		{
			if (Timing > this.Timing + Duration)
			{
				Percent = 1;
				return;
			}
			else if (Timing <= this.Timing)
			{
				Percent = 0;
				return;
			}
			Percent = Mathf.Clamp((1f * Timing - this.Timing) / Duration, 0, 1);
			switch (CameraType)
			{
				case CameraEaseType.Qi:
					Percent = ArcAlgorithm.Qi(Percent);
					break;
				case CameraEaseType.Qo:
					Percent = ArcAlgorithm.Qo(Percent);
					break;
				case CameraEaseType.S:
					Percent = ArcAlgorithm.S(0, 1, Percent);
					break;
			}
		}
	}

	public class ArcTimingGroup
	{
		public int Id;
		public List<ArcTiming> Timings = new List<ArcTiming>();
		public string Attributes = "";
		private bool noInput;
		public bool NoInput
		{
			get => noInput;
			set
			{
				if (noInput == value) return;
				noInput = value;
				ArcScoreManager.Invalidate();
			}
		}
		public bool NoClip = false;
		public bool FadingHolds = false;
        public Color32 TraceColor;
        public bool UseTraceColor;
        public bool TraceBodyGold;
		public int AngleX = 0;
		public int AngleY = 0;
		public bool GroupHide = false;
		public float earliestRenderTime = 0;
		public float latestRenderTime = 0;

		public void ApplyAttributes(string attributes)
		{
			ArcScoreManager.Invalidate();
			Attributes = attributes;
			NoInput = false;
			FadingHolds = false;
			AngleX = AngleY = 0;
			NoClip = false;
            TraceColor = new Color32(0, 0, 0, 0);
            UseTraceColor = false;
            TraceBodyGold = false;
            bool colorParseSuccess = false;
			var attributeList = attributes.Split('_');
			foreach (var attribute in attributeList)
			{
                // Match Alpha's ordered attributes and legacy gold-trace fallback.
                if (attribute.StartsWith("tracecol", StringComparison.Ordinal))
                {
                    string rgb = attribute.Substring(8);
                    if (rgb.Length == 6 && uint.TryParse(rgb, NumberStyles.AllowHexSpecifier,
                        CultureInfo.InvariantCulture, out uint packed))
                    {
                        TraceColor = new Color32((byte)(packed >> 16), (byte)(packed >> 8), (byte)packed, 255);
                        colorParseSuccess = true;
                    }
                    TraceBodyGold = !UseTraceColor;
                    continue;
                }
                if (attribute == "usetracecol" || attribute == "usetracecolor")
                {
                    UseTraceColor = colorParseSuccess;
                    TraceBodyGold = !UseTraceColor;
                    continue;
                }
				if (attribute == "noinput")
				{
					NoInput = true;
					continue;
				}
				if (attribute == "fadingholds")
				{
					FadingHolds = true;
					continue;
				}
				if (attribute == "noclip")
				{
					NoClip = true;
					continue;
				}
				Match matchAngleX = Regex.Match(attribute, "^anglex([0-9]+)$");
				if (matchAngleX.Success)
				{
					bool result = int.TryParse(matchAngleX.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out AngleX);
					if (!result)
					{
						Debug.LogWarning($"Timinggroup attribute applying failed:{attribute}");
					}
					continue;
				}
				Match matchAngleY = Regex.Match(attribute, "^angley([0-9]+)$");
				if (matchAngleY.Success)
				{
					bool result = int.TryParse(matchAngleY.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out AngleY);
					if (!result)
					{
						Debug.LogWarning($"Timinggroup attribute applying failed:{attribute}");
					}
					continue;
				}
			}
		}
	}
}
