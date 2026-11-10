using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using Arcade.Gameplay.Chart;
using Arcade.Compose.MarkingMenu;
using Arcade.Gameplay;
using Arcade.Compose.Command;
using System.Linq;
using Arcade.Compose.Editing;
using UnityEngine.InputSystem;
using Cysharp.Threading.Tasks;
using System.Threading;
using System;
using Arcade.Util.UniTaskHelper;

namespace Arcade.Compose.Operation
{
	public class AdeCopyOrCutPasteOperation : AdeOperation
	{
		public static AdeCopyOrCutPasteOperation Instance { get; private set; }

		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
		private static void ResetStaticState() => Instance = null;

		public MarkingMenuItem CopyItem;
		public MarkingMenuItem CutItem;
        private MarkingMenuItem horizontalCutItem;
        internal MarkingMenuItem HorizontalCutItem => horizontalCutItem;

		public override bool IsOnlyMarkingMenu => false;
		public override MarkingMenuItem[] MarkingMenuItems
		{
			get
			{
				if (!ArcGameplayManager.Instance.IsLoaded) return null;
				if (AdeCursorManager.Instance == null) return null;
				if (AdeSelectionManager.Instance.SelectedNotes.Count == 0) return null;
				return horizontalCutItem && AdeSelectionManager.Instance.SelectedNotes.Exists(note => note is ArcArc arc && arc.IsVoid)
                    ? new[] { CopyItem, CutItem, horizontalCutItem } : new[] { CopyItem, CutItem };
			}
		}

		private void Awake()
		{
			Instance = this;
		}

        private void Start()
        {
            horizontalCutItem = Instantiate(CutItem, CutItem.transform.parent);
            horizontalCutItem.name = "Horizontal cut";
            horizontalCutItem.StartupText = "水平剪切";
            horizontalCutItem.HasSubMenu = false;
            horizontalCutItem.SubItems = new MarkingMenuItem[0];
            horizontalCutItem.OnHangOver = new UnityEvent();
            horizontalCutItem.OnConfirmed = new UnityEvent();
            horizontalCutItem.OnConfirmed.AddListener(ManuallyExecuteHorizontalCut);
            horizontalCutItem.gameObject.SetActive(false);
        }

        private void OnDestroy()
        {
            if (horizontalCutItem) Destroy(horizontalCutItem.gameObject);
        }

        public void ManuallyExecuteHorizontalCut()
        {
            AdeOperationManager.Instance.TryExecuteOperation(() =>
            {
                var cancellation = new CancellationTokenSource();
                return new AdeOngoingOperation
                {
                    task = ExecuteHorizontalCut(cancellation.Token).WithExceptionLogger(),
                    cancellation = cancellation,
                };
            });
        }

        private async UniTask ExecuteHorizontalCut(CancellationToken cancellationToken)
        {
            if (!ArcGameplayManager.Instance.IsLoaded) return;
            var selection = AdeSelectionManager.Instance.SelectedNotes.ToArray();
            var arcs = selection.OfType<ArcArc>().Where(arc => arc.IsVoid)
                .OrderBy(arc => arc.Timing).ToArray();
            if (arcs.Length == 0) return;

            var originals = arcs.Select(arc => (ArcArc)arc.Clone()).ToArray();
            var moved = arcs.Select(arc => (ArcArc)arc.Clone()).ToArray();
            var command = new BatchCommand(arcs.Select((arc, i) =>
                (ICommand)new EditArcEventCommand(arc, moved[i])).ToArray(), "水平剪切");
            int cursorTiming = Mathf.Max(arcs[0].Timing, ArcGameplayManager.Instance.ChartTiming);
            float offset = 0;
            AdeSelectionManager.Instance.DeselectAllNotes();
            AdeCommandManager.Instance.Prepare(command);
            AdeToast.Instance.Show("水平剪切：以最早黑线的起点定位，左键确认，Esc 取消");
            try
            {
                Action<Vector2> updatePosition = point =>
                {
                    float nextOffset = point.x - originals[0].XStart;
                    if (Mathf.Approximately(nextOffset, offset)) return;
                    offset = nextOffset;
                    for (int i = 0; i < moved.Length; i++)
                    {
                        // Apply one shared offset from the original positions to preserve the shape.
                        moved[i].XStart = originals[i].XStart + offset;
                        moved[i].XEnd = originals[i].XEnd + offset;
                    }
                    command.Do();
                };
                var coordinate = await AdeCursorManager.Instance.SelectCoordinate(cursorTiming,
                    Progress.Create(updatePosition), cancellationToken);
                updatePosition(coordinate);
                if (Mathf.Approximately(offset, 0)) AdeCommandManager.Instance.Cancel();
                else AdeCommandManager.Instance.Commit();
            }
            catch
            {
                AdeCommandManager.Instance.Cancel();
                throw;
            }
            finally
            {
                foreach (var note in selection) AdeSelectionManager.Instance.SelectNote(note);
            }
        }

		private async UniTask ExecuteCopyOrCut(bool isCut, CancellationToken cancellationToken)
		{
			var oldNotes = AdeSelectionManager.Instance.SelectedNotesWithSingleArcTapHosts();
			AdeSelectionManager.Instance.DeselectAllNotes();
			if (oldNotes.Length == 0) return;
			List<ICommand> commands = new List<ICommand>();
			List<ArcNote> newNotes = new List<ArcNote>();
			var arcTapCommands = new Dictionary<ArcArcTap, List<ICommand>>();
			foreach (var oldNote in oldNotes)
			{
				ArcEvent newNote = oldNote.Clone();
				if (newNote is ArcArcTap)
				{
					if (oldNotes.Contains((oldNote as ArcArcTap).Arc))
					{
						continue;
					}
					var tapCommands = new List<ICommand>
					{
						new AddArcTapCommand((oldNote as ArcArcTap).Arc, newNote as ArcArcTap)
					};
					if (isCut)
					{
						tapCommands.Add(new RemoveArcTapCommand((oldNote as ArcArcTap).Arc, oldNote as ArcArcTap));
					}
					arcTapCommands.Add((ArcArcTap)newNote, tapCommands);
					commands.AddRange(tapCommands);
				}
				else
				{
					commands.Add(new AddArcEventCommand(newNote));
					if (isCut)
					{
						commands.Add(new RemoveArcEventCommand(oldNote));
					}
					if (newNote is ArcArc)
					{
						foreach (var at in (newNote as ArcArc).ArcTaps)
							newNotes.Add(at);
					}
				}
				newNotes.Add(newNote as ArcNote);
			}
			AdeCommandManager.Instance.Prepare(new BatchCommand(commands.ToArray(), isCut ? "剪切" : "复制"));

			try
			{
				Action<int> updateTiming = (int timing) =>
				{
					int beginTiming = newNotes.Min((n) => n.Timing);
					if (beginTiming != timing)
					{
						foreach (var n in newNotes)
						{
							n.Judged = false;
							int diff = n.Timing - beginTiming;
							switch (n)
							{
								case ArcLongNote note:
									int duration = note.EndTiming - note.Timing;
									note.Timing = timing + diff;
									note.EndTiming = timing + duration + diff;
									note.Judging = false;
									(note as ArcArc)?.Rebuild();
									if (note is ArcArc arc)
									{
										ArcArcManager.Instance.CalculateArcRelationship(arc);
									}
									(note as ArcArc)?.CalculateJudgeTimings();
									(note as ArcHold)?.CalculateJudgeTimings();
									break;
								case ArcArcTap note:
									note.RemoveArcTapConnection();
									note.Timing = timing + diff;
									note.Relocate();
									break;
								case ArcTap note:
									note.Timing = timing + diff;
									note.SetupArcTapConnection();
									break;
								default:
									n.Timing = timing + diff;
									break;
							}
						}
					}
				};
				var newTiming = await AdeCursorManager.Instance.SelectTiming(Progress.Create(updateTiming), cancellationToken);
				updateTiming(newTiming);
                if (ArcadeComposeManager.Instance.ArcadePreference.FixNotesTimeOnPaste)
                {
                    FixPastedNotesTime(newNotes, AdeGridManager.Instance.FixPasteTiming);
                    foreach (var note in newNotes)
                    {
                        if (note is ArcArc arc) { arc.Rebuild(); arc.CalculateJudgeTimings(); ArcArcManager.Instance.CalculateArcRelationship(arc); }
                        else if (note is ArcSlide slide) slide.Rebuild();
                        else if (note is ArcHold hold) hold.CalculateJudgeTimings();
                        else if (note is ArcArcTap tap) { tap.RemoveArcTapConnection(); tap.Relocate(); }
                        else if (note is ArcTap groundTap) groundTap.SetupArcTapConnection();
                    }
                }
				var skipped = arcTapCommands.Keys.Where(note => note.Timing < note.Arc.Timing
					|| note.Timing > note.Arc.EndTiming).ToArray();
				if (skipped.Length > 0)
				{
					// Roll back the preview before rebuilding the command, so skipped cut notes stay intact.
					AdeCommandManager.Instance.Cancel();
					foreach (var note in skipped)
					{
						foreach (var command in arcTapCommands[note]) commands.Remove(command);
						newNotes.Remove(note);
					}
					if (commands.Count > 0)
						AdeCommandManager.Instance.Prepare(new BatchCommand(commands.ToArray(), isCut ? "剪切" : "复制"));
					AdeToast.Instance.Show($"已跳过 {skipped.Length} 个超出所在黑线时间范围的 ArcTap"
						+ (newNotes.Count > 0 ? "，其余音符已粘贴" : "，未粘贴任何音符"));
				}
			}
			catch (OperationCanceledException ex)
			{
				AdeCommandManager.Instance.Cancel();
				throw ex;
			}
			AdeCommandManager.Instance.Commit();
			if (AdeInputManager.Instance.Inputs.MultipleSelection.IsPressed())
			{
				foreach (var note in newNotes)
				{
					if (note is ArcArc host && AdeSelectionManager.IsSingleArcTapHost(host)) continue;
					if (note is ArcArcTap tap && newNotes.Contains(tap.Arc) && !AdeSelectionManager.IsSingleArcTapHost(tap.Arc)) continue;
					AdeSelectionManager.Instance.SelectNote(note);
				}
			}
		}

        internal static void FixPastedNotesTime(List<ArcNote> notes, Func<int, int> snap)
        {
            var taps = new HashSet<ArcArcTap>(notes.OfType<ArcArcTap>());
            foreach (var arc in notes.OfType<ArcArc>()) foreach (var tap in arc.ArcTaps) taps.Add(tap);
            foreach (var tap in taps) tap.Timing = snap(tap.Timing);
            foreach (var note in notes)
            {
                if (note is ArcArcTap) continue;
                int start = snap(note.Timing);
                if (note is ArcLongNote longNote)
                {
                    int end = snap(longNote.EndTiming);
                    if (note is ArcArc arc)
                    {
                        foreach (var tap in arc.ArcTaps) { start = Math.Min(start, tap.Timing); end = Math.Max(end, tap.Timing); }
                        if (arc.ArcTaps.Count == 1 && Math.Abs((long)arc.EndTiming - arc.Timing) < 10)
                            end = start + (arc.EndTiming - arc.Timing);
                    }
                    // Preserve legal short Holds/Slides when independent rounding would collapse them.
                    int gap = note is ArcSlide ? 2 : note is ArcHold ? 1 : 0;
                    if ((long)end - start < gap) continue;
                    longNote.EndTiming = end;
                }
                note.Timing = start;
            }
        }

		public override AdeOperationResult TryExecuteOperation()
		{
			if (AdeInputManager.Instance.CheckHotkeyActionPressed(AdeInputManager.Instance.Hotkeys.Copy))
			{
				var cancellation = new CancellationTokenSource();
				return AdeOperationResult.FromOngoingOperation(new AdeOngoingOperation
				{
					task = ExecuteCopyOrCut(false, cancellation.Token).WithExceptionLogger(),
					cancellation = cancellation,
				});
			}
			else if (AdeInputManager.Instance.CheckHotkeyActionPressed(AdeInputManager.Instance.Hotkeys.Cut))
			{
				var cancellation = new CancellationTokenSource();
				return AdeOperationResult.FromOngoingOperation(new AdeOngoingOperation
				{
					task = ExecuteCopyOrCut(true, cancellation.Token).WithExceptionLogger(),
					cancellation = cancellation,
				});
			}
			return false;
		}

		public void ManuallyExecuteCopyOperation()
		{
			AdeOperationManager.Instance.TryExecuteOperation(() =>
			{
				var cancellation = new CancellationTokenSource();
				return new AdeOngoingOperation
				{
					task = ExecuteCopyOrCut(false, cancellation.Token).WithExceptionLogger(),
					cancellation = cancellation,
				};
			});
		}

		public void ManuallyExecuteCutOperation()
		{
			AdeOperationManager.Instance.TryExecuteOperation(() =>
			{
				var cancellation = new CancellationTokenSource();
				return new AdeOngoingOperation
				{
					task = ExecuteCopyOrCut(true, cancellation.Token).WithExceptionLogger(),
					cancellation = cancellation,
				};
			});
		}
	}
}
