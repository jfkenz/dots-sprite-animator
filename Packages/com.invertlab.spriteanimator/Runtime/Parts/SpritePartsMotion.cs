using Unity.Mathematics;

namespace InvertLab.Sprites.DOTS
{
    /// <summary>
    /// Packed Add overrides for common character motion. Aiming stays on
    /// <see cref="SpritePartsPoseMode.LookAt"/>; these cover recoil, bob, and spring.
    /// </summary>
    public static class SpritePartsMotion
    {
        public const int RecoilId = 10001;
        public const int BobId = 10002;
        public const int SpringId = 10003;

        public static SpritePartsPoseOverride Recoil(int slotIndex, float2 kick, float weight = 1f, int id = RecoilId)
            => Additive(id, slotIndex, SpritePartsPoseChannel.Position, kick, 0f, weight);

        public static SpritePartsPoseOverride Bob(int slotIndex, float amplitude, float phase01, float weight = 1f,
            int id = BobId)
        {
            float y = math.sin(phase01 * 2f * math.PI) * amplitude;
            return Additive(id, slotIndex, SpritePartsPoseChannel.Position, new float2(0f, y), 0f, weight);
        }

        public static SpritePartsPoseOverride Spring(int slotIndex, float2 offset, float rotationDeg, float weight = 1f,
            int id = SpringId)
            => Additive(id, slotIndex, SpritePartsPoseChannel.Position | SpritePartsPoseChannel.Rotation,
                offset, rotationDeg, weight);

        static SpritePartsPoseOverride Additive(
            int id, int slotIndex, SpritePartsPoseChannel channels, float2 position, float rotation, float weight)
        {
            return new SpritePartsPoseOverride
            {
                Id = id,
                SlotIndex = slotIndex,
                Channels = (byte)channels,
                Mode = (byte)SpritePartsPoseMode.Add,
                Space = (byte)SpritePartsPoseSpace.Local,
                Priority = id,
                Weight = math.saturate(weight),
                Position = position,
                Rotation = rotation,
                Scale = new float2(1f, 1f),
            };
        }
    }
}
