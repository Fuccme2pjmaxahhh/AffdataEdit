using System.Collections;
using System.Collections.Generic;
using Arcade.Compose.Command;
using Arcade.Compose.MarkingMenu;
using Arcade.Gameplay;
using Arcade.Gameplay.Chart;
using Arcade.Util.UnityExtension;
using UnityEngine;

namespace Arcade.Compose
{
	public interface INoteSelectEvent
	{
		void OnNoteSelect(ArcNote note);
		void OnNoteDeselect(ArcNote note);
		void OnNoteDeselectAll();
	}
	public class AdeSelectionManager : MonoBehaviour
	{
		public static AdeSelectionManager Instance { get; private set; }

		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
		private static void ResetStaticState() => Instance = null;

		[System.NonSerialized]
		public List<ArcNote> SelectedNotes = new List<ArcNote>();

		public List<INoteSelectEvent> NoteEventListeners = new List<INoteSelectEvent>();

		private void Awake()
		{
			Instance = this;
		}
		private void Start()
		{
			ArcGameplayManager.Instance.OnChartLoad.AddListener(this.DeselectAllNotes);
		}
		private void OnDestroy()
		{
			ArcGameplayManager.Instance.OnChartLoad.RemoveListener(this.DeselectAllNotes);
		}

		public void RangeSelectNote(float from, float to)
		{
			float start = Mathf.Min(from, to);
			float end = Mathf.Max(from, to);
			List<ArcNote> list = new List<ArcNote>();
			foreach (var slide in ArcSlideManager.Instance.Slides)
				if (slide.Timing >= start && slide.EndTiming <= end) SelectNote(slide);
			foreach (var tap in ArcTapNoteManager.Instance.Taps)
			{
				if (tap.Timing >= start && tap.Timing <= end)
				{
					SelectNote(tap);
				}
			}
			foreach (var hold in ArcHoldNoteManager.Instance.Holds)
			{
				if (hold.Timing >= start && hold.EndTiming <= end)
				{
					SelectNote(hold);
				}
			}
			foreach (var arc in ArcArcManager.Instance.Arcs)
			{
				if (arc.Timing >= start && arc.EndTiming <= end)
				{
					SelectNote(arc);
				}
				if (arc.Timing <= end && arc.EndTiming >= start)
				{
					foreach (var arctap in arc.ArcTaps)
					{
						if (arctap.Timing >= start && arctap.Timing <= end)
						{
							SelectNote(arctap);
						}
						if (arctap.Arc.EndTiming - arctap.Arc.Timing == 1)
						{
							SelectNote(arctap.Arc);
						}
					}
				}
			}
		}
		public void SelectIntersectingRange(int from, int to, Rect region)
		{
			void SelectIfIntersecting(ArcNote note)
			{
				if (Editing.AdeSelectionVolume.Intersects(note, from, to, region)) SelectNote(note);
			}
			foreach (var note in ArcTapNoteManager.Instance.Taps) SelectIfIntersecting(note);
			foreach (var note in ArcHoldNoteManager.Instance.Holds) SelectIfIntersecting(note);
			foreach (var note in ArcSlideManager.Instance.Slides) SelectIfIntersecting(note);
			foreach (var arc in ArcArcManager.Instance.Arcs)
			{
				SelectIfIntersecting(arc);
				foreach (var tap in arc.ArcTaps) SelectIfIntersecting(tap);
			}
		}

		public void SelectNote(ArcNote note)
		{
			note.Selected = true;
			if (!SelectedNotes.Contains(note))
			{
				SelectedNotes.Add(note);
				foreach (var l in NoteEventListeners) l.OnNoteSelect(note);
			}
		}
		public void DeselectNote(ArcNote note)
		{
			note.Selected = false;
			if (SelectedNotes.Contains(note))
			{
				SelectedNotes.Remove(note);
				foreach (var l in NoteEventListeners) l.OnNoteDeselect(note);
			}
		}
		public void DeselectAllNotes()
		{
			foreach (var note in SelectedNotes) note.Selected = false;
			SelectedNotes.Clear();
			foreach (var l in NoteEventListeners) l.OnNoteDeselectAll();
		}
		public void DeleteSelectedNotes()
		{
			List<ICommand> deleteCommands = new List<ICommand>();
			foreach (var s in SelectedNotesWithSingleArcTapHosts())
			{
				if (s is ArcArcTap arcTap) deleteCommands.Add(new RemoveArcTapCommand(arcTap.Arc, arcTap));
				else deleteCommands.Add(new RemoveArcEventCommand(s));
			}
			if (deleteCommands.Count != 0) AdeCommandManager.Instance.Add(new BatchCommand(deleteCommands.ToArray(), "删除"));
			SelectedNotes.Clear();
		}
		// A single ArcTap's 1ms void arc only exists to hold it, so it follows once all its ArcTaps are selected.
		internal ArcNote[] SelectedNotesWithSingleArcTapHosts()
		{
			var notes = new List<ArcNote>(SelectedNotes);
			foreach (var note in SelectedNotes)
				if (note is ArcArcTap arcTap && IsSingleArcTapHost(arcTap.Arc) && !notes.Contains(arcTap.Arc)
					&& arcTap.Arc.ArcTaps.TrueForAll(tap => SelectedNotes.Contains(tap)))
					notes.Add(arcTap.Arc);
			return notes.ToArray();
		}
		internal static bool IsSingleArcTapHost(ArcArc arc)
		{
			return arc != null && arc.IsVoid && arc.EndTiming - arc.Timing == 1
				&& arc.XStart == arc.XEnd && arc.YStart == arc.YEnd;
		}
	}
}
