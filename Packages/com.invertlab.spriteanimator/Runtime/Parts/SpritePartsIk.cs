using Unity.Collections;
using Unity.Mathematics;

namespace InvertLab.Sprites.DOTS
{
    /// <summary>
    /// Runtime IK constraints, solved in character-root space after the clip and overrides:
    /// Chain 1 aims the effector's parent at the target; Chain 2 is the classic two-bone solve
    /// (law of cosines) with a bend direction. Rotations are written back as local deltas.
    /// </summary>
    public static class SpritePartsIk
    {
        public static void Apply(ref SpritePartsSetBlob set, NativeArray<SpritePartsSampler.Pose> local, NativeArray<float4x4> localToRoot)
            => Apply(ref set, local, localToRoot, default, default);

        /// <param name="mix">Per constraint (keyed values); not created = each constraint's own Mix.</param>
        /// <param name="bend">Per constraint, +1 / -1; not created = each constraint's own bend.</param>
        public static void Apply(ref SpritePartsSetBlob set, NativeArray<SpritePartsSampler.Pose> local, NativeArray<float4x4> localToRoot,
            NativeArray<float> mix, NativeArray<float> bend)
        {
            for (int c = 0; c < set.IkConstraints.Length; c++)
            {
                ref var ik = ref set.IkConstraints[c];
                float m = mix.IsCreated && c < mix.Length ? mix[c] : ik.Mix;
                float b = bend.IsCreated && c < bend.Length ? bend[c] : ik.BendSign;
                if (m <= 0f || !Valid(ik.Effector, local) || !Valid(ik.Lower, local) || !Valid(ik.Target, local))
                    continue;
                float2 target = localToRoot[ik.Target].c3.xy;
                if (ik.Upper >= 0 && Valid(ik.Upper, local))
                    SolveTwo(ref set, ref ik, target, m, b, local, localToRoot);
                else
                    SolveOne(ref set, ik.Lower, ik.Effector, target, m, local, localToRoot);
                SpritePartsHierarchy.ComposeLocalToRoot(ref set, local, localToRoot);
            }
        }

        static bool Valid(int i, NativeArray<SpritePartsSampler.Pose> local) => i >= 0 && i < local.Length;

        /// <summary>Turns <paramref name="joint"/> so the effector points at the target.</summary>
        static void SolveOne(ref SpritePartsSetBlob set, int joint, int effector, float2 target, float mix,
            NativeArray<SpritePartsSampler.Pose> local, NativeArray<float4x4> localToRoot)
        {
            float2 j = localToRoot[joint].c3.xy;
            float2 e = localToRoot[effector].c3.xy;
            float delta = SignedAngle(e - j, target - j);
            Turn(ref set, joint, delta * mix, local, localToRoot);
        }

        static void SolveTwo(ref SpritePartsSetBlob set, ref SpritePartsIkBlob ik, float2 target, float mix, float bendSign,
            NativeArray<SpritePartsSampler.Pose> local, NativeArray<float4x4> localToRoot)
        {
            float2 a = localToRoot[ik.Upper].c3.xy;
            float2 b = localToRoot[ik.Lower].c3.xy;
            float2 e = localToRoot[ik.Effector].c3.xy;
            float l1 = math.distance(a, b);
            float l2 = math.distance(b, e);
            if (l1 < 1e-5f || l2 < 1e-5f)
            {
                SolveOne(ref set, ik.Lower, ik.Effector, target, mix, local, localToRoot);
                return;
            }
            float2 toTarget = target - a;
            float d = math.clamp(math.length(toTarget), math.abs(l1 - l2) + 1e-4f, l1 + l2 - 1e-4f);
            // Angle at the upper joint between the reach line and the upper bone (law of cosines).
            float cosA = math.clamp((l1 * l1 + d * d - l2 * l2) / (2f * l1 * d), -1f, 1f);
            float reach = math.atan2(toTarget.y, toTarget.x);
            float upperWanted = reach + bendSign * math.acos(cosA);
            float upperNow = math.atan2(b.y - a.y, b.x - a.x);
            Turn(ref set, ik.Upper, WrapDeg(math.degrees(upperWanted - upperNow)) * mix, local, localToRoot);
            SpritePartsHierarchy.ComposeLocalToRoot(ref set, local, localToRoot);
            // Then aim the lower bone's effector at the target.
            SolveOne(ref set, ik.Lower, ik.Effector, target, mix, local, localToRoot);
        }

        /// <summary>A turn in root space as a local rotation change (a mirrored parent flips its sign).</summary>
        internal static void Turn(ref SpritePartsSetBlob set, int joint, float degrees, NativeArray<SpritePartsSampler.Pose> local,
            NativeArray<float4x4> localToRoot)
        {
            if (math.abs(degrees) < 1e-6f)
                return;
            int parent = set.Slots[joint].ParentSlotIndex;
            float sign = 1f;
            if (parent >= 0 && parent < localToRoot.Length)
            {
                var m = localToRoot[parent];
                if (m.c0.x * m.c1.y - m.c0.y * m.c1.x < 0f)
                    sign = -1f;
            }
            var pose = local[joint];
            pose.Rotation += degrees * sign;
            local[joint] = pose;
        }

        /// <summary>Degrees from <paramref name="from"/> to <paramref name="to"/>, counter-clockwise positive.</summary>
        public static float SignedAngle(float2 from, float2 to)
        {
            if (math.lengthsq(from) < 1e-12f || math.lengthsq(to) < 1e-12f)
                return 0f;
            return math.degrees(math.atan2(from.x * to.y - from.y * to.x, math.dot(from, to)));
        }

        static float WrapDeg(float d)
        {
            d = math.fmod(d + 180f, 360f);
            if (d < 0f)
                d += 360f;
            return d - 180f;
        }
    }
}
