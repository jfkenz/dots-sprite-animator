using System;
using System.Collections;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;
using NUnit.Framework;
using UnityEngine;

namespace InvertLab.Sprites.DOTS.Tests
{
    public class SpriteProfileFlowTests
    {
        [Test] public void StaticPreviewSurvivesSaveAndClearsPreviousFrameOverrides()
        {
            var go = new GameObject("Static preview regression");
            var profile = ScriptableObject.CreateInstance<ScriptableSpriteSheetProfile>();
            try
            {
                var authoring = go.AddComponent<SpriteStaticAuthoring>();
                profile.Data = Profile();
                authoring.Profile = profile;
                authoring.UseProfileDefaultCell = true;
                var renderer = go.GetComponent<MeshRenderer>();
                var block = new MaterialPropertyBlock();
                block.SetColor("_Color", Color.clear);
                block.SetVector("_CropST", Vector4.zero);
                renderer.SetPropertyBlock(block);
                authoring.UpdatePreview();
                renderer.GetPropertyBlock(block);
                Assert.IsTrue(block.isEmpty, "Converted frame overrides must not hide static art");
                var mesh = go.GetComponent<MeshFilter>().sharedMesh;
                var material = renderer.sharedMaterial;
                authoring.UpdatePreview();
                Assert.AreSame(mesh, go.GetComponent<MeshFilter>().sharedMesh, "Refresh reuses its mesh");
                Assert.AreSame(material, renderer.sharedMaterial, "Refresh reuses its material");
                authoring.PreparePreviewForSceneSave();
                Assert.IsNull(renderer.sharedMaterial, "Transient material must not enter scene serialization");
                Assert.AreNotSame(mesh, go.GetComponent<MeshFilter>().sharedMesh);
                authoring.UpdatePreview();
                Assert.IsTrue(renderer.enabled);
                Assert.AreSame(mesh, go.GetComponent<MeshFilter>().sharedMesh);
                Assert.AreSame(material, renderer.sharedMaterial);
            }
            finally { Object.DestroyImmediate(go); Object.DestroyImmediate(profile); }
        }

        [Test] public void BakingStripsStaticPreviewButKeepsUnrelatedRenderers()
        {
            using var world = new Unity.Entities.World("Preview strip test");
            var em = world.EntityManager;
            Type Find(string name) => AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType(name)).First(t => t != null);
            var meshInfo = Unity.Entities.ComponentType.ReadWrite(Find("Unity.Rendering.MaterialMeshInfo"));
            var disabled = Unity.Entities.ComponentType.ReadWrite(Find("Unity.Rendering.DisableRendering"));
            var sprite = em.CreateEntity(meshInfo, disabled, Unity.Entities.ComponentType.ReadWrite<SpriteSheetBinding>(),
                Unity.Entities.ComponentType.ReadWrite<Unity.Entities.Prefab>());
            var unrelated = em.CreateEntity(meshInfo, disabled);
            var system = world.GetOrCreateSystem(Find("InvertLab.Sprites.DOTS.SpriteAnimSetPreviewRenderStripBakingSystem"));
            system.Update(world.Unmanaged);
            Assert.IsFalse(em.HasComponent(sprite, meshInfo));
            Assert.IsTrue(em.HasComponent<SpriteSheetBinding>(sprite), "Custom sprite renderer binding survives");
            Assert.IsTrue(em.HasComponent(unrelated, meshInfo));
        }

        Texture2D texture;
        [SetUp] public void Setup() => texture = new Texture2D(32, 32);
        [TearDown] public void Cleanup() => Object.DestroyImmediate(texture);
        SpriteSheetProfile Profile() => new SpriteSheetProfile
        {
            Sheets = new List<SpriteSheetDef>
            {
                new SpriteSheetDef { Name = "Unused", Texture = texture, Columns = 1, Rows = 1 },
                new SpriteSheetDef { Name = "Shared", Texture = texture, Columns = 4, Rows = 4 }
            },
            StaticSheetIndex = 1, StaticRow = 3, StaticColumn = 2
        };

        [Test] public void NewStaticProfileStartsWholeAndDoesNotReusePreviousDocument()
        {
            var type = AppDomain.CurrentDomain.GetAssemblies().Select(a =>
                a.GetType("InvertLab.Sprites.DOTS.Editor.SpriteSheetToolWindow")).First(t => t != null);
            const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.NonPublic;
            var window = (EditorWindow)ScriptableObject.CreateInstance(type);
            var original = Profile();
            string texturePath = $"Assets/__StaticFlow_{Guid.NewGuid():N}.asset";
            string savedPath = null;
            try
            {
                type.GetField("_profile", flags).SetValue(window, original);
                type.GetMethod("InitializeNewProfile", flags).Invoke(window,
                    new object[] { SpriteAnimKind.Static, false });
                var profile = (SpriteSheetProfile)type.GetField("_profile", flags).GetValue(window);
                Assert.AreNotSame(original, profile);
                Assert.AreEqual(4, original.Sheets[1].Columns, "Previous document remains intact");
                Assert.AreEqual(SpriteAnimKind.Static, profile.AnimKind);
                Assert.AreEqual("Static", type.GetField("_studioTab", flags).GetValue(window).ToString());
                Assert.AreEqual(1, profile.Sheets[0].Columns);
                Assert.AreEqual(1, profile.Sheets[0].Rows);
                Assert.IsEmpty(profile.Clips);
                Assert.IsTrue((bool)type.GetField("_createSeparateProfileOnSave", flags).GetValue(window),
                    "Saving a new profile must not overwrite a sibling using the same texture");
                AssetDatabase.CreateAsset(Object.Instantiate(texture), texturePath);
                var savedTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);
                type.GetMethod("ApplySheetTexture", flags).Invoke(window, new object[] { savedTexture });
                Assert.IsTrue(SpriteProfileSheetOps.HasValidStaticCell(profile));
                Assert.AreEqual(1, profile.Sheets[0].Columns, "Assigning art preserves whole-image slicing");
                type.GetMethod("SaveProfile", flags).Invoke(window, new object[] { true });
                var saved = (ScriptableSpriteSheetProfile)type.GetField("_asset", flags).GetValue(window);
                Assert.IsNotNull(saved);
                savedPath = AssetDatabase.GetAssetPath(saved);
                Assert.AreEqual(SpriteAnimKind.Static, saved.Data.AnimKind, "First save preserves the selected runtime mode");
                var json = SpriteSheetProfile.FromJson(saved.Data.ToJson());
                Assert.AreEqual(SpriteAnimKind.Static, json.AnimKind);
                type.GetMethod("AddSheet", flags).Invoke(window, null);
                Assert.AreEqual(1, profile.Sheets[1].Columns, "Additional static images also start whole");
                Assert.AreEqual(1, profile.Sheets[1].Rows);
            }
            finally
            {
                Object.DestroyImmediate(window);
                if (savedPath != null)
                {
                    AssetDatabase.DeleteAsset(savedPath);
                    string sidecar = System.IO.Path.ChangeExtension(savedPath, ".json");
                    System.IO.File.Delete(sidecar);
                    System.IO.File.Delete(sidecar + ".meta");
                }
                AssetDatabase.DeleteAsset(texturePath);
            }
        }

        [UnityTest] public IEnumerator ExplicitSceneConversionPreservesColliderAndSupportsUndo()
        {
            var asset = ScriptableObject.CreateInstance<ScriptableSpriteSheetProfile>();
            var go = new GameObject("Conversion test");
            go.SetActive(false);
            try
            {
                asset.Data = Profile();
                asset.Data.AnimKind = SpriteAnimKind.Static;
                var old = go.AddComponent<SpriteAnimSetAuthoring>();
                old.Profile = asset;
                var collider = go.AddComponent<BoxCollider2D>();
                collider.size = new Vector2(3, 5);
                var still = go.AddComponent<SpriteStaticAuthoring>();
                still.Profile = asset;
                // Validation itself must not queue deletion of either authoring stack.
                go.SendMessage("OnValidate", SendMessageOptions.DontRequireReceiver);
                yield return null;
                Assert.IsNotNull(go.GetComponent<SpriteAnimSetAuthoring>());
                Assert.IsNotNull(go.GetComponent<SpriteStaticAuthoring>());
                var helper = AppDomain.CurrentDomain.GetAssemblies().Select(a =>
                    a.GetType("InvertLab.Sprites.DOTS.Editor.SpriteProfileSceneSetup")).First(t => t != null);
                bool applied = (bool)helper.GetMethod("Apply").Invoke(null,
                    new object[] { go, asset, SpriteAnimKind.Static });
                Assert.IsTrue(applied);
                Undo.FlushUndoRecordObjects();
                Assert.IsNull(go.GetComponent<SpriteAnimSetAuthoring>());
                Assert.IsTrue(go.GetComponent<SpriteStaticAuthoring>().UseProfileDefaultCell);
                Assert.AreEqual(new Vector2(3, 5), collider.size);
                Undo.PerformUndo();
                Assert.IsNotNull(go.GetComponent<SpriteAnimSetAuthoring>());
                Assert.IsFalse(go.GetComponent<SpriteStaticAuthoring>().UseProfileDefaultCell);
                Undo.PerformRedo();
                Assert.IsNull(go.GetComponent<SpriteAnimSetAuthoring>());
                Assert.IsTrue(go.GetComponent<SpriteStaticAuthoring>().UseProfileDefaultCell);
                Assert.IsNotNull(go.GetComponent<BoxCollider2D>());
            }
            finally { Object.DestroyImmediate(go); Object.DestroyImmediate(asset); }
        }

        [Test] public void DeletingEarlierUnusedSheetPreservesAllWorkspaceBindings()
        {
            var p = Profile();
            p.Clips.Add(new SpriteClipDef { SheetIndex = 1 });
            p.PartsAppearances.Add(new SpritePartAppearanceDef { SheetIndex = 1 });
            p.SocketMotions.Add(new SpriteSocketMotionTrack { ReferenceSheetIndex = 1 });
            Assert.IsTrue(SpriteProfileSheetOps.TryDelete(p, 0, out var reason), reason);
            Assert.AreEqual("Shared", p.Sheets[0].Name);
            Assert.AreEqual(0, p.StaticSheetIndex);
            Assert.AreEqual(14, p.StaticRow * p.Sheets[0].Columns + p.StaticColumn);
            Assert.AreEqual(0, p.Clips[0].SheetIndex);
            Assert.AreEqual(0, p.PartsAppearances[0].SheetIndex);
            Assert.AreEqual(0, p.SocketMotions[0].ReferenceSheetIndex);
        }

        [Test] public void UsedSheetDeletionIsAtomic()
        {
            var p = Profile();
            p.PartsAppearances.Add(new SpritePartAppearanceDef { SheetIndex = 0 });
            Assert.IsFalse(SpriteProfileSheetOps.TryDelete(p, 0, out _));
            Assert.AreEqual(2, p.Sheets.Count);
            Assert.AreEqual(0, p.PartsAppearances[0].SheetIndex);
            Assert.AreEqual(1, p.StaticSheetIndex);
        }

        [Test] public void DeletingStaticDefaultSheetRemapsToRemaining()
        {
            var p = Profile();
            Assert.IsTrue(SpriteProfileSheetOps.TryDelete(p, 1, out var reason), reason);
            Assert.AreEqual(1, p.Sheets.Count);
            Assert.AreEqual("Unused", p.Sheets[0].Name);
            Assert.AreEqual(0, p.StaticSheetIndex);
        }

        [Test] public void StaticSizeUnitsFallBackToSheetWorldHeight()
        {
            var p = Profile();
            float natural = SpriteSheetProfile.GetWorldHeight(p.Sheets[1]);
            Assert.AreEqual(natural, SpriteProfileSheetOps.ResolveStaticSizeUnits(p), 0.0001f);
            p.StaticSizeUnits = 0.4f;
            Assert.AreEqual(0.4f, SpriteProfileSheetOps.ResolveStaticSizeUnits(p), 0.0001f);
        }

        [Test] public void StaticModeRequiresSelectedSheetAndValidCell()
        {
            var p = Profile();
            p.Sheets[1].Texture = null;
            Assert.IsFalse(SpritePartsAuthoringOps.TrySetAnimKind(p, SpriteAnimKind.Static).Ok);
            Assert.AreEqual(SpriteAnimKind.Frame, p.AnimKind);
            p.Sheets[1].Texture = texture;
            p.StaticRow = 4;
            Assert.IsFalse(SpritePartsAuthoringOps.TrySetAnimKind(p, SpriteAnimKind.Static).Ok);
            p.StaticRow = 3;
            Assert.IsTrue(SpritePartsAuthoringOps.TrySetAnimKind(p, SpriteAnimKind.Static).Ok);
        }

        [Test] public void StaticDefaultCellAndInstanceOverrideRemainDistinctAfterSerialization()
        {
            var asset = ScriptableObject.CreateInstance<ScriptableSpriteSheetProfile>();
            var go = new GameObject("Static flow test");
            go.SetActive(false);
            try
            {
                asset.Data = Profile();
                var a = go.AddComponent<SpriteStaticAuthoring>();
                a.Profile = asset;
                a.UseProfileDefaultCell = true;
                a.SheetIndex = 0; a.Row = 0; a.Column = 0;
                Assert.AreEqual(14, a.CellSlot);
                var json = JsonUtility.ToJson(asset.Data);
                asset.Data = JsonUtility.FromJson<SpriteSheetProfile>(json);
                Assert.AreEqual(14, a.CellSlot);
                asset.Data.StaticColumn = 1;
                Assert.AreEqual(13, a.CellSlot);
                a.UseProfileDefaultCell = false;
                Assert.AreEqual(0, a.CellSlot, "Legacy instance overrides are preserved");
            }
            finally { Object.DestroyImmediate(go); Object.DestroyImmediate(asset); }
        }
    }
}
