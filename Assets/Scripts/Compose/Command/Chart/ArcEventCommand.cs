using Arcade.Gameplay.Chart;
using Arcade.Aff;
using Arcade.Gameplay;
using System.Collections.Generic;
using UnityEngine;

namespace Arcade.Compose.Command
{
    public sealed class ConvertArcToSlideCommand : ICommand, ICommandMemoryCost
    {
        private readonly ArcNote original;
        private readonly ArcSlide[] slides;
        private readonly BatchCommand changes;
        private readonly List<ArcArcTap> selectedArcTaps = new List<ArcArcTap>();
        long ICommandMemoryCost.EstimatedBytes => 128 + 8L * selectedArcTaps.Capacity + CommandMemory.Estimate(changes);
        public string Name => "转换为 Slide";

        private ConvertArcToSlideCommand(ArcNote source, ArcSlide[] converted)
        {
            original = source;
            slides = converted;
            var commands = new List<ICommand>();
            var arc = source as ArcArc;
            if (arc != null) foreach (var tap in arc.ArcTaps)
                if (AdeSelectionManager.Instance.SelectedNotes.Contains(tap)) selectedArcTaps.Add(tap);
            if (arc != null && arc.ArcTaps.Count > 0)
            {
                // Slide cannot carry ArcTaps: retain their original geometry as a void trace.
                var carrier = (ArcArc)arc.Clone();
                carrier.LineType = ArcLineType.TrueIsVoid;
                commands.Add(new EditArcEventCommand(arc, carrier));
            }
            else commands.Add(new RemoveArcEventCommand(source));
            foreach (var slide in slides) commands.Add(new AddArcEventCommand(slide));
            changes = new BatchCommand(commands.ToArray(), Name);
        }

        internal static SlideHeight NearestHeight(float y)
        {
            float ground = Mathf.Abs(y - (-2f / 9f)), sky = Mathf.Abs(y - 1), high = Mathf.Abs(y - 1.61f);
            return ground <= sky && ground <= high ? SlideHeight.Ground : sky <= high ? SlideHeight.Sky : SlideHeight.HighSky;
        }

        public static bool TryCreate(ArcNote source, out ConvertArcToSlideCommand command)
        {
            command = null;
            if (source is ArcHold hold)
            {
                var converted = new ArcSlide { Timing = hold.Timing, EndTiming = hold.EndTiming, TimingGroup = hold.TimingGroup,
                    StartCenter = .5f - hold.WorldX / 8.5f, EndCenter = .5f - hold.WorldX / 8.5f, Height = SlideHeight.Ground };
                if (!converted.IsValid) return false;
                command = new ConvertArcToSlideCommand(hold, new[] { converted });
                return true;
            }
            if (!(source is ArcArc arc)) return false;
            long duration = (long)arc.EndTiming - arc.Timing;
            if (duration < 2 || duration > int.MaxValue) return false;
            int curve = arc.CurveType == ArcCurveType.B ? 3
                : arc.CurveType == ArcCurveType.Si || arc.CurveType == ArcCurveType.SiSi || arc.CurveType == ArcCurveType.SiSo ? 1
                : arc.CurveType == ArcCurveType.So || arc.CurveType == ArcCurveType.SoSi || arc.CurveType == ArcCurveType.SoSo ? 2 : 0;
            var slide = new ArcSlide
            {
                Timing = arc.Timing, EndTiming = arc.EndTiming, TimingGroup = arc.TimingGroup,
                StartCenter = arc.XStart, EndCenter = arc.XEnd,
                StartWidth = .5f, EndWidth = .5f, LeftCurve = curve, RightCurve = curve,
                Height = NearestHeight(ArcAlgorithm.Y(arc.YStart, arc.YEnd, .5f, arc.CurveType)),
            };
            if (!slide.IsValid) return false;
            var slides = new[] { slide };
            command = new ConvertArcToSlideCommand(arc, slides);
            return true;
        }

        public void Do()
        {
            AdeSelectionManager.Instance.DeselectNote(original);
            foreach (var tap in selectedArcTaps) AdeSelectionManager.Instance.DeselectNote(tap);
            changes.Do();
            foreach (var slide in slides) AdeSelectionManager.Instance.SelectNote(slide);
        }

        public void Undo()
        {
            foreach (var slide in slides) AdeSelectionManager.Instance.DeselectNote(slide);
            changes.Undo();
            AdeSelectionManager.Instance.SelectNote(original);
            foreach (var tap in selectedArcTaps) AdeSelectionManager.Instance.SelectNote(tap);
        }
    }

	public class AddArcEventCommand : ICommand, ICommandMemoryCost
	{
		private readonly ArcEvent @event = null;
        long ICommandMemoryCost.EstimatedBytes => 64 + CommandMemory.Estimate(@event);
		public AddArcEventCommand(ArcEvent note)
		{
			this.@event = note;
		}
		public string Name
		{
			get
			{
				return "添加 Note";
			}
		}
		public void Do()
		{
			switch (@event)
			{
				case ArcSlide note:
					ArcSlideManager.Instance.Add(note);
					break;
				case ArcTap note:
					ArcTapNoteManager.Instance.Add(note);
					break;
				case ArcHold note:
					ArcHoldNoteManager.Instance.Add(note);
					break;
				case ArcArc note:
					ArcArcManager.Instance.Add(note);
					break;
			}
		}
		public void Undo()
		{
			if (@event is ArcNote note)
			{
				AdeSelectionManager.Instance.DeselectNote(note);
			}
			if (@event is ArcSlide slide)
			{
				ArcSlideManager.Instance.Remove(slide);
			}
			else if (@event is ArcTap tap)
			{
				ArcTapNoteManager.Instance.Remove(tap);
			}
			else if (@event is ArcHold hold)
			{
				ArcHoldNoteManager.Instance.Remove(hold);
			}
			else if (@event is ArcArc arc)
			{
				foreach (var arctap in arc.ArcTaps)
				{
					AdeSelectionManager.Instance.DeselectNote(arctap);
				}
				ArcArcManager.Instance.Remove(arc);
			}
		}
	}
	public class RemoveArcEventCommand : ICommand, ICommandMemoryCost
	{
		private readonly ArcEvent @event = null;
        long ICommandMemoryCost.EstimatedBytes => 64 + CommandMemory.Estimate(@event);
		public RemoveArcEventCommand(ArcEvent note)
		{
			this.@event = note;
		}
		public string Name
		{
			get
			{
				return "删除 Note";
			}
		}
		public void Do()
		{
			if (@event is ArcNote note)
			{
				AdeSelectionManager.Instance.DeselectNote(note);
			}
			if (@event is ArcSlide slide)
			{
				ArcSlideManager.Instance.Remove(slide);
			}
			else if (@event is ArcTap tap)
			{
				ArcTapNoteManager.Instance.Remove(tap);
			}
			else if (@event is ArcHold hold)
			{
				ArcHoldNoteManager.Instance.Remove(hold);
			}
			else if (@event is ArcArc arc)
			{
				foreach (var arctap in arc.ArcTaps)
				{
					AdeSelectionManager.Instance.DeselectNote(arctap);
				}
				ArcArcManager.Instance.Remove(arc);
			}
		}
		public void Undo()
		{
			if (@event is ArcSlide slide)
			{
				ArcSlideManager.Instance.Add(slide);
			}
			else if (@event is ArcTap tap)
			{
				ArcTapNoteManager.Instance.Add(tap);
			}
			else if (@event is ArcHold hold)
			{
				ArcHoldNoteManager.Instance.Add(hold);
			}
			else if (@event is ArcArc arc)
			{
				ArcArcManager.Instance.Add(arc);
			}
		}
	}
	public class EditArcEventCommand : ICommand, ICommandMemoryCost
	{
		private readonly ArcEvent note = null;
		private readonly ArcEvent oldValues, newValues;
        long ICommandMemoryCost.EstimatedBytes => 128 + CommandMemory.Estimate(oldValues) + CommandMemory.Estimate(newValues);
        private readonly bool preservePlaybackJudgement;
		public EditArcEventCommand(ArcEvent note, ArcEvent newValues, bool preservePlaybackJudgement = false)
		{
			this.preservePlaybackJudgement = preservePlaybackJudgement;
			this.note = note;
			oldValues = note.Clone();
			this.newValues = newValues;
		}
		public string Name
		{
			get
			{
				return "修改 Note";
			}
		}
		public void Do()
		{
			(note as ArcArcTap)?.RemoveArcTapConnection();
			note.Assign(newValues);
			(note as ArcArcTap)?.Relocate();
			(note as ArcArc)?.Rebuild();
			(note as ArcTap)?.SetupArcTapConnection();
			if (note is ArcArc arc) ArcArcManager.Instance.CalculateArcRelationship(arc);
			RefreshJudgement();
		}
		public void Undo()
		{
			(note as ArcArcTap)?.RemoveArcTapConnection();
			note.Assign(oldValues);
			(note as ArcArcTap)?.Relocate();
			(note as ArcArc)?.Rebuild();
			(note as ArcTap)?.SetupArcTapConnection();
			if (note is ArcArc arc) ArcArcManager.Instance.CalculateArcRelationship(arc);
			RefreshJudgement();
		}
        private void RefreshJudgement()
        {
            var game = ArcGameplayManager.Instance;
            if (!preservePlaybackJudgement || !game.IsPlaying) game.ResetJudge();
            else if (note is ArcNote changed && game.ChartTiming < changed.Timing) changed.Judged = false;
        }
	}

	public class AddArcTapCommand : ICommand, ICommandMemoryCost
	{
		private readonly ArcArc arc;
		private readonly ArcArcTap arctap;
        long ICommandMemoryCost.EstimatedBytes => 64 + CommandMemory.Estimate(arctap);
		public AddArcTapCommand(ArcArc arc, ArcArcTap arctap)
		{
			this.arc = arc;
			this.arctap = arctap;
		}
		public string Name
		{
			get
			{
				return "添加 ArcTap";
			}
		}
		public void Do()
		{
			arc.AddArcTap(arctap);
		}
		public void Undo()
		{
			AdeSelectionManager.Instance.DeselectNote(arctap);
			arc.RemoveArcTap(arctap);
		}
	}
	public class RemoveArcTapCommand : ICommand, ICommandMemoryCost
	{
		private readonly ArcArc arc;
		private readonly ArcArcTap arctap;
        long ICommandMemoryCost.EstimatedBytes => 64 + CommandMemory.Estimate(arctap);
		public RemoveArcTapCommand(ArcArc arc, ArcArcTap arctap)
		{
			this.arc = arc;
			this.arctap = arctap;
		}
		public string Name
		{
			get
			{
				return "删除 ArcTap";
			}
		}
		public void Do()
		{
			AdeSelectionManager.Instance.DeselectNote(arctap);
			arc.RemoveArcTap(arctap);
		}
		public void Undo()
		{
			arc.AddArcTap(arctap);
		}
	}
}
