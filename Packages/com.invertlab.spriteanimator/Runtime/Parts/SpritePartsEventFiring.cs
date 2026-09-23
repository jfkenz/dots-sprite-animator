using Unity.Collections;
using Unity.Entities;

namespace InvertLab.Sprites.DOTS
{
    /// <summary>
    /// Parts clip events: each tick, the events whose time the playhead passed go into the character's
    /// <see cref="SpriteAnimEventBuffer"/> (FrameIndex = -1) and <see cref="SpriteAnimEventsPending"/> turns on,
    /// the same as Frame clip events, so <see cref="SpriteAnimEvents.Raised"/> and ECS readers see both.
    /// A span includes its end; it includes its start only at 0 (clip start or loop), so each event fires once.
    /// Loop clips wrap. Reverse playback fires nothing.
    /// </summary>
    public static class SpritePartsEventFiring
    {
        public struct Tick
        {
            public Entity Entity;
            public int ClipIndex;
            public float From;
            public float To;
            public bool Playing;
        }

        /// <summary>Indices (into clip.Events) of the events between <paramref name="from"/> and <paramref name="to"/>.</summary>
        public static void Collect(ref SpritePartsClipBlob clip, float from, float to, NativeList<int> hits)
        {
            const float eps = 1e-6f;
            if (to > from + eps)
                Range(ref clip, from, to, from <= eps, hits);
            else if (to < from - eps && clip.WrapMode != (byte)SpritePartsWrap.Once)
            {
                Range(ref clip, from, clip.Duration, from <= eps, hits);
                Range(ref clip, 0f, to, true, hits);
            }
        }

        static void Range(ref SpritePartsClipBlob clip, float a, float b, bool includeA, NativeList<int> hits)
        {
            for (int i = 0; i < clip.Events.Length; i++)
            {
                float t = clip.Events[i].Time;
                bool afterA = includeA ? t >= a - 1e-6f : t > a + 1e-6f;
                if (afterA && t <= b + 1e-6f)
                    hits.Add(i);
            }
        }

        public static void Fire(EntityManager em, in Tick tick)
        {
            if (!tick.Playing || !em.Exists(tick.Entity) || !em.HasComponent<SpritePartsSetRef>(tick.Entity))
                return;
            var blob = em.GetComponentData<SpritePartsSetRef>(tick.Entity).Set;
            if (!blob.IsCreated || tick.ClipIndex < 0 || tick.ClipIndex >= blob.Value.Clips.Length)
                return;
            ref var clip = ref blob.Value.Clips[tick.ClipIndex];
            var hits = new NativeList<int>(4, Allocator.Temp);
            try
            {
                Collect(ref clip, tick.From, tick.To, hits);
                if (hits.Length == 0)
                    return;
                SpriteAnimEvents.Ensure(em, tick.Entity);
                var bank = em.HasComponent<SpritePartsAudioBank>(tick.Entity)
                    ? em.GetComponentObject<SpritePartsAudioBank>(tick.Entity)
                    : null;
                var buffer = em.GetBuffer<SpriteAnimEventBuffer>(tick.Entity);
                for (int h = 0; h < hits.Length; h++)
                {
                    ref var ev = ref clip.Events[hits[h]];
                    if (bank?.Clips != null && ev.AudioIndex >= 0 && ev.AudioIndex < bank.Clips.Length)
                        SpritePartsAudio.Play(tick.Entity, bank.Clips[ev.AudioIndex], ev.Volume, ev.Balance);
                    buffer.Add(new SpriteAnimEventBuffer
                    {
                        Id = ev.Id,
                        ClipIndex = tick.ClipIndex,
                        FrameIndex = -1, // Parts clips have no frames
                        IntPayload = ev.IntPayload,
                        FloatPayload = ev.FloatPayload,
                        TextHash = ev.TextHash,
                    });
                }
                em.SetComponentEnabled<SpriteAnimEventsPending>(tick.Entity, true);
            }
            finally
            {
                hits.Dispose();
            }
        }
    }
}
