using UnityEngine;

namespace InvertLab.Sprites.DOTS
{
    /// <summary>
    /// Shared preview-playback mapping used by editor timeline/playhead tests.
    /// </summary>
    public static class SpriteAnimPlayback
    {
        public const float TimelineOriginX = 48f;

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

            if (wrap == SpriteAnimWrap.PingPong)
            {
                float cycle = PingPongCycleDuration(total);
                float local = loop ? Mathf.Repeat(time, cycle) : Mathf.Min(time, cycle);
                if (!loop && time >= cycle)
                    ended = true;
                timelineTime = local <= total ? Mathf.Min(local, total) : cycle - local;
            }
            else if (wrap == SpriteAnimWrap.ReverseLoop)
            {
                float local = loop ? Mathf.Repeat(time, total) : Mathf.Min(time, total);
                if (!loop && time >= total)
                    ended = true;
                timelineTime = local <= 0f ? total : total - local;
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
            if (clip?.FrameDurationScales == null ||
                frame < 0 || frame >= clip.FrameDurationScales.Length)
                return 1f / SpriteClipDef.DefaultFrameRate;
            return clip.FrameDurationScales[frame] / Mathf.Max(0.1f, clip.FrameRate);
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

            if (authoredTime <= 0f)
                return Mathf.Max(0f, total - 0.0001f);
            return Mathf.Max(0f, total - authoredTime);
        }

        static float PingPongCycleDuration(float total)
            => Mathf.Max(0.001f, total * 2f);
    }
}
