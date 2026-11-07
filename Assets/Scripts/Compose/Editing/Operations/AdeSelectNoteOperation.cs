using System.Collections.Generic;
using Arcade.Compose.Editing;
using Arcade.Gameplay;
using Arcade.Gameplay.Chart;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Arcade.Compose.Operation
{
	public class AdeSelectNoteOperation : AdeOperation
	{
		public static AdeSelectNoteOperation Instance { get; private set; }

		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
		private static void ResetStaticState() => Instance = null;

		private void Awake()
		{
			Instance = this;
		}

		private float? rangeSelectPosition = null;
		public float? RangeSelectPosition { get => rangeSelectPosition; }
		private const float CyclePositionTolerance = 4;
		private readonly List<ArcNote> hits = new List<ArcNote>();
		private readonly List<ArcNote> cycleNotes = new List<ArcNote>();
		private int cycleIndex;
		private Vector2 cyclePosition;
		private ArcChart cycleChart;
		private uint cycleSeekVersion;
		private Camera cycleCamera;
		private Matrix4x4 cycleView, cycleProjection;
		private Rect cycleCameraRect;

		public override AdeOperationResult TryExecuteOperation()
		{
			if (!AdeInputManager.Instance.Inputs.RangeSelection.IsPressed())
			{
				rangeSelectPosition = null;
			}

			if (!AdeGameplayContentInputHandler.InputActive)
			{
				ResetCycle();
				return false;
			}
			bool multiple = AdeInputManager.Instance.Inputs.MultipleSelection.IsPressed();
			bool range = AdeInputManager.Instance.Inputs.RangeSelection.IsPressed();
			var camera = AdeCursorManager.Instance.GameplayCamera;
			Vector2 position = Mouse.current.position.ReadValue();
			if (multiple || range || !CanContinueCycle(camera, position)) ResetCycle();

			if (Mouse.current.leftButton.wasPressedThisFrame)
			{
				//range selection shortcut
				if (AdeInputManager.Instance.Inputs.RangeSelection.IsPressed())
				{
					if (rangeSelectPosition == null)
					{
						rangeSelectPosition = AdeCursorManager.Instance.AttachedTiming;
					}
					else
					{
						if (!AdeInputManager.Instance.Inputs.MultipleSelection.IsPressed())
						{
							AdeSelectionManager.Instance.DeselectAllNotes();
						}
						AdeSelectionManager.Instance.RangeSelectNote(rangeSelectPosition.Value, AdeCursorManager.Instance.AttachedTiming);
						rangeSelectPosition = null;
						return true;
					}
				}

				var create = AdeClickToCreate.Instance;
				bool creating = !multiple && !range && ArcadeComposeManager.Instance.IsEditorMode
					&& create.Enable && create.Mode != ClickToCreateMode.Idle;
				ArcNote n = multiple || range || creating ? ArcNoteRenderer.PickNote(camera, position) : PickCyclingNote(camera, position);
				if (n != null)
				{
					if (creating && AdeSelectionManager.Instance.SelectedNotes.Contains(n))
					{
						// ArcTap creation still needs the selected Arc as its host during this call.
						var result = create.TryExecuteOperation();
						if (result.operationExecuted)
						{
							AdeSelectionManager.Instance.DeselectNote(n);
							ResetCycle();
							return result;
						}
					}
					if (!AdeInputManager.Instance.Inputs.MultipleSelection.IsPressed())
					{
						AdeSelectionManager.Instance.DeselectAllNotes();
						AdeSelectionManager.Instance.SelectNote(n);
						return true;
					}
					else
					{
						var selection = AdeSelectionManager.Instance;
						// As in Arcade Alpha, Ctrl-clicking a selected arc selects its whole arc group,
						// or deselects the group once all of it is selected.
						if (n is ArcArc arc && arc.ArcGroup != null && arc.ArcGroup.Count > 1 && selection.SelectedNotes.Contains(arc))
						{
							bool groupSelected = arc.ArcGroup.TrueForAll(a => selection.SelectedNotes.Contains(a));
							foreach (var a in arc.ArcGroup)
							{
								if (groupSelected) selection.DeselectNote(a);
								else selection.SelectNote(a);
							}
							return true;
						}
						if (AdeSelectionManager.Instance.SelectedNotes.Contains(n)) AdeSelectionManager.Instance.DeselectNote(n);
						else AdeSelectionManager.Instance.SelectNote(n);
						return true;
					}
				}
				else
				{
					if (!AdeInputManager.Instance.Inputs.MultipleSelection.IsPressed() && !AdeClickToCreate.Instance.MayAddArcTapOnArc())
					{
						AdeSelectionManager.Instance.DeselectAllNotes();
					}
				}
			}
			return false;
		}
		public override void Reset()
		{
			rangeSelectPosition = null;
			ResetCycle();
		}

		private bool CanContinueCycle(Camera camera, Vector2 position)
		{
			var game = ArcGameplayManager.Instance;
			var selected = AdeSelectionManager.Instance.SelectedNotes;
			return cycleNotes.Count > 0 && cycleCamera == camera && camera
				&& cycleChart == game.Chart && cycleSeekVersion == game.SeekVersion
				&& (position - cyclePosition).sqrMagnitude <= CyclePositionTolerance * CyclePositionTolerance
				&& cycleView.Equals(camera.worldToCameraMatrix) && cycleProjection.Equals(camera.projectionMatrix)
				&& cycleCameraRect.Equals(camera.pixelRect)
				&& selected.Count == 1 && selected[0] == cycleNotes[cycleIndex];
		}

		private ArcNote PickCyclingNote(Camera camera, Vector2 position)
		{
			bool same = CanContinueCycle(camera, position);
			ArcNoteRenderer.PickNotes(camera, position, hits);
			if (hits.Count == 0) { ResetCycle(); return null; }
			same &= hits.Count == cycleNotes.Count;
			// Animated Slide meshes can reorder near-equal ray depths without changing the overlapping notes.
			for (int i = 0; same && i < hits.Count; i++) same = cycleNotes.Contains(hits[i]);
			if (same) cycleIndex = (cycleIndex + 1) % cycleNotes.Count;
			else
			{
				cycleNotes.Clear();
				cycleNotes.AddRange(hits);
				cycleIndex = 0;
				cyclePosition = position;
				cycleChart = ArcGameplayManager.Instance.Chart;
				cycleSeekVersion = ArcGameplayManager.Instance.SeekVersion;
				cycleCamera = camera;
				cycleView = camera.worldToCameraMatrix;
				cycleProjection = camera.projectionMatrix;
				cycleCameraRect = camera.pixelRect;
			}
			return cycleNotes[cycleIndex];
		}

		private void ResetCycle()
		{
			hits.Clear();
			cycleNotes.Clear();
			cycleChart = null;
			cycleCamera = null;
		}
	}
}
