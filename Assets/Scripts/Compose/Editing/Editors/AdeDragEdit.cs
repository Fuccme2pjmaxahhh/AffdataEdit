using System;
using System.Threading;
using System.Linq;
using Arcade.Compose.Command;
using Arcade.Compose.UI;
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
        internal bool IsDraggingSlide => IsDragging && note is ArcSlide;
        private ArcNote note, before, draft;
        private ArcChart chart;
        private ArcTimingGroup previousGroup;
        private bool end, right, prepared, moved, wasEditingHold;
        private Vector2 slideRange;
        private DragMode activeMode;
        private Vector2 pointerStart;
        private int endpointTiming;
        private Vector3 previewPoint;
        private EditArcEventCommand command;
        private Action<bool> finishGroup;

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
            if (!IsDragging && !AdeGameplayContentInputHandler.InputActive) return;
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
            if (Mode == DragMode.Position && mouse != null && mouse.middleButton.wasPressedThisFrame && Keyboard.current != null
                && (Keyboard.current.leftAltKey.isPressed || Keyboard.current.rightAltKey.isPressed)
                && !AdeCameraManager.FreeCameraEnabled && AdeGameplayContentInputHandler.InputActive && selected.Count > 0)
            {
                var token = new CancellationTokenSource();
                return AdeOperationResult.FromOngoingOperation(new AdeOngoingOperation
                { cancellation = token, keepSelection = true, task = MoveSelection(token.Token).WithExceptionLogger() });
            }
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

        private async UniTask MoveSelection(CancellationToken cancellationToken)
        {
            var targets = AdeSelectionManager.Instance.SelectedNotes.Select(n => n is ArcArcTap tap
                && AdeSelectionManager.IsSingleArcTapHost(tap.Arc) ? tap.Arc : n).Distinct().ToArray();
            if (targets.Length == 0) return;
            if (targets.Any(n => !(n is ArcArc || n is ArcSlide))) { AdeToast.Instance.Show("整体位置移动请选择 Arc、Slide 或单 ArcTap"); return; }
            var cursor = AdeCursorManager.Instance;
            var camera = cursor.GameplayCamera;
            if (!TryChooseGroupAnchor(targets, camera, Mouse.current.position.ReadValue(), out var anchor, out int timing)) return;
            var originals = targets.Select(n => (ArcNote)n.Clone()).ToArray();
            var values = targets.Select(n => (ArcNote)n.Clone()).ToArray();
            var command = new BatchCommand(targets.Select((n, i) => (ICommand)new EditArcEventCommand(n, values[i], true)).ToArray(), "整体位置移动");
            float z = anchor.z;
            Vector2 ReadPoint(Vector2 pointer)
            {
                z = -ArcTimingManager.Instance.CalculatePositionByTiming(timing, ((IHasTimingGroup)targets[0]).TimingGroup) / 1000;
                var plane = new Plane(Vector3.forward, new Vector3(0, 0, z));
                var ray = camera.ScreenPointToRay(pointer);
                if (!plane.Raycast(ray, out float distance)) return new Vector2(float.NaN, float.NaN);
                var p = ray.GetPoint(distance);
                return new Vector2(ArcAlgorithm.WorldXToArc(p.x), ArcAlgorithm.WorldYToArc(p.y));
            }
            var start = new Vector2(ArcAlgorithm.WorldXToArc(anchor.x), ArcAlgorithm.WorldYToArc(anchor.y));
            if (!cursor.BeginDragCursor(true, true)) return;
            bool prepared = false, changed = false;
            IsDragging = true;
            finishGroup = commit =>
            {
                if (prepared) { if (commit && changed) AdeCommandManager.Instance.Commit(); else AdeCommandManager.Instance.Cancel(); prepared = false; }
                IsDragging = false;
            };
            try
            {
                while (true)
                {
                    await UniTask.NextFrame(cancellationToken);
                    if (!IsDragging) return;
                    if (!Application.isFocused || Mouse.current == null) return;
                    var point = ReadPoint(Mouse.current.position.ReadValue());
                    if (!float.IsNaN(point.x))
                    {
                        point = AdeGridManager.Instance.AttachDragCoordinate(point);
                        var delta = point - start;
                        for (int i = 0; i < values.Length; i++) OffsetNote(values[i], originals[i], delta);
                        changed = values.Where((value, i) => Changed(value, originals[i])).Any();
                        if (changed && !prepared) { AdeCommandManager.Instance.Prepare(command); prepared = true; }
                        else if (prepared) command.Do();
                        cursor.UpdateDragCursor(timing, new Vector3(ArcAlgorithm.ArcXToWorld(point.x), ArcAlgorithm.ArcYToWorld(point.y), z));
                        AdeValueEditor.Instance.UpdateFields();
                    }
                    if (!Mouse.current.middleButton.isPressed)
                    {
                        if (prepared) { if (changed) AdeCommandManager.Instance.Commit(); else AdeCommandManager.Instance.Cancel(); prepared = false; }
                        return;
                    }
                }
            }
            finally
            {
                if (prepared) AdeCommandManager.Instance.Cancel();
                IsDragging = false; finishGroup = null;
                cursor.EndDragCursor();
                AdeValueEditor.Instance.UpdateFields();
            }
        }

        internal static void OffsetNote(ArcNote value, ArcNote original, Vector2 delta)
        {
            if (value is ArcArc arc && original is ArcArc from)
            {
                arc.XStart = from.XStart + delta.x; arc.XEnd = from.XEnd + delta.x;
                arc.YStart = from.YStart + delta.y; arc.YEnd = from.YEnd + delta.y;
            }
            else if (value is ArcSlide slide && original is ArcSlide source)
            {
                slide.StartCenter = source.StartCenter + delta.x;
                slide.EndCenter = source.EndCenter + delta.x;
            }
        }

        internal bool BeginDrag(ArcNote target, DragMode mode, Vector2 pointer)
        {
            if (IsDragging || mode == DragMode.Off || !(target is ArcTap || target is ArcHold
                || target is ArcArc || target is ArcArcTap || target is ArcSlide)) return false;
            right = false;
            bool isEnd;
            if (mode == DragMode.Position && target is ArcSlide slide)
            {
                if (!TryChooseSlideCorner(slide, pointer, out isEnd, out right)) return false;
                slideRange = AdeSlideEditor.EditingRange(slide, isEnd);
            }
            else if (!TryChooseEndpoint(target, mode, pointer, out isEnd)) return false;
            if (!AdeCursorManager.Instance.BeginDragCursor(mode == DragMode.Position && (target is ArcArc || target is ArcSlide),
                target is ArcArc || target is ArcSlide)) return false;
            note = target; before = (ArcNote)target.Clone(); draft = (ArcNote)target.Clone();
            if (draft is ArcArcTap draftTap) draftTap.Arc = ((ArcArcTap)target).Arc;
            chart = ArcGameplayManager.Instance.Chart;
            activeMode = mode; end = isEnd; pointerStart = pointer;
            endpointTiming = end ? ((ArcLongNote)note).EndTiming : note.Timing;
            previewPoint = DragEndpointPosition();
            previousGroup = AdeTimingEditor.Instance.currentTimingGroup;
            var group = target is ArcArcTap tap ? tap.Arc.TimingGroup : ((IHasTimingGroup)target).TimingGroup;
            AdeTimingEditor.Instance.SetCurrentTimingGroup(group);
            if (note is ArcHold hold) { wasEditingHold = hold.IsEditing; hold.IsEditing = true; }
            prepared = false;
            moved = mode == DragMode.Time || note is ArcArcTap;
            command = new EditArcEventCommand(note, draft, true);
            IsDragging = true;
            if (moved) ApplyPointer(pointer);
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
                int snapped = AdeGridManager.Instance.AttachDragTimingFromPos(point.z, raw);
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
                float x = .5f - (ArcAlgorithm.ArcXToWorld(coordinate.x) / slide.WorldWidth);
                if (slideRange.x == slideRange.y) right = x > slideRange.x;
                var candidate = (ArcSlide)slide.Clone();
                AdeSlideEditor.SetRangeEndpoints(candidate, end,
                    right ? slideRange.x : Mathf.Min(x, slideRange.y),
                    right ? Mathf.Max(x, slideRange.x) : slideRange.y);
                if (!candidate.IsValid) return;
                slide.StartCenter = candidate.StartCenter; slide.StartWidth = candidate.StartWidth;
                slide.EndCenter = candidate.EndCenter; slide.EndWidth = candidate.EndWidth;
            }
        }

        internal void ApplyDraft()
        {
            if (!Changed(note, draft)) return;
            if (!prepared) { AdeCommandManager.Instance.Prepare(command); prepared = true; }
            else command.Do();
            previewPoint = DragEndpointPosition();
            AdeValueEditor.Instance.UpdateFields();
        }

        private void UpdateCursor()
        {
            previewPoint = DragEndpointPosition();
            AdeCursorManager.Instance.UpdateDragCursor(endpointTiming, previewPoint);
        }

        internal void Finish(bool commit)
        {
            if (finishGroup != null) { finishGroup(commit); return; }
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
