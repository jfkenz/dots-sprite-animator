using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;

namespace BallForge.Sprites.DOTS
{
    /// <summary>Rules for deciding whether a clip can run on the compact GPU clock.</summary>
    public static class SpriteGpuEligibility
    {
        public static bool IsGpuEligible(ref SpriteAnimSetBlob set, int clipIndex, out FixedString128Bytes reason)
        {
            reason = default;
            if (clipIndex < 0 || clipIndex >= set.Clips.Length)
            {
                reason = "Invalid clip index.";
                return false;
            }

            ref var def = ref set.Clips[clipIndex];
            if (def.WrapMode != SpriteAnimWrap.Loop && def.WrapMode != SpriteAnimWrap.Once)
            {
                reason = "Ping-pong and reverse playback require CPU timing.";
                return false;
            }
            if (def.FrameSockets.Length > 0)
            {
                reason = "Sockets require CPU playback.";
                return false;
            }

            int firstSlot = (int)set.Frames[def.FirstFrame].x;
            for (int frame = 0; frame < def.FrameCount; frame++)
            {
                float4 frameData = set.Frames[def.FirstFrame + frame];
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
