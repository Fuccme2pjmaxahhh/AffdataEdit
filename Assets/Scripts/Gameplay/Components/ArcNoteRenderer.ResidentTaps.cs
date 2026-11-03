using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Affdata.Core.Geometry;
using Arcade.Gameplay.Chart;
using Unity.Collections;
using UnityEngine;

namespace Arcade.Gameplay
{
    public sealed partial class ArcNoteRenderer
    {
        [StructLayout(LayoutKind.Sequential)]
        internal struct TapModel
        {
            public int Timing;
            public float X, Selected, Padding;
        }
        [StructLayout(LayoutKind.Sequential)]
        private struct TapFrame
        {
            public uint Id;
            public float Position, Length, Padding;
        }
        internal struct TapProjection
        {
            public Matrix4x4 Layer, Basis;
            public int Timing;
            public float Bpm, BaseBpm, Velocity, SpriteHeight;
            public bool EditorCamera;
            public Matrix4x4 Evaluate(TapModel model, out float position, out float alpha, out float length)
            {
                bool reversed = Timing > model.Timing;
                int start = reversed ? model.Timing : Timing, end = reversed ? Timing : model.Timing;
                position = 0;
                position += unchecked(end - start) * Bpm / BaseBpm * Velocity;
                if (reversed) position = -position;
                float pos = position / 1000f;
                length = EditorCamera ? 2 : NoteMeshDefinitions.TapLength(pos, Bpm / BaseBpm * Velocity) / SpriteHeight;
                Matrix4x4 matrix = Basis;
                matrix.m01 *= length; matrix.m11 *= length; matrix.m21 *= length; matrix.m31 *= length;
                Vector3 world = Layer.MultiplyPoint3x4(new Vector3(model.X, pos, 0));
                matrix.m03 = world.x; matrix.m13 = world.y; matrix.m23 = world.z; matrix.m33 = 1;
                alpha = pos < 90 ? 1 : (100 - pos) / 10f;
                return matrix;
            }
        }
        private List<ArcTap> residentSource;
        private TapModel[] tapModels = Array.Empty<TapModel>();
        private NativeArray<TapModel> nativeTapModels;
        private NativeArray<TapFrame> packedTapFrames;
        private GraphicsBuffer tapModelBuffer, tapFrameBuffer;
        private TapProjection tapProjection;
        private long tapModelVersion = -1;
        private int tapDirtyMin = int.MaxValue, tapDirtyMax = -1, residentFrame = -1;
        private static readonly int TapModelsId = Shader.PropertyToID("_TapModels"), TapFramesId = Shader.PropertyToID("_TapFrames"),
            TapLayerId = Shader.PropertyToID("_TapLayer"), TapBasisId = Shader.PropertyToID("_TapBasis");

        internal bool PrepareResidentTaps(List<ArcTap> taps, TapProjection projection)
        {
            if (!enabled || !ArcTapNoteManager.Instance.DefaultSprite) return false;
            if (!ReferenceEquals(residentSource, taps) || tapModelVersion != ArcEvent.RenderIndexVersion || tapModels.Length != taps.Count)
            {
                tapModelBuffer?.Dispose();
                if (nativeTapModels.IsCreated) nativeTapModels.Dispose();
                tapModels = new TapModel[taps.Count];
                nativeTapModels = new NativeArray<TapModel>(Math.Max(1, taps.Count), Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
                for (int i = 0; i < taps.Count; i++)
                {
                    taps[i].ResidentIndex = i;
                    tapModels[i] = new TapModel { Timing = taps[i].Timing, X = taps[i].WorldX, Selected = taps[i].Selected ? 1 : 0 };
                }
                tapModelBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, Math.Max(1, taps.Count), Marshal.SizeOf<TapModel>());
                residentSource = taps; tapModelVersion = ArcEvent.RenderIndexVersion;
                tapDirtyMin = 0; tapDirtyMax = taps.Count - 1;
            }
            FlushTapChanges();
            tapProjection = projection; residentFrame = Time.frameCount;
            return true;
        }
        private void FlushTapChanges()
        {
            if (tapDirtyMax >= tapDirtyMin)
            {
                int count = tapDirtyMax - tapDirtyMin + 1;
                tapModelBuffer.SetData(tapModels, tapDirtyMin, tapDirtyMin, count);
                NativeArray<TapModel>.Copy(tapModels, tapDirtyMin, nativeTapModels, tapDirtyMin, count);
                tapDirtyMin = int.MaxValue; tapDirtyMax = -1;
            }
        }
        internal static void UpdateResidentTap(ArcTap tap)
        {
            var owner = Instance;
            if (!owner || owner.residentSource == null) return;
            int index = tap.ResidentIndex;
            if (index < 0 || index >= owner.tapModels.Length || index >= owner.residentSource.Count
                || !ReferenceEquals(owner.residentSource[index], tap)) return;
            owner.tapModels[index] = new TapModel { Timing = tap.Timing, X = tap.WorldX, Selected = tap.Selected ? 1 : 0 };
            owner.tapDirtyMin = Math.Min(owner.tapDirtyMin, index);
            owner.tapDirtyMax = Math.Max(owner.tapDirtyMax, index);
        }
        private void QueueResidentTap(ArcTap tap, Item template, FrameInput input, SortKey key)
        {
            int index = items.Count;
            EnsureSortCapacity(index + 1);
            input.ResidentTap = true; input.TapIndex = tap.ResidentIndex;
            input.Data.Options.w = tap.Selected ? 1 : 0;
            frameInputs[index] = input;
            key.Index = index;
            sortKeys[index] = key;
            template.Note = tap; template.ResidentTap = true;
            items.Add(template);
        }
        private void BindResidentTaps(MaterialPropertyBlock properties)
        {
            properties.SetBuffer(TapModelsId, tapModelBuffer); properties.SetBuffer(TapFramesId, tapFrameBuffer);
            properties.SetMatrix(TapLayerId, tapProjection.Layer); properties.SetMatrix(TapBasisId, tapProjection.Basis);
        }
        private void DisposeResidentTaps()
        {
            tapModelBuffer?.Dispose(); tapFrameBuffer?.Dispose();
            if (nativeTapModels.IsCreated) nativeTapModels.Dispose();
            residentSource = null; tapModelBuffer = tapFrameBuffer = null;
        }
    }
}
