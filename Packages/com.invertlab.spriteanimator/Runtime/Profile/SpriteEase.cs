using Unity.Mathematics;

namespace InvertLab.Sprites.DOTS
{
    /// <summary>Small easing utility for per-frame TRS interpolation.</summary>
    public static class SpriteEase
    {
        public static bool IsValidMode(byte mode)
            => mode <= (byte)SpriteEaseMode.None;

        public static float Evaluate(SpriteEaseMode mode, float t)
            => Evaluate(mode, t, false);

        public static float Evaluate(SpriteEaseMode mode, float t, bool allowOvershoot)
        {
            t = math.saturate(t);
            float value = mode switch
            {
                SpriteEaseMode.SmoothStep => t * t * (3f - 2f * t),
                SpriteEaseMode.EaseIn => t * t,
                SpriteEaseMode.EaseOut => 1f - (1f - t) * (1f - t),
                SpriteEaseMode.Step => t < 1f ? 0f : 1f,
                SpriteEaseMode.EaseInOut => t < 0.5f
                    ? 4f * t * t * t
                    : 1f - math.pow(-2f * t + 2f, 3f) * 0.5f,
                SpriteEaseMode.SineIn => 1f - math.cos(t * math.PI * 0.5f),
                SpriteEaseMode.SineOut => math.sin(t * math.PI * 0.5f),
                SpriteEaseMode.SineInOut => -(math.cos(math.PI * t) - 1f) * 0.5f,
                SpriteEaseMode.QuadIn => t * t,
                SpriteEaseMode.QuadOut => 1f - (1f - t) * (1f - t),
                SpriteEaseMode.QuadInOut => PowerInOut(t, 2),
                SpriteEaseMode.CubicIn => t * t * t,
                SpriteEaseMode.CubicOut => 1f - math.pow(1f - t, 3f),
                SpriteEaseMode.CubicInOut => PowerInOut(t, 3),
                SpriteEaseMode.QuartIn => math.pow(t, 4f),
                SpriteEaseMode.QuartOut => 1f - math.pow(1f - t, 4f),
                SpriteEaseMode.QuartInOut => PowerInOut(t, 4),
                SpriteEaseMode.QuintIn => math.pow(t, 5f),
                SpriteEaseMode.QuintOut => 1f - math.pow(1f - t, 5f),
                SpriteEaseMode.QuintInOut => PowerInOut(t, 5),
                SpriteEaseMode.ExpoIn => t <= 0f ? 0f : math.pow(2f, 10f * t - 10f),
                SpriteEaseMode.ExpoOut => t >= 1f ? 1f : 1f - math.pow(2f, -10f * t),
                SpriteEaseMode.ExpoInOut => ExpoInOut(t),
                SpriteEaseMode.CircIn => 1f - math.sqrt(1f - t * t),
                SpriteEaseMode.CircOut => math.sqrt(1f - (t - 1f) * (t - 1f)),
                SpriteEaseMode.CircInOut => CircInOut(t),
                SpriteEaseMode.BackIn => BackIn(t),
                SpriteEaseMode.BackOut => BackOut(t),
                SpriteEaseMode.BackInOut => BackInOut(t),
                SpriteEaseMode.ElasticIn => ElasticIn(t),
                SpriteEaseMode.ElasticOut => ElasticOut(t),
                SpriteEaseMode.ElasticInOut => ElasticInOut(t),
                SpriteEaseMode.BounceIn => 1f - BounceOut(1f - t),
                SpriteEaseMode.BounceOut => BounceOut(t),
                SpriteEaseMode.BounceInOut => t < 0.5f
                    ? (1f - BounceOut(1f - 2f * t)) * 0.5f
                    : (1f + BounceOut(2f * t - 1f)) * 0.5f,
                _ => t,
            };
            return allowOvershoot ? value : math.saturate(value);
        }

        public static float EvaluateSamples(float4 samplesA, float4 samplesB, float t)
            => EvaluateSamples(samplesA, samplesB, t, false);

        public static float EvaluateSamples(
            float4 samplesA, float4 samplesB, float t, bool allowOvershoot)
        {
            t = math.saturate(t);
            float scaled = t * 7f;
            int from = math.min(6, (int)math.floor(scaled));
            int to = from + 1;
            float blend = scaled - from;
            float a = Sample(samplesA, samplesB, from);
            float b = Sample(samplesA, samplesB, to);
            float value = math.lerp(a, b, blend);
            return allowOvershoot ? value : math.saturate(value);
        }

        static float PowerInOut(float t, int power)
            => t < 0.5f
                ? math.pow(2f, power - 1) * math.pow(t, power)
                : 1f - math.pow(-2f * t + 2f, power) * 0.5f;

        static float ExpoInOut(float t)
        {
            if (t <= 0f || t >= 1f) return t;
            return t < 0.5f
                ? math.pow(2f, 20f * t - 10f) * 0.5f
                : (2f - math.pow(2f, -20f * t + 10f)) * 0.5f;
        }

        static float CircInOut(float t)
            => t < 0.5f
                ? (1f - math.sqrt(1f - 4f * t * t)) * 0.5f
                : (math.sqrt(1f - math.pow(-2f * t + 2f, 2f)) + 1f) * 0.5f;

        static float BackIn(float t)
        {
            const float c1 = 1.70158f;
            return (c1 + 1f) * t * t * t - c1 * t * t;
        }

        static float BackOut(float t)
        {
            const float c1 = 1.70158f;
            float x = t - 1f;
            return 1f + (c1 + 1f) * x * x * x + c1 * x * x;
        }

        static float BackInOut(float t)
        {
            const float c2 = 2.5949095f;
            return t < 0.5f
                ? 2f * t * t * ((c2 + 1f) * 2f * t - c2)
                : (math.pow(2f * t - 2f, 2f) *
                   ((c2 + 1f) * (t * 2f - 2f) + c2) + 2f) * 0.5f;
        }

        static float ElasticIn(float t)
        {
            if (t <= 0f || t >= 1f) return t;
            const float c4 = 2f * math.PI / 3f;
            return -math.pow(2f, 10f * t - 10f) *
                   math.sin((t * 10f - 10.75f) * c4);
        }

        static float ElasticOut(float t)
        {
            if (t <= 0f || t >= 1f) return t;
            const float c4 = 2f * math.PI / 3f;
            return math.pow(2f, -10f * t) *
                   math.sin((t * 10f - 0.75f) * c4) + 1f;
        }

        static float ElasticInOut(float t)
        {
            if (t <= 0f || t >= 1f) return t;
            const float c5 = 2f * math.PI / 4.5f;
            return t < 0.5f
                ? -(math.pow(2f, 20f * t - 10f) *
                    math.sin((20f * t - 11.125f) * c5)) * 0.5f
                : math.pow(2f, -20f * t + 10f) *
                  math.sin((20f * t - 11.125f) * c5) * 0.5f + 1f;
        }

        static float BounceOut(float t)
        {
            const float n1 = 7.5625f;
            const float d1 = 2.75f;
            if (t < 1f / d1) return n1 * t * t;
            if (t < 2f / d1)
            {
                t -= 1.5f / d1;
                return n1 * t * t + 0.75f;
            }
            if (t < 2.5f / d1)
            {
                t -= 2.25f / d1;
                return n1 * t * t + 0.9375f;
            }
            t -= 2.625f / d1;
            return n1 * t * t + 0.984375f;
        }

        static float Sample(float4 a, float4 b, int index)
        {
            return index switch
            {
                0 => a.x,
                1 => a.y,
                2 => a.z,
                3 => a.w,
                4 => b.x,
                5 => b.y,
                6 => b.z,
                _ => b.w,
            };
        }
    }

    public static class SpriteSocketMotionInterpolation
    {
        public static float2 Position(
            byte pathMode, float2 p0, float2 p1, float2 p2, float2 p3, float t)
            => Position(pathMode, p0, p1, p2, p3,
                float2.zero, float2.zero, 0f, 0, t);

        public static float2 Position(
            byte pathMode, float2 p0, float2 p1, float2 p2, float2 p3,
            float2 outTangent, float2 inTangent, float arcBulge,
            byte arcClockwise, float t)
        {
            if (pathMode == (byte)SpriteSocketPathMode.Hold ||
                pathMode == (byte)SpriteSocketPathMode.None)
                return t < 1f ? p1 : p2;
            if (pathMode == (byte)SpriteSocketPathMode.Linear)
                return math.lerp(p1, p2, t);
            if (pathMode == (byte)SpriteSocketPathMode.CubicBezier)
                return CubicBezier(p1, p1 + outTangent, p2 + inTangent, p2, t);
            if (pathMode == (byte)SpriteSocketPathMode.Hermite)
                return Hermite(p1, p2, outTangent, inTangent, t);
            if (pathMode == (byte)SpriteSocketPathMode.Arc)
                return Quadratic(p1, ArcControl(p1, p2, arcBulge, arcClockwise), p2, t);
            float t2 = t * t;
            float t3 = t2 * t;
            return 0.5f * ((2f * p1) +
                           (-p0 + p2) * t +
                           (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 +
                           (-p0 + 3f * p1 - 3f * p2 + p3) * t3);
        }

        public static float2 Derivative(
            byte pathMode, float2 p0, float2 p1, float2 p2, float2 p3,
            float2 outTangent, float2 inTangent, float arcBulge,
            byte arcClockwise, float t)
        {
            if (pathMode == (byte)SpriteSocketPathMode.Hold ||
                pathMode == (byte)SpriteSocketPathMode.None ||
                pathMode == (byte)SpriteSocketPathMode.Linear)
                return p2 - p1;
            if (pathMode == (byte)SpriteSocketPathMode.CubicBezier)
            {
                float2 c1 = p1 + outTangent;
                float2 c2 = p2 + inTangent;
                float u = 1f - t;
                return 3f * u * u * (c1 - p1) +
                       6f * u * t * (c2 - c1) +
                       3f * t * t * (p2 - c2);
            }
            if (pathMode == (byte)SpriteSocketPathMode.Hermite)
            {
                float t2 = t * t;
                return (6f * t2 - 6f * t) * p1 +
                       (3f * t2 - 4f * t + 1f) * outTangent +
                       (-6f * t2 + 6f * t) * p2 +
                       (3f * t2 - 2f * t) * inTangent;
            }
            if (pathMode == (byte)SpriteSocketPathMode.Arc)
            {
                float2 control = ArcControl(p1, p2, arcBulge, arcClockwise);
                return 2f * (1f - t) * (control - p1) +
                       2f * t * (p2 - control);
            }
            return 0.5f * ((-p0 + p2) +
                           2f * (2f * p0 - 5f * p1 + 4f * p2 - p3) * t +
                           3f * (-p0 + 3f * p1 - 3f * p2 + p3) * t * t);
        }

        static float2 CubicBezier(
            float2 p0, float2 p1, float2 p2, float2 p3, float t)
        {
            float u = 1f - t;
            return u * u * u * p0 + 3f * u * u * t * p1 +
                   3f * u * t * t * p2 + t * t * t * p3;
        }

        static float2 Hermite(
            float2 p0, float2 p1, float2 m0, float2 m1, float t)
        {
            float t2 = t * t;
            float t3 = t2 * t;
            return (2f * t3 - 3f * t2 + 1f) * p0 +
                   (t3 - 2f * t2 + t) * m0 +
                   (-2f * t3 + 3f * t2) * p1 +
                   (t3 - t2) * m1;
        }

        static float2 Quadratic(float2 p0, float2 p1, float2 p2, float t)
        {
            float u = 1f - t;
            return u * u * p0 + 2f * u * t * p1 + t * t * p2;
        }

        static float2 ArcControl(
            float2 from, float2 to, float bulge, byte clockwise)
        {
            float2 delta = to - from;
            float length = math.length(delta);
            float2 normal = length > 1e-6f
                ? new float2(-delta.y, delta.x) / length
                : new float2(0f, 1f);
            float sign = clockwise != 0 ? -1f : 1f;
            return (from + to) * 0.5f + normal * math.abs(bulge) * sign;
        }

        public static float Rotation(
            byte rotationMode, float fromAngle, float toAngle, int turns,
            float facingOffset, float2 pathDerivative, float t)
        {
            if (rotationMode == (byte)SpriteSocketRotationMode.Hold ||
                rotationMode == (byte)SpriteSocketRotationMode.None)
                return t < 1f ? fromAngle : toAngle;
            if (rotationMode == (byte)SpriteSocketRotationMode.FacePath)
            {
                if (math.lengthsq(pathDerivative) <= 1e-10f)
                    return fromAngle;
                return math.degrees(math.atan2(
                    pathDerivative.y, pathDerivative.x)) + facingOffset;
            }
            float shortest = math.fmod(toAngle - fromAngle + 180f, 360f);
            if (shortest < 0f)
                shortest += 360f;
            shortest -= 180f;
            float delta = shortest;
            if (rotationMode == (byte)SpriteSocketRotationMode.Clockwise &&
                delta > 0f)
                delta -= 360f;
            else if (rotationMode ==
                     (byte)SpriteSocketRotationMode.CounterClockwise &&
                     delta < 0f)
                delta += 360f;
            else if (rotationMode ==
                     (byte)SpriteSocketRotationMode.ContinuousTurns)
                delta += 360f * turns;
            return fromAngle + delta * t;
        }
    }
}
