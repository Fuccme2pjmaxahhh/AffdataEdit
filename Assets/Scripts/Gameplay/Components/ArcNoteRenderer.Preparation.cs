using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;

namespace Arcade.Gameplay
{
    public sealed partial class ArcNoteRenderer
    {
        // Native frame data is separate from Unity object references and picking owners.
        private struct FrameInput
        {
            public NoteInstance Data;
            public Bounds LocalBounds;
            public bool Connection;
            public Vector3 ConnectionEnd, ConnectionNormal;
        }
        private struct PreparedGeometry
        {
            public Bounds Bounds;
            public bool Visible;
        }
        private FrameInput[] frameInputs = System.Array.Empty<FrameInput>();
        private NativeArray<FrameInput> nativeInputs;
        private NativeArray<PreparedGeometry> preparedGeometry;
        private NativeArray<NoteInstance> packedInstances;
        private int visibleItemCount;
        private NativeList<int> connectionFallbacks;

        private void EnsureFrameBuffers()
        {
            if (nativeSortKeys.IsCreated && nativeSortKeys.Length >= sortKeys.Length) return;
            DisposeFrameBuffers();
            int capacity = sortKeys.Length;
            nativeSortKeys = new NativeArray<SortKey>(capacity, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
            nativeInputs = new NativeArray<FrameInput>(capacity, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
            preparedGeometry = new NativeArray<PreparedGeometry>(capacity, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
            packedInstances = new NativeArray<NoteInstance>(capacity, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
            connectionFallbacks = new NativeList<int>(capacity, Allocator.Persistent);
        }
        private void DisposeFrameBuffers()
        {
            if (nativeSortKeys.IsCreated) nativeSortKeys.Dispose();
            if (nativeInputs.IsCreated) nativeInputs.Dispose();
            if (preparedGeometry.IsCreated) preparedGeometry.Dispose();
            if (packedInstances.IsCreated) packedInstances.Dispose();
            if (connectionFallbacks.IsCreated) connectionFallbacks.Dispose();
        }

        private void PrepareConnectionFallbacks()
        {
            foreach (int index in connectionFallbacks)
            {
                var input = nativeInputs[index];
                Matrix4x4 parent = input.Data.Transform;
                Vector3 start = parent.MultiplyPoint3x4(Vector3.zero);
                Vector3 direction = parent.MultiplyPoint3x4(input.ConnectionEnd) - start;
                input.Data.Transform = ConnectionTransform(start, direction, input.ConnectionNormal, direction.magnitude);
                nativeInputs[index] = input;
                Bounds bounds = PrepareVisibleJob.TransformBounds(input.LocalBounds, input.Data.Transform);
                bool visible = GeometryUtility.TestPlanesAABB(frustum, bounds);
                preparedGeometry[index] = new PreparedGeometry { Bounds = bounds, Visible = visible };
                var key = nativeSortKeys[index];
                key.Culled = !visible;
                Vector3 offset = bounds.center - cameraPosition;
                key.Distance = orthographic ? Vector3.Dot(offset, cameraForward) : offset.sqrMagnitude;
                key.PrimaryDistance = key.DistanceOrdered ? key.Distance : 0;
                nativeSortKeys[index] = key;
            }
        }

        [BurstCompile(CompileSynchronously = true)]
        private struct PrepareVisibleJob : IJobParallelFor
        {
            public NativeArray<FrameInput> Inputs;
            public NativeList<int>.ParallelWriter Fallbacks;
            public NativeArray<SortKey> Keys;
            [WriteOnly] public NativeArray<PreparedGeometry> Geometry;
            public Plane Plane0, Plane1, Plane2, Plane3, Plane4, Plane5;
            public Vector3 CameraPosition, CameraForward;
            public bool Orthographic;
            public void Execute(int index)
            {
                var input = Inputs[index];
                if (input.Connection)
                {
                    Matrix4x4 parent = input.Data.Transform;
                    Vector3 start = parent.MultiplyPoint3x4(Vector3.zero);
                    Vector3 direction = parent.MultiplyPoint3x4(input.ConnectionEnd) - start;
                    float length = direction.magnitude;
                    if (length <= .000001f)
                    {
                        var hidden = Keys[index]; hidden.Culled = true; Keys[index] = hidden;
                        Geometry[index] = default;
                        return;
                    }
                    Vector3 forward = direction / length;
                    Vector3 right = Vector3.Cross(input.ConnectionNormal, forward);
                    if (right.sqrMagnitude <= .000000000001f)
                    {
                        Fallbacks.AddNoResize(index);
                        return;
                    }
                    right.Normalize();
                    Vector3 up = Vector3.Cross(forward, right);
                    input.Data.Transform = new Matrix4x4(new Vector4(right.x, right.y, right.z, 0),
                        new Vector4(up.x, up.y, up.z, 0), new Vector4(direction.x, direction.y, direction.z, 0),
                        new Vector4(start.x, start.y, start.z, 1));
                    Inputs[index] = input;
                }
                Bounds bounds = TransformBounds(input.LocalBounds, input.Data.Transform);
                Vector3 center = bounds.center, extents = bounds.extents;
                bool visible = Inside(Plane0, center, extents) && Inside(Plane1, center, extents)
                    && Inside(Plane2, center, extents) && Inside(Plane3, center, extents)
                    && Inside(Plane4, center, extents) && Inside(Plane5, center, extents);
                Geometry[index] = new PreparedGeometry { Bounds = bounds, Visible = visible };
                var key = Keys[index];
                key.Culled = !visible;
                Vector3 offset = center - CameraPosition;
                key.Distance = Orthographic ? Vector3.Dot(offset, CameraForward) : offset.sqrMagnitude;
                key.PrimaryDistance = key.DistanceOrdered ? key.Distance : 0;
                Keys[index] = key;
            }
            internal static Bounds TransformBounds(Bounds bounds, Matrix4x4 matrix)
            {
                Vector3 x = matrix.MultiplyVector(new Vector3(bounds.extents.x,0,0));
                Vector3 y = matrix.MultiplyVector(new Vector3(0,bounds.extents.y,0));
                Vector3 z = matrix.MultiplyVector(new Vector3(0,0,bounds.extents.z));
                return new Bounds(matrix.MultiplyPoint3x4(bounds.center), 2 * new Vector3(
                    Mathf.Abs(x.x)+Mathf.Abs(y.x)+Mathf.Abs(z.x), Mathf.Abs(x.y)+Mathf.Abs(y.y)+Mathf.Abs(z.y), Mathf.Abs(x.z)+Mathf.Abs(y.z)+Mathf.Abs(z.z)));
            }
            private static bool Inside(Plane plane, Vector3 center, Vector3 extents)
            {
                Vector3 normal = plane.normal;
                float radius = Mathf.Abs(normal.x) * extents.x + Mathf.Abs(normal.y) * extents.y + Mathf.Abs(normal.z) * extents.z;
                return Vector3.Dot(normal, center) + plane.distance + radius >= 0;
            }
        }

        [BurstCompile(CompileSynchronously = true)]
        private struct PackVisibleJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<FrameInput> Inputs;
            [ReadOnly] public NativeArray<SortKey> Keys;
            [WriteOnly] public NativeArray<NoteInstance> Output;
            public void Execute(int index) => Output[index] = Inputs[Keys[index].Index].Data;
        }
    }
}
