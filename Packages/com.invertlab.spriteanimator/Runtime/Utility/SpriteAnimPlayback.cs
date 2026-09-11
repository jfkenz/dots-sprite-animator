using UnityEngine;
using Unity.Mathematics;

namespace InvertLab.Sprites.DOTS
{
    /// <summary>
    /// Shared preview-playback mapping used by editor timeline/playhead tests.
    /// </summary>
    public static class SpriteAnimPlayback
    {
        public const float TimelineOriginX = 48f;

        /// <summary>Maps the runtime phase step to its authored frame, including negative loops.</summary>
        public static int DisplayFrame(int phaseStep, int frameCount, byte wrapMode)
        {
            if (frameCount <= 1) return 0;
            if (wrapMode == SpriteAnimWrap.Once || wrapMode == SpriteAnimWrap.ReverseOnce)
                return math.clamp(phaseStep, 0, frameCount - 1);
            int cycle = CycleLength(frameCount, wrapMode);
            int raw = phaseStep % cycle;
            if (raw < 0) raw += cycle;
            if (wrapMode == SpriteAnimWrap.PingPong)
                return raw < frameCount ? raw : cycle - raw;
            return wrapMode == SpriteAnimWrap.ReverseLoop ? frameCount - 1 - raw : raw;
        }

        public static int CycleLength(int frameCount, byte wrapMode)
            => frameCount <= 1 ? 1 : wrapMode == SpriteAnimWrap.PingPong ? 2 * (frameCount - 1) : frameCount;

        public static float CycleDuration(SpriteClipDef clip)
        {
            float total = TotalAuthoredDuration(clip);
            return clip != null && clip.WrapMode == SpriteAnimWrap.PingPong && clip.Frames.Length > 1
                ? 2 * total - FrameDuration(clip, 0) - FrameDuration(clip, clip.Frames.Length - 1)
                : total;
        }

        public readonly struct PreviewSample
        {
            public readonly int Frame;
            public readonly float Fraction;
            public readonly float TimelineTime;
            public readonly bool Ended;

            public PreviewSample(int frame, float fraction, float timelineTime, bool ended)
            {
                Frame = frame;
                Fraction = fraction;
                TimelineTime = timelineTime;
                Ended = ended;
            }
        }

        public static PreviewSample EvaluatePreview(SpriteClipDef clip, float time, bool previewLoop)
        {
            if (clip == null || clip.Frames == null || clip.Frames.Length == 0)
                return default;

            clip.EnsureFrameData();
            time = Mathf.Max(0f, time);
            float total = TotalAuthoredDuration(clip);
            byte wrap = clip.WrapMode;
            bool onceStyle = wrap == SpriteAnimWrap.Once || wrap == SpriteAnimWrap.ReverseOnce;
            bool loop = previewLoop || !onceStyle;
            bool ended = false;
            float timelineTime;

            if (wrap == SpriteAnimWrap.PingPong || wrap == SpriteAnimWrap.ReverseLoop)
            {
                float local = Mathf.Repeat(time, CycleDuration(clip));
                int steps = CycleLength(clip.Frames.Length, wrap);
                for (int step = 0; step < steps; step++)
                {
                    int display = DisplayFrame(step, clip.Frames.Length, wrap);
                    float dwell = FrameDuration(clip, display);
                    if (local < dwell || step == steps - 1)
                    {
                        float progress = Mathf.Clamp01(local / dwell);
                        return new PreviewSample(display, progress,
                            AuthoredStartTime(clip, display) + progress * dwell, false);
                    }
                    local -= dwell;
                }
                return default;
            }
            else if (wrap == SpriteAnimWrap.ReverseOnce)
            {
                // Reverse-once callers count authored seconds down from total.
                // Reaching total is the starting pose, not completion.
                timelineTime = Mathf.Clamp(time, 0, total);
                ended = !previewLoop && time <= 0;
            }
            else
            {
                if (!loop && time >= total)
                {
                    ended = true;
                    timelineTime = total;
                }
                else
                {
                    timelineTime = loop ? Mathf.Repeat(time, total) : Mathf.Min(time, total);
                }
            }

            timelineTime = Mathf.Clamp(timelineTime, 0f, total);
            int frame = AuthoredFrameAtTime(clip, timelineTime, out float fraction);
            return new PreviewSample(frame, fraction, timelineTime, ended);
        }

        public static float PlayheadX(float timelineTime, float originX, float pixelsPerSecond)
            => originX + Mathf.Max(0f, timelineTime) * Mathf.Max(0f, pixelsPerSecond);

        public static float PlayheadX(float timelineTime, float pixelsPerSecond)
            => PlayheadX(timelineTime, TimelineOriginX, pixelsPerSecond);

        public static float FrameDuration(SpriteClipDef clip, int frame)
        {
            float rate = clip != null ? Mathf.Max(0.1f, clip.FrameRate) : SpriteClipDef.DefaultFrameRate;
            if (clip?.FrameDurationScales == null ||
                frame < 0 || frame >= clip.FrameDurationScales.Length)
                return 1f / rate;
            return Mathf.Max(0.01f, clip.FrameDurationScales[frame]) / rate;
        }

        public static float TotalAuthoredDuration(SpriteClipDef clip)
        {
            if (clip?.Frames == null || clip.Frames.Length == 0)
                return 0.001f;
            clip.EnsureFrameData();
            float total = 0f;
            for (int i = 0; i < clip.Frames.Length; i++)
                total += FrameDuration(clip, i);
            return Mathf.Max(0.001f, total);
        }

        public static float AuthoredStartTime(SpriteClipDef clip, int frame)
        {
            if (clip?.Frames == null)
                return 0f;
            float time = 0f;
            int last = Mathf.Clamp(frame, 0, clip.Frames.Length);
            for (int i = 0; i < last; i++)
                time += FrameDuration(clip, i);
            return time;
        }

        public static int AuthoredFrameAtTime(SpriteClipDef clip, float authoredTime, out float fraction)
        {
            if (clip == null || clip.Frames == null || clip.Frames.Length == 0)
            {
                fraction = 0f;
                return 0;
            }

            clip.EnsureFrameData();
            float cursor = 0f;
            for (int i = 0; i < clip.Frames.Length; i++)
            {
                float duration = FrameDuration(clip, i);
                if (authoredTime < cursor + duration || i == clip.Frames.Length - 1)
                {
                    fraction = duration > 1e-8f
                        ? Mathf.Clamp01((authoredTime - cursor) / duration)
                        : 0f;
                    return i;
                }
                cursor += duration;
            }

            fraction = 0f;
            return 0;
        }

        public static float PreviewTimeForAuthoredTime(SpriteClipDef clip, float authoredTime)
        {
            if (clip == null)
                return Mathf.Max(0f, authoredTime);

            clip.EnsureFrameData();
            float total = TotalAuthoredDuration(clip);
            authoredTime = Mathf.Clamp(authoredTime, 0f, total);
            if (clip.WrapMode != SpriteAnimWrap.ReverseLoop)
                return authoredTime;

            int frame = AuthoredFrameAtTime(clip, authoredTime, out float fraction);
            float elapsed = 0;
            for (int i = clip.Frames.Length - 1; i > frame; i--) elapsed += FrameDuration(clip, i);
            return elapsed + fraction * FrameDuration(clip, frame);
        }
    }
}
