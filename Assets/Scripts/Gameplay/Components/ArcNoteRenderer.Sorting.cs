using System;
using System.Collections.Generic;
using Affdata.Core.Chart;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;

namespace Arcade.Gameplay
{
    public sealed partial class ArcNoteRenderer
    {
        private struct SortKey
        {
            public int Index, LayerValue, Order, Queue, ArcKey;
            public bool ArcFace;
            public float Distance, PrimaryDistance;
        }
        private SortKey[] sortKeys = Array.Empty<SortKey>();
        private NativeArray<SortKey> nativeSortKeys;

        private void EnsureSortCapacity(int count)
        {
            if (sortKeys.Length >= count) return;
            Array.Resize(ref sortKeys, Math.Max(256, sortKeys.Length * 2));
        }

        private void SortVisibleItems()
        {
            if (items.Count < 2) return;
            // Small charts avoid scheduling overhead. Dense charts use the same comparator in Burst.
            if (items.Count < 256)
            {
                Array.Sort(sortKeys, 0, items.Count, new SortComparer());
                return;
            }
            if (!nativeSortKeys.IsCreated || nativeSortKeys.Length < sortKeys.Length)
            {
                if (nativeSortKeys.IsCreated) nativeSortKeys.Dispose();
                nativeSortKeys = new NativeArray<SortKey>(sortKeys.Length, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
            }
            NativeArray<SortKey>.Copy(sortKeys, nativeSortKeys, items.Count);
            new SortVisibleJob { Keys = nativeSortKeys.GetSubArray(0, items.Count) }.Schedule().Complete();
            NativeArray<SortKey>.Copy(nativeSortKeys, sortKeys, items.Count);
        }

        [BurstCompile(CompileSynchronously = true)]
        private struct SortVisibleJob : IJob
        {
            public NativeArray<SortKey> Keys;
            public void Execute() => Keys.Sort(new SortComparer());
        }

        private struct SortComparer : IComparer<SortKey>
        {
            public int Compare(SortKey a, SortKey b)
            {
                if (a.LayerValue != b.LayerValue) return a.LayerValue < b.LayerValue ? -1 : 1;
                // Keep Arc faces together and preserve Play's face order.
                if (a.ArcFace != b.ArcFace) return a.ArcFace ? 1 : -1;
                if (a.ArcFace)
                {
                    int faceOrder = ArcRenderOrder.Compare(a.Order, a.ArcKey, b.Order, b.ArcKey);
                    if (faceOrder != 0) return faceOrder;
                }
                else
                {
                    int depth = b.PrimaryDistance.CompareTo(a.PrimaryDistance);
                    if (depth != 0) return depth;
                    if (a.Order != b.Order) return a.Order < b.Order ? -1 : 1;
                    if (a.Queue != b.Queue) return a.Queue < b.Queue ? -1 : 1;
                    depth = b.Distance.CompareTo(a.Distance);
                    if (depth != 0) return depth;
                }
                return a.Index < b.Index ? -1 : a.Index > b.Index ? 1 : 0;
            }
        }
    }
}
