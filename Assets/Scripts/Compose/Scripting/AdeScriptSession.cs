using System;
using System.Collections.Generic;
using System.Linq;
using Arcade.Compose.Command;
using Arcade.Compose.Editing;
using Arcade.Gameplay;
using Arcade.Gameplay.Chart;
using MoonSharp.Interpreter;

namespace Arcade.Compose.Scripting
{
    // One tool run. Like Arcade Alpha, edits are recorded and applied together when the tool
    // returns, as a single undo step. Unlike Alpha, the script reads its own pending values, and
    // a run that fails or is cancelled commits nothing.
    internal sealed class AdeScriptSession
    {
        public static AdeScriptSession Current { get; private set; }

        public static AdeScriptSession Require()
            => Current ?? throw new ScriptRuntimeException("只能在工具执行时读取或修改谱面。");

        private readonly string name;
        private readonly ArcTimingGroup initialGroup;
        // Commands are built at commit, before any of them runs, so each sees the original chart.
        private readonly List<Func<ICommand>> steps = new List<Func<ICommand>>();
        private readonly Dictionary<ArcEvent, ArcEvent> pending = new Dictionary<ArcEvent, ArcEvent>();
        private readonly HashSet<ArcEvent> created = new HashSet<ArcEvent>();
        private readonly HashSet<ArcEvent> removed = new HashSet<ArcEvent>();
        private readonly Dictionary<ArcTiming, ArcTimingGroup> timingGroups = new Dictionary<ArcTiming, ArcTimingGroup>();
        private readonly List<ArcTimingGroup> newGroups = new List<ArcTimingGroup>();
        private readonly Dictionary<ArcTimingGroup, string> attributes = new Dictionary<ArcTimingGroup, string>();

        public ArcTimingGroup CurrentGroup { get; set; }

        private AdeScriptSession(string name)
        {
            this.name = name;
            initialGroup = AdeTimingEditor.Instance.currentTimingGroup;
            CurrentGroup = initialGroup;
        }

        public static AdeScriptSession Begin(string name)
        {
            if (Current != null) throw new InvalidOperationException("Another script session is running.");
            Current = new AdeScriptSession(name);
            return Current;
        }

        public bool HasChanges => steps.Count > 0;

        public void Commit()
        {
            Current = null;
            if (steps.Count > 0)
            {
                var commands = steps.Select(step => step()).ToArray();
                AdeCommandManager.Instance.Add(new BatchCommand(commands, "外置脚本：" + name));
            }
            if (CurrentGroup != initialGroup
                && (CurrentGroup == null || ArcTimingManager.Instance.timingGroups.Contains(CurrentGroup)))
                AdeTimingEditor.Instance.SetCurrentTimingGroup(CurrentGroup);
        }

        public void Discard()
        {
            if (Current == this) Current = null;
        }

        // ---- Timing groups. Id 0 is the default group (null); new groups follow the chart's.

        private static List<ArcTimingGroup> LiveGroups => ArcTimingManager.Instance.timingGroups;

        public int GroupCount => LiveGroups.Count + newGroups.Count + 1;

        public ArcTimingGroup Group(int id)
        {
            id = Math.Max(0, Math.Min(id, GroupCount - 1));
            if (id == 0) return null;
            int index = id - 1;
            return index < LiveGroups.Count ? LiveGroups[index] : newGroups[index - LiveGroups.Count];
        }

        public int IdOf(ArcTimingGroup group)
        {
            if (group == null) return 0;
            int index = LiveGroups.IndexOf(group);
            if (index >= 0) return index + 1;
            index = newGroups.IndexOf(group);
            return index >= 0 ? LiveGroups.Count + index + 1 : group.Id;
        }

        public ArcTimingGroup CreateGroup()
        {
            var group = new ArcTimingGroup
            {
                Id = GroupCount,
                Timings = new List<ArcTiming> { new ArcTiming { Timing = 0, Bpm = ArcTimingManager.Instance.BaseBpm, BeatsPerLine = 4 } },
            };
            newGroups.Add(group);
            steps.Add(() => new AddTimingGroup(group));
            return group;
        }

        public string Attributes(ArcTimingGroup group)
            => group == null ? "" : attributes.TryGetValue(group, out var value) ? value : group.Attributes;

        public void SetAttributes(ArcTimingGroup group, string value)
        {
            if (group == null || Attributes(group) == value) return;
            if (newGroups.Contains(group))
            {
                group.ApplyAttributes(value);
                return;
            }
            if (!attributes.ContainsKey(group))
                steps.Add(() => new EditTimingGroupAttributes(group, attributes[group]));
            attributes[group] = value;
        }

        // ---- Events.

        public T View<T>(T e) where T : ArcEvent => e != null && pending.TryGetValue(e, out var value) ? (T)value : e;

        public bool IsCreated(ArcEvent e) => created.Contains(e);

        // Returns the object to change: the event itself before it is added, otherwise its pending copy.
        public T Mutable<T>(T e) where T : ArcEvent
        {
            if (created.Contains(e)) return e;
            if (removed.Contains(e)) throw new ScriptRuntimeException("不能修改已删除的事件。");
            if (pending.TryGetValue(e, out var value)) return (T)value;
            var copy = (T)e.Clone();
            if (copy is ArcArcTap tap) tap.Arc = (e as ArcArcTap)?.Arc;
            pending[e] = copy;
            steps.Add(() => EditCommand(e, pending[e]));
            return copy;
        }

        private ICommand EditCommand(ArcEvent e, ArcEvent values)
        {
            switch (e)
            {
                case ArcTiming timing:
                    return new EditTimingEvent(GroupOf(timing), timing, (ArcTiming)values.Clone());
                case ArcCamera _:
                case ArcSceneControl _:
                    return new ChartControlCommand(e, e, values);
                default:
                    return new EditArcEventCommand(e, values);
            }
        }

        public void Add(ArcEvent e)
        {
            if (!created.Add(e)) return;
            switch (e)
            {
                case ArcTap _:
                case ArcHold _:
                case ArcArc _:
                    steps.Add(() => new AddArcEventCommand(e));
                    break;
                case ArcCamera _:
                case ArcSceneControl _:
                    steps.Add(() => new ChartControlCommand(e, null, e));
                    break;
                default:
                    throw new ScriptRuntimeException("AffdataEdit 不支持添加该类型的事件。");
            }
        }

        public void AddTiming(ArcTiming timing, ArcTimingGroup group)
        {
            if (!created.Add(timing)) return;
            timingGroups[timing] = group;
            steps.Add(() => new AddTimingEvent(timingGroups[timing], timing));
        }

        public void AddArcTap(ArcArc arc, ArcArcTap tap)
        {
            if (removed.Contains(arc)) throw new ScriptRuntimeException("不能向已删除的 Arc 添加 ArcTap。");
            var view = View(arc);
            if (tap.Timing < view.Timing || tap.Timing > view.EndTiming)
                throw new ScriptRuntimeException("ArcTap 时间不在 Arc 范围内。");
            tap.Arc = arc;
            if (created.Contains(arc))
            {
                arc.ArcTaps.Add(tap);
                return;
            }
            created.Add(tap);
            steps.Add(() => new AddArcTapCommand(arc, tap));
        }

        public void Remove(ArcEvent e)
        {
            if (e == null || !removed.Add(e)) return;
            switch (e)
            {
                case ArcArcTap tap:
                    var arc = tap.Arc;
                    if (arc != null && created.Contains(arc) && !created.Contains(tap)) arc.ArcTaps.Remove(tap);
                    else if (arc != null) steps.Add(() => new RemoveArcTapCommand(arc, tap));
                    break;
                case ArcTiming timing:
                    steps.Add(() => new RemoveTimingEvent(GroupOf(timing), timing));
                    break;
                case ArcCamera _:
                case ArcSceneControl _:
                    steps.Add(() => new ChartControlCommand(e, e, null));
                    break;
                default:
                    steps.Add(() => new RemoveArcEventCommand(e));
                    break;
            }
        }

        public ArcTimingGroup GroupOf(ArcTiming timing)
        {
            if (timingGroups.TryGetValue(timing, out var group)) return group;
            if (ArcTimingManager.Instance.Timings.Contains(timing)) return null;
            return LiveGroups.Concat(newGroups).FirstOrDefault(g => g.Timings.Contains(timing));
        }

        public void MoveTiming(ArcTiming timing, ArcTimingGroup group)
        {
            if (created.Contains(timing))
            {
                timingGroups[timing] = group;
                return;
            }
            if (GroupOf(timing) == group) return;
            var copy = (ArcTiming)View(timing).Clone();
            Remove(timing);
            AddTiming(copy, group);
        }

        public ArcTimingGroup GroupOf(ArcEvent e)
            => e is ArcTiming timing ? GroupOf(timing) : (View(e) as IHasTimingGroup)?.TimingGroup;
    }
}
