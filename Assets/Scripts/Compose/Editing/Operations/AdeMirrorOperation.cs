using System.Collections.Generic;
using Arcade.Aff;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;
using Arcade.Gameplay.Chart;
using Arcade.Compose;
using Arcade.Compose.MarkingMenu;
using Arcade.Compose.Command;
using Arcade.Gameplay;

namespace Arcade.Compose.Operation
{
	public class AdeMirrorOperation : AdeOperation
	{
		public static AdeMirrorOperation Instance { get; private set; }

		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
		private static void ResetStaticState() => Instance = null;

		public MarkingMenuItem Entry;
		private MarkingMenuItem verticalEntry;
        private MarkingMenuItem convertToSlideEntry;
        internal MarkingMenuItem VerticalItem => verticalEntry;
        internal MarkingMenuItem ConvertToSlideItem => convertToSlideEntry;
		private InputAction verticalMirror;

		public override bool IsOnlyMarkingMenu => false;
		public override MarkingMenuItem[] MarkingMenuItems
		{
			get
			{
				if (!ArcGameplayManager.Instance.IsLoaded) return null;
				if (AdeCursorManager.Instance == null) return null;
				if (AdeSelectionManager.Instance.SelectedNotes.Count == 0) return null;
				var items = new List<MarkingMenuItem> { Entry };
                var selected = AdeSelectionManager.Instance.SelectedNotes;
                if (verticalEntry && selected.Exists(note => note is ArcArc || note is ArcSlide)) items.Add(verticalEntry);
                if (convertToSlideEntry && selected.Exists(note => note is ArcArc || note is ArcHold)) items.Add(convertToSlideEntry);
                return items.ToArray();
			}
		}

		private void Awake()
		{
			Instance = this;
		}

        private void Start()
        {
            Entry.StartupText = "水平镜像";
            Entry.Text = Entry.StartupText;
            verticalEntry = Instantiate(Entry, Entry.transform.parent);
            verticalEntry.name = "Vertical mirror";
            verticalEntry.StartupText = "上下翻转";
            verticalEntry.HasSubMenu = false;
            verticalEntry.SubItems = new MarkingMenuItem[0];
            verticalEntry.OnHangOver = new UnityEvent();
            verticalEntry.OnConfirmed = new UnityEvent();
            verticalEntry.OnConfirmed.AddListener(ManuallyFlipVertically);
            verticalEntry.gameObject.SetActive(false);
            convertToSlideEntry = Instantiate(Entry, Entry.transform.parent);
            convertToSlideEntry.name = "Convert Arc to Slide";
            convertToSlideEntry.StartupText = "转Slide";
            convertToSlideEntry.HasSubMenu = false;
            convertToSlideEntry.SubItems = new MarkingMenuItem[0];
            convertToSlideEntry.OnHangOver = new UnityEvent();
            convertToSlideEntry.OnConfirmed = new UnityEvent();
            convertToSlideEntry.OnConfirmed.AddListener(ManuallyConvertToSlide);
            convertToSlideEntry.gameObject.SetActive(false);
            verticalMirror = AdeInputManager.Instance.Hotkeys.Get().FindAction("VerticalMirror", true);
        }

        private void FlipSelectedNotesVertically()
        {
            if (!ArcGameplayManager.Instance.IsLoaded) return;
            var commands = new List<ICommand>();
            // Alpha mirrors around the current sky range, including enwiden.
            float top = ArcAlgorithm.WorldYToArc(ArcSceneControlManager.Instance.SkyInput.localPosition.y);
            foreach (var note in AdeSelectionManager.Instance.SelectedNotes)
            {
                if (note is ArcSlide slide)
                {
                    var mirrored = (ArcSlide)slide.Clone();
                    // Slides use discrete planes; choose the sky plane nearest the current SkyInput.
                    mirrored.Height = slide.IsFloor
                        ? (top > (1f + 1.61f) / 2 ? SlideHeight.HighSky : SlideHeight.Sky)
                        : SlideHeight.Ground;
                    commands.Add(new EditArcEventCommand(slide, mirrored));
                    continue;
                }
                if (!(note is ArcArc arc)) continue;
                var flipped = (ArcArc)arc.Clone();
                flipped.YStart = top - arc.YStart;
                flipped.YEnd = top - arc.YEnd;
                commands.Add(new EditArcEventCommand(arc, flipped));
            }
            if (commands.Count > 0)
                AdeCommandManager.Instance.Add(new BatchCommand(commands.ToArray(), "上下翻转"));
        }

        public void ManuallyFlipVertically()
        {
            AdeOperationManager.Instance.TryExecuteOperation(() =>
            {
                FlipSelectedNotesVertically();
                return null;
            });
        }

        private void ManuallyConvertToSlide()
        {
            AdeOperationManager.Instance.TryExecuteOperation(() =>
            {
                var commands = new List<ICommand>();
                int skipped = 0;
                foreach (var note in AdeSelectionManager.Instance.SelectedNotes.ToArray())
                {
                    if (!(note is ArcArc || note is ArcHold)) continue;
                    if (ConvertArcToSlideCommand.TryCreate(note, out var command)) commands.Add(command);
                    else skipped++;
                }
                if (commands.Count > 0)
                    AdeCommandManager.Instance.Add(new BatchCommand(commands.ToArray(), "转换为 Slide"));
                if (skipped > 0)
                    AdeToast.Instance.Show($"已跳过 {skipped} 条无法转换的物件（时长或坐标无效）");
                return null;
            });
        }

        private void OnDestroy()
        {
            if (verticalEntry) Destroy(verticalEntry.gameObject);
            if (convertToSlideEntry) Destroy(convertToSlideEntry.gameObject);
        }

		private void MirrorSelectedNotes()
		{
			var selected = AdeSelectionManager.Instance.SelectedNotes;
			List<ICommand> commands = new List<ICommand>();
			foreach (var n in selected)
			{
				switch (n)
				{
					case ArcSlide slide:
						var mirrored = (ArcSlide)slide.Clone();
						mirrored.StartCenter = 1 - slide.StartCenter; mirrored.EndCenter = 1 - slide.EndCenter;
						mirrored.LeftCurve = slide.RightCurve; mirrored.RightCurve = slide.LeftCurve;
						commands.Add(new EditArcEventCommand(slide, mirrored));
						break;
					case ArcTap tap:
						ArcTap newtap = tap.Clone() as ArcTap;
						if (newtap.FloatLane.HasValue) newtap.FloatLane = 1 - newtap.FloatLane.Value;
                        else newtap.Track = 5 - newtap.Track;
						commands.Add(new EditArcEventCommand(tap, newtap));
						break;
					case ArcHold hold:
						ArcHold newhold = hold.Clone() as ArcHold;
						if (newhold.FloatLane.HasValue) newhold.FloatLane = 1 - newhold.FloatLane.Value;
                        else newhold.Track = 5 - newhold.Track;
						commands.Add(new EditArcEventCommand(hold, newhold));
						break;
					case ArcArc arc:
						ArcArc newarc = arc.Clone() as ArcArc;
						newarc.XStart = 1 - newarc.XStart;
						newarc.XEnd = 1 - newarc.XEnd;
						if (newarc.Color < 2)
						{
							newarc.Color = 1 - newarc.Color;
						}
						commands.Add(new EditArcEventCommand(arc, newarc));
						break;
				}
			}
			AdeCommandManager.Instance.Add(new BatchCommand(commands.ToArray(), "镜像"));
		}

		public override AdeOperationResult TryExecuteOperation()
		{
            if (verticalMirror != null && AdeInputManager.Instance.CheckHotkeyActionPressed(verticalMirror))
            {
                FlipSelectedNotesVertically();
                return true;
            }
			if (AdeInputManager.Instance.CheckHotkeyActionPressed(AdeInputManager.Instance.Hotkeys.Mirror))
			{
				MirrorSelectedNotes();
				return true;
			}
			return false;
		}
		public void ManuallyExecuteOperation()
		{
			AdeOperationManager.Instance.TryExecuteOperation(() =>
			{
				MirrorSelectedNotes();
				return null;
			});
		}
	}
}
