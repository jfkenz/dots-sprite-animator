using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace InvertLab.Sprites.DOTS
{
    /// <summary>A control parameter's current value, one per <see cref="SpritePartsSetBlob.Params"/> in order.</summary>
    [InternalBufferCapacity(0)]
    public struct SpritePartsParamValue : IBufferElementData
    {
        public float Value;
    }

    /// <summary>
    /// Control parameters (AnyPortrait-style): each value scrubs its clip from start (Min) to end (Max).
    /// Additive parameters add the change from their default pose on top of the playing clip, so a talking mouth
    /// works while walking; the others replace the pose of the parts their clip keys. Gameplay sets values with
    /// <see cref="Set"/>. A parameter whose clip is the one playing is skipped (it is already applied).
    /// </summary>
    public static class SpritePartsParams
    {
        public static int Find(ref SpritePartsSetBlob set, in FixedString64Bytes name)
        {
            for (int i = 0; i < set.Params.Length; i++)
            {
                if (set.Params[i].Name == name)
                    return i;
            }
            return -1;
        }

        /// <summary>Sets a parameter by name. False when the character has no parameter with that name.</summary>
        public static bool Set(EntityManager em, Entity root, string name, float value)
        {
            if (!em.HasComponent<SpritePartsSetRef>(root))
                return false;
            var blob = em.GetComponentData<SpritePartsSetRef>(root).Set;
            if (!blob.IsCreated)
                return false;
            int index = Find(ref blob.Value, new FixedString64Bytes(name ?? string.Empty));
            if (index < 0)
                return false;
            var values = EnsureValues(em, root, ref blob.Value);
            ref var p = ref blob.Value.Params[index];
            values[index] = new SpritePartsParamValue { Value = math.clamp(value, math.min(p.Min, p.Max), math.max(p.Min, p.Max)) };
            return true;
        }

        /// <summary>The value of a parameter, or its default when never set; NaN when there is no such parameter.</summary>
        public static float Get(EntityManager em, Entity root, string name)
        {
            if (!em.HasComponent<SpritePartsSetRef>(root))
                return float.NaN;
            var blob = em.GetComponentData<SpritePartsSetRef>(root).Set;
            if (!blob.IsCreated)
                return float.NaN;
            int index = Find(ref blob.Value, new FixedString64Bytes(name ?? string.Empty));
            if (index < 0)
                return float.NaN;
            if (em.HasBuffer<SpritePartsParamValue>(root))
            {
                var values = em.GetBuffer<SpritePartsParamValue>(root);
                if (index < values.Length)
                    return values[index].Value;
            }
            return blob.Value.Params[index].Default;
        }

        /// <summary>The value buffer, created and filled with defaults when missing or the wrong size.</summary>
        public static DynamicBuffer<SpritePartsParamValue> EnsureValues(EntityManager em, Entity root, ref SpritePartsSetBlob set)
        {
            if (!em.HasBuffer<SpritePartsParamValue>(root))
                em.AddBuffer<SpritePartsParamValue>(root);
            var values = em.GetBuffer<SpritePartsParamValue>(root);
            if (values.Length != set.Params.Length)
            {
                int old = values.Length;
                values.ResizeUninitialized(set.Params.Length);
                for (int i = old; i < values.Length; i++)
                    values[i] = new SpritePartsParamValue { Value = set.Params[i].Default };
            }
            return values;
        }

        public static void Apply(ref SpritePartsSetBlob set, int playingClip, NativeArray<float> values,
            NativeArray<SpritePartsSampler.Pose> local)
        {
            for (int k = 0; k < set.Params.Length; k++)
            {
                ref var p = ref set.Params[k];
                if (p.ClipIndex < 0 || p.ClipIndex >= set.Clips.Length || p.ClipIndex == playingClip)
                    continue;
                float value = values.IsCreated && k < values.Length ? values[k] : p.Default;
                if (!math.isfinite(value))
                    value = p.Default;
                bool additive = p.Additive != 0;
                if (additive && math.abs(value - p.Default) < 1e-6f)
                    continue; // no change from the default pose
                ref var clip = ref set.Clips[p.ClipIndex];
                float t = Time(p, value, clip.Duration);
                float t0 = Time(p, p.Default, clip.Duration);
                for (int i = 0; i < local.Length && i < set.Slots.Length; i++)
                {
                    if (SpritePartsSampler.TrackIndexForSlot(ref clip, i) < 0)
                        continue;
                    SpritePartsSampler.SampleSlot(ref set, p.ClipIndex, i, t, false, out var at);
                    if (!additive)
                    {
                        local[i] = at;
                        continue;
                    }
                    SpritePartsSampler.SampleSlot(ref set, p.ClipIndex, i, t0, false, out var rest);
                    local[i] = AddDelta(local[i], rest, at);
                }
            }
        }

        static float Time(in SpritePartsParamBlob p, float value, float duration)
        {
            float range = p.Max - p.Min;
            float u = math.abs(range) > 1e-6f ? math.saturate((value - p.Min) / range) : 0f;
            return u * math.max(0f, duration);
        }

        /// <summary><paramref name="pose"/> plus the change from <paramref name="from"/> to <paramref name="to"/>.</summary>
        static SpritePartsSampler.Pose AddDelta(SpritePartsSampler.Pose pose, in SpritePartsSampler.Pose from, in SpritePartsSampler.Pose to)
        {
            pose.Position += to.Position - from.Position;
            pose.Rotation += SpritePartsSampler.LerpAngleShortest(from.Rotation, to.Rotation, 1f) - from.Rotation;
            pose.Scale *= new float2(Ratio(to.Scale.x, from.Scale.x), Ratio(to.Scale.y, from.Scale.y));
            pose.Shear += to.Shear - from.Shear;
            int n = pose.Lattice.PointCount;
            if (n > 0 && n == from.Lattice.PointCount && n == to.Lattice.PointCount)
            {
                for (int v = 0; v < n; v++)
                    pose.Lattice.Points[v] += to.Lattice.Points[v] - from.Lattice.Points[v];
            }
            return pose;
        }

        static float Ratio(float a, float b) => math.abs(b) > 1e-6f ? a / b : 1f;
    }
}
