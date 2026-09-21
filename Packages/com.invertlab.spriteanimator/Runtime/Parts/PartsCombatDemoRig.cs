using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace InvertLab.Sprites.DOTS
{
    /// <summary>Explicit demo data keeps the gameplay controller readable. Crop rectangles
    /// are normalized from the supplied 1254px atlas; no texture readback at runtime.</summary>
    public static class PartsCombatDemoRig
    {
        public static readonly float2 Grip = new float2(0.25f, 0.32f);
        public static readonly float2[] Sizes = {
            new float2(1.1f, 1.45f), new float2(0.34f, 0.36f),
            new float2(1.3f, 0.766f), new float2(1.65f, 0.746f) };
        public static readonly float4[] Crops = {
            new float4(458, 606, 100, 578) / 1254f,
            new float4(289, 307, 830, 677) / 1254f,
            new float4(497, 293, 70, 119) / 1254f,
            new float4(602, 272, 628, 129) / 1254f };

        public static BlobAssetReference<SpritePartsSetBlob> Build()
        {
            var slots = new[] {
                Slot("body", null, float2.zero, "body", 0),
                Slot("hand.l", "body", new float2(-0.43f, -0.16f), "hand", 1),
                Slot("hand.r", "body", new float2(0.43f, -0.12f), "hand", 3),
                Slot("weapon", "hand.r", new float2(0.04f, 0f), "blaster", 2) };
            var appearances = new[] { Art("body", 0), Art("hand", 1), Art("blaster", 2), Art("rifle", 3) };
            var clips = new[] {
                new SpritePartsSetBuilder.ClipInput { Name = "Idle", ClipId = "idle", Duration = 1.2f, SpeedMultiplier = 1f,
                    WrapMode = (byte)SpritePartsWrap.Loop, Tracks = new[] {
                        Track("body", Key(0, 0, 0, 0), Key(0.6f, 0, 0.025f, 0), Key(1.2f, 0, 0, 0)) } },
                new SpritePartsSetBuilder.ClipInput { Name = "Walk", ClipId = "walk", Duration = 0.5f, SpeedMultiplier = 1f,
                    WrapMode = (byte)SpritePartsWrap.Loop, Tracks = new[] {
                        Track("body", Key(0, 0, 0, -3), Key(0.125f, 0, 0.09f, 0), Key(0.25f, 0, 0, 3),
                            Key(0.375f, 0, 0.09f, 0), Key(0.5f, 0, 0, -3)),
                        Track("hand.l", Key(0, -0.43f, -0.16f, -12), Key(0.25f, -0.46f, -0.05f, 12),
                            Key(0.5f, -0.43f, -0.16f, -12)) } } };
            var skins = new[] {
                new SpritePartsSetBuilder.SkinInput { SkinId = "blaster", Bindings = new[] {
                    new SpritePartsSetBuilder.SkinBindingInput { SlotId = "weapon", AppearanceId = "blaster" } } },
                new SpritePartsSetBuilder.SkinInput { SkinId = "rifle", Bindings = new[] {
                    new SpritePartsSetBuilder.SkinBindingInput { SlotId = "weapon", AppearanceId = "rifle" } } } };
            return SpritePartsSetBuilder.Build(Allocator.Persistent, slots, appearances, clips, skins);
        }

        static SpritePartsSetBuilder.SlotInput Slot(string id, string parent, float2 position, string art, int rank)
            => new SpritePartsSetBuilder.SlotInput { Name = id, SlotId = id, ParentSlotId = parent,
                RestPosition = position, RestScale = new float2(1f), DefaultAppearanceId = art, DrawRank = rank };
        static SpritePartsSetBuilder.AppearanceInput Art(string id, int sheet)
        {
            SpritePartsGeometry.TryResolveExplicit(Sizes[sheet], sheet >= 2 ? Grip : new float2(0.5f), sheet, 0, out var geo, out _);
            return new SpritePartsSetBuilder.AppearanceInput { AppearanceId = id, SheetTableIndex = sheet,
                CellIndex = 0, LogicalWorldSize = geo.LogicalWorldSize, Pivot = geo.Pivot,
                FrameOffset = geo.FrameOffset, FrameScale = geo.FrameScale };
        }
        static SpritePartsSetBuilder.TrackInput Track(string slot, params SpritePartsSetBuilder.KeyInput[] keys)
            => new SpritePartsSetBuilder.TrackInput { SlotId = slot, Keys = keys };
        static SpritePartsSetBuilder.KeyInput Key(float time, float x, float y, float rotation)
            => new SpritePartsSetBuilder.KeyInput { Time = time, Position = new float2(x, y), Rotation = rotation,
                Scale = new float2(1f), EaseMode = (byte)SpriteEaseMode.Linear };
    }
}
