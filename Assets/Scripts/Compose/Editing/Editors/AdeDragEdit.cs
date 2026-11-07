using System;
using System.Threading;
using Arcade.Compose.Command;
using Arcade.Gameplay;
using Arcade.Gameplay.Chart;
using Arcade.Util.UniTaskHelper;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Arcade.Compose.Editing
{
    // One's interaction: select one note, hold middle mouse, release for one undoable edit.
    public sealed partial class AdeDragEdit : AdeOperation
    {
        public enum DragMode { Off, Time, Position }
        public static AdeDragEdit Instance { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticState() => Instance = null;
        private const string PreferenceKey = "AffdataEdit.DragMode";
        public DragMode Mode { get; private set; }
        public bool IsDragging { get; private set; }
        private ArcNote note, before, draft;
        private ArcChart chart;
        private ArcTimingGroup previousGroup;
        private bool end, prepared, moved, wasEditingHold;
        private DragMode activeMode;
        private Vector2 pointerStart;
        private int endpointTiming;
        private Vector3 previewPoint;
        private EditArcEventCommand command;

        private void Awake()
        {
            Instance = this;
            Mode = (DragMode)Mathf.Clamp(AdeSettings.GetInt(PreferenceKey, (int)DragMode.Time), 0, 2);
        }

        public void SetMode(DragMode mode)
        {
            if (Mode == mode) return;
            if (IsDragging) AdeOperationManager.Instance.CancelOngoingOperation();
            Mode = mode;
            AdeSettings.SetInt(PreferenceKey, (int)mode);
            AdeToast.Instance.Show(mode == DragMode.Time ? "中键拖动：时间" : mode == DragMode.Position ? "中键拖动：位置" : "已关闭中键拖动");
        }

        private void Update()
        {
            if (!ArcadeComposeManager.Instance.IsEditorMode || (!IsDragging && !AdeGameplayContentInputHandler.InputActive)) return;
            var input = AdeInputManager.Instance;
            var hotkeys = input.Hotkeys.Get();
            if (input.CheckHotkeyActionPressed(hotkeys.FindAction("Drag Edit Time"))) SetMode(DragMode.Time);
            else if (input.CheckHotkeyActionPressed(hotkeys.FindAction("Drag Edit Position"))) SetMode(DragMode.Position);
            else if (input.CheckHotkeyActionPressed(hotkeys.FindAction("Drag Edit Off")))
            {
                if (IsDragging) AdeOperationManager.Instance.CancelOngoingOperation();
                SetMode(DragMode.Off);
            }
        }

        public override AdeOperationResult TryExecuteOperation()
        {
            var mouse = Mouse.current;
            var selected = AdeSelectionManager.Instance.SelectedNotes;
            if (AdeCameraManager.FreeCameraEnabled || Mode == DragMode.Off || mouse == null || !mouse.middleButton.wasPressedThisFrame
                || !AdeGameplayContentInputHandler.InputActive || !ArcGameplayManager.Instance.IsLoaded || selected.Count != 1) return false;
            if (!BeginDrag(selected[0], Mode, mouse.position.ReadValue())) return false;
            var cancellation = new CancellationTokenSource();
            return AdeOperationResult.FromOngoingOperation(new AdeOngoingOperation
            {
                cancellation = cancellation, keepSelection = true, cancel = () => Finish(false),
                task = RunDrag(cancellation).WithExceptionLogger()
            });
        }

        internal bool BeginDrag(ArcNote target, DragMode mode, Vector2 pointer)
        {
            if (IsDragging || mode == DragMode.Off || !(target is ArcTap || target is ArcHold
                || target is ArcArc || target is ArcArcTap || target is ArcSlide)) return false;
            if (!TryChooseEndpoint(target, mode, pointer, out bool isEnd)) return false;
            if (!AdeCursorManager.Instance.BeginDragCursor(mode == DragMode.Position && (target is ArcArc || target is ArcSlide),
                target is ArcArc || target is ArcSlide)) return false;
            ArcGameplayManager.Instance.Pause();
            note = target; before = (ArcNote)target.Clone(); draft = (ArcNote)target.Clone();
            if (draft is ArcArcTap draftTap) draftTap.Arc = ((ArcArcTap)target).Arc;
            chart = ArcGameplayManager.Instance.Chart;
            activeMode = mode; end = isEnd; pointerStart = pointer;
            endpointTiming = end ? ((ArcLongNote)note).EndTiming : note.Timing;
            previewPoint = EndpointPosition(note, end);
            previousGroup = AdeTimingEditor.Instance.currentTimingGroup;
            var group = target is ArcArcTap tap ? tap.Arc.TimingGroup : ((IHasTimingGroup)target).TimingGroup;
            AdeTimingEditor.Instance.SetCurrentTimingGroup(group);
            if (note is ArcHold hold) { wasEditingHold = hold.IsEditing; hold.IsEditing = true; }
            prepared = moved = false;
            command = new EditArcEventCommand(note, draft);
            IsDragging = true;
            UpdateCursor();
            AdeToast.Instance.Show(mode == DragMode.Time || target is ArcArcTap
                ? (end ? "拖动结束时间" : "拖动开始时间") : (end ? "拖动终点位置" : "拖动起点位置"));
            return true;
        }

        private async UniTask RunDrag(CancellationTokenSource cancellation)
        {
            try
            {
                while (IsDragging)
                {
                    await UniTask.NextFrame(cancellation.Token);
                    if (!Application.isFocused || Mouse.current == null || ArcGameplayManager.Instance.Chart != chart) return;
                    bool constrainedPosition = activeMode == DragMode.Position && (note is ArcArc || note is ArcSlide)
                        && AdeGridManager.Instance.EnableVertical;
                    if (AdeGameplayContentInputHandler.InputActive || constrainedPosition) ApplyPointer(Mouse.current.position.ReadValue());
                    else UpdateCursor();
                    if (!Mouse.current.middleButton.isPressed) { Finish(true); return; }
                }
            }
            finally
            {
                Finish(false);
                cancellation.Dispose();
            }
        }

        internal void ApplyPointer(Vector2 pointer)
        {
            moved |= (pointer - pointerStart).sqrMagnitude >= 4;
            if (!moved) { UpdateCursor(); return; }
            bool timing = activeMode == DragMode.Time || note is ArcArcTap;
            Ray ray = AdeCursorManager.Instance.GameplayCamera.ScreenPointToRay(pointer);
            if (timing)
            {
                if (!TryGroundTiming(ray, endpointTiming, out int raw, out var point)) return;
                int snapped = AdeGridManager.Instance.AttachBeatlineTimingFromPos(point.z, raw);
                ApplyTiming(snapped);
            }
            else if (note is ArcTap || note is ArcHold)
            {
                if (!Ground.Raycast(ray, out float distance)) return;
                float x = ray.GetPoint(distance).x;
                int track = Mathf.Clamp((int)(x / -4.25f + 3), 0, 5);
                if (draft is ArcTap tap)
                {
                    if (tap.FloatLane.HasValue) tap.FloatLane = (8.5f - x) / 17f;
                    else tap.Track = track;
                }
                else
                {
                    var hold = (ArcHold)draft;
                    if (hold.FloatLane.HasValue) hold.FloatLane = (8.5f - x) / 17f;
                    else hold.Track = track;
                }
            }
            else
            {
                float z = -ArcTimingManager.Instance.CalculatePositionByTiming(endpointTiming,
                    AdeTimingEditor.Instance.currentTimingGroup) / 1000;
                if (!new Plane(Vector3.forward, new Vector3(0, 0, z)).Raycast(ray, out float distance)) return;
                Vector3 point = ray.GetPoint(distance);
                var grid = AdeGridManager.Instance;
                Vector2 coordinate = grid.AttachDragCoordinate(new Vector2(ArcAlgorithm.WorldXToArc(point.x),
                    ArcAlgorithm.WorldYToArc(point.y)));
                ApplyCoordinate(coordinate);
            }
            ApplyDraft();
            UpdateCursor();
        }

        internal void ApplyTiming(int timing)
        {
            if (draft is ArcArc arc)
            {
                int lower = before.Timing, upper = ((ArcLongNote)before).EndTiming;
                foreach (var tap in arc.ArcTaps) { lower = Math.Max(lower, tap.Timing); upper = Math.Min(upper, tap.Timing); }
                if (end) arc.EndTiming = Math.Max(lower, timing);
                else arc.Timing = Math.Min(upper, timing);
            }
            else if (draft is ArcLongNote longNote)
            {
                int gap = draft is ArcSlide ? 2 : 1;
                if (end) longNote.EndTiming = (int)Math.Min(int.MaxValue, Math.Max((long)before.Timing + gap, timing));
                else longNote.Timing = (int)Math.Max(int.MinValue, Math.Min((long)((ArcLongNote)before).EndTiming - gap, timing));
            }
            else if (draft is ArcArcTap tap)
                tap.Timing = Mathf.Clamp(timing, tap.Arc.Timing, tap.Arc.EndTiming);
            else draft.Timing = timing;
            endpointTiming = end ? ((ArcLongNote)draft).EndTiming : draft.Timing;
        }

        internal void ApplyCoordinate(Vector2 coordinate)
        {
            if (draft is ArcArc arc)
            {
                if (end) { arc.XEnd = coordinate.x; arc.YEnd = coordinate.y; }
                else { arc.XStart = coordinate.x; arc.YStart = coordinate.y; }
            }
            else if (draft is ArcSlide slide)
            {
                // Slide uses half the Arc's world width, and retains its endpoint width.
                float center = .5f - (ArcAlgorithm.ArcXToWorld(coordinate.x) / slide.WorldWidth);
                if (end) slide.EndCenter = center; else slide.StartCenter = center;
                if (!slide.IsValid)
                {
                    if (end) slide.EndCenter = ((ArcSlide)note).EndCenter;
                    else slide.StartCenter = ((ArcSlide)note).StartCenter;
                }
            }
        }

        internal void ApplyDraft()
        {
            if (!Changed(note, draft)) return;
            if (!prepared) { AdeCommandManager.Instance.Prepare(command); prepared = true; }
            else command.Do();
            previewPoint = EndpointPosition(note, end);
            AdeValueEditor.Instance.UpdateFields();
        }

        private void UpdateCursor()
        {
            previewPoint = EndpointPosition(note, end);
            AdeCursorManager.Instance.UpdateDragCursor(endpointTiming, previewPoint);
        }

        internal void Finish(bool commit)
        {
            if (!IsDragging) return;
            IsDragging = false;
            try
            {
                if (prepared)
                {
                    if (commit && Changed(note, before)) AdeCommandManager.Instance.Commit();
                    else AdeCommandManager.Instance.Cancel();
                }
            }
            finally
            {
                if (note is ArcHold hold) hold.IsEditing = wasEditingHold;
                AdeCursorManager.Instance.EndDragCursor();
                AdeTimingEditor.Instance.SetCurrentTimingGroup(previousGroup);
                note = before = draft = null; chart = null; command = null; previousGroup = null;
                prepared = moved = false;
                AdeValueEditor.Instance.UpdateFields();
            }
        }

        private void OnApplicationFocus(bool focused) { if (!focused && IsDragging) AdeOperationManager.Instance.CancelOngoingOperation(); }
        private void OnDisable() { if (IsDragging) AdeOperationManager.Instance.CancelOngoingOperation(); }
        private void OnDestroy() { if (Instance == this) Instance = null; }
    }
}
