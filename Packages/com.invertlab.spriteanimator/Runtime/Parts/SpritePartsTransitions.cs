using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace InvertLab.Sprites.DOTS
{
    /// <summary>
    /// Clips older than <see cref="SpritePartsPlayer.PreviousClipIndex"/> that are still visible because a new
    /// crossfade began before the last one ended (Spine's mixing-from chain). Entry 0 is the fully weighted base;
    /// each later entry fades in over the ones before it.
    /// </summary>
    [InternalBufferCapacity(3)]
    public struct SpritePartsMixEntry : IBufferElementData
    {
        public int ClipIndex;
        public float Time;
        public float Duration;
        public float Elapsed;
        public byte Ease;
    }

    /// <summary>
    /// Clips waiting to play (Spine's addAnimation). Entry 0 starts when the current clip has played
    /// <see cref="ExitNormalized"/> of its length plus <see cref="Delay"/> seconds, then the next entry waits on it.
    /// </summary>
    [InternalBufferCapacity(2)]
    public struct SpritePartsQueueEntry : IBufferElementData
    {
        public int ClipIndex;
        /// <summary>Clip seconds after the exit point (negative starts the fade before it).</summary>
        public float Delay;
        /// <summary>-1 = the mix table.</summary>
        public float Crossfade;
        /// <summary>1 = after one full play; 0.5 = halfway; 2.5 = halfway through the third loop.</summary>
        public float ExitNormalized;
        /// <summary>Blend space to play instead of a clip (-1 = none), at <see cref="BlendValue"/>.</summary>
        public int BlendSpace;
        public float BlendValue;
    }

    /// <summary>Colour and alpha over the whole character (every part), on the root. Keys and part tints stay.</summary>
    public struct SpritePartsCharacterTint : IComponentData
    {
        public float4 Value;
        public float4 From;
        public float4 To;
        public float Elapsed;
        public float Duration;
        public byte Ease;
    }

    /// <summary>
    /// Blend by value (a 1D blend space: walk -&gt; jog -&gt; run by speed). The clips share one normalized cycle so
    /// their feet stay in step. The player's clip follows the heaviest point, for events and draw order.
    /// </summary>
    public struct SpritePartsBlendState : IComponentData
    {
        /// <summary>-1 = no blend space playing.</summary>
        public int SpaceIndex;
        public float Value;
        public float Phase;
        /// <summary>The blend space fading out under the current clip (-1 = none).</summary>
        public int PreviousSpaceIndex;
        public float PreviousValue;
        public float PreviousPhase;

        public static SpritePartsBlendState None => new SpritePartsBlendState { SpaceIndex = -1, PreviousSpaceIndex = -1 };
    }

    /// <summary>Crossfade, queue, layer-fade, character-fade and blend-space helpers shared by systems, API and tests.</summary>
    public static class SpritePartsTransitions
    {
        /// <summary>Most older clips kept under a chained crossfade.</summary>
        public const int MaxChain = 3;

        /// <summary>How far in a fade is (0..1), eased.</summary>
        public static float Weight(float elapsed, float duration, byte ease)
        {
            if (!(duration > 1e-8f))
                return 1f;
            float t = math.saturate(elapsed / duration);
            if (ease == (byte)SpriteEaseMode.Linear || !SpriteEase.IsValidMode(ease) || ease == (byte)SpriteEaseMode.Bezier)
                return t;
            return math.saturate(SpriteEase.Evaluate((SpriteEaseMode)ease, t));
        }

        /// <summary>
        /// Crossfade from <paramref name="from"/> into <paramref name="to"/>: the exact pair, else "any clip" into
        /// <paramref name="to"/>, else the default. False when the default was used.
        /// </summary>
        public static bool FindMix(ref SpritePartsSetBlob set, int from, int to, out float duration, out byte ease)
        {
            int any = -1;
            for (int i = 0; i < set.Mixes.Length; i++)
            {
                ref var m = ref set.Mixes[i];
                if (m.To != to)
                    continue;
                if (m.From == from && from >= 0)
                {
                    duration = m.Duration;
                    ease = m.Ease;
                    return true;
                }
                if (m.From < 0 && any < 0)
                    any = i;
            }
            if (any >= 0)
            {
                duration = set.Mixes[any].Duration;
                ease = set.Mixes[any].Ease;
                return true;
            }
            duration = set.DefaultMix;
            ease = set.DefaultMixEase;
            return false;
        }

        /// <summary>Negative <paramref name="requested"/> = the mix table; otherwise that many seconds with the table's ease.</summary>
        public static void ResolveMix(ref SpritePartsSetBlob set, int from, int to, float requested,
            out float duration, out byte ease)
        {
            FindMix(ref set, from, to, out duration, out ease);
            if (requested >= 0f && math.isfinite(requested))
                duration = requested;
            duration = math.max(0f, duration);
        }

        public static int FindMask(ref SpritePartsSetBlob set, string name)
        {
            if (string.IsNullOrEmpty(name))
                return -1;
            var key = new FixedString64Bytes(name);
            for (int i = 0; i < set.Masks.Length; i++)
                if (set.Masks[i].Name.Equals(key))
                    return i;
            return -1;
        }

        public static int FindBlendSpace(ref SpritePartsSetBlob set, string name)
        {
            if (string.IsNullOrEmpty(name))
                return -1;
            var key = new FixedString64Bytes(name);
            for (int i = 0; i < set.BlendSpaces.Length; i++)
                if (set.BlendSpaces[i].Name.Equals(key))
                    return i;
            return -1;
        }

        // ---- Blend spaces ----

        /// <summary>The two points around <paramref name="value"/> and the weight of <paramref name="b"/> (clamped at the ends).</summary>
        public static bool BlendPoints(ref SpritePartsSetBlob set, int space, float value, out int a, out int b, out float w)
        {
            a = b = -1;
            w = 0f;
            if (space < 0 || space >= set.BlendSpaces.Length)
                return false;
            ref var pts = ref set.BlendSpaces[space].Points;
            if (pts.Length == 0)
                return false;
            if (pts.Length == 1 || value <= pts[0].Value)
            {
                a = b = 0;
                return true;
            }
            if (value >= pts[pts.Length - 1].Value)
            {
                a = b = pts.Length - 1;
                return true;
            }
            for (int i = 0; i + 1 < pts.Length; i++)
            {
                if (value <= pts[i + 1].Value)
                {
                    a = i;
                    b = i + 1;
                    float span = pts[b].Value - pts[a].Value;
                    w = span > 1e-8f ? math.saturate((value - pts[a].Value) / span) : 1f;
                    return true;
                }
            }
            a = b = pts.Length - 1;
            return true;
        }

        /// <summary>The clip with the most weight at <paramref name="value"/> (-1 when the space is empty).</summary>
        public static int DominantClip(ref SpritePartsSetBlob set, int space, float value)
        {
            if (!BlendPoints(ref set, space, value, out int a, out int b, out float w))
                return -1;
            ref var pts = ref set.BlendSpaces[space].Points;
            return w >= 0.5f ? pts[b].ClipIndex : pts[a].ClipIndex;
        }

        /// <summary>Cycles per clip-second at <paramref name="value"/>: the lengths blend, so steps stay in sync.</summary>
        public static float PhaseRate(ref SpritePartsSetBlob set, int space, float value)
        {
            if (!BlendPoints(ref set, space, value, out int a, out int b, out float w))
                return 0f;
            ref var pts = ref set.BlendSpaces[space].Points;
            ref var ca = ref set.Clips[pts[a].ClipIndex];
            ref var cb = ref set.Clips[pts[b].ClipIndex];
            float ra = math.abs(ca.SpeedMultiplier) / math.max(1e-3f, ca.Duration);
            float rb = math.abs(cb.SpeedMultiplier) / math.max(1e-3f, cb.Duration);
            return math.lerp(ra, rb, w);
        }

        public static void SampleBlend(ref SpritePartsSetBlob set, int space, float value, float phase, int slot,
            bool stepped, out SpritePartsSampler.Pose pose)
        {
            if (!BlendPoints(ref set, space, value, out int a, out int b, out float w))
            {
                SpritePartsSampler.SampleSlot(ref set, -1, slot, 0f, true, stepped, out pose);
                return;
            }
            ref var pts = ref set.BlendSpaces[space].Points;
            int clipA = pts[a].ClipIndex, clipB = pts[b].ClipIndex;
            SpritePartsSampler.SampleSlot(ref set, clipA, slot, phase * set.Clips[clipA].Duration, true, stepped, out pose);
            if (a == b || w <= 1e-6f)
                return;
            SpritePartsSampler.SampleSlot(ref set, clipB, slot, phase * set.Clips[clipB].Duration, true, stepped, out var other);
            pose = Blend(pose, other, w);
        }

        public struct BlendTick
        {
            public int ClipIndex;
            public float From;
            public float To;
        }

        /// <summary>
        /// Advances the shared cycle (and a fading-out one), points the player at the heaviest clip at the matching
        /// time, and returns the event span in that clip's seconds.
        /// </summary>
        public static BlendTick TickBlend(ref SpritePartsSetBlob set, ref SpritePartsBlendState blend,
            ref SpritePartsPlayer player, float dt)
        {
            var result = new BlendTick { ClipIndex = -1 };
            bool paused = SpritePartsPoseWriter.IsPaused(player);
            float speed = math.isfinite(player.SpeedMultiplier) ? math.abs(player.SpeedMultiplier) : 0f;
            float step = paused ? 0f : math.max(0f, dt) * speed;
            if (blend.PreviousSpaceIndex >= 0)
            {
                if (player.PreviousClipIndex < 0)
                    blend.PreviousSpaceIndex = -1;
                else
                    blend.PreviousPhase = math.frac(blend.PreviousPhase
                        + step * PhaseRate(ref set, blend.PreviousSpaceIndex, blend.PreviousValue));
            }
            if (blend.SpaceIndex < 0 || blend.SpaceIndex >= set.BlendSpaces.Length)
                return result;
            float from = blend.Phase;
            blend.Phase = math.frac(blend.Phase + step * PhaseRate(ref set, blend.SpaceIndex, blend.Value));
            int clip = DominantClip(ref set, blend.SpaceIndex, blend.Value);
            if (clip < 0)
                return result;
            float duration = set.Clips[clip].Duration;
            player.ClipIndex = clip;
            player.TimeSeconds = blend.Phase * duration;
            if (!paused)
            {
                player.Playing = 1;
                player.Completed = 0;
            }
            result.ClipIndex = clip;
            result.From = from * duration;
            result.To = player.TimeSeconds;
            return result;
        }

        // ---- Crossfade chain ----

        /// <summary>
        /// Starts <paramref name="clipIndex"/> on <paramref name="player"/>. A crossfade that begins mid-fade keeps the
        /// older clips in <paramref name="chain"/> so nothing pops. <paramref name="blend"/> gets the blend space when
        /// <paramref name="blendSpace"/> is set, and hands a playing one over to the fading-out side.
        /// </summary>
        public static void Start(ref SpritePartsSetBlob set, ref SpritePartsPlayer player,
            DynamicBuffer<SpritePartsMixEntry> chain, bool hasChain, ref SpritePartsBlendState blend,
            int clipIndex, float crossfadeSeconds, int blendSpace = -1, float blendValue = 0f)
        {
            ResolveMix(ref set, player.ClipIndex, clipIndex, crossfadeSeconds, out float fade, out byte ease);
            bool fromBlend = blend.SpaceIndex >= 0;
            bool toBlend = blendSpace >= 0;
            bool fading = fade > 1e-8f && player.ClipIndex >= 0
                && (player.ClipIndex != clipIndex || fromBlend || toBlend);
            if (fading)
            {
                bool midFade = player.PreviousClipIndex >= 0 && player.BlendDuration > 1e-8f
                    && player.BlendElapsed < player.BlendDuration;
                if (midFade && hasChain)
                {
                    chain.Add(new SpritePartsMixEntry
                    {
                        ClipIndex = player.PreviousClipIndex,
                        Time = player.PreviousTimeSeconds,
                        Duration = player.PreviousBlendDuration,
                        Elapsed = player.PreviousBlendElapsed,
                        Ease = player.PreviousBlendEase,
                    });
                    while (chain.Length > MaxChain)
                        chain.RemoveAt(0);
                    player.PreviousBlendDuration = player.BlendDuration;
                    player.PreviousBlendElapsed = player.BlendElapsed;
                    player.PreviousBlendEase = player.BlendEase;
                }
                else
                {
                    if (hasChain)
                        chain.Clear();
                    player.PreviousBlendDuration = 0f;
                    player.PreviousBlendElapsed = 0f;
                    player.PreviousBlendEase = 0;
                }
                player.PreviousClipIndex = player.ClipIndex;
                player.PreviousTimeSeconds = player.TimeSeconds;
                player.BlendDuration = fade;
                player.BlendElapsed = 0f;
                player.BlendEase = ease;
                // The blend space (if one was playing) keeps playing on the way out.
                blend.PreviousSpaceIndex = fromBlend ? blend.SpaceIndex : -1;
                blend.PreviousValue = blend.Value;
                blend.PreviousPhase = blend.Phase;
            }
            else
            {
                if (hasChain)
                    chain.Clear();
                player.PreviousClipIndex = -1;
                player.PreviousTimeSeconds = 0f;
                player.BlendDuration = 0f;
                player.BlendElapsed = 0f;
                player.BlendEase = 0;
                player.PreviousBlendDuration = 0f;
                player.PreviousBlendElapsed = 0f;
                player.PreviousBlendEase = 0;
                blend.PreviousSpaceIndex = -1;
            }

            blend.SpaceIndex = toBlend ? blendSpace : -1;
            if (toBlend)
            {
                blend.Value = blendValue;
                blend.Phase = 0f;
            }
            player.ClipIndex = clipIndex;
            player.TimeSeconds = 0f;
            player.PlayedSeconds = 0f;
            player.Playing = 1;
            player.Completed = 0;
            player.Paused = 0;
            if (!(player.SpeedMultiplier > 0f) && player.SpeedMultiplier == 0f)
                player.SpeedMultiplier = 1f;
            if (!math.isfinite(player.SpeedMultiplier))
                player.SpeedMultiplier = 1f;
        }

        /// <summary>Advances the older clips and drops the ones fully covered.</summary>
        public static void TickChain(ref SpritePartsSetBlob set, in SpritePartsPlayer player,
            DynamicBuffer<SpritePartsMixEntry> chain, float dt)
        {
            if (chain.Length == 0)
                return;
            // Nothing is under the current clip any more, or the previous clip is fully in: the chain is hidden.
            if (player.PreviousClipIndex < 0 || !(player.PreviousBlendDuration > 1e-8f)
                || player.PreviousBlendElapsed >= player.PreviousBlendDuration)
            {
                chain.Clear();
                return;
            }
            if (SpritePartsPoseWriter.IsPaused(player))
                return;
            dt = math.max(0f, dt);
            for (int k = 0; k < chain.Length; k++)
            {
                var e = chain[k];
                if (e.ClipIndex >= 0 && e.ClipIndex < set.Clips.Length)
                {
                    ref var clip = ref set.Clips[e.ClipIndex];
                    var tick = SpritePartsPlayback.Tick(e.Time, player.SpeedMultiplier, clip.SpeedMultiplier,
                        clip.Duration, clip.WrapMode, 1, 0, dt);
                    e.Time = tick.TimeSeconds;
                }
                e.Elapsed += dt;
                chain[k] = e;
            }
            // An entry that is fully in hides everything under it.
            for (int k = chain.Length - 1; k >= 1; k--)
            {
                if (chain[k].Elapsed >= chain[k].Duration)
                {
                    chain.RemoveRange(0, k);
                    break;
                }
            }
        }

        /// <summary>The pose of the chain and previous clip under the current one, for one part.</summary>
        public static SpritePartsSampler.Pose SampleOutgoing(ref SpritePartsSetBlob set, in SpritePartsPlayer player,
            NativeArray<SpritePartsMixEntry> chain, in SpritePartsBlendState blend, bool hasBlend, int slot, bool stepped)
        {
            SpritePartsSampler.Pose prev;
            if (hasBlend && blend.PreviousSpaceIndex >= 0)
                SampleBlend(ref set, blend.PreviousSpaceIndex, blend.PreviousValue, blend.PreviousPhase, slot, stepped, out prev);
            else
                SpritePartsSampler.SampleSlot(ref set, player.PreviousClipIndex, slot, player.PreviousTimeSeconds, true, stepped, out prev);
            if (!chain.IsCreated || chain.Length == 0)
                return prev;
            float prevWeight = Weight(player.PreviousBlendElapsed, player.PreviousBlendDuration, player.PreviousBlendEase);
            if (prevWeight >= 1f)
                return prev;
            SpritePartsSampler.SampleSlot(ref set, chain[0].ClipIndex, slot, chain[0].Time, true, stepped, out var under);
            for (int k = 1; k < chain.Length; k++)
            {
                SpritePartsSampler.SampleSlot(ref set, chain[k].ClipIndex, slot, chain[k].Time, true, stepped, out var over);
                under = Blend(under, over, Weight(chain[k].Elapsed, chain[k].Duration, chain[k].Ease));
            }
            return Blend(under, prev, prevWeight);
        }

        internal static SpritePartsSampler.Pose Blend(in SpritePartsSampler.Pose from, in SpritePartsSampler.Pose to, float t)
            => new SpritePartsSampler.Pose
            {
                Position = math.lerp(from.Position, to.Position, t),
                Scale = math.lerp(from.Scale, to.Scale, t),
                Rotation = SpritePartsSampler.LerpAngleShortest(from.Rotation, to.Rotation, t),
                Lattice = SpritePartsLattice.Lerp(from.Lattice, to.Lattice, t),
            };

        // ---- Queue ----

        /// <summary>True when the head of the queue is due on this player.</summary>
        public static bool QueueDue(ref SpritePartsSetBlob set, in SpritePartsPlayer player, in SpritePartsQueueEntry next)
        {
            if (player.ClipIndex < 0 || player.ClipIndex >= set.Clips.Length)
                return true;
            float duration = set.Clips[player.ClipIndex].Duration;
            return player.PlayedSeconds >= next.ExitNormalized * duration + next.Delay - 1e-5f;
        }

        // ---- Layers ----

        /// <summary>Fades layer weights and runs own-clock layers. Removes layers whose fade to 0 ended with RemoveAtZero.</summary>
        public static void TickLayers(ref SpritePartsSetBlob set, DynamicBuffer<SpritePartsAnimLayer> layers, float dt, bool paused)
        {
            dt = math.max(0f, dt);
            for (int L = layers.Length - 1; L >= 0; L--)
            {
                var layer = layers[L];
                if (layer.FadeSpeed > 0f)
                {
                    float d = layer.TargetWeight - layer.Weight;
                    float stepW = layer.FadeSpeed * dt;
                    layer.Weight = math.abs(d) <= stepW ? layer.TargetWeight : layer.Weight + math.sign(d) * stepW;
                    if (layer.Weight == layer.TargetWeight)
                    {
                        layer.FadeSpeed = 0f;
                        if (layer.TargetWeight <= 1e-6f && layer.RemoveAtZero != 0)
                        {
                            layers.RemoveAt(L);
                            continue;
                        }
                    }
                }
                if (layer.OwnClock != 0 && !paused && layer.ClipIndex >= 0 && layer.ClipIndex < set.Clips.Length)
                {
                    ref var clip = ref set.Clips[layer.ClipIndex];
                    layer.Time = SpritePartsPlayback.Tick(layer.Time, 1f, clip.SpeedMultiplier, clip.Duration,
                        clip.WrapMode, 1, 0, dt).TimeSeconds;
                }
                layers[L] = layer;
            }
        }

        // ---- Character tint ----

        public static void TickTint(ref SpritePartsCharacterTint tint, float dt)
        {
            if (!(tint.Duration > 1e-8f) || tint.Elapsed >= tint.Duration)
            {
                tint.Value = tint.To;
                return;
            }
            tint.Elapsed = math.min(tint.Duration, tint.Elapsed + math.max(0f, dt));
            tint.Value = math.lerp(tint.From, tint.To, Weight(tint.Elapsed, tint.Duration, tint.Ease));
        }
    }
}
