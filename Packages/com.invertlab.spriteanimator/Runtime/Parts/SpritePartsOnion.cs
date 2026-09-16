using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace InvertLab.Sprites.DOTS
{
    /// <summary>
    /// Parts onion-skin evaluation using the same <see cref="SpritePartsSampler"/> as runtime.
    /// Ghosts are whole-character poses; parent motion applies to unkeyed children.
    /// </summary>
    public static class SpritePartsOnion
    {
        public struct GhostSample
        {
            public float Time;
            public int FrameDelta;
            public bool IsPast;
        }

        /// <summary>
        /// Collect onion sample times. Past negative deltas, future positive.
        /// Loop wraps; Once omits out-of-range. Deduplicates identical times.
        /// Returns empty while playing unless showWhilePlaying.
        /// </summary>
        public static List<GhostSample> CollectGhostTimes(
            float playhead,
            float duration,
            byte wrapMode,
            int beforeCount,
            int afterCount,
            int spacingFrames,
            float displayFps,
            bool playing,
            bool showWhilePlaying)
        {
            var result = new List<GhostSample>();
            if (playing && !showWhilePlaying)
                return result;
            if (!(duration > 0f) || !(displayFps > 0f))
                return result;

            beforeCount = math.clamp(beforeCount, 0, 3);
            afterCount = math.clamp(afterCount, 0, 3);
            spacingFrames = math.max(1, spacingFrames);
            var seen = new HashSet<int>();

            void TryAdd(int frameDelta)
            {
                if (frameDelta == 0) return;
                float raw = playhead + frameDelta / displayFps;
                float sample;
                if (wrapMode == (byte)SpritePartsWrap.Once)
                {
                    if (raw < -1e-5f || raw > duration + 1e-5f)
                        return;
                    sample = math.clamp(raw, 0f, duration);
                }
                else
                {
                    sample = SpritePartsSampler.WrapTime(raw, duration, wrapMode);
                }

                int key = (int)math.round(sample * 1000f);
                if (!seen.Add(key))
                    return;
                // Skip ghost that lands on the live playhead.
                if (math.abs(sample - playhead) < 1e-4f)
                    return;
                result.Add(new GhostSample
                {
                    Time = sample,
                    FrameDelta = frameDelta,
                    IsPast = frameDelta < 0,
                });
            }

            for (int i = beforeCount; i >= 1; i--)
                TryAdd(-i * spacingFrames);
            for (int i = 1; i <= afterCount; i++)
                TryAdd(i * spacingFrames);
            return result;
        }

        /// <summary>
        /// True when every slot's local-to-root matrix matches within epsilon.
        /// Idle with only rest (or a single key) would otherwise onion-draw a
        /// second grey square on top of the live pose.
        /// </summary>
        public static bool MatricesApproximatelyEqual(
            NativeArray<float4x4> a, NativeArray<float4x4> b, float epsilon = 1e-3f)
        {
            if (!a.IsCreated || !b.IsCreated || a.Length != b.Length || a.Length == 0)
                return false;
            float e = math.max(1e-6f, epsilon);
            for (int i = 0; i < a.Length; i++)
            {
                float4x4 da = a[i];
                float4x4 db = b[i];
                for (int c = 0; c < 4; c++)
                {
                    if (math.cmax(math.abs(da[c] - db[c])) > e)
                        return false;
                }
            }
            return true;
        }

        /// <summary>
        /// Build a pose-only blob and sample all slots at time (local + root matrices).
        /// Caller must <see cref="DisposeSample"/>.
        /// </summary>
        public static bool TrySampleCharacter(
            SpriteSheetProfile profile,
            int clipIndex,
            float timeSeconds,
            Allocator allocator,
            out BlobAssetReference<SpritePartsSetBlob> blob,
            out NativeArray<SpritePartsSampler.Pose> localPoses,
            out NativeArray<float4x4> localToRoot,
            out string error)
        {
            blob = default;
            localPoses = default;
            localToRoot = default;
            error = null;
            if (profile == null)
            {
                error = "Profile is null.";
                return false;
            }
            profile.EnsurePartsRig();
            if (profile.PartsSlots == null || profile.PartsSlots.Count == 0)
            {
                error = "No Parts slots.";
                return false;
            }
            if (!SpritePartsClipConversion.TryBuildPoseEvaluationBlob(profile, allocator, out blob, out error))
                return false;

            int n = blob.Value.Slots.Length;
            localPoses = new NativeArray<SpritePartsSampler.Pose>(n, allocator);
            localToRoot = new NativeArray<float4x4>(n, allocator);
            SpritePartsSampler.SampleAll(ref blob.Value, clipIndex, timeSeconds, localPoses);
            SpritePartsHierarchy.ComposeLocalToRoot(ref blob.Value, localPoses, localToRoot);
            return true;
        }

        /// <summary>
        /// Sample a transient clip (e.g. an import-preview clone) against a rig
        /// profile without inserting the clip into the profile. Strictly
        /// read-only on the profile. Caller must <see cref="DisposeSample"/>.
        /// </summary>
        public static bool TrySampleClipOnRig(
            SpriteSheetProfile rigProfile,
            SpritePartsClipDef transientClip,
            float timeSeconds,
            Allocator allocator,
            out BlobAssetReference<SpritePartsSetBlob> blob,
            out NativeArray<SpritePartsSampler.Pose> localPoses,
            out NativeArray<float4x4> localToRoot,
            out string error)
        {
            blob = default;
            localPoses = default;
            localToRoot = default;
            error = null;
            if (transientClip == null)
            {
                error = "Preview clip is null.";
                return false;
            }
            var clips = new List<SpritePartsClipDef> { transientClip };
            if (!SpritePartsClipConversion.TryBuildPoseEvaluationBlob(rigProfile, clips, allocator, out blob, out error))
                return false;

            int n = blob.Value.Slots.Length;
            localPoses = new NativeArray<SpritePartsSampler.Pose>(n, allocator);
            localToRoot = new NativeArray<float4x4>(n, allocator);
            SpritePartsSampler.SampleAll(ref blob.Value, 0, timeSeconds, localPoses);
            SpritePartsHierarchy.ComposeLocalToRoot(ref blob.Value, localPoses, localToRoot);
            return true;
        }

        public static void DisposeSample(
            BlobAssetReference<SpritePartsSetBlob> blob,
            NativeArray<SpritePartsSampler.Pose> localPoses,
            NativeArray<float4x4> localToRoot)
        {
            if (localPoses.IsCreated) localPoses.Dispose();
            if (localToRoot.IsCreated) localToRoot.Dispose();
            if (blob.IsCreated) blob.Dispose();
        }
    }
}
