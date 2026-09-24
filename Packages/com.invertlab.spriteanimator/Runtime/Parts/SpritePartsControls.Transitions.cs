using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace InvertLab.Sprites.DOTS
{
    /// <summary>Transitions: queued clips, exit times, blend spaces, layer tracks and fades, whole-character fades.</summary>
    public static partial class SpriteParts
    {
        // ---- Queue ----

        /// <summary>
        /// Plays <paramref name="clipName"/> after the current clip (and anything queued before it) has played once,
        /// plus <paramref name="delay"/> clip seconds (Spine's addAnimation). A negative delay starts the crossfade
        /// before the end. <see cref="Play(EntityManager, Entity, string, bool, float)"/> clears the queue.
        /// </summary>
        public static bool Queue(EntityManager em, Entity e, string clipName, float delay = 0f, float crossfadeSeconds = -1f)
            => TryClip(em, e, clipName, out int index) && Queue(em, e, index, delay, crossfadeSeconds);

        public static bool Queue(EntityManager em, Entity e, int clipIndex, float delay = 0f, float crossfadeSeconds = -1f)
            => Enqueue(em, e, new SpritePartsQueueEntry
            {
                ClipIndex = clipIndex, Delay = delay, Crossfade = crossfadeSeconds, ExitNormalized = 1f, BlendSpace = -1,
            });

        /// <summary>
        /// Plays <paramref name="clipName"/> when the current clip reaches <paramref name="exitNormalized"/> of its length
        /// (Unity's exit time: 0.8 = near the end of the first play, 1.5 = halfway through the second loop).
        /// </summary>
        public static bool PlayAtExit(EntityManager em, Entity e, string clipName, float exitNormalized, float crossfadeSeconds = -1f)
            => TryClip(em, e, clipName, out int index) && PlayAtExit(em, e, index, exitNormalized, crossfadeSeconds);

        public static bool PlayAtExit(EntityManager em, Entity e, int clipIndex, float exitNormalized, float crossfadeSeconds = -1f)
            => Enqueue(em, e, new SpritePartsQueueEntry
            {
                ClipIndex = clipIndex, ExitNormalized = math.max(0f, exitNormalized), Crossfade = crossfadeSeconds, BlendSpace = -1,
            });

        /// <summary>Queues a blend space (see <see cref="PlayBlend"/>) after the current clip.</summary>
        public static bool QueueBlend(EntityManager em, Entity e, string spaceName, float value, float delay = 0f,
            float crossfadeSeconds = -1f)
        {
            if (!TrySet(em, e, out var blob))
                return false;
            int space = SpritePartsTransitions.FindBlendSpace(ref blob.Value, spaceName);
            int clip = SpritePartsTransitions.DominantClip(ref blob.Value, space, value);
            return clip >= 0 && Enqueue(em, e, new SpritePartsQueueEntry
            {
                ClipIndex = clip, Delay = delay, Crossfade = crossfadeSeconds, ExitNormalized = 1f,
                BlendSpace = space, BlendValue = value,
            });
        }

        /// <summary>Clears the clips queued on <paramref name="track"/> (0 = the base clip's queue).</summary>
        public static void ClearQueue(EntityManager em, Entity e, int track = 0)
        {
            if (!em.Exists(e) || !em.HasBuffer<SpritePartsQueueEntry>(e))
                return;
            var queue = em.GetBuffer<SpritePartsQueueEntry>(e);
            for (int i = queue.Length - 1; i >= 0; i--)
                if (queue[i].Track == track)
                    queue.RemoveAt(i);
        }

        /// <summary>
        /// Plays <paramref name="clipName"/> on a layer track after the clip there has played once (plus
        /// <paramref name="delay"/> seconds; negative starts the crossfade early), or now if the track is empty
        /// (Spine's addAnimation on track 1, 2...). Settings are those of <see cref="PlayLayer"/>.
        /// </summary>
        public static bool QueueLayer(EntityManager em, Entity root, int track, string clipName, float delay = 0f,
            float fadeSeconds = -1f, string maskName = null, float endFadeSeconds = 0.2f, bool additive = false)
        {
            if (track <= 0 || !TryClip(em, root, clipName, out int index) || !TrySet(em, root, out var blob))
                return false;
            if (!TryMaskBits(ref blob.Value, maskName, out uint mask))
                return false;
            return Enqueue(em, root, new SpritePartsQueueEntry
            {
                ClipIndex = index, Delay = delay, Crossfade = fadeSeconds, ExitNormalized = 1f, BlendSpace = -1,
                Track = track, SlotMask = mask, EndFade = math.max(0f, endFadeSeconds), Additive = additive ? (byte)1 : (byte)0,
            });
        }

        static bool TryMaskBits(ref SpritePartsSetBlob set, string maskName, out uint bits)
        {
            bits = 0;
            if (string.IsNullOrEmpty(maskName))
                return true;
            int m = SpritePartsTransitions.FindMask(ref set, maskName);
            if (m < 0)
                return false;
            bits = set.Masks[m].Bits;
            return true;
        }

        public static int QueuedCount(EntityManager em, Entity e)
            => em.Exists(e) && em.HasBuffer<SpritePartsQueueEntry>(e) ? em.GetBuffer<SpritePartsQueueEntry>(e).Length : 0;

        static bool Enqueue(EntityManager em, Entity e, SpritePartsQueueEntry entry)
        {
            if (!TrySet(em, e, out var blob) || entry.ClipIndex < 0 || entry.ClipIndex >= blob.Value.Clips.Length)
                return false;
            if (!em.HasBuffer<SpritePartsQueueEntry>(e))
                em.AddBuffer<SpritePartsQueueEntry>(e);
            em.GetBuffer<SpritePartsQueueEntry>(e).Add(entry);
            return true;
        }

        // ---- Blend spaces ----

        /// <summary>
        /// Plays a blend space from the profile (walk / jog / run placed on a value line) at <paramref name="value"/>.
        /// The clips share one cycle, so changing the value with <see cref="SetBlendValue"/> keeps feet in step.
        /// </summary>
        public static bool PlayBlend(EntityManager em, Entity e, string spaceName, float value, bool force = false,
            float crossfadeSeconds = -1f)
        {
            if (!TrySet(em, e, out var blob))
                return false;
            ref var set = ref blob.Value;
            int space = SpritePartsTransitions.FindBlendSpace(ref set, spaceName);
            int clip = SpritePartsTransitions.DominantClip(ref set, space, value);
            if (clip < 0)
                return false;
            if (!force && em.HasComponent<SpritePartsBlendState>(e)
                && em.GetComponentData<SpritePartsBlendState>(e).SpaceIndex == space)
            {
                SetBlendValue(em, e, value);
                return true;
            }
            ClearQueue(em, e);
            StartClip(em, e, clip, crossfadeSeconds, space, value);
            SpritePartsPoseUtility.ApplyPose(em, e);
            return true;
        }

        public static void SetBlendValue(EntityManager em, Entity e, float value)
        {
            if (!em.Exists(e) || !em.HasComponent<SpritePartsBlendState>(e) || !math.isfinite(value))
                return;
            var blend = em.GetComponentData<SpritePartsBlendState>(e);
            blend.Value = value;
            em.SetComponentData(e, blend);
        }

        public static float GetBlendValue(EntityManager em, Entity e)
            => em.Exists(e) && em.HasComponent<SpritePartsBlendState>(e) ? em.GetComponentData<SpritePartsBlendState>(e).Value : 0f;

        static bool BlendActive(EntityManager em, Entity e)
            => em.HasComponent<SpritePartsBlendState>(e) && em.GetComponentData<SpritePartsBlendState>(e).SpaceIndex >= 0;

        // ---- Layers ----

        /// <summary>
        /// Sets a layer by clip name with a named part mask from the profile (null = every part). An additive layer adds
        /// its change from the setup pose (breathing over any clip); an own-clock layer runs its clip on its own time.
        /// </summary>
        public static bool SetLayer(EntityManager em, Entity root, string clipName, float weight, string maskName = null,
            bool additive = false, bool ownClock = false)
            => TryClip(em, root, clipName, out int index) && SetLayer(em, root, index, weight, maskName, additive, ownClock);

        public static bool SetLayer(EntityManager em, Entity root, int clipIndex, float weight, string maskName,
            bool additive = false, bool ownClock = false)
        {
            if (!TrySet(em, root, out var blob))
                return false;
            uint mask = 0;
            if (!string.IsNullOrEmpty(maskName))
            {
                int m = SpritePartsTransitions.FindMask(ref blob.Value, maskName);
                if (m < 0)
                    return false;
                mask = blob.Value.Masks[m].Bits;
            }
            SpritePartsPoseWriter.EnsureBuffers(em, root);
            var buf = em.GetBuffer<SpritePartsAnimLayer>(root);
            int i = LayerIndex(buf, clipIndex);
            var layer = i >= 0 ? buf[i] : new SpritePartsAnimLayer { ClipIndex = clipIndex };
            layer.Weight = math.saturate(weight);
            layer.TargetWeight = layer.Weight;
            layer.FadeSpeed = 0f;
            layer.SlotMask = mask;
            layer.Additive = additive ? (byte)1 : (byte)0;
            layer.OwnClock = ownClock ? (byte)1 : (byte)0;
            if (i >= 0)
                buf[i] = layer;
            else
                buf.Add(layer);
            return true;
        }

        /// <summary>
        /// Fades a layer's weight to <paramref name="targetWeight"/> over <paramref name="seconds"/>. A missing layer is
        /// added at 0 first (so it fades in); with <paramref name="removeAtZero"/> a fade to 0 removes it at the end.
        /// </summary>
        public static bool FadeLayer(EntityManager em, Entity root, string clipName, float targetWeight, float seconds,
            bool removeAtZero = true)
            => TryClip(em, root, clipName, out int index) && FadeLayer(em, root, index, targetWeight, seconds, removeAtZero);

        public static bool FadeLayer(EntityManager em, Entity root, int clipIndex, float targetWeight, float seconds,
            bool removeAtZero = true)
        {
            if (!TrySet(em, root, out var blob) || clipIndex < 0 || clipIndex >= blob.Value.Clips.Length)
                return false;
            SpritePartsPoseWriter.EnsureBuffers(em, root);
            var buf = em.GetBuffer<SpritePartsAnimLayer>(root);
            int i = LayerIndex(buf, clipIndex);
            var layer = i >= 0 ? buf[i] : new SpritePartsAnimLayer { ClipIndex = clipIndex };
            layer.TargetWeight = math.saturate(targetWeight);
            layer.RemoveAtZero = removeAtZero ? (byte)1 : (byte)0;
            float distance = math.abs(layer.TargetWeight - layer.Weight);
            if (!(seconds > 1e-6f) || distance <= 1e-6f)
            {
                layer.Weight = layer.TargetWeight;
                layer.FadeSpeed = 0f;
            }
            else
                layer.FadeSpeed = distance / seconds;
            if (layer.FadeSpeed <= 0f && layer.Weight <= 1e-6f && removeAtZero)
            {
                if (i >= 0)
                    buf.RemoveAt(i);
                return true;
            }
            if (i >= 0)
                buf[i] = layer;
            else
                buf.Add(layer);
            return true;
        }

        /// <summary>
        /// Plays a clip on a layer track (Spine's tracks 1, 2...), on its own clock from its start: only the parts it keys
        /// follow it (inside the mask, if one is named), so "Shoot" on track 1 runs over "Run" on the base.
        /// A clip already on the track crossfades out: <paramref name="fadeSeconds"/>, or the mix table when negative.
        /// A clip that plays once fades out over <paramref name="endFadeSeconds"/> at its end (0 = hold its last pose).
        /// </summary>
        public static bool PlayLayer(EntityManager em, Entity root, int track, string clipName, string maskName = null,
            float fadeSeconds = -1f, float endFadeSeconds = 0.2f, bool additive = false)
        {
            if (track <= 0 || !TryClip(em, root, clipName, out int index) || !TrySet(em, root, out var blob))
                return false;
            if (!TryMaskBits(ref blob.Value, maskName, out uint mask))
                return false;
            ClearQueue(em, root, track);
            return StartLayer(em, root, track, index, mask, fadeSeconds, endFadeSeconds, additive);
        }

        static bool StartLayer(EntityManager em, Entity root, int track, int index, uint mask, float fadeSeconds,
            float endFadeSeconds, bool additive)
        {
            if (!TrySet(em, root, out var blob) || index < 0 || index >= blob.Value.Clips.Length)
                return false;
            ref var set = ref blob.Value;
            SpritePartsPoseWriter.EnsureBuffers(em, root);
            var buf = em.GetBuffer<SpritePartsAnimLayer>(root);
            int previous = -1;
            for (int i = 0; i < buf.Length; i++)
                if (buf[i].Track == track && buf[i].ClipIndex != index && buf[i].TargetWeight > 0f)
                    previous = buf[i].ClipIndex;
            SpritePartsTransitions.ResolveMix(ref set, previous, index, fadeSeconds, out float fade, out _);
            // The clip leaving the track fades out (and goes at 0).
            for (int i = buf.Length - 1; i >= 0; i--)
            {
                var old = buf[i];
                if (old.Track != track || old.ClipIndex == index)
                    continue;
                if (!(fade > 1e-6f))
                {
                    buf.RemoveAt(i);
                    continue;
                }
                old.TargetWeight = 0f;
                old.FadeSpeed = math.max(old.Weight, 1e-3f) / fade;
                old.RemoveAtZero = 1;
                buf[i] = old;
            }
            int at = LayerIndex(buf, index);
            var layer = at >= 0 ? buf[at] : new SpritePartsAnimLayer { ClipIndex = index };
            layer.Track = track;
            layer.SlotMask = mask;
            layer.Additive = additive ? (byte)1 : (byte)0;
            layer.OwnClock = 1;
            layer.Time = 0f;
            layer.Played = 0f;
            layer.EndFade = math.max(0f, endFadeSeconds);
            layer.RemoveAtZero = 1;
            layer.TargetWeight = 1f;
            if (fade > 1e-6f && layer.Weight < 1f)
                layer.FadeSpeed = (1f - layer.Weight) / fade;
            else
            {
                layer.Weight = 1f;
                layer.FadeSpeed = 0f;
            }
            // The incoming clip goes last so it blends over the one leaving the track.
            if (at >= 0)
                buf.RemoveAt(at);
            buf.Add(layer);
            return true;
        }

        /// <summary>Fades out whatever plays on a layer track (removed at 0).</summary>
        public static void StopLayer(EntityManager em, Entity root, int track, float fadeSeconds = 0.2f)
        {
            if (!IsPartsRoot(em, root) || !em.HasBuffer<SpritePartsAnimLayer>(root))
                return;
            var buf = em.GetBuffer<SpritePartsAnimLayer>(root);
            for (int i = buf.Length - 1; i >= 0; i--)
            {
                var layer = buf[i];
                if (layer.Track != track)
                    continue;
                if (!(fadeSeconds > 1e-6f))
                {
                    buf.RemoveAt(i);
                    continue;
                }
                layer.TargetWeight = 0f;
                layer.FadeSpeed = math.max(layer.Weight, 1e-3f) / fadeSeconds;
                layer.RemoveAtZero = 1;
                buf[i] = layer;
            }
        }

        /// <summary>The part bits of a named mask from the profile (0 = no such mask).</summary>
        public static uint MaskBits(EntityManager em, Entity root, string maskName)
        {
            if (!TrySet(em, root, out var blob))
                return 0;
            int m = SpritePartsTransitions.FindMask(ref blob.Value, maskName);
            return m >= 0 ? blob.Value.Masks[m].Bits : 0u;
        }

        static int LayerIndex(DynamicBuffer<SpritePartsAnimLayer> buf, int clipIndex)
        {
            for (int i = 0; i < buf.Length; i++)
                if (buf[i].ClipIndex == clipIndex)
                    return i;
            return -1;
        }

        // ---- Whole-character colour ----

        /// <summary>Fades the whole character's alpha (every part together) to <paramref name="alpha"/>.</summary>
        public static void FadeCharacter(EntityManager em, Entity root, float alpha, float seconds,
            SpriteEaseMode ease = SpriteEaseMode.Linear)
        {
            var current = GetCharacterTint(em, root);
            TintCharacter(em, root, new float4(current.xyz, math.saturate(alpha)), seconds, ease);
        }

        /// <summary>Tints the whole character (multiplied over keys and part tints), optionally over time.</summary>
        public static void TintCharacter(EntityManager em, Entity root, float4 color, float seconds = 0f,
            SpriteEaseMode ease = SpriteEaseMode.Linear)
        {
            if (!IsPartsRoot(em, root))
                return;
            var tint = new SpritePartsCharacterTint
            {
                From = GetCharacterTint(em, root),
                To = color,
                Duration = math.max(0f, seconds),
                Ease = (byte)ease,
            };
            SpritePartsTransitions.TickTint(ref tint, 0f);
            if (em.HasComponent<SpritePartsCharacterTint>(root))
                em.SetComponentData(root, tint);
            else
                em.AddComponentData(root, tint);
        }

        public static float4 GetCharacterTint(EntityManager em, Entity root)
            => em.Exists(root) && em.HasComponent<SpritePartsCharacterTint>(root)
                ? em.GetComponentData<SpritePartsCharacterTint>(root).Value
                : new float4(1f);

        // ---- Shared ----

        static bool TrySet(EntityManager em, Entity e, out BlobAssetReference<SpritePartsSetBlob> blob)
        {
            blob = default;
            if (!IsPartsRoot(em, e))
                return false;
            blob = em.GetComponentData<SpritePartsSetRef>(e).Set;
            return blob.IsCreated;
        }

        static bool TryClip(EntityManager em, Entity e, string clipName, out int index)
        {
            index = -1;
            if (!TrySet(em, e, out var blob))
                return false;
            index = SpritePartsPlayback.FindClipIndexByName(ref blob.Value, clipName);
            return index >= 0;
        }

        /// <summary>Starts a clip (or blend space) with the mix table's crossfade; no pose write.</summary>
        internal static void StartClip(EntityManager em, Entity e, int clipIndex, float crossfadeSeconds,
            int blendSpace, float blendValue)
        {
            if (!TrySet(em, e, out var blob))
                return;
            SpritePartsPoseWriter.EnsureBuffers(em, e);
            bool hasBlend = em.HasComponent<SpritePartsBlendState>(e);
            if (!hasBlend && blendSpace >= 0)
            {
                em.AddComponentData(e, SpritePartsBlendState.None);
                hasBlend = true;
            }
            if (em.HasComponent<SpritePartsCompleted>(e))
                em.RemoveComponent<SpritePartsCompleted>(e);
            var player = em.GetComponentData<SpritePartsPlayer>(e);
            var blend = hasBlend ? em.GetComponentData<SpritePartsBlendState>(e) : SpritePartsBlendState.None;
            var chain = em.GetBuffer<SpritePartsMixEntry>(e);
            SpritePartsTransitions.Start(ref blob.Value, ref player, chain, true, ref blend, clipIndex, crossfadeSeconds,
                blendSpace, blendValue);
            em.SetComponentData(e, player);
            if (hasBlend)
                em.SetComponentData(e, blend);
        }

        static void ClearFades(EntityManager em, Entity e, ref SpritePartsPlayer player)
        {
            player.PreviousClipIndex = -1;
            player.PreviousTimeSeconds = 0f;
            player.BlendDuration = 0f;
            player.BlendElapsed = 0f;
            player.PreviousBlendDuration = 0f;
            player.PreviousBlendElapsed = 0f;
            if (em.HasBuffer<SpritePartsMixEntry>(e))
                em.GetBuffer<SpritePartsMixEntry>(e).Clear();
            if (em.HasComponent<SpritePartsBlendState>(e))
            {
                var blend = em.GetComponentData<SpritePartsBlendState>(e);
                blend.PreviousSpaceIndex = -1;
                blend.Phase = 0f;
                em.SetComponentData(e, blend);
            }
        }

        /// <summary>
        /// Per-frame transition work after the clocks tick: older crossfade clips, layer fades and clocks, the
        /// character tint, then the queue (which may start the next clip). Main thread; may change structure.
        /// </summary>
        internal static void TickTransitions(EntityManager em, Entity root, float dt)
        {
            if (!TrySet(em, root, out var blob))
                return;
            ref var set = ref blob.Value;
            var player = em.GetComponentData<SpritePartsPlayer>(root);
            bool paused = SpritePartsPoseWriter.IsPaused(player);
            if (em.HasBuffer<SpritePartsMixEntry>(root))
                SpritePartsTransitions.TickChain(ref set, player, em.GetBuffer<SpritePartsMixEntry>(root), dt);
            if (em.HasBuffer<SpritePartsAnimLayer>(root))
            {
                // Own-clock layers fire their clips' events too (after the buffer is done with: firing may add components).
                var layerEvents = new NativeList<SpritePartsEventFiring.Tick>(2, Allocator.Temp);
                SpritePartsTransitions.TickLayers(ref set, em.GetBuffer<SpritePartsAnimLayer>(root), dt, paused, root, layerEvents);
                for (int i = 0; i < layerEvents.Length; i++)
                    SpritePartsEventFiring.Fire(em, layerEvents[i]);
                layerEvents.Dispose();
            }
            if (em.HasComponent<SpritePartsCharacterTint>(root))
            {
                var tint = em.GetComponentData<SpritePartsCharacterTint>(root);
                SpritePartsTransitions.TickTint(ref tint, dt);
                em.SetComponentData(root, tint);
            }
            if (paused || !em.HasBuffer<SpritePartsQueueEntry>(root))
                return;
            // The first entry of each track (0 = base) waits on what plays there; due ones start after the scan.
            var queue = em.GetBuffer<SpritePartsQueueEntry>(root);
            if (queue.Length == 0)
                return;
            bool hasLayers = em.HasBuffer<SpritePartsAnimLayer>(root);
            var layers = hasLayers ? em.GetBuffer<SpritePartsAnimLayer>(root) : default;
            var seen = new NativeList<int>(4, Allocator.Temp);
            var due = new NativeList<SpritePartsQueueEntry>(2, Allocator.Temp);
            try
            {
                for (int q = 0; q < queue.Length; q++)
                {
                    var entry = queue[q];
                    if (seen.Contains(entry.Track))
                        continue;
                    seen.Add(entry.Track);
                    bool ready = entry.Track == 0
                        ? SpritePartsTransitions.QueueDue(ref set, player, entry)
                        : SpritePartsTransitions.TrackQueueDue(ref set, layers, hasLayers, entry);
                    if (!ready)
                        continue;
                    due.Add(entry);
                    queue.RemoveAt(q);
                    q--;
                }
                for (int i = 0; i < due.Length; i++)
                {
                    var next = due[i];
                    if (next.ClipIndex < 0 || next.ClipIndex >= set.Clips.Length)
                        continue;
                    if (next.Track == 0)
                        StartClip(em, root, next.ClipIndex, next.Crossfade, next.BlendSpace, next.BlendValue);
                    else
                        StartLayer(em, root, next.Track, next.ClipIndex, next.SlotMask, next.Crossfade, next.EndFade, next.Additive != 0);
                }
            }
            finally
            {
                seen.Dispose();
                due.Dispose();
            }
        }
    }
}
