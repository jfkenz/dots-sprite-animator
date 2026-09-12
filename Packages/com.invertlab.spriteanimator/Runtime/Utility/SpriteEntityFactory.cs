using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Rendering;
using Unity.Transforms;
using UnityEngine;

namespace InvertLab.Sprites.DOTS
{
    /// <summary>
    /// Pure-ECS sprite entity factory — no GameObjects, no bakers.
    /// Creates an animated sprite entity rendered by the GPU-instanced path
    /// (SpriteInstanceRenderSystem). Frames must be Sprites from ONE grid sheet;
    /// grid size is derived from their atlas rects and registered globally.
    /// Call from editor tools, bootstrap code, or gameplay.
    /// </summary>
    public static class SpriteEntityFactory
    {
        /// <summary>
        /// Create an animated sprite entity. UV flip uses shader flags so
        /// instanced + GPU-anim paths do not need a 180-degree transform hack.
        /// </summary>
        public static Entity Create(
            EntityManager em,
            IReadOnlyList<Sprite> frames,
            float frameRate,
            bool loop,
            Vector3 position,
            float sizeUnits,
            Color? tint = null,
            int orderInLayer = 0,
            bool flipX = false,
            bool flipY = false,
            float alphaCutoff = 0f)
        {
            if (frames == null || frames.Count == 0 || frames[0] == null)
                throw new System.ArgumentException("At least one sprite frame is required.", nameof(frames));
            var atlas = frames[0].texture;

            // The atlas dimensions, not the number of supplied frames, define the grid.
            // Packed/trimmed/rotated sprites are not uniform sheet cells.
            var first = frames[0].rect;
            float cw = first.width;
            float ch = first.height;
            int cols = Mathf.RoundToInt(atlas.width / cw);
            int rows = Mathf.RoundToInt(atlas.height / ch);
            if (cols < 1 || rows < 1 || Mathf.Abs(cols * cw - atlas.width) > 0.01f ||
                Mathf.Abs(rows * ch - atlas.height) > 0.01f)
                throw new System.ArgumentException("Frames must be full cells on a uniform grid that covers the texture.", nameof(frames));
            foreach (var f in frames)
            {
                if (f == null || f.texture != atlas)
                    throw new System.ArgumentException("All frames must belong to the same texture.", nameof(frames));
                var rect = f.rect;
                if (f.packed || Mathf.Abs(rect.width - cw) > 0.01f || Mathf.Abs(rect.height - ch) > 0.01f ||
                    Mathf.Abs(rect.x / cw - Mathf.Round(rect.x / cw)) > 0.0001f ||
                    Mathf.Abs(rect.y / ch - Mathf.Round(rect.y / ch)) > 0.0001f)
                    throw new System.ArgumentException("Frames must be unpacked, aligned cells of equal size. Use profile cropped layouts for irregular atlases.", nameof(frames));
            }

            // slot per frame within that grid (row-major, row 0 = top)
            var slots = new int[frames.Count];
            for (int i = 0; i < frames.Count; i++)
            {
                var r = frames[i].rect;
                int col = Mathf.Clamp((int)((r.x + r.width * 0.5f) / cw), 0, cols - 1);
                // rect origin is bottom-left in Unity -> invert for row index
                int row = Mathf.Clamp((int)((atlas.height - r.y - r.height * 0.5f) / ch), 0, rows - 1);
                slots[i] = row * cols + col;
            }

            // Bind this entity to its own sheet; creating another atlas must not
            // change existing sprites' legacy sheet or GPU grid.
            SpriteInstanceRenderSystem.Install(em);
            var sheet = GetOrCreateSheet(em, atlas, cols, rows, cw / ch);

            // ---- clip blob ----
            var lifetime = em.World.GetOrCreateSystemManaged<SpriteAnimBlobLifetimeSystem>();
            em.World.GetOrCreateSystemManaged<SimulationSystemGroup>().AddSystemToUpdateList(lifetime);
            var (setRef, player) = SpriteAnimSetBuilder.Build(Allocator.Persistent,
                new[]
                {
                    new SpriteAnimSetBuilder.ClipInput
                    {
                        Name = "clip",
                        Loop = loop,
                        FrameRate = math.max(0.1f, frameRate),
                        GlobalFrameIndices = slots,
                        OnCompleteClipIndex = -1,
                    },
                });

            // ---- entity ----
            var e = em.CreateEntity();
            try
            {
                em.AddComponentData(e, new LocalTransform
                {
                    Position = new float3(position.x, position.y,
                                          position.z - orderInLayer * 0.001f),
                    Rotation = quaternion.identity,
                    Scale = sizeUnits,
                });
                // the render packer reads LocalToWorld (rotation/scale/parent support)
                em.AddComponentData(e, new LocalToWorld
                {
                    Value = float4x4.TRS(position, quaternion.identity, new float3(sizeUnits)),
                });
                em.AddComponentData(e, setRef);
                em.AddComponentData(e, new SpriteSheetBinding { Sheet = sheet });
                em.AddComponentData(e, player);
                em.AddComponentData(e, new SpriteAnimFrame
                {
                    Slot = slots[0],
                    Offset = float2.zero,
                    Scale = new float2(1f, 1f),
                    Rotation = 0f,
                });
                var t = tint ?? Color.white;
                em.AddComponentData(e, new SpriteTint
                {
                    Value = new float4(t.r, t.g, t.b, t.a),
                });
                em.AddComponentData(e, new SpriteAnimEnabled());
                em.AddComponentData(e, new SpriteFlip
                {
                    X = (byte)(flipX ? 1 : 0),
                    Y = (byte)(flipY ? 1 : 0),
                    Pivot = new float2(0.5f, 0.5f),
                });
                // NOTE: no event storage by default — SpriteAnimEventBuffer +
                // SpriteAnimEventsPending are opt-in (see SpriteAnimEvents).
                // Callers that subscribe to animation events add them explicitly:
                //   em.AddBuffer<SpriteAnimEventBuffer>(e);
                //   em.AddComponent<SpriteAnimEventsPending>(e); (enabled=false)
                em.AddComponentData(e, new SpriteAnimBlobOwnerAlive { Blob = setRef.Set });
                em.AddComponentData(e, new SpriteAnimOwnedBlob { Blob = setRef.Set });
                return e;
            }
            catch
            {
                em.DestroyEntity(e);
                setRef.Set.Dispose();
                throw;
            }
        }

        static Entity GetOrCreateSheet(EntityManager em, Texture2D texture, int cols, int rows, float aspect)
        {
            using var query = em.CreateEntityQuery(typeof(SpriteSheetDefinition), typeof(SpriteSheetAsset));
            using var entities = query.ToEntityArray(Allocator.Temp);
            foreach (var entity in entities)
            {
                var def = em.GetComponentData<SpriteSheetDefinition>(entity);
                if (def.Cols == cols && def.Rows == rows && def.CellAspect == aspect && def.UseCellCrops == 0 &&
                    em.GetComponentObject<SpriteSheetAsset>(entity).Texture == texture) return entity;
            }
            var result = em.CreateEntity();
            em.AddComponentData(result, new SpriteSheetDefinition { Cols = cols, Rows = rows, CellAspect = aspect });
            em.AddComponentObject(result, new SpriteSheetAsset { Texture = texture });
            var registration = em.World.GetOrCreateSystemManaged<SpriteSheetRegistrationSystem>();
            em.World.GetOrCreateSystemManaged<SimulationSystemGroup>().AddSystemToUpdateList(registration);
            return result;
        }
    }
}
