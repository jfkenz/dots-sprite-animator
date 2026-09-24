using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace InvertLab.Sprites.DOTS
{
    /// <summary>
    /// Point and bounding-box parts (Spine's point and bounding box attachments): world positions and hit tests from the
    /// pose written this frame (works before the transform system runs).
    /// </summary>
    public static partial class SpriteParts
    {
        /// <summary>A point part's world position and direction (degrees, its X axis).</summary>
        public static bool TryGetPoint(EntityManager em, Entity root, string partName, out float2 position, out float rotationDeg)
        {
            position = default;
            rotationDeg = 0f;
            if (!TryPartWorld(em, root, partName, out _, out float4x4 world))
                return false;
            position = world.c3.xy;
            rotationDeg = math.degrees(math.atan2(world.c0.y, world.c0.x));
            return true;
        }

        /// <summary>A bounding box part's outline in world space (with its keyed deform), into <paramref name="worldPoints"/>.</summary>
        public static bool GetBoundingBox(EntityManager em, Entity root, string partName, NativeList<float2> worldPoints)
        {
            worldPoints.Clear();
            if (!TryPartWorld(em, root, partName, out int slot, out float4x4 world))
                return false;
            ref var set = ref em.GetComponentData<SpritePartsSetRef>(root).Set.Value;
            ref var box = ref set.Slots[slot];
            if (box.IsBoundingBox == 0 || box.ClipPolygon.Length < 3)
                return false;
            var player = em.GetComponentData<SpritePartsPlayer>(root);
            bool deformed = SpritePartsSampler.SampleDeformOffsets(ref set, player.ClipIndex, slot, player.TimeSeconds,
                box.ClipPolygon.Length, out var offsets);
            for (int i = 0; i < box.ClipPolygon.Length; i++)
                worldPoints.Add(SpritePartsHierarchy.TransformPoint(world, box.ClipPolygon[i] + (deformed ? offsets[i] : float2.zero)));
            return true;
        }

        /// <summary>True when <paramref name="worldPoint"/> is inside the bounding box part (any outline, even concave).</summary>
        public static bool BoundingBoxContains(EntityManager em, Entity root, string partName, float2 worldPoint)
        {
            var points = new NativeList<float2>(16, Allocator.Temp);
            try
            {
                return GetBoundingBox(em, root, partName, points) && PolygonContains(points.AsArray(), worldPoint);
            }
            finally
            {
                points.Dispose();
            }
        }

        /// <summary>Even-odd point-in-polygon test.</summary>
        public static bool PolygonContains(NativeArray<float2> polygon, float2 p)
        {
            bool inside = false;
            for (int i = 0, j = polygon.Length - 1; i < polygon.Length; j = i++)
            {
                float2 a = polygon[i], b = polygon[j];
                if ((a.y > p.y) != (b.y > p.y) && p.x < (b.x - a.x) * (p.y - a.y) / (b.y - a.y) + a.x)
                    inside = !inside;
            }
            return inside;
        }

        /// <summary>A part (by name or id) in world space: this frame's local poses up its parents, then the character's facing and place.</summary>
        static bool TryPartWorld(EntityManager em, Entity root, string partName, out int slot, out float4x4 world)
        {
            slot = -1;
            world = float4x4.identity;
            if (!TrySet(em, root, out var blob) || !em.HasBuffer<SpritePartFinalPose>(root))
                return false;
            ref var set = ref blob.Value;
            slot = FindPart(ref set, partName);
            var finals = em.GetBuffer<SpritePartFinalPose>(root);
            if (slot < 0 || finals.Length != set.Slots.Length)
                return false;
            float4x4 local = float4x4.identity;
            for (int s = slot, guard = 0; s >= 0 && guard < 256; s = set.Slots[s].ParentSlotIndex, guard++)
            {
                var f = finals[s];
                local = math.mul(SpritePartsHierarchy.LocalMatrix(f.Position, f.Rotation, f.Scale, f.Shear), local);
            }
            bool flipX = false, flipY = false;
            if (em.HasComponent<SpritePartsFacing>(root))
            {
                var facing = em.GetComponentData<SpritePartsFacing>(root);
                flipX = facing.FlipX != 0;
                flipY = facing.FlipY != 0;
            }
            world = math.mul(math.mul(SpritePartsPoseWriter.CurrentEntityWorld(em, root), SpritePartsPlayback.FacingMatrix(flipX, flipY)), local);
            return true;
        }

        static int FindPart(ref SpritePartsSetBlob set, string partName)
        {
            if (string.IsNullOrEmpty(partName))
                return -1;
            var name = new FixedString64Bytes(partName);
            for (int i = 0; i < set.Slots.Length; i++)
                if (set.Slots[i].Name.Equals(name))
                    return i;
            var id = new FixedString64Bytes(SpritePartIdUtility.Canonical(partName));
            for (int i = 0; i < set.Slots.Length; i++)
                if (set.Slots[i].SlotId.Equals(id))
                    return i;
            return -1;
        }
    }
}
