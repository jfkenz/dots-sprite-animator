using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace InvertLab.Sprites.DOTS
{
    /// <summary>Applies current-frame socket poses to baked child attachments.</summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(SpriteAnimPlayerSystem))]
    [BurstCompile]
    public partial struct SpriteSocketAttachmentSystem : ISystem
    {
        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var socketBuffers = SystemAPI.GetBufferLookup<SpriteSocketBuffer>(true);
            var sourceTransforms = SystemAPI.GetComponentLookup<LocalTransform>(true);
            var postTransforms = SystemAPI.GetComponentLookup<PostTransformMatrix>();

            foreach (var (attachment, transform, entity) in
                     SystemAPI.Query<RefRO<SpriteSocketAttachment>, RefRW<LocalTransform>>()
                         .WithEntityAccess())
            {
                Entity source = attachment.ValueRO.Source;
                if (!socketBuffers.HasBuffer(source) || !sourceTransforms.HasComponent(source))
                    continue;

                var sockets = socketBuffers[source];
                float sourceScale = sourceTransforms[source].Scale;
                if (math.abs(sourceScale) < 0.0001f)
                    sourceScale = 1f;
                for (int i = 0; i < sockets.Length; i++)
                {
                    var socket = sockets[i];
                    if (!socket.Name.Equals(attachment.ValueRO.SocketName))
                        continue;

                    float angle = socket.LocalAngle + attachment.ValueRO.AngleOffset;
                    quaternion rotation = quaternion.RotateZ(math.radians(angle));
                    float2 offset = math.mul(
                        rotation,
                        new float3(attachment.ValueRO.PositionOffset, 0f)).xy;
                    float3 position = transform.ValueRO.Position;
                    position.xy = (socket.LocalPosition + offset) / sourceScale;

                    // The source Quad is scaled to its rendered world size. Cancel that local
                    // scale here because socket positions were already converted from pixels
                    // to world units during baking. A missing key performs no write, retaining
                    // the last valid socket pose automatically.
                    transform.ValueRW.Position = position;
                    transform.ValueRW.Rotation = rotation;
                    transform.ValueRW.Scale = attachment.ValueRO.BaseScale / sourceScale;
                    if (postTransforms.HasComponent(entity))
                    {
                        float2 socketScale = math.all(socket.LocalScale == float2.zero)
                            ? new float2(1f, 1f)
                            : socket.LocalScale;
                        postTransforms[entity] = new PostTransformMatrix
                        {
                            Value = float4x4.Scale(new float3(socketScale, 1f)),
                        };
                    }
                    break;
                }
            }
        }
    }
}
