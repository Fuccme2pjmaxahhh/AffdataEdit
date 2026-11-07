using System;
using System.Collections.Generic;
using System.Linq;
using Arcade.Aff;
using Arcade.Compose.Command;
using Arcade.Compose.Dialog;
using Arcade.Compose.Operation;
using Arcade.Gameplay;
using Arcade.Gameplay.Chart;
using UnityEngine;

namespace Arcade.Compose
{
    // One dispatcher for both presets; bindings never contain editor behavior.
    public sealed class AdeEditorHotkeyCommands : MonoBehaviour
    {
        private bool previewOpen;
        private static readonly ArcCurveType[] CurveCycle =
        {
            ArcCurveType.S, ArcCurveType.B, ArcCurveType.Si, ArcCurveType.So,
            ArcCurveType.SiSi, ArcCurveType.SoSo, ArcCurveType.SiSo, ArcCurveType.SoSi
        };

        private void Update()
        {
            var input = AdeInputManager.Instance;
            if (!input || !ArcadeComposeManager.Instance || !ArcGameplayManager.Instance.IsLoaded) return;
            bool modal = AdeDialogManager.Instance && AdeDialogManager.Instance.Opening.childCount > 0;
            if (input.CheckHotkeyActionPressed(input.Hotkeys.Get().FindAction("Preview Selected Code")))
            {
                if (previewOpen) AdeBasicSingleDialogContent.Instance.Dialog.Close();
                else if (!modal && !AdeOperationManager.Instance.HasOngoingOperation) ShowSelectedCode();
                return;
            }
            if (modal || !ArcadeComposeManager.Instance.IsEditorMode || AdeOperationManager.Instance.HasOngoingOperation) return;
            foreach (var action in AdeHotkeyPresets.ExtraActions)
            {
                if (action.Action == "Select Spatial Range" || action.Action == "Preview Selected Code") continue;
                if (!input.CheckHotkeyActionPressed(input.Hotkeys.Get().FindAction(action.Action))) continue;
                ExecuteArcAction(action.Action);
                return;
            }
        }

        public static ArcCurveType ReverseCurve(ArcCurveType curve)
        {
            switch (curve)
            {
                case ArcCurveType.Si: return ArcCurveType.So;
                case ArcCurveType.So: return ArcCurveType.Si;
                case ArcCurveType.SiSi: return ArcCurveType.SoSo;
                case ArcCurveType.SoSo: return ArcCurveType.SiSi;
                case ArcCurveType.SiSo: return ArcCurveType.SoSi;
                case ArcCurveType.SoSi: return ArcCurveType.SiSo;
                default: return curve;
            }
        }

        private static ArcCurveType EditCurve(string action, ArcCurveType curve)
        {
            switch (action)
            {
                case "Arc Curve S": return ArcCurveType.S;
                case "Arc Curve SiSi": return ArcCurveType.SiSi;
                case "Arc Cycle Curve": return CurveCycle[(Array.IndexOf(CurveCycle, curve) + 1) % CurveCycle.Length];
                case "Arc Toggle Curve Pair": return curve == ArcCurveType.S ? ArcCurveType.B
                    : curve == ArcCurveType.B ? ArcCurveType.S : ReverseCurve(curve);
                default: return curve;
            }
        }

        public static void ExecuteArcAction(string action)
        {
            var arcs = AdeSelectionManager.Instance.SelectedNotes.OfType<ArcArc>().ToArray();
            if (arcs.Length == 0)
            {
                if (action == "Reverse Arc") { AdeToast.Instance.Show("请先选择 Arc"); return; }
                var create = AdeClickToCreate.Instance;
                if (action == "Arc Toggle Void") create.SwitchIsVoid();
                else if (action == "Arc Toggle Color") create.SetArcColor(create.ArcColor == 0 ? 1 : 0);
                else create.SetArcCurveTypeMode((int)EditCurve(action, create.ArcCurveType));
                return;
            }
            if (action == "Arc Toggle Void" && arcs.Any(a => a.IsVoid && a.ArcTaps.Count > 0))
            {
                AdeToast.Instance.Show("Arc 含有 ArcTap 时必须是黑线");
                return;
            }
            var commands = new List<ICommand>();
            foreach (var arc in arcs)
            {
                var changed = (ArcArc)arc.Clone();
                if (action == "Reverse Arc")
                {
                    changed.XStart = arc.XEnd; changed.YStart = arc.YEnd;
                    changed.XEnd = arc.XStart; changed.YEnd = arc.YStart;
                    changed.CurveType = ReverseCurve(arc.CurveType);
                }
                else if (action == "Arc Toggle Color") changed.Color = arc.Color == 0 ? 1 : 0;
                else if (action == "Arc Toggle Void") changed.LineType = arc.IsVoid ? ArcLineType.FalseNotVoid : ArcLineType.TrueIsVoid;
                else changed.CurveType = EditCurve(action, arc.CurveType);
                if (changed.XStart != arc.XStart || changed.YStart != arc.YStart || changed.XEnd != arc.XEnd
                    || changed.YEnd != arc.YEnd || changed.Color != arc.Color || changed.LineType != arc.LineType
                    || changed.CurveType != arc.CurveType) commands.Add(new EditArcEventCommand(arc, changed));
            }
            if (commands.Count > 0) AdeCommandManager.Instance.Add(new BatchCommand(commands.ToArray(), action == "Reverse Arc" ? "Arc 反转" : "修改 Arc 属性"));
        }

        private void ShowSelectedCode()
        {
            var notes = AdeSelectionManager.Instance.SelectedNotes;
            if (notes.Count == 0) { AdeToast.Instance.Show("请先选择物件"); return; }
            var items = new List<(int Timing, ArcTimingGroup Group, IRawAffItem Item)>();
            foreach (var note in notes)
            {
                if (note is IIntoRawItem raw)
                    items.Add((note.Timing, (note as IHasTimingGroup)?.TimingGroup, raw.IntoRawItem()));
            }
            foreach (var taps in notes.OfType<ArcArcTap>().Where(t => !notes.Contains(t.Arc)).GroupBy(t => t.Arc))
            {
                // AFF requires ArcTaps to be embedded in a parent Arc statement.
                var raw = (RawAffArc)taps.Key.IntoRawItem();
                raw.ArcTaps = taps.Select(t => new RawAffArctap { Timing = t.Timing, Width = t.Width, IsMine = t.IsMine }).ToList();
                items.Add((taps.Min(t => t.Timing), taps.Key.TimingGroup, raw));
            }
            var lines = new List<string>();
            foreach (var group in items.OrderBy(i => i.Timing).GroupBy(i => i.Group))
            {
                if (group.Key == null) lines.AddRange(group.Select(i => ArcaeaFileFormat.FormatItem(i.Item)));
                else lines.Add(ArcaeaFileFormat.FormatItem(new RawAffTimingGroup
                {
                    Attributes = group.Key.Attributes,
                    Items = group.Select(i => (IRawAffNestableItem)i.Item).ToList()
                }));
            }
            previewOpen = true;
            AdeBasicSingleDialogContent.Instance.Show(string.Join("\n", lines), "选中物件代码", "关闭",
                () => previewOpen = false, Copyable: true);
        }
    }
}
