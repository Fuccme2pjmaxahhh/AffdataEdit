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
            public bool ArcFace, Culled, DistanceOrdered;
            public float Distance, PrimaryDistance;
        }
        private SortKey[] sortKeys = Array.Empty<SortKey>();
        private NativeArray<SortKey> nativeSortKeys;

        private void EnsureSortCapacity(int count)
        {
            if (sortKeys.Length >= count) return;
            int capacity = Math.Max(256, sortKeys.Length * 2);
            Array.Resize(ref sortKeys, capacity);
            Array.Resize(ref frameInputs, capacity);
        }

        private void SortVisibleItems()
        {
            EnsureFrameBuffers();
            NativeArray<FrameInput>.Copy(frameInputs, nativeInputs, items.Count);
            NativeArray<SortKey>.Copy(sortKeys, nativeSortKeys, items.Count);
            connectionFallbacks.Clear();
            var preparation = new PrepareVisibleJob {
                Inputs = nativeInputs, Keys = nativeSortKeys, Geometry = preparedGeometry, Fallbacks = connectionFallbacks.AsParallelWriter(),
                Plane0 = frustum[0], Plane1 = frustum[1], Plane2 = frustum[2], Plane3 = frustum[3], Plane4 = frustum[4], Plane5 = frustum[5],
                CameraPosition = cameraPosition, CameraForward = cameraForward, Orthographic = orthographic
            }.Schedule(items.Count, 64);
            preparation.Complete();
            PrepareConnectionFallbacks();
            new SortVisibleJob { Keys = nativeSortKeys.GetSubArray(0, items.Count) }.Schedule().Complete();
            NativeArray<SortKey>.Copy(nativeSortKeys, sortKeys, items.Count);
            visibleItemCount = 0;
            while (visibleItemCount < items.Count && !sortKeys[visibleItemCount].Culled) visibleItemCount++;
            if (visibleItemCount > 0)
                new PackVisibleJob { Inputs = nativeInputs, Keys = nativeSortKeys, Output = packedInstances }
                    .Schedule(visibleItemCount, 64).Complete();
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
                if (a.Culled != b.Culled) return a.Culled ? 1 : -1;
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
