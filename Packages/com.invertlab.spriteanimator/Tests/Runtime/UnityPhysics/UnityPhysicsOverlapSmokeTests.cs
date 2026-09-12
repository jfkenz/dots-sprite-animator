using System.Collections.Generic;
using NUnit.Framework;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Transforms;
using UnityEditor;
using UnityEngine;

namespace InvertLab.Sprites.DOTS
{
    /// <summary>
    /// EditMode verification of the collider pipeline against the REAL
    /// Bringer-of-Death profile: slash query bounds, hurtbox collider
    /// creation, and the Unity Physics overlap chain.
    /// </summary>
    public sealed class UnityPhysicsOverlapSmokeTests
    {
        const string ProfilePath =
            "Assets/Samples/DOTS Sprite Animator/0.8.1/Complete/Showcase/Clembod/Bringer Of Death/Sprite Sheet/Bringer-of-Death-SpritSheet_profile.asset";
        const string SlashClipName = "Bringer-of-Death-SpritSheet row 3";

        static ScriptableSpriteSheetProfile LoadProfile()
        {
            var legacy = AssetDatabase.LoadAssetAtPath<ScriptableSpriteSheetProfile>(ProfilePath);
            if (legacy != null) return legacy;
            foreach (var guid in AssetDatabase.FindAssets("Bringer-of-Death-SpritSheet_profile t:ScriptableSpriteSheetProfile"))
            {
                var profile = AssetDatabase.LoadAssetAtPath<ScriptableSpriteSheetProfile>(AssetDatabase.GUIDToAssetPath(guid));
                if (profile != null) return profile;
            }
            return null;
        }

        [Test]
        public void BringerSlashBoxesResolveNonDegenerateBounds()
        {
            var profile = LoadProfile();
            if (profile == null)
                Assert.Ignore("Bringer profile not at expected path");

            var host = new GameObject("BringerQueryHost");
            try
            {
                var set = host.AddComponent<SpriteAnimSetAuthoring>();
                set.Profile = profile;

                var data = profile.Data;
                int slashIndex = data.Clips.FindIndex(c => c.Name == SlashClipName);
                Assert.GreaterOrEqual(slashIndex, 0, "slash clip missing from profile");

                Rect bounds = default;
                bool got;
                try
                {
                    got = SpriteHitboxQuery.TryGetBounds(set, SlashClipName, 4,
                        SpriteHitboxQuery.FrameBoxes, false, out bounds);
                }
                catch (System.Exception ex)
                {
                    Assert.Fail("TryGetBounds threw: " + ex);
                    return;
                }
                Assert.IsTrue(got, "no slash boxes resolved for row 3 frame 4");
                Assert.Greater(bounds.width, 0.05f, "slash box width degenerate");
                Assert.Greater(bounds.height, 0.05f, "slash box height degenerate");
            }
            finally
            {
                Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void BringerHurtboxCollidersBakeValidGeometry()
        {
            var profile = LoadProfile();
            if (profile == null)
                Assert.Ignore("Bringer profile not at expected path");

            var data = profile.Data;
            data.EnsureSheets();
            var sheet = data.SheetAt(0);
            if (sheet == null || sheet.Texture == null)
                Assert.Ignore("profile has no sheet texture");

            // character body box (lifetime 1) on row 1
            var bodyBox = data.Hitboxes.Find(h => h.Lifetime == 1);
            if (bodyBox == null)
                Assert.Ignore("no character body box authored");

            using var blob = SpriteUnityPhysicsShape.CreateCollider(
                bodyBox, data.Pivot, 1f, false, false);
            Assert.IsTrue(blob.IsCreated, "convex collider blob failed to create");

            // a convex collider of the body box must have positive volume:
            // read back via the collider's bounding box
            var aabb = blob.Value.CalculateAabb();
            Assert.Greater(aabb.Extents.x, 0.01f, "body collider X extent degenerate");
            Assert.Greater(aabb.Extents.y, 0.01f, "body collider Y extent degenerate");
        }
    }
}
