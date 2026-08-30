using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace InvertLab.Sprites.DOTS
{
    /// <summary>
    /// Runtime hitbox library for one character: authored boxes grouped by
    /// clip, keyed to clip-local frame indices. Attach next to
    /// SpriteAnimSetRef; SpriteHitboxActivationSystem turns it into per-tick
    /// live boxes (SpriteHitboxLive buffer). Built from a SpriteSheetProfile
    /// via SpriteHitboxSetBuilder.FromProfile, or by hand via Build().
    /// </summary>
    public struct SpriteHitboxSetRef : IComponentData
    {
        public BlobAssetReference<SpriteHitboxSetBlob> Set;
    }

    public struct SpriteHitboxSetBlob
    {
        public BlobArray<SpriteHitboxClip> Clips;
        /// <summary>Character-lifetime query boxes, live on every clip.</summary>
        public BlobArray<SpriteHitboxEntry> Shared;
    }

    public struct SpriteHitboxClip
    {
        public ulong ClipHash;                     // FNV1a64 of the clip name
        public BlobArray<SpriteHitboxEntry> Boxes; // every box authored in this clip
    }

    public struct SpriteHitboxEntry
    {
        public int FrameIndex; // clip-local DISPLAYED frame (0-based, after reverse flip)
        public FrameBox Box;   // uv space within the cell, origin bottom-left
    }

    /// <summary>
    /// Boxes live THIS tick on one entity. Rewritten (Clear+Add) every update
    /// by SpriteHitboxActivationSystem; gameplay reads/consumes, never writes.
    /// </summary>
    [InternalBufferCapacity(4)]
    public struct SpriteHitboxLive : IBufferElementData
    {
        public FrameBox Box;
    }

    /// <summary>Builds SpriteHitboxSetBlob from managed definitions.</summary>
    public static class SpriteHitboxSetBuilder
    {
        public struct BoxInput
        {
            public string  ClipName;
            public int     FrameIndex;
            public float2  Center;   // uv, origin bottom-left
            public float2  Extents;  // half-size in uv units
            public float   Angle;    // degrees, y-up runtime UV
            public byte    Id;
            public SpriteColliderShape Shape;
            public FixedList128Bytes<float2> Polygon;
        }

        /// <summary>
        /// Convenience converter from tool-space rects (RectUV origin TOP-LEFT,
        /// y-down, as authored in SpriteSheetToolWindow) to runtime uv space.
        /// </summary>
        public static BoxInput Rect(string clipName, int frameIndex, Rect r, byte id)
        {
            return new BoxInput
            {
                ClipName  = clipName,
                FrameIndex = frameIndex,
                Center    = new float2(r.x + r.width * 0.5f, 1f - (r.y + r.height * 0.5f)),
                Extents   = new float2(r.width * 0.5f, r.height * 0.5f),
                Id        = id,
                Shape     = SpriteColliderShape.Square,
            };
        }

        /// <summary>Build the blob. Groups inputs by clip name (order of first appearance).</summary>
        public static BlobAssetReference<SpriteHitboxSetBlob> Build(
            Allocator allocator, BoxInput[] boxes, BoxInput[] shared = null)
        {
            var builder = new BlobBuilder(Allocator.Temp);
            ref var root = ref builder.ConstructRoot<SpriteHitboxSetBlob>();

            int boxCount = boxes?.Length ?? 0;

            // group by clip name, preserving first-appearance order
            var nameOrder = new List<string>(8);
            var byName    = new Dictionary<string, List<BoxInput>>(8);
            for (int i = 0; i < boxCount; i++)
            {
                var key = string.IsNullOrEmpty(boxes[i].ClipName) ? "clip" : boxes[i].ClipName;
                if (!byName.TryGetValue(key, out var list))
                {
                    list = new List<BoxInput>(8);
                    byName[key] = list;
                    nameOrder.Add(key);
                }
                list.Add(boxes[i]);
            }

            var clips = builder.Allocate(ref root.Clips, nameOrder.Count);
            for (int c = 0; c < nameOrder.Count; c++)
            {
                var list = byName[nameOrder[c]];
                ref var clip = ref clips[c];
                clip.ClipHash = Fnv(nameOrder[c]);
                var dst = builder.Allocate(ref clip.Boxes, list.Count);
                for (int b = 0; b < list.Count; b++)
                    dst[b] = ToEntry(list[b]);
            }

            int sharedCount = shared?.Length ?? 0;
            var sharedDst = builder.Allocate(ref root.Shared, sharedCount);
            for (int s = 0; s < sharedCount; s++)
                sharedDst[s] = ToEntry(shared[s]);

            var result = builder.CreateBlobAssetReference<SpriteHitboxSetBlob>(allocator);
            builder.Dispose();
            return result;
        }

        static SpriteHitboxEntry ToEntry(in BoxInput input)
        {
            return new SpriteHitboxEntry
            {
                FrameIndex = input.FrameIndex,
                Box = new FrameBox
                {
                    Center  = input.Center,
                    Extents = input.Extents,
                    Angle   = input.Angle,
                    Id      = input.Id,
                    Shape   = input.Shape,
                    Polygon = input.Polygon,
                }
            };
        }

        /// <summary>Build straight from an authored SpriteSheetProfile asset.</summary>
        public static BlobAssetReference<SpriteHitboxSetBlob> FromProfile(
            SpriteSheetProfile profile, Allocator allocator)
        {
            if (profile?.Hitboxes == null || profile.Hitboxes.Count == 0)
                return Build(allocator, null);

            var frameInputs = new List<BoxInput>(profile.Hitboxes.Count);
            var sharedInputs = new List<BoxInput>(4);
            for (int i = 0; i < profile.Hitboxes.Count; i++)
            {
                var hb = profile.Hitboxes[i];
                if (hb == null || !hb.UsesQuery)
                    continue;
                var input = ToInput(hb);
                if (hb.IsCharacter)
                    sharedInputs.Add(input);
                else
                    frameInputs.Add(input);
            }
            return Build(allocator, frameInputs.ToArray(), sharedInputs.ToArray());
        }

        static BoxInput ToInput(FrameBoxDef hb)
        {
            var input = Rect(
                string.IsNullOrEmpty(hb.ClipName) ? "clip" : hb.ClipName,
                hb.FrameIndex, hb.RectUV, hb.Id);
            input.Shape = hb.Shape;
            input.Angle = -hb.Angle;
            if (hb.Shape == SpriteColliderShape.Polygon)
            {
                Vector2[] polygon = hb.PolygonUV != null && hb.PolygonUV.Length >= 3
                    ? hb.PolygonUV
                    : FrameBoxDef.CreateRegularPolygon();
                for (int point = 0; point < polygon.Length && point < 12; point++)
                {
                    Vector2 local = polygon[point];
                    input.Polygon.Add(new float2(
                        hb.RectUV.x + local.x * hb.RectUV.width,
                        1f - (hb.RectUV.y + local.y * hb.RectUV.height)));
                }
            }
            return input;
        }

        /// <summary>
        /// Opt one entity in: attaches the set ref and the live-box buffer.
        /// Entities without the set ref pay nothing in the activation system.
        /// </summary>
        public static void Install(EntityManager em, Entity e,
                                   BlobAssetReference<SpriteHitboxSetBlob> blob)
        {
            if (em.HasComponent<SpriteHitboxSetRef>(e))
                em.SetComponentData(e, new SpriteHitboxSetRef { Set = blob });
            else
                em.AddComponentData(e, new SpriteHitboxSetRef { Set = blob });

            if (!em.HasBuffer<SpriteHitboxLive>(e))
                em.AddBuffer<SpriteHitboxLive>(e);
        }

        public static ulong Fnv(string s) => SpriteAnimSetBuilder.Fnv(s);
    }
}
