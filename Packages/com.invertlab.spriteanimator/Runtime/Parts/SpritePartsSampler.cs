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
            public SpritePartsLattice Lattice;
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
            => SampleSlot(ref set, clipIndex, slotIndex, timeSeconds, true, out pose);

        /// <param name="wrap">False: the time is clamped to the clip instead of wrapped (control parameters).</param>
        internal static void SampleSlot(
            ref SpritePartsSetBlob set, int clipIndex, int slotIndex, float timeSeconds, bool wrap,
            out Pose pose)
            => SampleSlot(ref set, clipIndex, slotIndex, timeSeconds, wrap, false, out pose);

        /// <param name="stepped">True: hold each key's pose until the next key (pose-to-pose blocking preview).</param>
        internal static void SampleSlot(
            ref SpritePartsSetBlob set, int clipIndex, int slotIndex, float timeSeconds, bool wrap, bool stepped,
            out Pose pose)
        {
            ref var slot = ref set.Slots[slotIndex];
            pose = new Pose
            {
                Position = slot.RestPosition,
                Rotation = slot.RestRotation,
                Scale = slot.RestScale,
                Lattice = slot.Mesh,
            };

            if (clipIndex < 0 || clipIndex >= set.Clips.Length)
                return;

            ref var clip = ref set.Clips[clipIndex];
            float time = wrap
                ? WrapTime(timeSeconds, clip.Duration, clip.WrapMode)
                : math.clamp(timeSeconds, 0f, math.max(0f, clip.Duration));
            int trackIndex = TrackIndexForSlot(ref clip, slotIndex);
            if (trackIndex < 0)
                return;

            ref var track = ref clip.Tracks[trackIndex];
            if (track.Keys.Length == 0)
                return;

            // Each channel blends between the keys that hold it (Spine timelines): a rotation key does not pin
            // the position, so each channel keeps its own timing. Held before its first and after its last key.
            if (Span(ref track, SpritePartsKeyChannel.Position, time, out int a, out int b, out float u))
                pose.Position = math.lerp(track.Keys[a].Position, track.Keys[b].Position, stepped ? 0f : u);
            if (Span(ref track, SpritePartsKeyChannel.Rotation, time, out a, out b, out u))
                pose.Rotation = LerpAngleShortest(track.Keys[a].Rotation, track.Keys[b].Rotation, stepped ? 0f : u);
            if (Span(ref track, SpritePartsKeyChannel.Scale, time, out a, out b, out u))
                pose.Scale = math.lerp(track.Keys[a].Scale, track.Keys[b].Scale, stepped ? 0f : u);
            if (Span(ref track, SpritePartsKeyChannel.Deform, time, out a, out b, out u))
                SpritePartsLattice.ApplyDeform(ref pose.Lattice, track.Keys[a].Deform, track.Keys[b].Deform, stepped ? 0f : u);
        }

        /// <summary>
        /// The keys around <paramref name="time"/> that hold <paramref name="channel"/> and the eased blend between
        /// them (the earlier key's ease). Before the first / after the last such key both are that key. False when
        /// no key holds the channel.
        /// </summary>
        static bool Span(ref SpritePartsTrackBlob track, SpritePartsKeyChannel channel, float time, out int a, out int b, out float u)
        {
            a = -1;
            b = -1;
            u = 0f;
            for (int i = 0; i < track.Keys.Length; i++)
            {
                if (!track.Keys[i].Holds(channel))
                    continue;
                if (track.Keys[i].Time <= time)
                    a = i;
                else
                {
                    b = i;
                    break;
                }
            }
            if (a < 0 && b < 0)
                return false;
            if (a < 0)
            {
                a = b;
                return true;
            }
            if (b < 0)
            {
                b = a;
                return true;
            }
            ref var ka = ref track.Keys[a];
            float span = track.Keys[b].Time - ka.Time;
            u = EaseKey(ref ka, span > 1e-8f ? (time - ka.Time) / span : 0f);
            return true;
        }

        /// <summary>
        /// Last non-empty keyed appearance at/before time.
        /// Reads the slot's appearance track; falls back to the pose track for
        /// legacy baked data where ids were mixed into pose keys.
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
            int trackIndex = AppearanceTrackIndexForSlot(ref clip, slotIndex);
            if (trackIndex < 0)
                trackIndex = TrackIndexForSlot(ref clip, slotIndex);
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
        /// <summary>The key's ease for the span after it: preset, or its own Bezier handles.</summary>
        public static float EaseKey(ref SpritePartsKeyBlob key, float u)
            => key.EaseMode == (byte)SpriteEaseMode.Bezier
                ? SpriteEase.EvaluateBezier(key.Curve, u)
                : SpriteEase.Evaluate((SpriteEaseMode)key.EaseMode, u);

        /// <summary>
        /// Keyed colour of a slot: colour keys blend with each other only (keys without colour are skipped),
        /// held before the first and after the last. False (white) when the clip has no colour key for it.
        /// </summary>
        public static bool SampleColor(ref SpritePartsSetBlob set, int clipIndex, int slotIndex, float timeSeconds, out float4 color)
        {
            color = new float4(1f);
            if (clipIndex < 0 || clipIndex >= set.Clips.Length)
                return false;
            ref var clip = ref set.Clips[clipIndex];
            int trackIndex = TrackIndexForSlot(ref clip, slotIndex);
            if (trackIndex < 0)
                return false;
            ref var track = ref clip.Tracks[trackIndex];
            float time = WrapTime(timeSeconds, clip.Duration, clip.WrapMode);
            int before = -1, after = -1;
            for (int i = 0; i < track.Keys.Length; i++)
            {
                if (track.Keys[i].HasColor == 0)
                    continue;
                if (track.Keys[i].Time <= time)
                    before = i;
                else
                {
                    after = i;
                    break;
                }
            }
            if (before < 0 && after < 0)
                return false;
            if (before < 0)
            {
                color = track.Keys[after].Color;
                return true;
            }
            ref var a = ref track.Keys[before];
            if (after < 0)
            {
                color = a.Color;
                return true;
            }
            ref var b = ref track.Keys[after];
            float span = b.Time - a.Time;
            float u = span > 1e-8f ? (time - a.Time) / span : 0f;
            color = math.lerp(a.Color, b.Color, EaseKey(ref a, u));
            return true;
        }

        /// <summary>
        /// Keyed draw rank of a slot: the last draw-order key at or before the time (held). Loop clips carry the
        /// last one from the previous pass. -1 = no key, use the slot's own rank.
        /// </summary>
        public static int SampleDrawOrder(ref SpritePartsSetBlob set, int clipIndex, int slotIndex, float timeSeconds)
        {
            if (clipIndex < 0 || clipIndex >= set.Clips.Length)
                return -1;
            ref var clip = ref set.Clips[clipIndex];
            int trackIndex = TrackIndexForSlot(ref clip, slotIndex);
            if (trackIndex < 0)
                return -1;
            ref var track = ref clip.Tracks[trackIndex];
            float time = WrapTime(timeSeconds, clip.Duration, clip.WrapMode);
            int best = -1, lastInClip = -1;
            for (int i = 0; i < track.Keys.Length; i++)
            {
                if (track.Keys[i].HasDrawOrder == 0)
                    continue;
                lastInClip = track.Keys[i].DrawOrder;
                if (track.Keys[i].Time <= time + 1e-6f)
                    best = track.Keys[i].DrawOrder;
            }
            if (best >= 0)
                return best;
            return clip.WrapMode != (byte)SpritePartsWrap.Once ? lastInClip : -1;
        }

        /// <summary>
        /// Whether a clip shape clips at the time: the last clip key at or before it (held; loops carry the last one
        /// round). On (true) when the clip has no clip key for it.
        /// </summary>
        public static bool SampleClipActive(ref SpritePartsSetBlob set, int clipIndex, int slotIndex, float timeSeconds)
        {
            if (clipIndex < 0 || clipIndex >= set.Clips.Length)
                return true;
            ref var clip = ref set.Clips[clipIndex];
            int trackIndex = TrackIndexForSlot(ref clip, slotIndex);
            if (trackIndex < 0)
                return true;
            ref var track = ref clip.Tracks[trackIndex];
            float time = WrapTime(timeSeconds, clip.Duration, clip.WrapMode);
            int best = -1, lastInClip = -1;
            for (int i = 0; i < track.Keys.Length; i++)
            {
                if (track.Keys[i].HasClipActive == 0)
                    continue;
                lastInClip = track.Keys[i].ClipActive;
                if (track.Keys[i].Time <= time + 1e-6f)
                    best = track.Keys[i].ClipActive;
            }
            if (best >= 0)
                return best != 0;
            if (clip.WrapMode != (byte)SpritePartsWrap.Once && lastInClip >= 0)
                return lastInClip != 0;
            return true;
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

        internal static int TrackIndexForSlot(ref SpritePartsClipBlob clip, int slotIndex)
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

        /// <summary>Dense slot -> appearance track index; -1 when the slot has none.</summary>
        static int AppearanceTrackIndexForSlot(ref SpritePartsClipBlob clip, int slotIndex)
        {
            if (slotIndex < 0 || slotIndex >= clip.SlotAppearanceTrackIndices.Length)
                return -1;
            return clip.SlotAppearanceTrackIndices[slotIndex];
        }
    }
}
