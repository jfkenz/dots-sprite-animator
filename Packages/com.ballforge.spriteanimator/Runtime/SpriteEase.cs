using Unity.Mathematics;

namespace BallForge.Sprites.DOTS
{
    /// <summary>Small easing utility for per-frame TRS interpolation.</summary>
    public static class SpriteEase
    {
        public static float Evaluate(SpriteEaseMode mode, float t)
        {
            t = math.saturate(t);
            return mode switch
            {
                SpriteEaseMode.SmoothStep => t * t * (3f - 2f * t),
                SpriteEaseMode.EaseIn => t * t,
                SpriteEaseMode.EaseOut => 1f - (1f - t) * (1f - t),
                SpriteEaseMode.Step => t < 1f ? 0f : 1f,
                _ => t,
            };
        }
    }
}
