using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Arcade.Compose.Editing;
using Arcade.Gameplay;
using Arcade.Gameplay.Chart;
using MoonSharp.Interpreter;
using UnityEngine;
using ChartLineType = Arcade.Gameplay.Chart.ArcLineType;

// Arcade Alpha's external tool API (Tool, NoteEditor and helpers) for AffdataEdit.
namespace Arcade.Compose.Scripting.Aa
{
    public enum ArgumentType { String, Float, Int, Bool, Timing, List }

    // DOTween's Ease names, which Alpha exposes through EaseManager.
    public enum Ease
    {
        Unset, Linear, InSine, OutSine, InOutSine, InQuad, OutQuad, InOutQuad, InCubic, OutCubic, InOutCubic,
        InQuart, OutQuart, InOutQuart, InQuint, OutQuint, InOutQuint, InExpo, OutExpo, InOutExpo,
        InCirc, OutCirc, InOutCirc, InElastic, OutElastic, InOutElastic, InBack, OutBack, InOutBack,
        InBounce, OutBounce, InOutBounce, Flash, InFlash, OutFlash, InOutFlash, INTERNAL_Zero, INTERNAL_Custom
    }

    internal static class AaGlobals
    {
        public static void Assign(Table globals)
        {
            globals["NoteEditor"] = new NoteEditorController();
            globals["Tool"] = new ToolAPI();
            globals["ArgumentType"] = UserData.CreateStatic<ArgumentType>();
            globals["ArcLineType"] = UserData.CreateStatic<ArcLineType>();
            globals["CameraEasing"] = UserData.CreateStatic<CameraEasing>();
            globals["ScenecontrolType"] = UserData.CreateStatic<SceneControlType>();
            globals["SceneControlType"] = UserData.CreateStatic<SceneControlType>();
            globals["Ease"] = UserData.CreateStatic<Ease>();
            globals["EaseManager"] = new EaseManagerAPI();
            globals["NoteEvent"] = new NoteEventAPI();
            globals["TimingManager"] = new TimingManagerAPI();
            globals["Toast"] = new ToastAPI();
            globals["Dialog"] = new DialogAPI();
            globals["Random"] = new RandomAPI();
            globals["Data"] = new DataAPI();
            globals["Mathf"] = UserData.CreateStatic(typeof(Mathf));
        }
    }

    public class ToolAPI
    {
        public void Register(string name, Table table, Closure function)
        {
            var parameters = new List<AdeScriptParameter>();
            for (int i = 1; i <= table.Length; i++)
            {
                var entry = table.Get(i);
                if (entry.Type != DataType.Table || entry.Table.Length < 2) continue;
                var row = entry.Table;
                var kind = row.Get(2);
                ArgumentType type;
                if (kind.Type == DataType.UserData && kind.UserData.Object is ArgumentType value) type = value;
                else if (kind.Type == DataType.Number) type = (ArgumentType)(int)kind.Number;
                else throw new ScriptRuntimeException($"参数 {row.Get(1).CastToString()} 的类型无效。");
                var parameter = new AdeScriptParameter { Name = row.Get(1).CastToString() ?? "", Type = (AdeScriptParameterType)type };
                var preset = row.Get(3);
                if (type == ArgumentType.List)
                {
                    if (preset.Type == DataType.Table)
                        parameter.Options = preset.Table.Values.Select(v => v.CastToString() ?? "").ToList();
                    else if (preset.Type == DataType.UserData && preset.UserData.Object is IEnumerable list)
                        parameter.Options = list.Cast<object>().Select(o => Convert.ToString(o, CultureInfo.InvariantCulture) ?? "").ToList();
                    else throw new ScriptRuntimeException("List 参数必须提供选项表。");
                    var selected = row.Get(4);
                    parameter.Default = selected.IsNil() ? parameter.Options.FirstOrDefault() ?? "" : selected.CastToString();
                }
                else parameter.Default = Text(type, preset);
                parameters.Add(parameter);
            }
            AdeScriptLibrary.RegisterAa(name, AdeScriptToolKind.Parameters, function, null, parameters);
        }

        public void Register(Table table, Closure function) => Register("", table, function);

        public void Register(string name, Closure function)
            => AdeScriptLibrary.RegisterAa(name, AdeScriptToolKind.Selection, function, null, null);

        public void Register(string name, AsyncEditing editing)
            => AdeScriptLibrary.RegisterAa(name, AdeScriptToolKind.Interactive, null, editing, null);

        private static string Text(ArgumentType type, DynValue value)
        {
            if (value.IsNil()) return type == ArgumentType.Bool ? "false" : type == ArgumentType.String ? "" : "0";
            switch (type)
            {
                case ArgumentType.Float:
                    return ((float)value.CastToNumber().GetValueOrDefault()).ToString(CultureInfo.InvariantCulture);
                case ArgumentType.Int:
                case ArgumentType.Timing:
                    return ((int)value.CastToNumber().GetValueOrDefault()).ToString(CultureInfo.InvariantCulture);
                case ArgumentType.Bool:
                    return value.CastToBool() ? "true" : "false";
                default:
                    return value.CastToString() ?? "";
            }
        }
    }

    public class AsyncEditing
    {
        internal readonly Closure Function;
        internal readonly List<(string Name, bool Position)> Commands = new List<(string, bool)>();

        public AsyncEditing(Closure function) { Function = function; }

        public void AddSelectTimingCommand(string argName) => Commands.Add((argName, false));
        public void AddSelectPositionCommand(string argName) => Commands.Add((argName, true));
    }

    public class NoteEditorController
    {
        private static readonly Dictionary<string, Type> TypeMap = new Dictionary<string, Type>
        {
            [nameof(LuaTapNote)] = typeof(LuaTapNote),
            [nameof(LuaHoldNote)] = typeof(LuaHoldNote),
            [nameof(LuaArcNote)] = typeof(LuaArcNote),
            [nameof(LuaArcTapNote)] = typeof(LuaArcTapNote),
            ["LuaFlickNote"] = typeof(void),
            [nameof(LuaCameraControl)] = typeof(LuaCameraControl),
        };

        private static AdeScriptSession S => AdeScriptSession.Require();

        public int TimingGroupCount => S.GroupCount;
        public float BeatlineDensity => AdeGridManager.Instance.BeatlineDensity;

        // ---- Timing groups.

        public void CreateTimingGroup() => S.CurrentGroup = S.CreateGroup();
        public void SelectBaseGroup() => S.CurrentGroup = null;
        public void SelectLastGroup() => S.CurrentGroup = S.Group(S.GroupCount - 1);
        public void SelectTimingGroup(int id) => S.CurrentGroup = S.Group(id);
        public LuaTimingGroup GetCurrentTimingGroup() => new LuaTimingGroup(S.CurrentGroup);
        public LuaTimingGroup GetBaseTimingGroup() => new LuaTimingGroup(null);
        public LuaTimingGroup GetLastTimingGroup() => new LuaTimingGroup(S.Group(S.GroupCount - 1));
        public LuaTimingGroup GetTimingGroup(int id) => new LuaTimingGroup(S.Group(id));

        // ---- Adding events. New events go to the current timing group.

        public LuaTimingEvent AddTimingEvent(int timing, float bpm, float beats)
        {
            var e = new ArcTiming { Timing = timing, Bpm = bpm, BeatsPerLine = beats };
            S.AddTiming(e, S.CurrentGroup);
            return new LuaTimingEvent(e);
        }

        public LuaTapNote AddTapNote(int timing, int track) => AddTap(timing, track, null);
        public LuaTapNote AddTapNoteFloat(int timing, float lane) => AddTap(timing, Track(lane), lane);

        private LuaTapNote AddTap(int timing, int track, float? lane)
        {
            var note = new ArcTap { Timing = timing, Track = track, TimingGroup = S.CurrentGroup };
            note.FloatLane = lane;
            S.Add(note);
            return new LuaTapNote(note);
        }

        public LuaHoldNote AddHoldNote(int timing, int endTiming, int track) => AddHold(timing, endTiming, track, null);
        public LuaHoldNote AddHoldNoteFloat(int timing, int endTiming, float lane) => AddHold(timing, endTiming, Track(lane), lane);

        private LuaHoldNote AddHold(int timing, int endTiming, int track, float? lane)
        {
            var note = new ArcHold { Timing = timing, EndTiming = endTiming, Track = track, TimingGroup = S.CurrentGroup };
            note.FloatLane = lane;
            S.Add(note);
            return new LuaHoldNote(note);
        }

        private static int Track(float lane) => Mathf.Clamp(Mathf.FloorToInt(lane * 4) + 1, 0, 5);

        public LuaArcNote AddArcNote(int timing, int endTiming, float startX, float endX, string easing,
            float startY, float endY, int colorID, string sfxName, bool isTrace)
            => AddArc(timing, endTiming, startX, endX, Curve(easing), startY, endY, colorID, sfxName, isTrace, null);

        public LuaArcNote AddArcNote(int timing, int endTiming, float startX, float endX, string easing,
            float startY, float endY, int colorID, bool isTrace)
            => AddArc(timing, endTiming, startX, endX, Curve(easing), startY, endY, colorID, "none", isTrace, null);

        public LuaArcNote AddArcNote(int timing, int endTiming, float startX, float endX, ArcLineType easing,
            float startY, float endY, int colorID, bool isTrace)
            => AddArc(timing, endTiming, startX, endX, AaConvert.CurveType(easing), startY, endY, colorID, "none", isTrace, null);

        public LuaArcNote AddArcNote(int timing, int endTiming, float startX, float endX, string easing,
            float startY, float endY, int colorID, string sfxName, bool isTrace, Table arctaps)
            => AddArc(timing, endTiming, startX, endX, Curve(easing), startY, endY, colorID, sfxName, isTrace, arctaps);

        public LuaArcNote AddArcNote(int timing, int endTiming, float startX, float endX, ArcLineType easing,
            float startY, float endY, int colorID, string sfxName, bool isTrace, Table arctaps)
            => AddArc(timing, endTiming, startX, endX, AaConvert.CurveType(easing), startY, endY, colorID, sfxName, isTrace, arctaps);

        public LuaArcNote AddSingleArcTapNote(int timing, float x, float y)
        {
            var arc = NewArc(timing, timing + 1, x, x, ArcCurveType.S, y, y, 0, "none", true);
            arc.ArcTaps.Add(new ArcArcTap { Timing = timing });
            S.Add(arc);
            return new LuaArcNote(arc);
        }

        private static ArcCurveType Curve(string easing)
        {
            if (AaConvert.TryCurveType(easing, out var curve)) return curve;
            throw new ScriptRuntimeException($"AffdataEdit 不支持 Arc 缓动类型 {easing}。");
        }

        private LuaArcNote AddArc(int timing, int endTiming, float startX, float endX, ArcCurveType curve,
            float startY, float endY, int color, string sfx, bool trace, Table arctaps)
        {
            var arc = NewArc(timing, endTiming, startX, endX, curve, startY, endY, color, sfx, trace);
            if (arctaps != null)
            {
                foreach (var value in arctaps.Values)
                {
                    int time;
                    float width = 1;
                    if (value.Type == DataType.Table)
                    {
                        time = (int)value.Table.Get(1).CastToNumber().GetValueOrDefault();
                        var w = value.Table.Get(2);
                        if (!w.IsNil()) width = (float)w.CastToNumber().GetValueOrDefault(1);
                    }
                    else time = (int)value.CastToNumber().GetValueOrDefault();
                    arc.ArcTaps.Add(new ArcArcTap { Timing = time, Width = width });
                }
            }
            S.Add(arc);
            return new LuaArcNote(arc);
        }

        private static ArcArc NewArc(int timing, int endTiming, float startX, float endX, ArcCurveType curve,
            float startY, float endY, int color, string sfx, bool trace) => new ArcArc
            {
                Timing = timing,
                EndTiming = endTiming,
                XStart = startX,
                XEnd = endX,
                CurveType = curve,
                YStart = startY,
                YEnd = endY,
                Color = color,
                Effect = string.IsNullOrEmpty(sfx) ? "none" : sfx,
                LineType = trace ? ChartLineType.TrueIsVoid : ChartLineType.FalseNotVoid,
                TimingGroup = S.CurrentGroup,
            };

        public LuaCameraControl AddCameraControl(int timing, Vec3Lua move, Vec3Lua rotate, CameraEasing ease, int duration)
        {
            var camera = new ArcCamera
            {
                Timing = timing,
                Move = move.ToVector3(),
                Rotate = rotate.ToVector3(),
                CameraType = (CameraEaseType)Enum.Parse(typeof(CameraEaseType), ease.ToString()),
                Duration = duration,
                TimingGroup = S.CurrentGroup,
            };
            S.Add(camera);
            return new LuaCameraControl(camera);
        }

        public void AddInternalSceneControl(int timing, SceneControlType type, float floatArg, int intArg)
            => AddSceneControl(timing, type.ToString().ToLowerInvariant(), floatArg, intArg);

        public void AddSceneControl(int timing, string type) => AddScene(timing, type, new List<Aff.IRawAffValue>());

        public void AddSceneControl(int timing, string type, float floatArg, int intArg)
            => AddScene(timing, type, new List<Aff.IRawAffValue> { new Aff.RawAffFloat { data = floatArg }, new Aff.RawAffInt { data = intArg } });

        private void AddScene(int timing, string type, List<Aff.IRawAffValue> args)
        {
            var raw = new Aff.RawAffSceneControl { Timing = timing, Type = type, Params = args };
            S.Add(new ArcSceneControl(raw, S.CurrentGroup));
        }

        // ---- Queries read the chart as it was when the tool started, like Alpha.

        public List<object> SelectEventsInRange(int startTiming, int endTiming)
        {
            var result = new List<(int Timing, object Event)>();
            foreach (var tap in ArcTapNoteManager.Instance.Taps)
                if (tap.Timing >= startTiming && tap.Timing <= endTiming) result.Add((tap.Timing, new LuaTapNote(tap)));
            foreach (var hold in ArcHoldNoteManager.Instance.Holds)
                if (hold.Timing >= startTiming && hold.EndTiming <= endTiming) result.Add((hold.Timing, new LuaHoldNote(hold)));
            foreach (var arc in ArcArcManager.Instance.Arcs)
            {
                if (arc.Timing >= startTiming && arc.EndTiming <= endTiming) result.Add((arc.Timing, new LuaArcNote(arc)));
                foreach (var tap in arc.ArcTaps)
                    if (tap.Timing >= startTiming && tap.Timing <= endTiming) result.Add((tap.Timing, new LuaArcTapNote(tap)));
            }
            foreach (var camera in ArcCameraManager.Instance.Cameras)
                if (camera.Timing >= startTiming && camera.Timing <= endTiming) result.Add((camera.Timing, new LuaCameraControl(camera)));
            return result.OrderBy(e => e.Timing).Select(e => e.Event).ToList();
        }

        public List<object> SelectEventsInRangeWithinCurrentGroup(int startTiming, int endTiming)
            => SelectEventsInRange(startTiming, endTiming).Where(e => ((ILuaEvent)e).GroupID == S.IdOf(S.CurrentGroup)).ToList();

        public List<object> SelectEventsInRangeWithType(string type, int startTiming, int endTiming)
        {
            if (!TypeMap.TryGetValue(type, out var t)) throw new ScriptRuntimeException($"未知事件类型 {type}。");
            return SelectEventsInRange(startTiming, endTiming).Where(e => e.GetType() == t).ToList();
        }

        public List<object> SelectEventsInRangeWithTypeWithinCurrentGroup(string type, int startTiming, int endTiming)
            => SelectEventsInRangeWithType(type, startTiming, endTiming).Where(e => ((ILuaEvent)e).GroupID == S.IdOf(S.CurrentGroup)).ToList();

        public List<LuaTimingEvent> SelectTimingEventsInRange(int startTiming, int endTiming, int groupID)
            => ArcTimingManager.Instance.GetTiming(S.Group(groupID))
                .Where(t => t.Timing >= startTiming && t.Timing <= endTiming)
                .Select(t => new LuaTimingEvent(t)).ToList();

        public List<LuaTimingEvent> SelectTimingEventsInRangeWithinCurrentGroup(int startTiming, int endTiming)
            => SelectTimingEventsInRange(startTiming, endTiming, S.IdOf(S.CurrentGroup));

        public LuaTimingEvent SelectTimingEvent(int time, int groupID)
        {
            var result = ArcTimingManager.Instance.GetTiming(S.Group(groupID)).LastOrDefault(t => time > t.Timing);
            return result == null ? null : new LuaTimingEvent(result);
        }

        public LuaTimingEvent SelectTimingEvent(int time) => SelectTimingEvent(time, S.IdOf(S.CurrentGroup));

        public void RemoveEvent(ILuaEvent e) => e?.Remove();

        public void RemoveEvents(Table events)
        {
            foreach (var value in events.Values)
                if (value.Type == DataType.UserData && value.UserData.Object is ILuaEvent e) e.Remove();
        }

        public void RemoveEvents(IEnumerable events)
        {
            foreach (var value in events.Cast<object>().ToList())
                if (value is ILuaEvent e) e.Remove();
        }

        public static List<object> GetSelectedNotes() => AdeScriptLibrary.SelectedAaNotes();

        public float GetIntervalOfBeatlineAtTiming(int timing, float density, int groupId)
        {
            float bpm = Math.Abs(ArcTimingManager.Instance.CalculateBpmByTiming(timing, S.Group(groupId)));
            return bpm < float.Epsilon ? 0 : 60000 / bpm / density;
        }

        public float GetIntervalOfBeatlineAtTiming(int timing, int groupId)
            => GetIntervalOfBeatlineAtTiming(timing, AdeGridManager.Instance.BeatlineDensity, groupId);

        public float GetIntervalOfBeatlineAtTimingInCurrentGroup(int timing, float density)
            => GetIntervalOfBeatlineAtTiming(timing, density, S.IdOf(S.CurrentGroup));

        public float GetIntervalOfBeatlineAtTimingInCurrentGroup(int timing)
            => GetIntervalOfBeatlineAtTiming(timing, AdeGridManager.Instance.BeatlineDensity, S.IdOf(S.CurrentGroup));

        public AsyncEditing CreateEditing(Closure function) => new AsyncEditing(function);
    }

    public class NoteEventAPI
    {
        public bool IsLaneNote(ILuaEvent e) => e is LuaTapNote || e is LuaHoldNote;
        public bool IsNote(ILuaEvent e) => e is ILuaNote;
        public bool IsTiming(ILuaEvent e) => e is LuaTimingEvent;
        public bool IsTap(ILuaEvent e) => e is LuaTapNote;
        public bool IsHold(ILuaEvent e) => e is LuaHoldNote;
        public bool IsArc(ILuaEvent e) => e is LuaArcNote;
        public bool IsArcTap(ILuaEvent e) => e is LuaArcTapNote;
        public bool IsFlick(ILuaEvent e) => false;
        public bool IsCamera(ILuaEvent e) => e is LuaCameraControl;
        public bool IsColorIndexNote(ILuaEvent e) => false;
        public bool IsWidthNote(ILuaEvent e) => e is LuaArcTapNote;

        public int CalculateType(ILuaEvent e)
            => e is LuaTapNote ? 1 : e is LuaHoldNote ? 2 : e is LuaArcNote ? 3 : e is LuaCameraControl ? 5 : e is LuaArcTapNote ? 6 : 0;

        public LuaTapNote CastToTapNote(ILuaEvent e) => e as LuaTapNote;
        public LuaHoldNote CastToHoldNote(ILuaEvent e) => e as LuaHoldNote;
        public LuaArcNote CastToArcNote(ILuaEvent e) => e as LuaArcNote;
        public LuaArcTapNote CastToArcTapNote(ILuaEvent e) => e as LuaArcTapNote;
        public LuaCameraControl CastToCameraControl(ILuaEvent e) => e as LuaCameraControl;
    }

    public class EaseManagerAPI
    {
        public static float Evaluate(Ease ease, float progress)
        {
            if (ease == Ease.Unset || ease == Ease.INTERNAL_Custom || ease == Ease.INTERNAL_Zero)
                throw new ScriptRuntimeException("Argument ease value is invalid");
            if (!Enum.TryParse(ease.ToString(), out LitMotion.Ease mapped))
                throw new ScriptRuntimeException($"AffdataEdit 不支持缓动 {ease}。");
            return LitMotion.EaseUtility.Evaluate(progress, mapped);
        }

        public static float Evaluate(string ease, float progress) => Evaluate(ParseEase(ease), progress);

        public static Ease ParseEase(string name)
        {
            if (!Enum.TryParse(name, out Ease ease) || ease == Ease.Unset || ease == Ease.INTERNAL_Custom || ease == Ease.INTERNAL_Zero)
                throw new ScriptRuntimeException("Unknown ease name");
            return ease;
        }

        public static List<string> GetEaseNames()
            => Enum.GetNames(typeof(Ease)).Where(n => Enum.TryParse(n, out LitMotion.Ease _)).ToList();
    }

    public class TimingManagerAPI
    {
        public int CurrentTimingGroupID => AdeScriptSession.Current is AdeScriptSession s
            ? s.IdOf(s.CurrentGroup) : AdeTimingEditor.Instance.currentTimingGroup?.Id ?? 0;
        public int TimingGroupCount => AdeScriptSession.Require().GroupCount;
    }

    public class DialogAPI
    {
        public void Show(string title, string content, string buttonContent)
            => Arcade.Compose.Dialog.AdeBasicSingleDialogContent.Instance.Show(content, title, buttonContent);
    }

    public class ToastAPI
    {
        public void Show(string message) => AdeScriptLibrary.Report(message, false);
        public void ShowError(string message) => AdeScriptLibrary.Report(message, true);
    }

    public class RandomAPI
    {
        private static readonly System.Random random = new System.Random();
        public int RandInt() => random.Next();
        public float RandFloat() => UnityEngine.Random.value;
        public int RandInt(int minValue, int maxValue) => random.Next(minValue, maxValue);
        public float RandFloat(float a, float b) => UnityEngine.Random.Range(a, b);
        public float RandF(float seed)
        {
            float value = Mathf.Sin((seed + 0.873f) * 77820.7190f) * 9128.2892f;
            return value - Mathf.Floor(value);
        }
    }

    public class DataAPI
    {
        public Vec2Lua Vec2() => new Vec2Lua(0, 0);
        public Vec2Lua Vec2(float x, float y) => new Vec2Lua(x, y);
        public Vec3Lua Vec3() => new Vec3Lua(0, 0, 0);
        public Vec3Lua Vec3(float x, float y, float z) => new Vec3Lua(x, y, z);
        public Vec2IntLua Vec2Int(int x, int y) => new Vec2IntLua(x, y);
    }
}
