using NUnit.Framework;
using UnityEngine;

namespace InvertLab.Sprites.DOTS.Tests
{
    public sealed class SpriteSheetPixelFlipTests
    {
        static Color32 C(byte v) => new(v, 0, 0, 255);

        [Test]
        public void FlipCellsHorizontalMirrorsEachCellInPlace()
        {
            // 2 columns, 1 row, 2x2 cells. Bottom-left origin.
            // y=1: 1 2 | 5 6
            // y=0: 3 4 | 7 8
            var pixels = new[]
            {
                C(3), C(4), C(7), C(8),
                C(1), C(2), C(5), C(6),
            };

            SpriteSheetPixelFlip.FlipCells(pixels, 4, 2, 2, 1, flipX: true, flipY: false);

            Assert.AreEqual(C(4), pixels[0]);
            Assert.AreEqual(C(3), pixels[1]);
            Assert.AreEqual(C(8), pixels[2]);
            Assert.AreEqual(C(7), pixels[3]);
            Assert.AreEqual(C(2), pixels[4]);
            Assert.AreEqual(C(1), pixels[5]);
            Assert.AreEqual(C(6), pixels[6]);
            Assert.AreEqual(C(5), pixels[7]);
        }

        [Test]
        public void FlipCellRectUvMirrorsTopLeftBox()
        {
            var rect = new Rect(0.1f, 0.2f, 0.3f, 0.4f);
            var flippedX = SpriteSheetPixelFlip.FlipCellRectUv(rect, flipX: true, flipY: false);
            Assert.AreEqual(0.6f, flippedX.x, 0.0001f);
            Assert.AreEqual(0.2f, flippedX.y, 0.0001f);
            Assert.AreEqual(0.3f, flippedX.width, 0.0001f);
            Assert.AreEqual(0.4f, flippedX.height, 0.0001f);

            var flippedY = SpriteSheetPixelFlip.FlipCellRectUv(rect, flipX: false, flipY: true);
            Assert.AreEqual(0.1f, flippedY.x, 0.0001f);
            Assert.AreEqual(0.4f, flippedY.y, 0.0001f);
        }

        [Test]
        public void FlipFacingSwapsLeftAndRight()
        {
            Assert.AreEqual(SpriteFacingDirection.Left,
                SpriteSheetPixelFlip.FlipFacing(SpriteFacingDirection.Right, true, false));
            Assert.AreEqual(SpriteFacingDirection.Right,
                SpriteSheetPixelFlip.FlipFacing(SpriteFacingDirection.Left, true, false));
            Assert.AreEqual(SpriteFacingDirection.Up,
                SpriteSheetPixelFlip.FlipFacing(SpriteFacingDirection.Up, true, false));
            Assert.AreEqual(SpriteFacingDirection.Down,
                SpriteSheetPixelFlip.FlipFacing(SpriteFacingDirection.Up, false, true));
        }

        [Test]
        public void RemapProfileFlipsPivotSocketsAndColliders()
        {
            var profile = new SpriteSheetProfile
            {
                Sheets =
                {
                    new SpriteSheetDef { Pivot = new Vector2(0.25f, 0.4f), Columns = 4, Rows = 2 },
                },
                Clips =
                {
                    new SpriteClipDef
                    {
                        SheetIndex = 0,
                        Facing = SpriteFacingDirection.Right,
                        Sockets =
                        {
                            new FrameSocketDef
                            {
                                LocalPosition = new Vector2(10f, 4f),
                                LocalAngle = 15f,
                            },
                        },
                    },
                },
                Hitboxes =
                {
                    new FrameBoxDef
                    {
                        ClipName = "Idle",
                        RectUV = new Rect(0.1f, 0.2f, 0.3f, 0.4f),
                        Angle = 20f,
                    },
                },
            };
            profile.Clips[0].Name = "Idle";
            profile.Clips[0].EnsureFrameData();
            profile.Clips[0].OnionOffsets[0] = new Vector2(3f, -1f);
            profile.EnsureSocketMotions();
            profile.SocketMotions.Add(new SpriteSocketMotionTrack
            {
                ReferenceSheetIndex = 0,
                Keys =
                {
                    new SpriteSocketMotionKey
                    {
                        LocalPosition = new Vector2(8f, 2f),
                        InTangent = new Vector2(1f, 0f),
                        OutTangent = new Vector2(2f, 0f),
                        ArcClockwise = true,
                    },
                },
            });

            SpriteSheetPixelFlip.RemapProfileAfterCellFlip(profile, 0, flipX: true, flipY: false);

            Assert.AreEqual(0.75f, profile.Sheets[0].Pivot.x, 0.0001f);
            Assert.AreEqual(0.4f, profile.Sheets[0].Pivot.y, 0.0001f);
            Assert.AreEqual(SpriteFacingDirection.Left, profile.Clips[0].Facing);
            Assert.AreEqual(new Vector2(-10f, 4f), profile.Clips[0].Sockets[0].LocalPosition);
            Assert.AreEqual(-15f, profile.Clips[0].Sockets[0].LocalAngle);
            Assert.AreEqual(new Vector2(-3f, -1f), profile.Clips[0].OnionOffsets[0]);
            Assert.AreEqual(0.6f, profile.Hitboxes[0].RectUV.x, 0.0001f);
            Assert.AreEqual(-20f, profile.Hitboxes[0].Angle);
            Assert.AreEqual(new Vector2(-8f, 2f), profile.SocketMotions[0].Keys[0].LocalPosition);
            Assert.AreEqual(new Vector2(-1f, 0f), profile.SocketMotions[0].Keys[0].InTangent);
            Assert.IsFalse(profile.SocketMotions[0].Keys[0].ArcClockwise);
        }
    }
}
