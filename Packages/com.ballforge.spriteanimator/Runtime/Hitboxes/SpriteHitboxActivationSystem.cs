using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;

namespace BallForge.Sprites.DOTS
{
    /// <summary>
    /// Turns authored hitboxes (SpriteHitboxSetRef) into per-tick live boxes
    /// (SpriteHitboxLive buffer): a box is live exactly while its clip is the
    /// one playing, playback is active, and the DISPLAYED frame matches the
    /// box's frame.
    ///
    /// Runs right after SpriteAnimPlayerSystem, so SpriteAnimPlayer.Time has
    /// ALREADY been advanced+wrapped this tick — reading it directly gives the
    /// exact time on screen. The displayed index mirrors the player's math:
    /// idx = clamp(floor(t)), flipped n-1-idx under ReverseLoop. Entities
    /// without a hitbox set never enter the query (implicit filtering).
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(SpriteAnimPlayerSystem))]
    [BurstCompile]
    public partial struct SpriteHitboxActivationSystem : ISystem
    {
        public void OnUpdate(ref SystemState state)
        {
            foreach (var (hbSetRef, animSetRef, player, buffer) in
                     SystemAPI.Query<RefRO<SpriteHitboxSetRef>,
                                     RefRO<SpriteAnimSetRef>,
                                     RefRO<SpriteAnimPlayer>,
                                     DynamicBuffer<SpriteHitboxLive>>())
            {
                buffer.Clear();

                if (player.ValueRO.Playing == 0)
                    continue; // paused / finished attack -> boxes stay dead

                ref var anim = ref animSetRef.ValueRO.Set.Value;
                int ci = math.clamp(player.ValueRO.ClipIndex, 0, anim.Clips.Length - 1);
                ref var def = ref anim.Clips[ci];

                // locate the matching hitbox group by the playing clip's name hash
                ref var hbSet = ref hbSetRef.ValueRO.Set.Value;
                int hc = -1;
                for (int i = 0; i < hbSet.Clips.Length; i++)
                    if (hbSet.Clips[i].ClipHash == def.NameHash) { hc = i; break; }
                if (hc < 0) continue;

                ref var boxes = ref hbSet.Clips[hc];
                int nBoxes = boxes.Boxes.Length;
                if (nBoxes == 0) continue;

                int n = def.FrameCount;
                if (n <= 0) continue;

                float t = player.ValueRO.Time;          // already wrapped by the player
                int idx = math.clamp((int)t, 0, n - 1);
                int drawIdx = def.WrapMode == SpriteAnimWrap.ReverseLoop ? n - 1 - idx : idx;

                for (int b = 0; b < nBoxes; b++)
                {
                    if (boxes.Boxes[b].FrameIndex != drawIdx) continue;
                    buffer.Add(new SpriteHitboxLive { Box = boxes.Boxes[b].Box });
                }
            }
        }
    }
}
