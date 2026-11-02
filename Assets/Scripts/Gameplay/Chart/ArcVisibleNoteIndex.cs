using System;
using System.Collections.Generic;
using System.Linq;

namespace Arcade.Gameplay.Chart
{
    // Conservative time-interval lookup. Existing position/visibility checks remain authoritative.
    internal sealed class ArcVisibleNoteIndex<T> where T : ArcEvent, IHasTimingGroup
    {
        private struct Entry
        {
            public T Note;
            public int Order, End;
        }
        private sealed class Group
        {
            public ArcTimingGroup TimingGroup;
            public Entry[] Entries;
            public int[] MaxEnd;
        }
        private readonly Func<T, int> endTiming;
        private readonly List<Group> groups = new List<Group>();
        private readonly List<Entry> matches = new List<Entry>();
        private long version = -1;
        private int count = -1;
        private IReadOnlyList<T> source;

        public ArcVisibleNoteIndex(Func<T, int> endTiming) => this.endTiming = endTiming;

        public void Clear()
        {
            groups.Clear(); matches.Clear(); version = -1; count = -1; source = null;
        }

        public void Query(IReadOnlyList<T> notes, List<T> result)
        {
            if (!ReferenceEquals(source, notes) || version != ArcEvent.RenderIndexVersion || count != notes.Count) Rebuild(notes);
            matches.Clear(); result.Clear();
            foreach (var group in groups)
            {
                ArcTimingManager.Instance.GetRenderRange(group.TimingGroup, out float earliest, out float latest);
                if (earliest > latest || float.IsNaN(earliest) || float.IsNaN(latest)) continue;
                Find(group, 1, 0, group.Entries.Length, earliest, latest);
            }
            // Keep chart order for transparent rendering and equal-depth picking.
            bool inOrder = true;
            for (int i = 1; i < matches.Count; i++)
                if (matches[i - 1].Order > matches[i].Order) { inOrder = false; break; }
            if (!inOrder) matches.Sort((a, b) => a.Order.CompareTo(b.Order));
            foreach (var match in matches) result.Add(match.Note);
        }

        private void Rebuild(IReadOnlyList<T> notes)
        {
            groups.Clear();
            var entries = new Entry[notes.Count];
            for (int i = 0; i < notes.Count; i++)
                entries[i] = new Entry { Note = notes[i], Order = i, End = Math.Max(notes[i].Timing, endTiming(notes[i])) };
            foreach (var bucket in entries.GroupBy(e => e.Note.TimingGroup))
            {
                var sorted = bucket.OrderBy(e => e.Note.Timing).ThenBy(e => e.Order).ToArray();
                var group = new Group { TimingGroup = bucket.Key, Entries = sorted, MaxEnd = new int[sorted.Length * 4] };
                Build(group, 1, 0, sorted.Length);
                groups.Add(group);
            }
            source = notes; count = notes.Count; version = ArcEvent.RenderIndexVersion;
        }

        private static int Build(Group group, int node, int begin, int end)
        {
            if (end - begin == 1) return group.MaxEnd[node] = group.Entries[begin].End;
            int middle = begin + (end - begin) / 2;
            return group.MaxEnd[node] = Math.Max(Build(group, node * 2, begin, middle), Build(group, node * 2 + 1, middle, end));
        }

        private void Find(Group group, int node, int begin, int end, float earliest, float latest)
        {
            if (group.MaxEnd[node] < earliest || group.Entries[begin].Note.Timing > latest) return;
            if (end - begin == 1) { matches.Add(group.Entries[begin]); return; }
            int middle = begin + (end - begin) / 2;
            Find(group, node * 2, begin, middle, earliest, latest);
            Find(group, node * 2 + 1, middle, end, earliest, latest);
        }
    }
}
