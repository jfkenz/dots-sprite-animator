using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;

namespace InvertLab.Sprites.DOTS
{
    /// <summary>
    /// Turns authored hitboxes (SpriteHitboxSetRef) into per-tick live boxes
    /// (SpriteHitboxLive buffer). Character boxes in Shared are always live.
    /// Clip-lifetime boxes (FrameIndex &lt; 0) are live while that clip is current.
    /// Frame boxes are live only while playback is active and the displayed
    /// frame matches.
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

                ref var hbSet = ref hbSetRef.ValueRO.Set.Value;
                for (int s = 0; s < hbSet.Shared.Length; s++)
                    buffer.Add(new SpriteHitboxLive { Box = hbSet.Shared[s].Box });

                ref var anim = ref animSetRef.ValueRO.Set.Value;
                if (anim.Clips.Length == 0)
                    continue;

                int ci = math.clamp(player.ValueRO.ClipIndex, 0, anim.Clips.Length - 1);
                ref var def = ref anim.Clips[ci];

                int hc = -1;
                for (int i = 0; i < hbSet.Clips.Length; i++)
                    if (hbSet.Clips[i].ClipHash == def.NameHash) { hc = i; break; }
                if (hc < 0) continue;

                ref var boxes = ref hbSet.Clips[hc];
                int nBoxes = boxes.Boxes.Length;
                if (nBoxes == 0) continue;

                // Clip-lifetime boxes (FrameIndex < 0) stay live the whole time
                // this clip is current, including pause. Frame boxes need playback.
                for (int b = 0; b < nBoxes; b++)
                {
                    if (boxes.Boxes[b].FrameIndex >= 0) continue;
                    buffer.Add(new SpriteHitboxLive { Box = boxes.Boxes[b].Box });
                }

                if (player.ValueRO.Playing == 0)
                    continue;

                int n = def.FrameCount;
                if (n <= 0) continue;

                float t = player.ValueRO.Time;          // already wrapped by the player
                int idx = math.clamp((int)t, 0, n - 1);
                int drawIdx = def.WrapMode == SpriteAnimWrap.ReverseLoop ? n - 1 - idx : idx;

                for (int b = 0; b < nBoxes; b++)
                {
                    if (boxes.Boxes[b].FrameIndex != drawIdx) continue;
                    // Box.Angle is copied through for OBB-aware gameplay. This system
                    // does not rotate the live AABB itself.
                    buffer.Add(new SpriteHitboxLive { Box = boxes.Boxes[b].Box });
                }
            }
        }
    }
}
