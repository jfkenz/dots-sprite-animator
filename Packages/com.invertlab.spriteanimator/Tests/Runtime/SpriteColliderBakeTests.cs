using System.Collections.Generic;
using NUnit.Framework;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;

namespace InvertLab.Sprites.DOTS.Tests
{
    public sealed class SpriteColliderBakeTests
    {
        [Test]
        public void LocalFromUvPutsFullCellAboveBottomPivot()
        {
            var box = new FrameBoxDef
            {
                RectUV = new Rect(0f, 0f, 1f, 1f),
                Shape = SpriteColliderShape.Square,
            };

            Assert.IsTrue(SpriteColliderWorld.TryLocalFromUv(
                box, out var offset, out var size, out float angle));
            // Bottom-center cell pivot: full-cell center sits at local (0, 0.5).
            Assert.AreEqual(0f, offset.x, 0.0001f);
            Assert.AreEqual(0.5f, offset.y, 0.0001f);
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
        public void CharacterExcludeDropsAttackClip()
        {
            var body = new FrameBoxDef
            {
                Lifetime = (byte)SpriteColliderLifetime.Character,
                CharacterExcludeClips = new List<string> { "Attack" },
            };
            var boxes = new List<FrameBoxDef> { body };

            var idle = new List<FrameBoxDef>(
                SpriteColliderWorld.VisibleOn(boxes, "Idle", 0));
            var attack = new List<FrameBoxDef>(
                SpriteColliderWorld.VisibleOn(boxes, "Attack", 0));

            Assert.AreEqual(1, idle.Count);
            Assert.AreEqual(0, attack.Count);
            Assert.IsFalse(body.AppliesToClip("Attack"));
            Assert.IsTrue(body.AppliesToClip("Idle"));
        }

        [Test]
        public void CharacterIncludeOnlyListedClips()
        {
            var body = new FrameBoxDef
            {
                Lifetime = (byte)SpriteColliderLifetime.Character,
                CharacterIncludeClips = new List<string> { "Idle", "Walk" },
            };
            var boxes = new List<FrameBoxDef> { body };

            Assert.AreEqual(1, new List<FrameBoxDef>(
                SpriteColliderWorld.VisibleOn(boxes, "Idle", 0)).Count);
            Assert.AreEqual(1, new List<FrameBoxDef>(
                SpriteColliderWorld.VisibleOn(boxes, "Walk", 0)).Count);
            Assert.AreEqual(0, new List<FrameBoxDef>(
                SpriteColliderWorld.VisibleOn(boxes, "Attack", 0)).Count);
        }

        [Test]
        public void CharacterExcludeWinsOverInclude()
        {
            var body = new FrameBoxDef
            {
                Lifetime = (byte)SpriteColliderLifetime.Character,
                CharacterIncludeClips = new List<string> { "Idle", "Attack" },
                CharacterExcludeClips = new List<string> { "Attack" },
            };
            Assert.IsTrue(body.AppliesToClip("Idle"));
            Assert.IsFalse(body.AppliesToClip("Attack"));
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

        [Test]
        public void PolygonLocalPointsMatchPreviewSpaceInsideRectUv()
        {
            var box = new FrameBoxDef
            {
                RectUV = new Rect(0.1f, 0.2f, 0.4f, 0.5f),
                Shape = SpriteColliderShape.Polygon,
                PolygonUV = new[]
                {
                    new Vector2(0f, 0f),
                    new Vector2(1f, 0f),
                    new Vector2(0.5f, 1f),
                },
            };

            var points = SpriteColliderWorld.PolygonLocalPoints(box);
            Assert.AreEqual(3, points.Length);
            Assert.AreEqual(new Vector2(-0.2f, 0.25f), points[0]);
            Assert.AreEqual(new Vector2(0.2f, 0.25f), points[1]);
            Assert.AreEqual(new Vector2(0f, -0.25f), points[2]);
        }

        [Test]
        public void QueryPolygonBakePreservesShapeAndVertices()
        {
            var profile = new SpriteSheetProfile
            {
                Hitboxes = new List<FrameBoxDef>
                {
                    new()
                    {
                        ClipName = "Attack",
                        FrameIndex = 2,
                        RectUV = new Rect(0.1f, 0.2f, 0.4f, 0.5f),
                        Shape = SpriteColliderShape.Polygon,
                        Physics = (byte)SpriteColliderPhysics.Query,
                        PolygonUV = new[]
                        {
                            new Vector2(0f, 0f),
                            new Vector2(1f, 0f),
                            new Vector2(0.5f, 1f),
                        },
                    },
                },
            };

            var blob = SpriteHitboxSetBuilder.FromProfile(profile, Allocator.Temp);
            FrameBox baked = blob.Value.Clips[0].Boxes[0].Box;

            Assert.AreEqual(SpriteColliderShape.Polygon, baked.Shape);
            Assert.AreEqual(3, baked.Polygon.Length);
            Assert.AreEqual(0.1f, baked.Polygon[0].x, 0.0001f);
            Assert.AreEqual(0.8f, baked.Polygon[0].y, 0.0001f);
            Assert.AreEqual(0.5f, baked.Polygon[1].x, 0.0001f);
            Assert.AreEqual(0.8f, baked.Polygon[1].y, 0.0001f);
            Assert.AreEqual(0.3f, baked.Polygon[2].x, 0.0001f);
            Assert.AreEqual(0.3f, baked.Polygon[2].y, 0.0001f);
            blob.Dispose();
        }

        [Test]
        public void FacingFlipMirrorsRuntimeBoxAndSocketGeometry()
        {
            var polygon = new FixedList128Bytes<float2>();
            polygon.Add(new float2(0.2f, 0.3f));
            var box = new FrameBox
            {
                Center = new float2(0.25f, 0.75f),
                Extents = new float2(0.1f, 0.2f),
                Angle = 30f,
                Polygon = polygon,
            };
            var socket = new SpriteSocketBuffer
            {
                LocalPosition = new float2(2f, 3f),
                LocalAngle = 30f,
                LocalScale = new float2(1f, 2f),
            };
            var flip = new SpriteFlip { X = 1 };

            FrameBox mirroredBox = SpriteFlipUtility.Box(box, flip);
            SpriteSocketBuffer mirroredSocket = SpriteFlipUtility.Socket(socket, flip);

            Assert.AreEqual(0.75f, mirroredBox.Center.x, 0.0001f);
            Assert.AreEqual(0.75f, mirroredBox.Center.y, 0.0001f);
            Assert.AreEqual(-30f, mirroredBox.Angle, 0.0001f);
            Assert.AreEqual(0.8f, mirroredBox.Polygon[0].x, 0.0001f);
            Assert.AreEqual(-2f, mirroredSocket.LocalPosition.x, 0.0001f);
            Assert.AreEqual(3f, mirroredSocket.LocalPosition.y, 0.0001f);
            Assert.AreEqual(-30f, mirroredSocket.LocalAngle, 0.0001f);
            Assert.AreEqual(-1f, mirroredSocket.LocalScale.x, 0.0001f);
        }

        [Test]
        public void UnityColliderRootUsesFacingScale()
        {
            var host = new GameObject("FacingColliderHost");
            try
            {
                var boxes = new List<FrameBoxDef>
                {
                    new()
                    {
                        ClipName = "Idle",
                        FrameIndex = 0,
                        RectUV = new Rect(0.1f, 0.2f, 0.3f, 0.4f),
                        Physics = (byte)SpriteColliderPhysics.Unity2D,
                    },
                };

                SpriteColliderWorld.SyncUnityColliders(
                    host.transform, boxes, "Idle", 0, true, flipX: true, flipY: false);

                Transform root = host.transform.Find(SpriteColliderWorld.RootName);
                Assert.NotNull(root);
                Assert.AreEqual(new Vector3(-1f, 1f, 1f), root.localScale);
                Assert.AreEqual(1, root.childCount);
            }
            finally
            {
                Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void SpriteSortDepthUsesCompactStableSteps()
        {
            Assert.AreEqual(-1f, SpriteSortDepth.FromLayerOrder(1, 0, 0), 0.000001f);
            Assert.AreEqual(-0.001f, SpriteSortDepth.FromLayerOrder(0, 1, 0), 0.000001f);
            // one integer on the whole number line: offset milli = order milli
            Assert.AreEqual(-0.25f, SpriteSortDepth.FromLayerOrder(0, 0, 250), 0.000001f);
            Assert.AreEqual(-2.59f, SpriteSortDepth.FromLayerOrder(2, 340, 250), 0.000001f);
        }

        [Test]
        public void SpriteSortWarnsBeforeOrderOverlapsAnotherLayer()
        {
            Assert.IsTrue(SpriteSortDepth.StaysInsideLayer(400));
            Assert.IsFalse(SpriteSortDepth.StaysInsideLayer(600));
        }

        [Test]
        public void SpriteSortIndexFlattensGridLike3DArray()
        {
            Assert.AreEqual(0f, SpriteSortDepth.FromIndex(0), 0.000001f);
            Assert.AreEqual(-1f, SpriteSortDepth.FromIndex(1000), 0.000001f);
            Assert.AreEqual(-2.34f, SpriteSortDepth.FromIndex(2340), 0.000001f);
            Assert.AreEqual(0.001f, SpriteSortDepth.FromIndex(-1), 0.000001f);

            SpriteSortDepth.DecomposeIndex(2340, out int layer, out int order);
            Assert.AreEqual(2, layer);
            Assert.AreEqual(340, order);

            // negative indices use floor semantics: -1 = layer -1, order 999
            SpriteSortDepth.DecomposeIndex(-1, out int negLayer, out int negOrder);
            Assert.AreEqual(-1, negLayer);
            Assert.AreEqual(999, negOrder);

            // total index folds offset in: layer 2, order 340, offset 125
            Assert.AreEqual(2465, SpriteSortDepth.ToIndex(2, 340, 125));
            Assert.AreEqual(2340, SpriteSortDepth.ToIndex(2, 340));
            Assert.AreEqual(-1, SpriteSortDepth.ToIndex(negLayer, negOrder));
        }

        [Test]
        public void SpriteStaticAuthoringNeedsProfileToResolve()
        {
            var host = new GameObject("StaticAuthoringResolve");
            try
            {
                var authoring = host.AddComponent<SpriteStaticAuthoring>();
                authoring.Row = 3;
                authoring.Column = 5;

                // no profile -> nothing resolves, slot guards to 0
                Assert.IsFalse(authoring.ResolveSheet(out _, out _, out _, out _));
                Assert.AreEqual(0, authoring.CellSlot);
            }
            finally
            {
                Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void SpriteColliderScopeResolvesLifetimeMask()
        {
            var data = new SpriteSheetProfile();
            data.Hitboxes.Add(new FrameBoxDef { Lifetime = 1 }); // character
            data.Hitboxes.Add(new FrameBoxDef { Lifetime = 0 }); // frame

            var host = new GameObject("ColliderAuthoringMask");
            try
            {
                var authoring = host.AddComponent<SpriteColliderAuthoring>();

                // Auto detects exactly the lifetimes present
                authoring.Scope = SpriteColliderScope.Auto;
                Assert.AreEqual(
                    SpriteColliderAuthoring.LifetimeCharacter |
                    SpriteColliderAuthoring.LifetimeFrame,
                    authoring.ResolveLifetimeMask(data));

                // explicit scopes map to their bit
                authoring.Scope = SpriteColliderScope.Character;
                Assert.AreEqual(SpriteColliderAuthoring.LifetimeCharacter,
                    authoring.ResolveLifetimeMask(data));
                authoring.Scope = SpriteColliderScope.All;
                Assert.AreEqual(SpriteColliderAuthoring.LifetimeAll,
                    authoring.ResolveLifetimeMask(data));

                // no boxes at all -> Auto falls back to everything
                data.Hitboxes.Clear();
                Assert.AreEqual(SpriteColliderAuthoring.LifetimeAll,
                    authoring.ResolveLifetimeMask(data));
            }
            finally
            {
                Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void SpriteTintTweenEvaluateShapes()
        {
            Assert.AreEqual(0f, SpriteTintFx.Evaluate(0f, 1f, 0), 0.0001f);
            Assert.AreEqual(1f, SpriteTintFx.Evaluate(1f, 1f, 0), 0.0001f);
            Assert.AreEqual(1f, SpriteTintFx.Evaluate(2f, 1f, 0), 0.0001f, "clamps past end");
            // pingpong folds 0..1..0
            Assert.AreEqual(0.5f, SpriteTintFx.Evaluate(0.25f, 1f, 2), 0.0001f);
            Assert.AreEqual(0.5f, SpriteTintFx.Evaluate(0.75f, 1f, 2), 0.0001f);
            // zero duration snaps to the end
            Assert.AreEqual(1f, SpriteTintFx.Evaluate(0f, 0f, 0), 0.0001f);
        }

        [Test]
        public void SpriteSheetRegistryDedupesByTexture()
        {
            if (Shader.Find(SpriteShaderLibrary.InstancedShader) == null)
                Assert.Ignore("instanced shader not available in test context");

            var textureA = new Texture2D(8, 8);
            var textureB = new Texture2D(16, 16);
            try
            {
                int a1 = SpriteSheetRegistry.GetOrAdd(textureA);
                int a2 = SpriteSheetRegistry.GetOrAdd(textureA);
                int b = SpriteSheetRegistry.GetOrAdd(textureB);

                Assert.AreEqual(a1, a2, "same texture must reuse one record");
                Assert.AreNotEqual(a1, b, "different textures need separate records");
                Assert.GreaterOrEqual(a1, 0);
            }
            finally
            {
                Object.DestroyImmediate(textureA);
                Object.DestroyImmediate(textureB);
                SpriteSheetRegistry.ClearForTests();
            }
        }

        [TestCase(typeof(SpriteAnimSetAuthoring))]
        [TestCase(typeof(SpriteAnimPlayerAuthoring))]
        [TestCase(typeof(SpriteSortAuthoring))]
        public void AddingAnySpriteAuthoringAddsBundleWithoutRemovalLock(System.Type firstType)
        {
            var host = new GameObject("SpriteAuthoringBundleTest");
            try
            {
                host.AddComponent(firstType);
                Assert.NotNull(host.GetComponent<SpriteAnimSetAuthoring>());
                Assert.NotNull(host.GetComponent<SpriteAnimPlayerAuthoring>());
                Assert.NotNull(host.GetComponent<SpriteSortAuthoring>());

                Object.DestroyImmediate(host.GetComponent<SpriteSortAuthoring>());
                Assert.Null(host.GetComponent<SpriteSortAuthoring>());
            }
            finally
            {
                Object.DestroyImmediate(host);
            }
        }
    }
}
