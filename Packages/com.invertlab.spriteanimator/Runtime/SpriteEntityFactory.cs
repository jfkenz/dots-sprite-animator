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
            var atlas = frames[0].texture;

            // ---- derive grid from the sprites' atlas rects ----
            var xs = new HashSet<int>();
            var ys = new HashSet<int>();
            foreach (var f in frames)
            {
                xs.Add(Mathf.RoundToInt(f.textureRect.x));
                ys.Add(Mathf.RoundToInt(f.textureRect.y));
            }
            int cols = math.max(1, xs.Count);
            int rows = math.max(1, ys.Count);

            // slot per frame within that grid (row-major, row 0 = top)
            float cw = atlas.width / (float)cols;
            float ch = atlas.height / (float)rows;
            var slots = new int[frames.Count];
            for (int i = 0; i < frames.Count; i++)
            {
                var r = frames[i].textureRect;
                int col = Mathf.Clamp((int)((r.x + r.width * 0.5f) / cw), 0, cols - 1);
                // rect origin is bottom-left in Unity -> invert for row index
                int row = Mathf.Clamp((int)((atlas.height - r.y - r.height * 0.5f) / ch), 0, rows - 1);
                slots[i] = row * cols + col;
            }

            // ---- register sheet + grid with the instanced renderer ----
            SpriteInstanceRenderSystem.SetSheet(atlas);
            SpriteInstanceRenderSystem.SetGrid(em, cols, rows,
                SpriteSheetProfile.GetCellAspect(atlas, cols, rows));

            // ---- clip blob ----
            var (setRef, player) = SpriteAnimSetBuilder.Build(Allocator.Persistent,
                new[]
                {
                    new SpriteAnimSetBuilder.ClipInput
                    {
                        Name = "clip",
                        Loop = loop,
                        FrameRate = math.max(0.1f, frameRate),
                        GlobalFrameIndices = slots,
                    },
                });

            // ---- entity ----
            var e = em.CreateEntity();
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
            return e;
        }
    }
}
