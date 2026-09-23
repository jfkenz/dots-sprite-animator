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
        /// Out-of-range times clamp to [0, duration] (never wrap). Wrapping before t=0
        /// on Loop used to sample the last→first seam blend, so Before ghosts at the
        /// first frame looked like a mid pose between key 0 and the next key.
        /// Once still omits ghosts wholly outside the clip. Deduplicates identical times.
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
                // Onion is a timeline neighbor preview, not a loop preview. Always clamp
                // into the clip so Before at t=0 stays on the first-frame pose.
                if (wrapMode == (byte)SpritePartsWrap.Once)
                {
                    if (raw < -1e-5f || raw > duration + 1e-5f)
                        return;
                }
                float sample = math.clamp(raw, 0f, duration);

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
        /// Editor preview switch: simulate jiggle springs in <see cref="TrySampleCharacter"/>. The editor turns it
        /// on while its preview plays; the game never reads it.
        /// </summary>
        public static bool PreviewPhysics;

        /// <summary>
        /// Build a pose-only blob and sample all slots at time (local + root matrices), as shown: IK and,
        /// with <see cref="PreviewPhysics"/>, jiggle. Caller must <see cref="DisposeSample"/>.
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
            => TrySampleCharacter(profile, clipIndex, timeSeconds, allocator, false,
                out blob, out localPoses, out localToRoot, out error);

        /// <param name="keyedPose">
        /// True: the pose the keys store (no IK, no jiggle), for writing keys. False: the pose as shown.
        /// </param>
        public static bool TrySampleCharacter(
            SpriteSheetProfile profile,
            int clipIndex,
            float timeSeconds,
            Allocator allocator,
            bool keyedPose,
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
            if (keyedPose)
                SpritePartsPoseWriter.EvaluateEditor(ref blob.Value, clipIndex, timeSeconds, localPoses, localToRoot,
                    new SpritePartsEvalExtras { KeyedPoseOnly = true });
            else if (PreviewPhysics && blob.Value.Jiggles.Length > 0)
                EvaluateWithPhysics(profile, ref blob.Value, clipIndex, timeSeconds, localPoses, localToRoot);
            else
                SpritePartsPoseWriter.EvaluateEditor(ref blob.Value, clipIndex, timeSeconds, localPoses, localToRoot);
            return true;
        }

        const float PreviewStep = 1f / 60f;
        static SpriteSheetProfile s_physicsProfile;
        static int s_physicsClip = -1;
        static float s_physicsTime = -1f;
        static SpritePartJiggleState[] s_physicsState;

        /// <summary>
        /// Jiggle in the editor preview: carries the spring state from the last preview time forward (across a
        /// loop too), or warms up over the second before <paramref name="time"/> when it cannot. Deterministic.
        /// </summary>
        static void EvaluateWithPhysics(SpriteSheetProfile profile, ref SpritePartsSetBlob set, int clip, float time,
            NativeArray<SpritePartsSampler.Pose> local, NativeArray<float4x4> localToRoot)
        {
            int count = set.Jiggles.Length;
            float duration = clip >= 0 && clip < set.Clips.Length ? set.Clips[clip].Duration : 0f;
            var states = new NativeArray<SpritePartJiggleState>(count, Allocator.Temp);
            try
            {
                bool same = ReferenceEquals(profile, s_physicsProfile) && clip == s_physicsClip
                            && s_physicsState != null && s_physicsState.Length == count;
                float from = s_physicsTime;
                bool wrapped = same && time < from && duration > 1e-4f && from - time > duration * 0.5f;
                float span = wrapped ? duration - from + time : time - from;
                if (same && span >= 0f && span <= 0.25f)
                {
                    states.CopyFrom(s_physicsState);
                }
                else
                {
                    from = math.max(0f, time - 1f);
                    wrapped = false;
                    var init = new SpritePartsEvalExtras { Jiggle = states, DeltaTime = 0f };
                    SpritePartsPoseWriter.EvaluateEditor(ref set, clip, from, local, localToRoot, init);
                }
                if (wrapped)
                {
                    StepTo(ref set, clip, ref from, duration, states, local, localToRoot);
                    from = 0f;
                }
                StepTo(ref set, clip, ref from, time, states, local, localToRoot);
                // Final pose at exactly this time (no further motion).
                SpritePartsPoseWriter.EvaluateEditor(ref set, clip, time, local, localToRoot,
                    new SpritePartsEvalExtras { Jiggle = states, DeltaTime = 0f });
                s_physicsState ??= new SpritePartJiggleState[0];
                if (s_physicsState.Length != count)
                    s_physicsState = new SpritePartJiggleState[count];
                states.CopyTo(s_physicsState);
                s_physicsProfile = profile;
                s_physicsClip = clip;
                s_physicsTime = time;
            }
            finally
            {
                states.Dispose();
            }
        }

        static void StepTo(ref SpritePartsSetBlob set, int clip, ref float t, float end, NativeArray<SpritePartJiggleState> states,
            NativeArray<SpritePartsSampler.Pose> local, NativeArray<float4x4> localToRoot)
        {
            while (t < end - 1e-5f)
            {
                float h = math.min(PreviewStep, end - t);
                t += h;
                SpritePartsPoseWriter.EvaluateEditor(ref set, clip, t, local, localToRoot,
                    new SpritePartsEvalExtras { Jiggle = states, DeltaTime = h });
            }
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
            SpritePartsPoseWriter.EvaluateEditor(ref blob.Value, 0, timeSeconds, localPoses, localToRoot);
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
