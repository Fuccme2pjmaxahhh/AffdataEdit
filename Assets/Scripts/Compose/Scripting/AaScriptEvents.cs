using System;
using System.Collections.Generic;
using System.Linq;
using Arcade.Gameplay;
using Arcade.Gameplay.Chart;
using MoonSharp.Interpreter;
using UnityEngine;
using ChartLineType = Arcade.Gameplay.Chart.ArcLineType;

// Arcade Alpha's Lua object model, mapped onto AffdataEdit events. Names and member shapes
// follow Alpha so its external tools run unchanged; everything goes through AdeScriptSession.
namespace Arcade.Compose.Scripting.Aa
{
    public enum ArcLineType { S, B, Si, So, SiSi, SiSo, SoSi, SoSo }

    public enum CameraEasing { L, Qi, Qo, Reset, S }

    public class Vec2Lua : IEquatable<Vec2Lua>
    {
        public float X, Y;
        public Vec2Lua(float x, float y) { X = x; Y = y; }
        public float x { get => X; set => X = value; }
        public float y { get => Y; set => Y = value; }
        public override string ToString() => $"Vec2({X:F3}, {Y:F3})";
        public override bool Equals(object obj) => Equals(obj as Vec2Lua);
        public bool Equals(Vec2Lua other) => !(other is null) && Mathf.Approximately(X, other.X) && Mathf.Approximately(Y, other.Y);
        public override int GetHashCode() => X.GetHashCode() ^ (Y.GetHashCode() << 2);
        public static Vec2Lua operator +(Vec2Lua a, Vec2Lua b) => new Vec2Lua(a.X + b.X, a.Y + b.Y);
        public static Vec2Lua operator +(Vec2Lua a, float v) => new Vec2Lua(a.X + v, a.Y + v);
        public static Vec2Lua operator -(Vec2Lua a, Vec2Lua b) => new Vec2Lua(a.X - b.X, a.Y - b.Y);
        public static Vec2Lua operator -(Vec2Lua a, float v) => new Vec2Lua(a.X - v, a.Y - v);
        public static Vec2Lua operator -(Vec2Lua a) => new Vec2Lua(-a.X, -a.Y);
        public static Vec2Lua operator *(Vec2Lua a, Vec2Lua b) => new Vec2Lua(a.X * b.X, a.Y * b.Y);
        public static Vec2Lua operator *(Vec2Lua a, float v) => new Vec2Lua(a.X * v, a.Y * v);
        public static Vec2Lua operator /(Vec2Lua a, Vec2Lua b) => new Vec2Lua(a.X / b.X, a.Y / b.Y);
        public static Vec2Lua operator /(Vec2Lua a, float v) => new Vec2Lua(a.X / v, a.Y / v);
        public static bool operator ==(Vec2Lua a, Vec2Lua b) => !(a is null) && !(b is null) && a.Equals(b);
        public static bool operator !=(Vec2Lua a, Vec2Lua b) => !(a == b);
    }

    public class Vec3Lua : IEquatable<Vec3Lua>
    {
        public float X, Y, Z;
        public Vec3Lua(float x, float y, float z) { X = x; Y = y; Z = z; }
        public float x { get => X; set => X = value; }
        public float y { get => Y; set => Y = value; }
        public float z { get => Z; set => Z = value; }
        [MoonSharpHidden] public Vector3 ToVector3() => new Vector3(X, Y, Z);
        public override string ToString() => $"Vec3({X:F3}, {Y:F3}, {Z:F3})";
        public override bool Equals(object obj) => Equals(obj as Vec3Lua);
        public bool Equals(Vec3Lua other) => !(other is null) && ToVector3() == other.ToVector3();
        public override int GetHashCode() => ToVector3().GetHashCode();
        public static Vec3Lua operator +(Vec3Lua a, Vec3Lua b) => new Vec3Lua(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
        public static Vec3Lua operator -(Vec3Lua a, Vec3Lua b) => new Vec3Lua(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
        public static Vec3Lua operator -(Vec3Lua a) => new Vec3Lua(-a.X, -a.Y, -a.Z);
        public static Vec3Lua operator *(Vec3Lua a, float v) => new Vec3Lua(a.X * v, a.Y * v, a.Z * v);
        public static Vec3Lua operator /(Vec3Lua a, float v) => new Vec3Lua(a.X / v, a.Y / v, a.Z / v);
        public static bool operator ==(Vec3Lua a, Vec3Lua b) => !(a is null) && !(b is null) && a.Equals(b);
        public static bool operator !=(Vec3Lua a, Vec3Lua b) => !(a == b);
    }

    public class Vec2IntLua
    {
        public int X, Y;
        public Vec2IntLua(int x, int y) { X = x; Y = y; }
        public int x { get => X; set => X = value; }
        public int y { get => Y; set => Y = value; }
        public override string ToString() => $"Vec2Int({X}, {Y})";
    }

    public interface ILuaEvent
    {
        int Timing { get; set; }
        int GroupID { get; set; }
        [MoonSharpHidden] ArcEvent Event { get; }
        [MoonSharpHidden] void Remove();
    }

    public interface ILuaNote : ILuaEvent { }

    public abstract class LuaEventBase<T> : ILuaEvent where T : ArcEvent
    {
        protected readonly T live;
        protected LuaEventBase(T e) { live = e; }

        private protected static AdeScriptSession S => AdeScriptSession.Require();
        protected T Value => S.View(live);
        protected T Edit() => S.Mutable(live);

        [MoonSharpHidden] public ArcEvent Event => live;

        public virtual int Timing
        {
            get => Value.Timing;
            set => Edit().Timing = value;
        }

        public virtual int GroupID
        {
            get => S.IdOf(S.GroupOf(live));
            set
            {
                var group = S.Group(value);
                if (live is ISetableTimingGroup) ((ISetableTimingGroup)Edit()).TimingGroup = group;
                else throw new ScriptRuntimeException("该事件不能修改时间组。");
            }
        }

        [MoonSharpHidden] public virtual void Remove() => S.Remove(live);

        public bool ReferenceEquals(ILuaEvent other) => other != null && other.Event == live;

        public override string ToString() => $"{GetType().Name}({Timing})";

        protected static ScriptRuntimeException Unsupported(string member)
            => new ScriptRuntimeException($"AffdataEdit 不支持 {member}。");
    }

    public abstract class LuaNote<T> : LuaEventBase<T>, ILuaNote where T : ArcNote
    {
        protected LuaNote(T e) : base(e) { }

        public int ColorIndex { get => throw Unsupported("ColorIndex"); set => throw Unsupported("ColorIndex"); }
        public int FingerIndex { get => throw Unsupported("FingerIndex"); set => throw Unsupported("FingerIndex"); }

        public bool ValueEquals(ILuaNote other)
        {
            if (!(other is LuaNote<T> note)) return false;
            return ValueKey() == note.ValueKey();
        }

        protected abstract string ValueKey();
    }

    public abstract class LuaLaneNote<T> : LuaNote<T> where T : ArcNote
    {
        protected LuaLaneNote(T e) : base(e) { }

        protected abstract int TrackOf(T note);
        protected abstract float? FloatLaneOf(T note);
        protected abstract void SetLane(T note, int track, float? lane);

        public bool IsFloatLane => FloatLaneOf(Value).HasValue;

        public int Track
        {
            get => TrackOf(Value);
            set => SetLane(Edit(), value, null);
        }

        public float Lane
        {
            get
            {
                var note = Value;
                return FloatLaneOf(note) ?? (8.5f - LanePosition.WorldX(TrackOf(note), null)) / 17f;
            }
            set => SetLane(Edit(), Mathf.Clamp(Mathf.FloorToInt(value * 4) + 1, 0, 5), value);
        }
    }

    public class LuaTapNote : LuaLaneNote<ArcTap>
    {
        public LuaTapNote(ArcTap e) : base(e) { }
        protected override int TrackOf(ArcTap note) => note.Track;
        protected override float? FloatLaneOf(ArcTap note) => note.FloatLane;
        protected override void SetLane(ArcTap note, int track, float? lane) { note.Track = track; note.FloatLane = lane; }
        protected override string ValueKey() { var v = Value; return $"{v.Timing},{v.Track},{v.FloatLane},{S.GroupOf(live)?.GetHashCode()}"; }

        public LuaTapNote Clone()
        {
            var copy = (ArcTap)Value.Clone();
            S.Add(copy);
            return new LuaTapNote(copy);
        }
    }

    public class LuaHoldNote : LuaLaneNote<ArcHold>
    {
        public LuaHoldNote(ArcHold e) : base(e) { }
        protected override int TrackOf(ArcHold note) => note.Track;
        protected override float? FloatLaneOf(ArcHold note) => note.FloatLane;
        protected override void SetLane(ArcHold note, int track, float? lane) { note.Track = track; note.FloatLane = lane; }
        protected override string ValueKey() { var v = Value; return $"{v.Timing},{v.EndTiming},{v.Track},{v.FloatLane},{S.GroupOf(live)?.GetHashCode()}"; }

        public int EndTiming
        {
            get => Value.EndTiming;
            set => Edit().EndTiming = value;
        }

        public LuaHoldNote Clone()
        {
            var copy = (ArcHold)Value.Clone();
            S.Add(copy);
            return new LuaHoldNote(copy);
        }
    }

    public class LuaArcNote : LuaNote<ArcArc>
    {
        public LuaArcNote(ArcArc e) : base(e) { }

        protected override string ValueKey()
        {
            var v = Value;
            return $"{v.Timing},{v.EndTiming},{v.XStart},{v.XEnd},{v.CurveType},{v.YStart},{v.YEnd},{v.Color},{v.Effect},{v.LineType},{S.GroupOf(live)?.GetHashCode()}";
        }

        public int EndTiming
        {
            get => Value.EndTiming;
            set => Edit().EndTiming = value;
        }

        public Vec2Lua StartPosition
        {
            get => new Vec2Lua(Value.XStart, Value.YStart);
            set { var arc = Edit(); arc.XStart = value.X; arc.YStart = value.Y; }
        }

        public Vec2Lua EndPosition
        {
            get => new Vec2Lua(Value.XEnd, Value.YEnd);
            set { var arc = Edit(); arc.XEnd = value.X; arc.YEnd = value.Y; }
        }

        public ArcLineType LineType
        {
            get => AaConvert.LineType(Value.CurveType);
            set => Edit().CurveType = AaConvert.CurveType(value);
        }

        public string LineTypeString => Value.CurveType.ToString().ToLowerInvariant();

        public int Color
        {
            get => Value.Color;
            set => Edit().Color = value;
        }

        public string SfxFileName
        {
            get => Value.Effect;
            set => Edit().Effect = string.IsNullOrEmpty(value) ? "none" : value;
        }

        public bool IsTrace
        {
            get => Value.IsVoid;
            set => Edit().LineType = value ? ChartLineType.TrueIsVoid : ChartLineType.FalseNotVoid;
        }

        public bool IsDesignant
        {
            get => Value.Designant;
            set => Edit().LineType = value ? ChartLineType.Designant : ChartLineType.FalseNotVoid;
        }

        public float Smoothness
        {
            get => Value.EffectiveSmoothness;
            set => Edit().Smoothness = value;
        }

        public bool IsScaledArcTap => Value.IsVariousSizedArctap;

        public bool RenderHead => live.RenderHead;

        public bool RenderHeightIndicator
        {
            get
            {
                var v = Value;
                return !v.IsVoid && !(Mathf.Approximately(v.YStart, v.YEnd) && !live.RenderHead);
            }
        }

        public float GetXAt(float progress) { var v = Value; return ArcAlgorithm.X(v.XStart, v.XEnd, progress, v.CurveType); }
        public float GetYAt(float progress) { var v = Value; return ArcAlgorithm.Y(v.YStart, v.YEnd, progress, v.CurveType); }
        public float GetXAtTiming(int timing) => GetXAt(Progress(timing));
        public float GetYAtTiming(int timing) => GetYAt(Progress(timing));

        private float Progress(int timing)
        {
            var v = Value;
            return (timing - v.Timing) / (float)(v.EndTiming - v.Timing);
        }

        public List<int> GetArcTapTimings() => live.ArcTaps.Select(t => S.View(t).Timing).ToList();

        public List<LuaArcTapNote> GetArcTapNotes() => live.ArcTaps.Select(t => new LuaArcTapNote(t)).ToList();

        public LuaArcTapNote AddArcTapNote(int time) => AddArcTapNote(time, 1);

        public LuaArcTapNote AddArcTapNote(int time, float width)
        {
            var tap = new ArcArcTap { Timing = time, Width = width };
            S.AddArcTap(live, tap);
            return new LuaArcTapNote(tap);
        }

        public void RemoveArcTapNote(LuaArcTapNote note) => note?.Remove();

        public LuaArcNote Clone()
        {
            var copy = (ArcArc)Value.Clone();
            S.Add(copy);
            return new LuaArcNote(copy);
        }
    }

    public class LuaArcTapNote : LuaNote<ArcArcTap>
    {
        public LuaArcTapNote(ArcArcTap e) : base(e) { }

        protected override string ValueKey() { var v = Value; return $"{v.Timing},{v.Width},{live.Arc?.GetHashCode()}"; }

        public override int GroupID
        {
            get => S.IdOf(live.Arc == null ? null : S.View(live.Arc).TimingGroup);
            set => throw new ScriptRuntimeException("ArcTap 的时间组随所属 Arc，不能单独修改。");
        }

        public LuaArcNote ParentArc => live.Arc == null ? null : new LuaArcNote(live.Arc);

        public float Width
        {
            get => Value.Width;
            set => Edit().Width = value;
        }

        public LuaArcTapNote Clone()
        {
            if (live.Arc == null) throw new ScriptRuntimeException("该 ArcTap 没有所属 Arc。");
            var copy = (ArcArcTap)Value.Clone();
            S.AddArcTap(live.Arc, copy);
            return new LuaArcTapNote(copy);
        }
    }

    public class LuaTimingEvent : LuaEventBase<ArcTiming>
    {
        public LuaTimingEvent(ArcTiming e) : base(e) { }

        public override int GroupID
        {
            get => S.IdOf(S.GroupOf(live));
            set => S.MoveTiming(live, S.Group(value));
        }

        public float Bpm
        {
            get => Value.Bpm;
            set => Edit().Bpm = value;
        }

        public float BeatsPerLine
        {
            get => Value.BeatsPerLine;
            set => Edit().BeatsPerLine = value;
        }

        public LuaTimingEvent Clone()
        {
            var copy = (ArcTiming)Value.Clone();
            S.AddTiming(copy, S.GroupOf(live));
            return new LuaTimingEvent(copy);
        }
    }

    public class LuaCameraControl : LuaEventBase<ArcCamera>
    {
        public LuaCameraControl(ArcCamera e) : base(e) { }

        public Vec3Lua Move
        {
            get { var v = Value.Move; return new Vec3Lua(v.x, v.y, v.z); }
            set => Edit().Move = value.ToVector3();
        }

        public Vec3Lua Rotate
        {
            get { var v = Value.Rotate; return new Vec3Lua(v.x, v.y, v.z); }
            set => Edit().Rotate = value.ToVector3();
        }

        public CameraEasing Easing
        {
            get => (CameraEasing)Enum.Parse(typeof(CameraEasing), Value.CameraType.ToString());
            set => Edit().CameraType = (CameraEaseType)Enum.Parse(typeof(CameraEaseType), value.ToString());
        }

        public int Duration
        {
            get => Value.Duration;
            set => Edit().Duration = value;
        }

        public LuaCameraControl Clone()
        {
            var copy = (ArcCamera)Value.Clone();
            S.Add(copy);
            return new LuaCameraControl(copy);
        }
    }

    public class LuaTimingGroup
    {
        private readonly ArcTimingGroup group;
        public LuaTimingGroup(ArcTimingGroup group) { this.group = group; }

        private static AdeScriptSession S => AdeScriptSession.Require();

        public int ID => S.IdOf(group);

        public bool NoInput
        {
            get => HasToken("noinput");
            set => SetToken("noinput", value);
        }

        public bool FadingHolds
        {
            get => HasToken("fadingholds");
            set => SetToken("fadingholds", value);
        }

        public Vec2IntLua Angle
        {
            get => new Vec2IntLua(Number("anglex"), Number("angley"));
            set
            {
                if (group == null) return;
                var tokens = Tokens().Where(t => !t.StartsWith("anglex") && !t.StartsWith("angley")).ToList();
                if (value.X != 0) tokens.Add("anglex" + value.X);
                if (value.Y != 0) tokens.Add("angley" + value.Y);
                S.SetAttributes(group, string.Join("_", tokens));
            }
        }

        public int Opacity { get => throw Unsupported(); set => throw Unsupported(); }

        private static ScriptRuntimeException Unsupported() => new ScriptRuntimeException("AffdataEdit 不支持时间组 Opacity。");

        private List<string> Tokens() => S.Attributes(group).Split('_').Where(t => t.Length > 0).ToList();
        private bool HasToken(string token) => Tokens().Contains(token);

        private int Number(string prefix)
        {
            var token = Tokens().FirstOrDefault(t => t.StartsWith(prefix));
            return token != null && int.TryParse(token.Substring(prefix.Length), out int value) ? value : 0;
        }

        private void SetToken(string token, bool enabled)
        {
            // Like Alpha, the default group has no attributes to change.
            if (group == null) return;
            var tokens = Tokens();
            tokens.Remove(token);
            if (enabled) tokens.Add(token);
            S.SetAttributes(group, string.Join("_", tokens));
        }
    }

    internal static class AaConvert
    {
        public static ArcLineType LineType(ArcCurveType curve) => (ArcLineType)Enum.Parse(typeof(ArcLineType), curve.ToString());
        public static ArcCurveType CurveType(ArcLineType line) => (ArcCurveType)Enum.Parse(typeof(ArcCurveType), line.ToString());

        public static bool TryCurveType(string text, out ArcCurveType curve)
        {
            foreach (ArcCurveType value in Enum.GetValues(typeof(ArcCurveType)))
            {
                if (string.Equals(value.ToString(), text, StringComparison.OrdinalIgnoreCase))
                {
                    curve = value;
                    return true;
                }
            }
            curve = ArcCurveType.S;
            return false;
        }

        public static object Wrap(ArcEvent e)
        {
            switch (e)
            {
                case ArcTap tap: return new LuaTapNote(tap);
                case ArcHold hold: return new LuaHoldNote(hold);
                case ArcArc arc: return new LuaArcNote(arc);
                case ArcArcTap tap: return new LuaArcTapNote(tap);
                case ArcTiming timing: return new LuaTimingEvent(timing);
                case ArcCamera camera: return new LuaCameraControl(camera);
                default: return null;
            }
        }
    }
}
