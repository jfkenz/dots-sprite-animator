using System.Collections.Generic;
using NUnit.Framework;
using Unity.Collections;
using UnityEngine;

namespace InvertLab.Sprites.DOTS.Tests
{
    public sealed class SpriteColliderBakeTests
    {
        [Test]
        public void LocalFromUvPutsFullCellAtOrigin()
        {
            var box = new FrameBoxDef
            {
                RectUV = new Rect(0f, 0f, 1f, 1f),
                Shape = SpriteColliderShape.Square,
            };

            Assert.IsTrue(SpriteColliderWorld.TryLocalFromUv(
                box, out var offset, out var size, out float angle));
            Assert.AreEqual(0f, offset.x, 0.0001f);
            Assert.AreEqual(0f, offset.y, 0.0001f);
            Assert.AreEqual(1f, size.x, 0.0001f);
            Assert.AreEqual(1f, size.y, 0.0001f);
            Assert.AreEqual(0f, angle, 0.0001f);
        }

        [Test]
        public void CharacterBoxesShowOnEveryFrame()
        {
            var boxes = new List<FrameBoxDef>
            {
                new()
                {
                    ClipName = "Idle",
                    FrameIndex = 0,
                    Lifetime = (byte)SpriteColliderLifetime.Character,
                },
                new()
                {
                    ClipName = "Attack",
                    FrameIndex = 2,
                    Lifetime = (byte)SpriteColliderLifetime.Frame,
                },
            };

            var idle = new List<FrameBoxDef>(
                SpriteColliderWorld.VisibleOn(boxes, "Idle", 3));
            var attack = new List<FrameBoxDef>(
                SpriteColliderWorld.VisibleOn(boxes, "Attack", 2));
            var miss = new List<FrameBoxDef>(
                SpriteColliderWorld.VisibleOn(boxes, "Attack", 0));

            Assert.AreEqual(1, idle.Count);
            Assert.IsTrue(idle[0].IsCharacter);
            Assert.AreEqual(2, attack.Count);
            Assert.AreEqual(1, miss.Count);
            Assert.IsTrue(miss[0].IsCharacter);
        }

        [Test]
        public void ClipBoxesShowOnEveryFrameOfThatClipOnly()
        {
            var boxes = new List<FrameBoxDef>
            {
                new()
                {
                    ClipName = "Crouch",
                    FrameIndex = -1,
                    Lifetime = (byte)SpriteColliderLifetime.Clip,
                },
                new()
                {
                    ClipName = "Stand",
                    FrameIndex = -1,
                    Lifetime = (byte)SpriteColliderLifetime.Clip,
                },
                new()
                {
                    ClipName = "Crouch",
                    FrameIndex = 1,
                    Lifetime = (byte)SpriteColliderLifetime.Frame,
                },
            };

            var crouch0 = new List<FrameBoxDef>(
                SpriteColliderWorld.VisibleOn(boxes, "Crouch", 0));
            var crouch1 = new List<FrameBoxDef>(
                SpriteColliderWorld.VisibleOn(boxes, "Crouch", 1));
            var stand = new List<FrameBoxDef>(
                SpriteColliderWorld.VisibleOn(boxes, "Stand", 0));

            Assert.AreEqual(1, crouch0.Count);
            Assert.IsTrue(crouch0[0].IsClip);
            Assert.AreEqual("Crouch", crouch0[0].ClipName);
            Assert.AreEqual(2, crouch1.Count);
            Assert.AreEqual(1, stand.Count);
            Assert.AreEqual("Stand", stand[0].ClipName);
        }

        [Test]
        public void QueryBakePutsClipBoxesOnClipNotShared()
        {
            var profile = new SpriteSheetProfile
            {
                Hitboxes = new List<FrameBoxDef>
                {
                    new()
                    {
                        ClipName = "Crouch",
                        FrameIndex = -1,
                        RectUV = new Rect(0f, 0.5f, 1f, 0.5f),
                        Lifetime = (byte)SpriteColliderLifetime.Clip,
                        Physics = (byte)SpriteColliderPhysics.Query,
                    },
                    new()
                    {
                        ClipName = "Idle",
                        FrameIndex = 0,
                        RectUV = new Rect(0f, 0f, 1f, 1f),
                        Lifetime = (byte)SpriteColliderLifetime.Character,
                        Physics = (byte)SpriteColliderPhysics.Query,
                    },
                },
            };

            var blob = SpriteHitboxSetBuilder.FromProfile(profile, Allocator.Temp);
            Assert.AreEqual(1, blob.Value.Shared.Length);
            Assert.AreEqual(1, blob.Value.Clips.Length);
            Assert.AreEqual(1, blob.Value.Clips[0].Boxes.Length);
            Assert.AreEqual(-1, blob.Value.Clips[0].Boxes[0].FrameIndex);
            blob.Dispose();
        }

        [Test]
        public void Unity2DOnlyBoxesSkipQueryBake()
        {
            var profile = new SpriteSheetProfile
            {
                Hitboxes = new List<FrameBoxDef>
                {
                    new()
                    {
                        ClipName = "Idle",
                        FrameIndex = 0,
                        RectUV = new Rect(0.25f, 0.25f, 0.5f, 0.5f),
                        Physics = (byte)SpriteColliderPhysics.Unity2D,
                    },
                    new()
                    {
                        ClipName = "Idle",
                        FrameIndex = 0,
                        RectUV = new Rect(0f, 0f, 1f, 1f),
                        Lifetime = (byte)SpriteColliderLifetime.Character,
                        Physics = (byte)SpriteColliderPhysics.Query,
                    },
                },
            };

            var blob = SpriteHitboxSetBuilder.FromProfile(profile, Allocator.Temp);
            Assert.AreEqual(1, blob.Value.Shared.Length);
            Assert.AreEqual(0, blob.Value.Clips.Length);
            blob.Dispose();
        }

        [Test]
        public void EditorLockSurvivesCloneWithoutChangingRuntimeVisibility()
        {
            var locked = new FrameBoxDef
            {
                ClipName = "Idle",
                FrameIndex = 0,
                Locked = true,
            };

            FrameBoxDef clone = locked.Clone();
            var visible = new List<FrameBoxDef>(
                SpriteColliderWorld.VisibleOn(new[] { clone }, "Idle", 0));

            Assert.IsTrue(clone.Locked);
            Assert.AreEqual(1, visible.Count);
        }
    }
}
