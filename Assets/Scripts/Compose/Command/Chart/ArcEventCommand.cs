using Arcade.Gameplay.Chart;
using Arcade.Aff;
using Arcade.Gameplay;
using System.Collections.Generic;
using UnityEngine;

namespace Arcade.Compose.Command
{
    public sealed class ConvertArcToSlideCommand : ICommand
    {
        private readonly ArcArc original;
        private readonly ArcSlide[] slides;
        private readonly BatchCommand changes;
        private readonly List<ArcArcTap> selectedArcTaps = new List<ArcArcTap>();
        public string Name => "Arc 转换为 Slide";

        private ConvertArcToSlideCommand(ArcArc arc, ArcSlide[] converted)
        {
            original = arc;
            slides = converted;
            var commands = new List<ICommand>();
            foreach (var tap in arc.ArcTaps)
                if (AdeSelectionManager.Instance.SelectedNotes.Contains(tap)) selectedArcTaps.Add(tap);
            if (arc.ArcTaps.Count > 0)
            {
                // Slide cannot carry ArcTaps: retain their original geometry as a void trace.
                var carrier = (ArcArc)arc.Clone();
                carrier.LineType = ArcLineType.TrueIsVoid;
                commands.Add(new EditArcEventCommand(arc, carrier));
            }
            else commands.Add(new RemoveArcEventCommand(arc));
            foreach (var slide in slides) commands.Add(new AddArcEventCommand(slide));
            changes = new BatchCommand(commands.ToArray(), Name);
        }

        public static bool TryCreate(ArcArc arc, out ConvertArcToSlideCommand command)
        {
            command = null;
            long duration = (long)arc.EndTiming - arc.Timing;
            if (duration < 2 || duration > int.MaxValue) return false;
            int curve = arc.CurveType == ArcCurveType.B ? 3
                : arc.CurveType == ArcCurveType.Si || arc.CurveType == ArcCurveType.SiSi || arc.CurveType == ArcCurveType.SiSo ? 1
                : arc.CurveType == ArcCurveType.So || arc.CurveType == ArcCurveType.SoSi || arc.CurveType == ArcCurveType.SoSo ? 2 : 0;
            var slide = new ArcSlide
            {
                Timing = arc.Timing, EndTiming = arc.EndTiming, TimingGroup = arc.TimingGroup,
                StartCenter = arc.XStart, EndCenter = arc.XEnd,
                StartWidth = 1f / 3, EndWidth = 1f / 3, LeftCurve = curve, RightCurve = curve,
                Height = SlideHeight.Sky,
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

	public class AddArcEventCommand : ICommand
	{
		private readonly ArcEvent @event = null;
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
	public class RemoveArcEventCommand : ICommand
	{
		private readonly ArcEvent @event = null;
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
	public class EditArcEventCommand : ICommand
	{
		private readonly ArcEvent note = null;
		private readonly ArcEvent oldValues, newValues;
		public EditArcEventCommand(ArcEvent note, ArcEvent newValues)
		{
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
			ArcGameplayManager.Instance.ResetJudge();
		}
		public void Undo()
		{
			(note as ArcArcTap)?.RemoveArcTapConnection();
			note.Assign(oldValues);
			(note as ArcArcTap)?.Relocate();
			(note as ArcArc)?.Rebuild();
			(note as ArcTap)?.SetupArcTapConnection();
			if (note is ArcArc arc) ArcArcManager.Instance.CalculateArcRelationship(arc);
			ArcGameplayManager.Instance.ResetJudge();
		}
	}

	public class AddArcTapCommand : ICommand
	{
		private readonly ArcArc arc;
		private readonly ArcArcTap arctap;
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
	public class RemoveArcTapCommand : ICommand
	{
		private readonly ArcArc arc;
		private readonly ArcArcTap arctap;
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
