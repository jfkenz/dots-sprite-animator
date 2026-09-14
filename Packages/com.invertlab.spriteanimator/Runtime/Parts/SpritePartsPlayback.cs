using Unity.Mathematics;

namespace InvertLab.Sprites.DOTS
{
    /// <summary>Pure Parts clock helpers shared by systems and tests.</summary>
    public static class SpritePartsPlayback
    {
        public struct TickResult
        {
            public float TimeSeconds;
            public byte Playing;
            public byte CompletedThisTick;
            public byte AlreadyCompleted;
        }

        public static TickResult Tick(
            float timeSeconds, float playerSpeed, float authoredClipSpeed,
            float duration, byte wrapMode, byte playing, byte alreadyCompleted,
            float deltaSeconds)
        {
            var result = new TickResult
            {
                TimeSeconds = timeSeconds,
                Playing = playing,
                AlreadyCompleted = alreadyCompleted,
            };
            if (playing == 0 || !(deltaSeconds > 0f))
                return result;

            float speed = playerSpeed * authoredClipSpeed;
            if (!math.isfinite(speed) || math.abs(speed) <= 1e-8f)
                return result;

            float next = timeSeconds + deltaSeconds * speed;
            bool once = wrapMode == (byte)SpritePartsWrap.Once || wrapMode == SpriteAnimWrap.Once;
            if (once)
            {
                if (speed > 0f && next >= duration)
                {
                    result.TimeSeconds = duration;
                    result.Playing = 0;
                    if (alreadyCompleted == 0)
                    {
                        result.CompletedThisTick = 1;
                        result.AlreadyCompleted = 1;
                    }
                    return result;
                }
                if (speed < 0f && next <= 0f)
                {
                    result.TimeSeconds = 0f;
                    result.Playing = 0;
                    if (alreadyCompleted == 0)
                    {
                        result.CompletedThisTick = 1;
                        result.AlreadyCompleted = 1;
                    }
                    return result;
                }
                result.TimeSeconds = math.clamp(next, 0f, duration);
                return result;
            }

            result.TimeSeconds = SpritePartsSampler.WrapTime(next, duration, (byte)SpritePartsWrap.Loop);
            return result;
        }

        public static float SeekNormalized(float t01, float duration)
            => math.saturate(t01) * math.max(1e-3f, duration);

        public static int FindClipIndexByName(ref SpritePartsSetBlob set, string clipName)
        {
            if (string.IsNullOrEmpty(clipName))
                return -1;
            ulong hash = SpriteAnimSetBuilder.Fnv(clipName);
            for (int i = 0; i < set.Clips.Length; i++)
            {
                if (set.Clips[i].NameHash == hash)
                    return i;
            }
            return -1;
        }

        public static int FindClipIndexById(ref SpritePartsSetBlob set, string clipId)
        {
            if (string.IsNullOrEmpty(clipId))
                return -1;
            string id = SpritePartIdUtility.Canonical(clipId);
            ulong hash = SpritePartIdUtility.Hash(id);
            for (int i = 0; i < set.Clips.Length; i++)
            {
                if (set.Clips[i].ClipIdHash == hash)
                    return i;
            }
            return -1;
        }

        public static int FindAppearanceIndex(ref SpritePartsSetBlob set, string appearanceId)
        {
            if (string.IsNullOrEmpty(appearanceId))
                return -1;
            string id = SpritePartIdUtility.Canonical(appearanceId);
            ulong hash = SpritePartIdUtility.Hash(id);
            for (int i = 0; i < set.Appearances.Length; i++)
            {
                if (set.Appearances[i].AppearanceIdHash == hash)
                    return i;
            }
            return -1;
        }

        public static int FindSkinIndex(ref SpritePartsSetBlob set, string skinId)
        {
            if (string.IsNullOrEmpty(skinId))
                return -1;
            string id = SpritePartIdUtility.Canonical(skinId);
            ulong hash = SpritePartIdUtility.Hash(id);
            for (int i = 0; i < set.SkinPatches.Length; i++)
            {
                if (set.SkinPatches[i].SkinIdHash == hash)
                    return i;
            }
            return -1;
        }

        public static int DrawIndex(int characterOrder, int partRank)
            => characterOrder * 64 + partRank;

        public static float4x4 ScaleMatrix(float sx, float sy)
            => float4x4.Scale(new float3(sx == 0f ? 1f : sx, sy == 0f ? 1f : sy, 1f));

        public static float4x4 FacingMatrix(bool flipX, bool flipY)
            => ScaleMatrix(flipX ? -1f : 1f, flipY ? -1f : 1f);
    }
}
