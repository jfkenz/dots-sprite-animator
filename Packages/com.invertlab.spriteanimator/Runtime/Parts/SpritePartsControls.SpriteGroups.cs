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

    /// <summary>Sprite groups: parts whose sprites change together (both eyes, a mouth set).</summary>
    public static partial class SpriteParts
    {
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
