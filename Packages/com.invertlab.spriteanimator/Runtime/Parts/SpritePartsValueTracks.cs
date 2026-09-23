using System;
using Unity.Collections;
using Unity.Mathematics;

namespace InvertLab.Sprites.DOTS
{
    /// <summary>
    /// Keyed IK Mix / bend, jiggle Mix and parameter values: the playing clip's keys replace the setup values (and a
    /// parameter's gameplay value) and crossfade with the clip. A clip with no key for a value leaves its setup value.
    /// </summary>
    public static class SpritePartsValueTracks
    {
        /// <summary>This frame's constraint and parameter values (one per IK / jiggle / parameter). Temp.</summary>
        public struct Values : IDisposable
        {
            public NativeArray<float> IkMix;
            public NativeArray<float> IkBend;
            public NativeArray<float> JiggleMix;
            public NativeArray<float> Params;

            public bool IsCreated => IkMix.IsCreated;

            public void Dispose()
            {
                if (IkMix.IsCreated) IkMix.Dispose();
                if (IkBend.IsCreated) IkBend.Dispose();
                if (JiggleMix.IsCreated) JiggleMix.Dispose();
                if (Params.IsCreated) Params.Dispose();
            }
        }

        public static bool HasTracks(ref SpritePartsSetBlob set, int clipIndex)
            => clipIndex >= 0 && clipIndex < set.Clips.Length && set.Clips[clipIndex].ValueTracks.Length > 0;

        /// <summary>The track's value at <paramref name="time"/>: held before the first and after the last key.</summary>
        public static float Sample(ref SpritePartsValueTrackBlob track, float time, bool stepped)
        {
            ref var keys = ref track.Keys;
            if (keys.Length == 0)
                return 0f;
            if (time <= keys[0].Time)
                return keys[0].Value;
            bool held = stepped || track.Kind == (byte)SpritePartsValueKind.IkBend;
            for (int i = 0; i + 1 < keys.Length; i++)
            {
                if (time >= keys[i + 1].Time)
                    continue;
                ref var a = ref keys[i];
                if (held || a.EaseMode == (byte)SpriteEaseMode.Step)
                    return a.Value;
                float span = keys[i + 1].Time - a.Time;
                float u = span > 1e-8f ? (time - a.Time) / span : 0f;
                u = a.EaseMode == (byte)SpriteEaseMode.Bezier
                    ? SpriteEase.EvaluateBezier(a.Curve, u)
                    : SpriteEase.Evaluate((SpriteEaseMode)a.EaseMode, u);
                return math.lerp(a.Value, keys[i + 1].Value, u);
            }
            return keys[keys.Length - 1].Value;
        }

        /// <summary>
        /// Values for this frame: setup (and <paramref name="paramValues"/>), then the current and previous clips'
        /// keys crossfaded by the incoming weight. Not created when neither clip keys anything (use the setup values).
        /// </summary>
        public static Values Resolve(ref SpritePartsSetBlob set, in SpritePartsPlayer player, float incoming,
            NativeArray<float> paramValues, bool stepped)
        {
            bool current = HasTracks(ref set, player.ClipIndex);
            bool previous = player.PreviousClipIndex >= 0 && incoming < 1f && HasTracks(ref set, player.PreviousClipIndex);
            if (!current && !previous)
                return default;
            var values = Setup(ref set, paramValues);
            if (!previous)
            {
                Write(ref set, player.ClipIndex, player.TimeSeconds, values, stepped);
                return values;
            }
            var incomingValues = Setup(ref set, paramValues);
            try
            {
                Write(ref set, player.PreviousClipIndex, player.PreviousTimeSeconds, values, stepped);
                if (current)
                    Write(ref set, player.ClipIndex, player.TimeSeconds, incomingValues, stepped);
                Lerp(values.IkMix, incomingValues.IkMix, incoming);
                Lerp(values.JiggleMix, incomingValues.JiggleMix, incoming);
                Lerp(values.Params, incomingValues.Params, incoming);
                if (incoming >= 0.5f)
                    values.IkBend.CopyFrom(incomingValues.IkBend);
            }
            finally
            {
                incomingValues.Dispose();
            }
            return values;
        }

        static Values Setup(ref SpritePartsSetBlob set, NativeArray<float> paramValues)
        {
            var v = new Values
            {
                IkMix = new NativeArray<float>(set.IkConstraints.Length, Allocator.Temp),
                IkBend = new NativeArray<float>(set.IkConstraints.Length, Allocator.Temp),
                JiggleMix = new NativeArray<float>(set.Jiggles.Length, Allocator.Temp),
                Params = new NativeArray<float>(set.Params.Length, Allocator.Temp),
            };
            for (int i = 0; i < set.IkConstraints.Length; i++)
            {
                v.IkMix[i] = set.IkConstraints[i].Mix;
                v.IkBend[i] = set.IkConstraints[i].BendSign;
            }
            for (int i = 0; i < set.Jiggles.Length; i++)
                v.JiggleMix[i] = set.Jiggles[i].Mix;
            for (int i = 0; i < set.Params.Length; i++)
            {
                float value = paramValues.IsCreated && i < paramValues.Length ? paramValues[i] : set.Params[i].Default;
                v.Params[i] = math.isfinite(value) ? value : set.Params[i].Default;
            }
            return v;
        }

        static void Write(ref SpritePartsSetBlob set, int clipIndex, float timeSeconds, Values v, bool stepped)
        {
            if (!HasTracks(ref set, clipIndex))
                return;
            ref var clip = ref set.Clips[clipIndex];
            float time = SpritePartsSampler.WrapTime(timeSeconds, clip.Duration, clip.WrapMode);
            for (int t = 0; t < clip.ValueTracks.Length; t++)
            {
                ref var track = ref clip.ValueTracks[t];
                float value = Sample(ref track, time, stepped);
                for (int k = 0; k < track.Targets.Length; k++)
                {
                    int target = track.Targets[k];
                    switch ((SpritePartsValueKind)track.Kind)
                    {
                        case SpritePartsValueKind.IkMix:
                            if (target < v.IkMix.Length)
                                v.IkMix[target] = math.saturate(value);
                            break;
                        case SpritePartsValueKind.IkBend:
                            if (target < v.IkBend.Length)
                                v.IkBend[target] = value < 0f ? -1f : 1f;
                            break;
                        case SpritePartsValueKind.JiggleMix:
                            if (target < v.JiggleMix.Length)
                                v.JiggleMix[target] = math.saturate(value);
                            break;
                        case SpritePartsValueKind.Param:
                            if (target < v.Params.Length)
                                v.Params[target] = value;
                            break;
                    }
                }
            }
        }

        static void Lerp(NativeArray<float> into, NativeArray<float> to, float t)
        {
            for (int i = 0; i < into.Length; i++)
                into[i] = math.lerp(into[i], to[i], t);
        }
    }
}
