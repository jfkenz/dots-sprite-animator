using Unity.Collections;
using Unity.Mathematics;

namespace InvertLab.Sprites.DOTS
{
    /// <summary>
    /// Compose parent-local Parts poses into character-root matrices.
    /// Shared by editor preview/onion and runtime hierarchy checks.
    /// </summary>
    public static class SpritePartsHierarchy
    {
        public static void ComposeLocalToRoot(
            ref SpritePartsSetBlob set,
            NativeArray<SpritePartsSampler.Pose> localPoses,
            NativeArray<float4x4> localToRoot)
        {
            int n = set.Slots.Length;
            var done = new NativeArray<bool>(n, Allocator.Temp);
            try
            {
                for (int i = 0; i < n; i++)
                    Ensure(ref set, localPoses, localToRoot, done, i);
            }
            finally
            {
                done.Dispose();
            }
        }

        static void Ensure(
            ref SpritePartsSetBlob set,
            NativeArray<SpritePartsSampler.Pose> localPoses,
            NativeArray<float4x4> localToRoot,
            NativeArray<bool> done,
            int index)
        {
            if (index < 0 || index >= set.Slots.Length || done[index])
                return;
            var pose = localPoses[index];
            float4x4 local = LocalMatrix(pose.Position, pose.Rotation, pose.Scale, pose.Shear);
            int parent = set.Slots[index].ParentSlotIndex;
            if (parent >= 0 && parent < set.Slots.Length)
            {
                Ensure(ref set, localPoses, localToRoot, done, parent);
                localToRoot[index] = math.mul(localToRoot[parent], local);
            }
            else
            {
                localToRoot[index] = local;
            }
            done[index] = true;
        }

        /// <summary>
        /// Spine's bone matrix with shear: the X axis turns by rotation + shear.x, the Y axis by rotation + 90 + shear.y,
        /// each scaled. No shear = <see cref="LocalMatrix(float2, float, float2)"/>.
        /// </summary>
        public static float4x4 LocalMatrix(float2 position, float rotationDeg, float2 scale, float2 shearDeg)
        {
            if (math.all(shearDeg == float2.zero))
                return LocalMatrix(position, rotationDeg, scale);
            float sx = scale.x == 0f ? 1f : scale.x;
            float sy = scale.y == 0f ? 1f : scale.y;
            float ax = math.radians(rotationDeg + shearDeg.x);
            float ay = math.radians(rotationDeg + 90f + shearDeg.y);
            return new float4x4(
                new float4(math.cos(ax) * sx, math.sin(ax) * sx, 0f, 0f),
                new float4(math.cos(ay) * sy, math.sin(ay) * sy, 0f, 0f),
                new float4(0f, 0f, 1f, 0f),
                new float4(position.x, position.y, 0f, 1f));
        }

        /// <summary>Shear then scale, without rotation or position (a part's PostTransformMatrix).</summary>
        public static float4x4 ShearScaleMatrix(float2 scale, float2 shearDeg)
            => LocalMatrix(float2.zero, 0f, scale, shearDeg);

        public static float4x4 LocalMatrix(float2 position, float rotationDeg, float2 scale)
        {
            float rad = math.radians(rotationDeg);
            float c = math.cos(rad);
            float s = math.sin(rad);
            float sx = scale.x == 0f ? 1f : scale.x;
            float sy = scale.y == 0f ? 1f : scale.y;
            return new float4x4(
                new float4(c * sx, s * sx, 0f, 0f),
                new float4(-s * sy, c * sy, 0f, 0f),
                new float4(0f, 0f, 1f, 0f),
                new float4(position.x, position.y, 0f, 1f));
        }

        public static float2 TransformPoint(float4x4 m, float2 p)
            => math.mul(m, new float4(p.x, p.y, 0f, 1f)).xy;

        public static float2 InverseTransformPoint(float4x4 m, float2 p)
        {
            float4 local = math.mul(math.inverse(m), new float4(p.x, p.y, 0f, 1f));
            return local.xy;
        }

        public static float ExtractRotationDeg(float4x4 m)
        {
            float2 x = math.normalizesafe(m.c0.xy, new float2(1f, 0f));
            return math.degrees(math.atan2(x.y, x.x));
        }
    }
}
