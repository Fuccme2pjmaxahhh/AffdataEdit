using System;
using System.Collections.Generic;
using System.Linq;

namespace Arcade.Gameplay.Chart
{
    // Only revisit passed point notes after a seek or a chart edit.
    internal sealed class ArcJudgementCursor<T> where T : ArcEvent
    {
        private T[] ordered = Array.Empty<T>();
        private IReadOnlyList<T> source;
        private long version = -1;
        private int next;
        public void Reset() => next = 0;
        public void Clear() { ordered = Array.Empty<T>(); source = null; version = -1; next = 0; }
        public void Advance(IReadOnlyList<T> notes, int timing, List<T> crossed)
        {
            if (!ReferenceEquals(source, notes) || version != ArcEvent.RenderIndexVersion || ordered.Length != notes.Count)
            {
                ordered = notes.OrderBy(note => note.Timing).ToArray();
                source = notes; version = ArcEvent.RenderIndexVersion; next = 0;
            }
            crossed.Clear();
            while (next < ordered.Length && ordered[next].Timing < timing) crossed.Add(ordered[next++]);
        }
    }
}
