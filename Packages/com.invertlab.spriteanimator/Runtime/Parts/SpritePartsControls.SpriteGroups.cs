using Unity.Collections;
using Unity.Entities;

namespace InvertLab.Sprites.DOTS
{
    /// <summary>A sprite group's state set by gameplay, one per group (-1 = the clip decides). On the character root.</summary>
    [InternalBufferCapacity(2)]
    public struct SpritePartsSpriteGroupState : IBufferElementData
    {
        public int State;
    }

    /// <summary>
    /// Auto blink on a sprite group: every few seconds (random) its blink state shows for a moment, unless gameplay
    /// has set the group. Baked from the group's Auto Blink setting or added with SpriteParts.EnableAutoBlink.
    /// </summary>
    public struct SpritePartsAutoBlink : IComponentData
    {
        public int Group;
        public int BlinkState;
        public float MinInterval;
        public float MaxInterval;
        public float CloseSeconds;
        public float Timer;
        public float ClosedLeft;
        public Unity.Mathematics.Random Rng;
    }

    /// <summary>Sprite groups: parts whose sprites change together (both eyes, a mouth set).</summary>
    public static partial class SpriteParts
    {
        /// <summary>Blinks a sprite group by itself: <paramref name="blinkState"/> shows for <paramref name="closeSeconds"/> every
        /// <paramref name="minSeconds"/>..<paramref name="maxSeconds"/> (random).</summary>
        public static bool EnableAutoBlink(EntityManager em, Entity e, string groupName, string blinkState, float minSeconds = 2f,
            float maxSeconds = 5f, float closeSeconds = 0.12f)
        {
            if (!TrySet(em, e, out var blob))
                return false;
            int group = FindSpriteGroup(ref blob.Value, groupName);
            int state = group >= 0 ? FindSpriteGroupState(ref blob.Value, group, blinkState) : -1;
            if (state < 0)
                return false;
            var blink = NewAutoBlink(group, state, minSeconds, maxSeconds, closeSeconds, (uint)e.Index * 2654435761u + 1u);
            if (em.HasComponent<SpritePartsAutoBlink>(e))
                em.SetComponentData(e, blink);
            else
                em.AddComponentData(e, blink);
            return true;
        }

        public static void DisableAutoBlink(EntityManager em, Entity e)
        {
            if (em.Exists(e) && em.HasComponent<SpritePartsAutoBlink>(e))
                em.RemoveComponent<SpritePartsAutoBlink>(e);
        }

        public static SpritePartsAutoBlink NewAutoBlink(int group, int state, float minSeconds, float maxSeconds, float closeSeconds, uint seed)
        {
            var rng = new Unity.Mathematics.Random(seed == 0 ? 1u : seed);
            float lo = Unity.Mathematics.math.max(0.05f, Unity.Mathematics.math.min(minSeconds, maxSeconds));
            float hi = Unity.Mathematics.math.max(lo, Unity.Mathematics.math.max(minSeconds, maxSeconds));
            return new SpritePartsAutoBlink
            {
                Group = group, BlinkState = state, MinInterval = lo, MaxInterval = hi,
                CloseSeconds = Unity.Mathematics.math.max(0.01f, closeSeconds), Timer = rng.NextFloat(lo, hi), Rng = rng,
            };
        }

        public static int FindSpriteGroupState(ref SpritePartsSetBlob set, int group, string stateName)
        {
            if (group < 0 || group >= set.SpriteGroups.Length)
                return -1;
            var key = new FixedString64Bytes(stateName ?? string.Empty);
            ref var states = ref set.SpriteGroups[group].States;
            for (int s = 0; s < states.Length; s++)
                if (states[s].Name.Equals(key))
                    return s;
            return -1;
        }

        /// <summary>Runs an auto blink one frame (main thread; may add the group-state buffer).</summary>
        internal static void TickAutoBlink(EntityManager em, Entity root, ref SpritePartsSetBlob set, float dt)
        {
            var blink = em.GetComponentData<SpritePartsAutoBlink>(root);
            if (blink.Group < 0 || blink.Group >= set.SpriteGroups.Length)
                return;
            int current = -1;
            if (em.HasBuffer<SpritePartsSpriteGroupState>(root))
            {
                var buf = em.GetBuffer<SpritePartsSpriteGroupState>(root);
                current = blink.Group < buf.Length ? buf[blink.Group].State : -1;
            }
            if (blink.ClosedLeft > 0f)
            {
                blink.ClosedLeft -= dt;
                if (blink.ClosedLeft <= 0f)
                {
                    if (current == blink.BlinkState)
                        SetGroupState(em, root, ref set, blink.Group, -1);
                    blink.Timer = blink.Rng.NextFloat(blink.MinInterval, blink.MaxInterval);
                }
            }
            else
            {
                blink.Timer -= dt;
                if (blink.Timer <= 0f)
                {
                    if (current < 0) // gameplay has not set the group
                    {
                        SetGroupState(em, root, ref set, blink.Group, blink.BlinkState);
                        blink.ClosedLeft = blink.CloseSeconds;
                    }
                    else
                        blink.Timer = blink.Rng.NextFloat(blink.MinInterval, blink.MaxInterval);
                }
            }
            em.SetComponentData(root, blink);
        }

        /// <summary>
        /// Sets a sprite group's state from gameplay (eyes "Closed" when hurt): it wins over clip keys until
        /// <see cref="ClearSpriteGroup"/>.
        /// </summary>
        public static bool SetSpriteGroup(EntityManager em, Entity e, string groupName, string stateName)
        {
            if (!TrySet(em, e, out var blob))
                return false;
            int group = FindSpriteGroup(ref blob.Value, groupName);
            if (group < 0)
                return false;
            var key = new FixedString64Bytes(stateName ?? string.Empty);
            ref var states = ref blob.Value.SpriteGroups[group].States;
            for (int s = 0; s < states.Length; s++)
                if (states[s].Name.Equals(key))
                    return SetGroupState(em, e, ref blob.Value, group, s);
            return false;
        }

        public static bool SetSpriteGroup(EntityManager em, Entity e, string groupName, int stateIndex)
        {
            if (!TrySet(em, e, out var blob))
                return false;
            int group = FindSpriteGroup(ref blob.Value, groupName);
            return group >= 0 && stateIndex >= 0 && stateIndex < blob.Value.SpriteGroups[group].States.Length
                && SetGroupState(em, e, ref blob.Value, group, stateIndex);
        }

        /// <summary>Hands the group back to the clips (their keys, else the parts' own sprites).</summary>
        public static void ClearSpriteGroup(EntityManager em, Entity e, string groupName)
        {
            if (!TrySet(em, e, out var blob))
                return;
            int group = FindSpriteGroup(ref blob.Value, groupName);
            if (group >= 0)
                SetGroupState(em, e, ref blob.Value, group, -1);
        }

        /// <summary>The state gameplay set (-1 = none).</summary>
        public static int GetSpriteGroup(EntityManager em, Entity e, string groupName)
        {
            if (!TrySet(em, e, out var blob) || !em.HasBuffer<SpritePartsSpriteGroupState>(e))
                return -1;
            int group = FindSpriteGroup(ref blob.Value, groupName);
            var buf = em.GetBuffer<SpritePartsSpriteGroupState>(e);
            return group >= 0 && group < buf.Length ? buf[group].State : -1;
        }

        public static int FindSpriteGroup(ref SpritePartsSetBlob set, string groupName)
        {
            var key = new FixedString64Bytes(groupName ?? string.Empty);
            for (int g = 0; g < set.SpriteGroups.Length; g++)
                if (set.SpriteGroups[g].Name.Equals(key))
                    return g;
            return -1;
        }

        static bool SetGroupState(EntityManager em, Entity e, ref SpritePartsSetBlob set, int group, int state)
        {
            if (!em.HasBuffer<SpritePartsSpriteGroupState>(e))
                em.AddBuffer<SpritePartsSpriteGroupState>(e);
            var buf = em.GetBuffer<SpritePartsSpriteGroupState>(e);
            while (buf.Length < set.SpriteGroups.Length)
                buf.Add(new SpritePartsSpriteGroupState { State = -1 });
            buf[group] = new SpritePartsSpriteGroupState { State = state };
            return true;
        }
    }
}
