using Unity.Collections;
using Unity.Mathematics;

namespace InvertLab.Sprites.DOTS
{
    /// <summary>
    /// Pure Parts pose sampler. Immutable def + clip/time → output poses.
    /// Shared by editor preview/onion and runtime. No per-frame managed alloc in hot path.
    /// </summary>
    public static class SpritePartsSampler
    {
        public struct Pose
        {
            public float2 Position;
            public float Rotation;
            public float2 Scale;
        }

        /// <summary>
        /// Sample every slot into <paramref name="poses"/> (length >= slot count).
        /// Missing/empty tracks use rest. Keys are absolute parent-local poses.
        /// </summary>
        public static void SampleAll(
            ref SpritePartsSetBlob set, int clipIndex, float timeSeconds,
            NativeArray<Pose> poses)
        {
            int slotCount = set.Slots.Length;
            for (int i = 0; i < slotCount; i++)
            {
                SampleSlot(ref set, clipIndex, i, timeSeconds, out var pose);
                if (i < poses.Length)
                    poses[i] = pose;
            }
        }

        public static void SampleSlot(
            ref SpritePartsSetBlob set, int clipIndex, int slotIndex, float timeSeconds,
            out Pose pose)
        {
            ref var slot = ref set.Slots[slotIndex];
            pose = new Pose
            {
                Position = slot.RestPosition,
                Rotation = slot.RestRotation,
                Scale = slot.RestScale,
            };

            if (clipIndex < 0 || clipIndex >= set.Clips.Length)
                return;

            ref var clip = ref set.Clips[clipIndex];
            float time = WrapTime(timeSeconds, clip.Duration, clip.WrapMode);
            int trackIndex = TrackIndexForSlot(ref clip, slotIndex);
            if (trackIndex < 0)
                return;

            ref var track = ref clip.Tracks[trackIndex];
            if (track.Keys.Length == 0)
                return;
            if (track.Keys.Length == 1)
            {
                ref var only = ref track.Keys[0];
                pose.Position = only.Position;
                pose.Rotation = only.Rotation;
                pose.Scale = only.Scale;
                return;
            }

            // Before first / after last: hold nearest key.
            if (time <= track.Keys[0].Time)
            {
                ref var first = ref track.Keys[0];
                pose.Position = first.Position;
                pose.Rotation = first.Rotation;
                pose.Scale = first.Scale;
                return;
            }
            int last = track.Keys.Length - 1;
            if (time >= track.Keys[last].Time)
            {
                ref var end = ref track.Keys[last];
                pose.Position = end.Position;
                pose.Rotation = end.Rotation;
                pose.Scale = end.Scale;
                return;
            }

            int i1 = 1;
            while (i1 < track.Keys.Length && track.Keys[i1].Time < time)
                i1++;
            int i0 = i1 - 1;
            ref var a = ref track.Keys[i0];
            ref var b = ref track.Keys[i1];
            float span = b.Time - a.Time;
            float u = span > 1e-8f ? (time - a.Time) / span : 0f;
            u = SpriteEase.Evaluate((SpriteEaseMode)a.EaseMode, u);
            pose.Position = math.lerp(a.Position, b.Position, u);
            pose.Scale = math.lerp(a.Scale, b.Scale, u);
            pose.Rotation = LerpAngleShortest(a.Rotation, b.Rotation, u);
        }

        /// <summary>
        /// Last non-empty keyed appearance at/before time.
        /// Empty AppearanceIndex on a key holds the previous keyed value.
        /// Loop: if none at/before wrapped time, carry the last keyed appearance from the clip
        /// (previous loop iteration). Once: no wrap-around carry — returns -1 (skin/default).
        /// Returns appearance index into set.Appearances, or -1 when no keyed appearance is active.
        /// </summary>
        public static int SampleAppearanceIndex(
            ref SpritePartsSetBlob set, int clipIndex, int slotIndex, float timeSeconds)
        {
            if (clipIndex < 0 || clipIndex >= set.Clips.Length)
                return -1;
            if (slotIndex < 0 || slotIndex >= set.Slots.Length)
                return -1;

            ref var clip = ref set.Clips[clipIndex];
            float time = WrapTime(timeSeconds, clip.Duration, clip.WrapMode);
            int trackIndex = TrackIndexForSlot(ref clip, slotIndex);
            if (trackIndex < 0)
                return -1;

            ref var track = ref clip.Tracks[trackIndex];
            if (track.Keys.Length == 0)
                return -1;

            int best = -1;
            int lastInClip = -1;
            for (int i = 0; i < track.Keys.Length; i++)
            {
                int app = track.Keys[i].AppearanceIndex;
                if (app < 0)
                    continue;
                lastInClip = app;
                if (track.Keys[i].Time <= time + 1e-6f)
                    best = app;
            }

            if (best >= 0)
                return best;

            // Loop carry from previous iteration when wrapped time sits before the first sprite key.
            if (clip.WrapMode != (byte)SpritePartsWrap.Once && lastInClip >= 0)
                return lastInClip;
            return -1;
        }
        public static float WrapTime(float time, float duration, byte wrapMode)
        {
            if (!(duration > 0f) || !math.isfinite(duration))
                return 0f;
            if (!math.isfinite(time))
                return 0f;

            if (wrapMode == (byte)SpritePartsWrap.Once || wrapMode == SpriteAnimWrap.Once)
                return math.clamp(time, 0f, duration);

            // Positive modulo, including negative time.
            float t = time % duration;
            if (t < 0f)
                t += duration;
            // Loop at exact duration wraps to start.
            if (t >= duration)
                t = 0f;
            return t;
        }

        public static float LerpAngleShortest(float fromDeg, float toDeg, float t)
        {
            float delta = toDeg - fromDeg;
            delta = math.fmod(delta + 180f, 360f);
            if (delta < 0f)
                delta += 360f;
            delta -= 180f;
            // +180 tie: fmod path yields -180 (deterministic).
            return fromDeg + delta * t;
        }

        static int TrackIndexForSlot(ref SpritePartsClipBlob clip, int slotIndex)
        {
            if (slotIndex < 0)
                return -1;
            if (clip.SlotTrackIndices.Length > 0)
            {
                if (slotIndex >= clip.SlotTrackIndices.Length)
                    return -1;
                return clip.SlotTrackIndices[slotIndex];
            }
            for (int i = 0; i < clip.Tracks.Length; i++)
            {
                if (clip.Tracks[i].SlotIndex == slotIndex)
                    return i;
            }
            return -1;
        }
    }
}
