using System;

namespace Arcade.Gameplay.Chart
{
    // A dense run keeps the original start + index * interval rule, including
    // duplicate millisecond timestamps, without allocating one integer per tick.
    internal sealed class ArcDenseJudgements
    {
        private readonly int start;
        private readonly double interval, first;
        private double end;
        internal double Count => end - first;
        internal int First => TimeAt(first);

        internal ArcDenseJudgements(int start, int endTiming, double interval, int firstIndex, double total)
        {
            this.start = start;
            this.interval = interval;
            first = firstIndex;
            end = total;
            end = UpperBound(endTiming - 1);
        }

        private int TimeAt(double index)
        {
            double value = start + index * interval;
            // Protect the integer time boundary from floating-point rounding.
            return value >= int.MaxValue ? int.MaxValue : value <= int.MinValue ? int.MinValue : (int)value;
        }

        private double UpperBound(int timing)
        {
            // Estimate the boundary directly, then verify against the original
            // truncating time formula. Most queries need only one correction;
            // binary search handles cancellation and indices above 2^53 safely.
            double boundary = timing < 0 ? timing : (double)timing + 1;
            double guess = Math.Max(first, Math.Min(end, Math.Floor((boundary - start) / interval) + 1));
            if (guess < 9007199254740992d)
            {
                for (int i = 0; i < 4; i++)
                {
                    if (guess > first && TimeAt(guess - 1) > timing) guess--;
                    else if (guess < end && TimeAt(guess) <= timing) guess++;
                    else return guess;
                }
            }
            double low = first, high = end;
            while (low < high)
            {
                double middle = Math.Floor(low + (high - low) * .5);
                // Above 2^53, individual tick indices exceed double's integer
                // precision. Stop at adjacent representable indices, not a stalled loop.
                if (middle <= low || middle >= high) return TimeAt(low) <= timing ? high : low;
                if (TimeAt(middle) <= timing) low = middle + 1;
                else high = middle;
            }
            return low;
        }

        internal double CountAtOrBefore(int timing) => UpperBound(timing) - first;
    }
}
