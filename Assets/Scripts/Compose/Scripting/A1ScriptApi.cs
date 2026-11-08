using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Arcade.Gameplay;
using Arcade.Gameplay.Chart;
using MoonSharp.Interpreter;
using UnityEngine;
using ChartLineType = Arcade.Gameplay.Chart.ArcLineType;

// Arcade-One's Lua external tool API ("Lua♭"), as its bundled tools use it:
// ToolsProviders/ToolsNames declarations, NewArcEvent/AddArcEvent, Ease and Flat.
namespace Arcade.Compose.Scripting.A1
{
    public enum EventType { Timing, Tap, Hold, Arc, ArcTap, Camera, SceneControl }

    public enum ArcLineType { B, S, Si, So, SiSi, SiSo, SoSi, SoSo }

    internal static class A1Globals
    {
        public static void Assign(Table globals)
        {
            globals["EventType"] = UserData.CreateStatic<EventType>();
            globals["ArcLineType"] = UserData.CreateStatic<ArcLineType>();
            globals["Ease"] = new EaseAPI();
            globals["Flat"] = new FlatAPI();
            globals["NewArcEvent"] = (Func<EventType, ArcEventLua>)NewArcEvent;
            globals["AddArcEvent"] = (Action<ArcEventLua>)AddArcEvent;
            globals["RemoveArcEvent"] = (Action<ArcEventLua>)RemoveArcEvent;
            globals["GetSelectedNotes"] = DynValue.NewCallback((context, args) => ToTable(context, AdeScriptLibrary.SelectedEvents()));
            globals["GetNotesInRange"] = DynValue.NewCallback((context, args) => ToTable(context, InRange(Int(args, 0), Int(args, 1), false)));
            globals["GetEventsInRange"] = DynValue.NewCallback((context, args) => ToTable(context, InRange(Int(args, 0), Int(args, 1), true)));
        }

        private static int Int(CallbackArguments args, int index) => (int)args.AsType(index, "range", DataType.Number).Number;

        private static ArcEventLua NewArcEvent(EventType type)
        {
            var session = AdeScriptSession.Require();
            switch (type)
            {
                case EventType.Timing:
                    return new ArcEventLua(new ArcTiming { Bpm = ArcTimingManager.Instance.BaseBpm, BeatsPerLine = 4 }, true);
                case EventType.Tap:
                    return new ArcEventLua(new ArcTap { Track = 1, TimingGroup = session.CurrentGroup }, true);
                case EventType.Hold:
                    return new ArcEventLua(new ArcHold { Track = 1, TimingGroup = session.CurrentGroup }, true);
                case EventType.Arc:
                    return new ArcEventLua(new ArcArc
                    {
                        CurveType = ArcCurveType.S,
                        Effect = "none",
                        LineType = ChartLineType.FalseNotVoid,
                        TimingGroup = session.CurrentGroup,
                    }, true);
                default:
                    throw new ScriptRuntimeException($"AffdataEdit 暂不支持创建 Arcade-One 的 {type} 事件。");
            }
        }

        private static void AddArcEvent(ArcEventLua e)
        {
            if (e == null) throw new ScriptRuntimeException("AddArcEvent 需要一个事件。");
            e.Add();
        }

        private static void RemoveArcEvent(ArcEventLua e) => e?.Remove();

        private static IEnumerable<ArcEvent> InRange(int start, int end, bool all)
        {
            var events = new List<ArcEvent>();
            events.AddRange(ArcTapNoteManager.Instance.Taps.Where(n => n.Timing >= start && n.Timing <= end));
            events.AddRange(ArcHoldNoteManager.Instance.Holds.Where(n => n.Timing >= start && n.Timing <= end));
            events.AddRange(ArcArcManager.Instance.Arcs.Where(n => n.Timing >= start && n.Timing <= end));
            if (all) events.AddRange(ArcTimingManager.Instance.Timings.Where(n => n.Timing >= start && n.Timing <= end));
            return events.OrderBy(e => e.Timing);
        }

        private static DynValue ToTable(ScriptExecutionContext context, IEnumerable<ArcEvent> events)
        {
            AdeScriptSession.Require();
            var table = new Table(context.GetScript());
            foreach (var e in events)
                if (e is ArcTap || e is ArcHold || e is ArcArc || e is ArcTiming) table.Append(UserData.Create(new ArcEventLua(e, false)));
            return DynValue.NewTable(table);
        }
    }

    // One object for every event type, with the field names Arcade-One's scripts assign.
    public class ArcEventLua
    {
        private readonly ArcEvent live;
        private bool added;

        internal ArcEventLua(ArcEvent e, bool detached)
        {
            live = e;
            added = !detached;
        }

        private static AdeScriptSession S => AdeScriptSession.Require();

        // A new event is changed in place until it is added; an existing one through the session.
        private ArcEvent Value => added ? S.View(live) : live;
        private ArcEvent Edit() => added ? S.Mutable(live) : live;

        private T As<T>(string member) where T : ArcEvent
            => Value as T ?? throw new ScriptRuntimeException($"{Type} 事件没有 {member} 字段。");

        private T EditAs<T>(string member) where T : ArcEvent
        {
            As<T>(member);
            return (T)Edit();
        }

        public EventType Type => live is ArcTiming ? EventType.Timing : live is ArcTap ? EventType.Tap
            : live is ArcHold ? EventType.Hold : EventType.Arc;

        public int Timing
        {
            get => Value.Timing;
            set => Edit().Timing = value;
        }

        public int EndTiming
        {
            get => As<ArcLongNote>(nameof(EndTiming)).EndTiming;
            set => EditAs<ArcLongNote>(nameof(EndTiming)).EndTiming = value;
        }

        private ArcTimingGroup timingGroup;

        public int TimingGroup
        {
            get => S.IdOf(live is ArcTiming t ? (added ? S.GroupOf(t) : timingGroup) : ((IHasTimingGroup)Value).TimingGroup);
            set
            {
                if (value < 0 || value >= S.GroupCount) throw new ScriptRuntimeException($"时间组 {value} 不存在。");
                var group = S.Group(value);
                if (live is ArcTiming timing)
                {
                    if (added) S.MoveTiming(timing, group);
                    else timingGroup = group;
                }
                else ((ISetableTimingGroup)Edit()).TimingGroup = group;
            }
        }

        public float Bpm
        {
            get => As<ArcTiming>(nameof(Bpm)).Bpm;
            set => EditAs<ArcTiming>(nameof(Bpm)).Bpm = value;
        }

        public float BeatsPerLine
        {
            get => As<ArcTiming>(nameof(BeatsPerLine)).BeatsPerLine;
            set => EditAs<ArcTiming>(nameof(BeatsPerLine)).BeatsPerLine = value;
        }

        public int Track
        {
            get => Value is ArcTap tap ? tap.Track : As<ArcHold>(nameof(Track)).Track;
            set
            {
                if (Value is ArcTap) EditAs<ArcTap>(nameof(Track)).Track = value;
                else EditAs<ArcHold>(nameof(Track)).Track = value;
            }
        }

        public float XStart { get => As<ArcArc>(nameof(XStart)).XStart; set => EditAs<ArcArc>(nameof(XStart)).XStart = value; }
        public float XEnd { get => As<ArcArc>(nameof(XEnd)).XEnd; set => EditAs<ArcArc>(nameof(XEnd)).XEnd = value; }
        public float YStart { get => As<ArcArc>(nameof(YStart)).YStart; set => EditAs<ArcArc>(nameof(YStart)).YStart = value; }
        public float YEnd { get => As<ArcArc>(nameof(YEnd)).YEnd; set => EditAs<ArcArc>(nameof(YEnd)).YEnd = value; }
        public int Color { get => As<ArcArc>(nameof(Color)).Color; set => EditAs<ArcArc>(nameof(Color)).Color = value; }

        public string Effect
        {
            get => As<ArcArc>(nameof(Effect)).Effect;
            set => EditAs<ArcArc>(nameof(Effect)).Effect = string.IsNullOrEmpty(value) ? "none" : value;
        }

        public ArcLineType LineType
        {
            get => (ArcLineType)Enum.Parse(typeof(ArcLineType), As<ArcArc>(nameof(LineType)).CurveType.ToString());
            set => EditAs<ArcArc>(nameof(LineType)).CurveType = (ArcCurveType)Enum.Parse(typeof(ArcCurveType), value.ToString());
        }

        public bool IsVoid
        {
            get => As<ArcArc>(nameof(IsVoid)).IsVoid;
            set => EditAs<ArcArc>(nameof(IsVoid)).LineType = value ? ChartLineType.TrueIsVoid : ChartLineType.FalseNotVoid;
        }

        internal void Add()
        {
            if (added) throw new ScriptRuntimeException("该事件已经在谱面中。");
            if (live is ArcTiming timing) S.AddTiming(timing, timingGroup);
            else S.Add(live);
            added = true;
        }

        internal void Remove()
        {
            if (!added) return;
            S.Remove(live);
        }

        public override string ToString() => $"{Type}({Timing})";
    }

    public class EaseAPI
    {
        // Returns -1 for an unknown ease name, as Arcade-One documents.
        public float Easing(string type, float start, float end, float p)
        {
            if (type == "Linear") return Linear(start, end, p);
            if (!Enum.TryParse(type, out Aa.Ease ease) || !Enum.TryParse(ease.ToString(), out LitMotion.Ease mapped)) return -1;
            return start + (end - start) * LitMotion.EaseUtility.Evaluate(p, mapped);
        }

        public float Linear(float start, float end, float p) => start + (end - start) * p;
        public float pow(float x, float y) => Mathf.Pow(x, y);
        public float sqrt(float x) => Mathf.Sqrt(x);
    }

    public class FlatAPI
    {
        private static readonly System.Random random = new System.Random();

        public float ToFloat(string text)
        {
            if (float.TryParse(text?.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float value)) return value;
            throw new ScriptRuntimeException($"无法把 \"{text}\" 转换为数字。");
        }

        public int Rand(int a, int b) => random.Next(Math.Min(a, b), Math.Max(a, b) + 1);

        public int GetRandomSeed() => random.Next();
    }
}
