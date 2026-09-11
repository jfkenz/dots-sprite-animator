using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

namespace InvertLab.Sprites.DOTS
{
    /// <summary>Rules for deciding whether a clip can run on the compact GPU clock.</summary>
    public static class SpriteGpuEligibility
    {
        /// <summary>Entity behavior must also fit the compact, visual-only GPU clock.</summary>
        public static bool IsGpuEligible(EntityManager em, Entity entity, out FixedString128Bytes reason)
        {
            reason = "Entity has no CPU playback state.";
            if (!em.Exists(entity) || !em.HasComponent<SpriteAnimSetRef>(entity) ||
                !em.HasComponent<SpriteAnimPlayer>(entity) || em.HasComponent<SpriteGpuDriven>(entity))
                return false;
            var blob = em.GetComponentData<SpriteAnimSetRef>(entity).Set;
            if (!blob.IsCreated) return false;
            var player = em.GetComponentData<SpriteAnimPlayer>(entity);
            if (!IsGpuEligible(ref blob.Value, player.ClipIndex, out reason)) return false;
            if (!math.isfinite(player.Speed) || player.Speed < 0 || !math.isfinite(player.Time))
            {
                reason = "Negative or invalid playback clocks require CPU playback.";
                return false;
            }
            if (player.QueuedClipIndex >= 0 || player.OneShotActive != 0 || player.ResumeClipIndex >= 0 ||
                player.HitstopActive != 0 || player.BlendOutTime > 0)
            {
                reason = "Queued clips, one-shots, hitstop and active fades require CPU playback.";
                return false;
            }
            if (em.HasComponent<SpriteHitboxSetRef>(entity) || em.HasComponent<SpriteSocketMotionPlayer>(entity) ||
                em.HasBuffer<SpriteSocketBuffer>(entity) || em.HasBuffer<SpriteAnimEventBuffer>(entity) ||
                em.HasComponent<SpriteAnimCompleted>(entity))
            {
                reason = "Hitboxes, sockets, lifecycle events or completed state require CPU playback.";
                return false;
            }
            if (em.HasComponent<SpriteAnimEnabled>(entity) && !em.IsComponentEnabled<SpriteAnimEnabled>(entity))
            {
                reason = "Disabled animation must stay on CPU.";
                return false;
            }
            if (em.HasComponent<Parent>(entity) || em.HasComponent<PostTransformMatrix>(entity) ||
                (em.HasComponent<LocalTransform>(entity) &&
                 math.abs(math.dot(em.GetComponentData<LocalTransform>(entity).Rotation.value,
                                   quaternion.identity.value)) < 0.99999f))
            {
                reason = "Parented, rotated or non-uniform transforms require CPU rendering.";
                return false;
            }
            return true;
        }

        public static bool IsGpuEligible(ref SpriteAnimSetBlob set, int clipIndex, out FixedString128Bytes reason)
        {
            reason = default;
            if (clipIndex < 0 || clipIndex >= set.Clips.Length)
            {
                reason = "Invalid clip index.";
                return false;
            }

            ref var def = ref set.Clips[clipIndex];
            if (set.SocketMotions.Length > 0 || def.OnCompleteClipIndex >= 0)
            {
                reason = "Independent socket motion or completion chaining requires CPU playback.";
                return false;
            }
            if (def.FrameCount <= 0 || !math.isfinite(def.FrameRate) || def.FrameRate <= 0f || def.FirstFrame < 0 ||
                def.FirstFrame > set.Frames.Length - def.FrameCount)
            {
                reason = "Clip has no valid frames or frame rate.";
                return false;
            }
            if (def.WrapMode != SpriteAnimWrap.Loop && def.WrapMode != SpriteAnimWrap.Once)
            {
                reason = "Ping-pong / reverse / reverse-once require CPU timing.";
                return false;
            }
            if (def.FrameSockets.Length > 0)
            {
                reason = "Sockets require CPU playback.";
                return false;
            }

            if (def.EventKeys.Length > 0)
            {
                reason = "Animation events require CPU playback.";
                return false;
            }

            int firstSlot = (int)set.Frames[def.FirstFrame].x;
            if (firstSlot < 0)
            {
                reason = "Invalid atlas slot.";
                return false;
            }
            for (int frame = 0; frame < def.FrameCount; frame++)
            {
                float4 frameData = set.Frames[def.FirstFrame + frame];
                if (!math.all(math.isfinite(frameData)))
                {
                    reason = "Invalid frame data.";
                    return false;
                }
                if ((int)frameData.x != firstSlot + frame)
                {
                    reason = "Frame reorder requires CPU playback.";
                    return false;
                }
                if (math.lengthsq(frameData.yz) > 1e-10f)
                {
                    reason = "Per-frame offsets require CPU playback.";
                    return false;
                }
                if (frame < def.DurationScales.Length && math.abs(def.DurationScales[frame] - 1f) > 1e-5f)
                {
                    reason = "Custom frame holds require CPU playback.";
                    return false;
                }
                if (frame < def.EventIds.Length && def.EventIds[frame] != 0)
                {
                    reason = "Animation events require CPU playback.";
                    return false;
                }
                if (frame < def.FrameScales.Length &&
                    math.lengthsq(def.FrameScales[frame] - new float2(1f, 1f)) > 1e-8f)
                {
                    reason = "Per-frame scale requires CPU playback.";
                    return false;
                }
                if (frame < def.FrameRotations.Length && math.abs(def.FrameRotations[frame]) > 1e-5f)
                {
                    reason = "Per-frame rotation requires CPU playback.";
                    return false;
                }
                if (frame < def.FrameTweenModes.Length &&
                    def.FrameTweenModes[frame] != (byte)SpriteEaseMode.Linear)
                {
                    reason = "TRS easing requires CPU playback.";
                    return false;
                }
            }

            reason = "GPU clock OK.";
            return true;
        }

        public static bool IsGpuEligible(SpriteClipDef clip, out string reason)
        {
            reason = "GPU clock OK.";
            if (clip == null || clip.Frames == null || clip.Frames.Length == 0)
            {
                reason = "No clip loaded.";
                return false;
            }

            clip.EnsureFrameData();
            if (clip.OnCompleteClipIndex >= 0)
            {
                reason = "Completion chaining requires CPU playback.";
                return false;
            }
            if (clip.UsesMixedSheetRows())
            {
                reason = "Frames from more than one sheet row require CPU playback.";
                return false;
            }
            if (clip.WrapMode != SpriteAnimWrap.Loop && clip.WrapMode != SpriteAnimWrap.Once)
            {
                reason = "Ping-pong and reverse playback require CPU timing.";
                return false;
            }
            if (clip.Sockets != null && clip.Sockets.Count > 0)
            {
                reason = "Sockets require CPU playback.";
                return false;
            }
            if (clip.EventMarkers != null && clip.EventMarkers.Count > 0)
            {
                reason = "Animation events require CPU playback.";
                return false;
            }

            for (int frame = 0; frame < clip.Frames.Length; frame++)
            {
                if (clip.Frames[frame] != clip.Frames[0] + frame)
                {
                    reason = "Frame reorder requires CPU playback.";
                    return false;
                }
                if (frame < clip.OnionOffsets.Length && clip.OnionOffsets[frame] != Vector2.zero)
                {
                    reason = "Per-frame offsets require CPU playback.";
                    return false;
                }
                if (frame < clip.FrameDurationScales.Length &&
                    Mathf.Abs(clip.FrameDurationScales[frame] - 1f) > 1e-5f)
                {
                    reason = "Custom frame holds require CPU playback.";
                    return false;
                }
                if (frame < clip.EventIds.Length && clip.EventIds[frame] != 0)
                {
                    reason = "Animation events require CPU playback.";
                    return false;
                }
                if (frame < clip.FrameScales.Length &&
                    (Mathf.Abs(clip.FrameScales[frame].x - 1f) > 1e-5f ||
                     Mathf.Abs(clip.FrameScales[frame].y - 1f) > 1e-5f))
                {
                    reason = "Per-frame scale requires CPU playback.";
                    return false;
                }
                if (frame < clip.FrameRotations.Length && Mathf.Abs(clip.FrameRotations[frame]) > 1e-5f)
                {
                    reason = "Per-frame rotation requires CPU playback.";
                    return false;
                }
                if (frame < clip.FrameTweenModes.Length &&
                    clip.FrameTweenModes[frame] != (byte)SpriteEaseMode.Linear)
                {
                    reason = "TRS easing requires CPU playback.";
                    return false;
                }
            }

            return true;
        }
    }
}
