using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

namespace InvertLab.Sprites.DOTS
{
    /// <summary>
    /// Pure-ECS Parts hierarchy factory for tests and runtime spawners.
    /// Creates root + visual root + part entities with CPU pose components.
    /// </summary>
    public static class SpritePartsEntityFactory
    {
        public struct CreateResult
        {
            public Entity Root;
            public Entity VisualRoot;
            public NativeArray<Entity> Parts;
        }

        public static CreateResult Create(
            EntityManager em,
            BlobAssetReference<SpritePartsSetBlob> setBlob,
            float3 position,
            int characterOrder = 0,
            bool flipX = false,
            bool flipY = false,
            int clipIndex = 0,
            bool playing = true,
            Color? tint = null)
        {
            if (!setBlob.IsCreated)
                throw new System.ArgumentException("Parts blob is required.", nameof(setBlob));
            ref var set = ref setBlob.Value;
            if (set.Slots.Length == 0 || set.Slots.Length > SpritePartIdUtility.MaxParts)
                throw new System.ArgumentException("Invalid Parts slot count.");

            var root = em.CreateEntity();
            em.AddComponentData(root, new LocalTransform
            {
                Position = position,
                Rotation = quaternion.identity,
                Scale = 1f,
            });
            em.AddComponentData(root, new LocalToWorld
            {
                Value = float4x4.TRS(position, quaternion.identity, new float3(1f)),
            });
            em.AddComponentData(root, new SpritePartsSetRef { Set = setBlob });
            em.AddComponentData(root, SpritePartsPoseWriter.DefaultPlayer(
                math.clamp(clipIndex, 0, math.max(0, set.Clips.Length - 1)), playing));
            em.AddComponentData(root, new SpritePartsDrawGroup { CharacterOrder = characterOrder });
            em.AddComponentData(root, new SpritePartsFacing
            {
                FlipX = flipX ? (byte)1 : (byte)0,
                FlipY = flipY ? (byte)1 : (byte)0,
            });
            em.AddComponentData(root, new SpritePartsEnabled());
            em.AddBuffer<SpritePartLink>(root);
            em.AddBuffer<SpritePartSheetEntry>(root);

            var visual = em.CreateEntity();
            em.AddComponentData(visual, LocalTransform.Identity);
            em.AddComponentData(visual, new LocalToWorld { Value = float4x4.identity });
            em.AddComponentData(visual, new Parent { Value = root });
            em.AddComponentData(visual, new PostTransformMatrix
            {
                Value = SpritePartsPlayback.FacingMatrix(flipX, flipY),
            });
            em.AddComponentData(visual, new SpritePartsVisualRoot { Root = root });
            em.AddComponentData(visual, new SpritePartsOwner { Root = root });
            em.AddComponentData(root, new SpritePartsVisualRootRef { VisualRoot = visual });

            var parts = new NativeArray<Entity>(set.Slots.Length, Allocator.Temp);
            var tint4 = tint.HasValue
                ? new float4(tint.Value.r, tint.Value.g, tint.Value.b, tint.Value.a)
                : new float4(1f, 1f, 1f, 1f);
            int startClip = math.clamp(clipIndex, 0, math.max(0, set.Clips.Length - 1));

            for (int i = 0; i < set.Slots.Length; i++)
            {
                ref var slot = ref set.Slots[i];
                var part = em.CreateEntity();
                parts[i] = part;
                SpritePartsSampler.SampleSlot(ref set, startClip, i, 0f, out var pose);
                em.AddComponentData(part, new LocalTransform
                {
                    Position = new float3(pose.Position.x, pose.Position.y, 0f),
                    Rotation = quaternion.RotateZ(math.radians(pose.Rotation)),
                    Scale = 1f,
                });
                em.AddComponentData(part, new LocalToWorld { Value = float4x4.identity });
                em.AddComponentData(part, new PostTransformMatrix
                {
                    Value = SpritePartsPlayback.ScaleMatrix(pose.Scale.x, pose.Scale.y),
                });
                em.AddComponentData(part, new SpritePartSlot
                {
                    Root = root,
                    SlotIndex = i,
                    ParentSlotIndex = slot.ParentSlotIndex,
                    SlotIdHash = slot.SlotIdHash,
                    Hidden = slot.Hidden,
                });
                em.AddComponentData(part, new SpritePartsOwner { Root = root });
                em.AddComponentData(part, new SpritePartRenderDepth { Value = 0f });
                em.AddComponentData(part, new SpriteAnimEnabled());
                if (slot.Hidden != 0)
                    em.SetComponentEnabled<SpriteAnimEnabled>(part, false);
                em.AddComponentData(part, new SpriteTint { Value = tint4 });
                em.AddComponentData(part, new SpritePartKeyedTint { Value = new float4(1f) });
                em.AddComponentData(part, new SpriteFlip { X = 0, Y = 0, Pivot = new float2(0.5f, 0.5f) });

                int appIndex = slot.DefaultAppearanceIndex;
                float2 frameOffset = float2.zero;
                float2 frameScale = new float2(1f, 1f);
                float2 logical = new float2(1f, 1f);
                float2 pivot = new float2(0.5f, 0.5f);
                int sheetTable = 0;
                int cell = 0;
                if (appIndex >= 0 && appIndex < set.Appearances.Length)
                {
                    ref var app = ref set.Appearances[appIndex];
                    frameOffset = app.FrameOffset;
                    frameScale = app.FrameScale;
                    logical = app.LogicalWorldSize;
                    pivot = app.Pivot;
                    sheetTable = app.SheetTableIndex;
                    cell = app.CellIndex;
                }
                em.AddComponentData(part, new SpritePartAppearanceState
                {
                    AppearanceIndex = appIndex,
                    SheetTableIndex = sheetTable,
                    CellIndex = cell,
                    LogicalWorldSize = logical,
                    Pivot = pivot,
                    FrameOffset = frameOffset,
                    FrameScale = frameScale,
                });
                em.AddComponentData(part, new SpriteAnimFrame
                {
                    Slot = cell,
                    Offset = frameOffset,
                    Scale = frameScale,
                    Rotation = 0f,
                });
                em.AddComponentData(part, new SpriteSheetBinding { Sheet = Entity.Null });

                em.GetBuffer<SpritePartLink>(root).Add(new SpritePartLink
                {
                    Part = part,
                    SlotIdHash = slot.SlotIdHash,
                    SlotIndex = i,
                });
            }

            for (int i = 0; i < set.Slots.Length; i++)
            {
                int parentSlot = set.Slots[i].ParentSlotIndex;
                Entity parentEntity = visual;
                if (parentSlot >= 0 && parentSlot < parts.Length)
                    parentEntity = parts[parentSlot];
                em.AddComponentData(parts[i], new Parent { Value = parentEntity });
            }

            // LinkedEntityGroup after all structural changes (destroy root destroys owned children).
            var group = em.AddBuffer<LinkedEntityGroup>(root);
            group.Add(new LinkedEntityGroup { Value = root });
            group.Add(new LinkedEntityGroup { Value = visual });
            for (int i = 0; i < parts.Length; i++)
                group.Add(new LinkedEntityGroup { Value = parts[i] });

            SpritePartsPoseWriter.EnsureBuffers(em, root);
            SpritePartsPoseWriter.Apply(em, root);

            return new CreateResult
            {
                Root = root,
                VisualRoot = visual,
                Parts = parts,
            };
        }
    }
}
