using System.Collections;
using NUnit.Framework;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.SceneManagement;

namespace InvertLab.Sprites.DOTS.Tests
{
    public sealed class PartsCombatDemoPlayModeTests
    {
        PartsCombatDemo _demo;
        GameObject _camera;
#if UNITY_EDITOR
        string _importedFolder;
        Scene _loadedScene;
#endif
        Texture2D Atlas()
        {
#if UNITY_EDITOR
            return UnityEditor.AssetDatabase.LoadAssetAtPath<Texture2D>("Packages/com.invertlab.spriteanimator/Runtime/DemoArt/PartsCombatAtlas.png");
#else
            return Texture2D.whiteTexture;
#endif
        }

        [UnitySetUp]
        public IEnumerator Setup()
        {
            _camera = new GameObject("Combat test camera", typeof(Camera));
            var cam = _camera.GetComponent<Camera>();
            cam.orthographic = true;
            cam.orthographicSize = 3.5f;
            cam.transform.position = new Vector3(-0.7f, 0.2f, -10f);
            cam.backgroundColor = new Color(0.035f, 0.065f, 0.085f);
            cam.clearFlags = CameraClearFlags.SolidColor;
            _demo = new GameObject("Combat integration test").AddComponent<PartsCombatDemo>();
            _demo.Atlas = Atlas();
#if UNITY_EDITOR
            _demo.Profile = UnityEditor.AssetDatabase.LoadAssetAtPath<ScriptableSpriteSheetProfile>(
                "Packages/com.invertlab.spriteanimator/Runtime/DemoArt/PartsCombatProfile.asset");
            Assert.IsNotNull(_demo.Profile, "Ship the editable profile with the example.");
#endif
            _demo.ViewCamera = cam;
            _demo.ShowControls = false;
            yield return null;
            yield return null;
            Assert.IsTrue(_demo.Ready);
            var em = World.DefaultGameObjectInjectionWorld.EntityManager;
            var links = em.GetBuffer<SpritePartLink>(_demo.Root);
            Assert.AreEqual(4, links.Length);
            foreach (var link in links)
            {
                Entity sheet = em.GetComponentData<SpriteSheetBinding>(link.Part).Sheet;
                Assert.AreNotEqual(Entity.Null, sheet, "Every default part must be bound, not just the weapon skin.");
                Assert.IsTrue(em.Exists(sheet));
                Assert.IsTrue(em.HasComponent<SpriteSheetAsset>(sheet));
                var art = em.GetComponentData<SpritePartAppearanceState>(link.Part);
                var frame = em.GetComponentData<SpriteAnimFrame>(link.Part);
                var definition = em.GetComponentData<SpriteSheetDefinition>(sheet);
                Assert.AreEqual(art.LogicalWorldSize.x, frame.Scale.x * definition.CellAspect, 0.001f,
                    "Rendered width must match the profile preview, including cropped art.");
                Assert.AreEqual(art.LogicalWorldSize.y, frame.Scale.y, 0.001f);
            }
        }

        [UnityTearDown]
        public IEnumerator Teardown()
        {
            if (_demo != null) Object.Destroy(_demo.gameObject);
            if (_camera != null) Object.Destroy(_camera);
            yield return null;
            yield return null;
#if UNITY_EDITOR
            if (_loadedScene.IsValid() && _loadedScene.isLoaded)
                yield return SceneManager.UnloadSceneAsync(_loadedScene);
            if (!string.IsNullOrEmpty(_importedFolder))
                UnityEditor.AssetDatabase.DeleteAsset(_importedFolder);
            _importedFolder = null;
#endif
        }

        [UnityTest]
        public IEnumerator WalkAimShootSwapPauseAndPhysicsHandoff()
        {
            var em = World.DefaultGameObjectInjectionWorld.EntityManager;
            float start = em.GetComponentData<LocalTransform>(_demo.Root).Position.x;
            _demo.SetMove(new float2(1, 0));
            _demo.AimAt(new float2(3f, 0.7f));
            _demo.Fire();
            yield return new WaitForSeconds(0.25f);
            Assert.Greater(em.GetComponentData<LocalTransform>(_demo.Root).Position.x, start + 0.1f);
            Assert.AreEqual(1, _demo.ShotsFired);
            Assert.AreEqual(1, em.GetComponentData<SpritePartsPlayer>(_demo.Root).ClipIndex);
            _demo.SetMove(float2.zero);
            yield return new WaitForSeconds(0.6f);
            Assert.GreaterOrEqual(_demo.Hits, 1, "Socket-fired shot should hit the practice target.");
            _demo.SwapWeapon();
            Assert.AreEqual(1, _demo.WeaponStyle);
            Assert.AreEqual(3, em.GetComponentData<SpritePartAppearanceState>(_demo.Weapon).SheetTableIndex);
            _demo.SetPaused(true);
            var beforePause = em.GetComponentData<SpritePartsPlayer>(_demo.Root);
            yield return new WaitForSeconds(0.12f);
            Assert.AreEqual(beforePause.TimeSeconds, em.GetComponentData<SpritePartsPlayer>(_demo.Root).TimeSeconds);
            _demo.SetPaused(false);
            _demo.TogglePhysics();
            Assert.IsTrue(em.HasComponent<SpritePartPhysicsOwned>(_demo.Weapon));
            Assert.IsFalse(em.HasComponent<Parent>(_demo.Weapon));
            float3 detachedAt = em.GetComponentData<LocalTransform>(_demo.Weapon).Position;
            yield return new WaitForSeconds(0.25f);
            Assert.Greater(math.distance(detachedAt, em.GetComponentData<LocalTransform>(_demo.Weapon).Position), 0.1f);
            _demo.TogglePhysics();
            yield return null;
            Assert.IsFalse(em.HasComponent<SpritePartPhysicsOwned>(_demo.Weapon));
            Assert.IsTrue(em.HasComponent<Parent>(_demo.Weapon));
            Assert.AreEqual(0, em.GetComponentData<SpritePartsPoseDiagnostics>(_demo.Root).Flags);
            Assert.IsTrue(SpriteInstanceRenderSystem.Active, SpriteInstanceRenderSystem.LastError);
        }

        [UnityTest]
        public IEnumerator MovementKeysDriveWalkFromVelocityAndKeepWeaponOnSameHand()
        {
            var em = World.DefaultGameObjectInjectionWorld.EntityManager;
            var handleKey = typeof(PartsCombatDemo).GetMethod("HandleKey",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            void Key(KeyCode key, bool down) => handleKey.Invoke(_demo, new object[] { key, down });
            Entity parent = em.GetComponentData<Parent>(_demo.Weapon).Value;
            Key(KeyCode.LeftArrow, true);
            Key(KeyCode.UpArrow, true);
            Key(KeyCode.RightArrow, true);
            yield return new WaitForSeconds(0.08f);
            Assert.AreEqual(0f, _demo.Velocity.x, 0.001f);
            Assert.Greater(_demo.Velocity.y, 2f);
            Assert.AreEqual(1, em.GetComponentData<SpritePartsPlayer>(_demo.Root).ClipIndex);

            Key(KeyCode.UpArrow, false);
            Key(KeyCode.RightArrow, false);
            yield return new WaitForSeconds(0.08f);
            Assert.Less(_demo.Velocity.x, -2f);
            float walkTime = em.GetComponentData<SpritePartsPlayer>(_demo.Root).TimeSeconds;
            Key(KeyCode.LeftArrow, false);
            Key(KeyCode.RightArrow, true);
            yield return new WaitForSeconds(0.08f);
            Assert.Greater(_demo.Velocity.x, 2f);
            Assert.AreEqual(1, em.GetComponentData<SpritePartsPlayer>(_demo.Root).ClipIndex);
            Assert.Greater(em.GetComponentData<SpritePartsPlayer>(_demo.Root).TimeSeconds, walkTime,
                "Changing direction must not restart Walk.");

            Key(KeyCode.D, true);
            Key(KeyCode.RightArrow, false);
            yield return new WaitForSeconds(0.06f);
            Assert.Greater(_demo.Velocity.x, 2f, "D remains held when Right Arrow is released.");
            var root = em.GetComponentData<LocalTransform>(_demo.Root);
            root.Position.x = 4f;
            em.SetComponentData(_demo.Root, root);
            yield return new WaitForSeconds(0.08f);
            Assert.AreEqual(float2.zero, _demo.Velocity);
            Assert.AreEqual(0, em.GetComponentData<SpritePartsPlayer>(_demo.Root).ClipIndex,
                "Held input without actual motion must return to Idle.");
            Key(KeyCode.W, true);
            yield return new WaitForSeconds(0.08f);
            Assert.Greater(_demo.Velocity.y, 0f);
            Assert.AreEqual(1, em.GetComponentData<SpritePartsPlayer>(_demo.Root).ClipIndex);
            Key(KeyCode.W, false);
            Key(KeyCode.D, false);
            _demo.AimAt(new float2(-3f, 1.4f));
            yield return new WaitForSeconds(0.06f);
            Assert.AreEqual(0, em.GetComponentData<SpritePartsPlayer>(_demo.Root).ClipIndex);
            Assert.AreEqual(parent, em.GetComponentData<Parent>(_demo.Weapon).Value);
            Assert.AreEqual(0, em.GetComponentData<SpritePartsFacing>(_demo.Root).FlipX,
                "Aiming left must not mirror the weapon to the opposite side.");
        }

        [UnityTest]
        public IEnumerator DestroyAndRespawnLeavesNoOwnedCharactersOrPhysicsBodies()
        {
            var em = World.DefaultGameObjectInjectionWorld.EntityManager;
            Entity root = _demo.Root;
            Entity weapon = _demo.Weapon;
            var atlas = _demo.Atlas;
            _demo.TogglePhysics();
            _demo.Fire();
            Object.Destroy(_demo.gameObject);
            yield return null;
            yield return null;
            Assert.IsFalse(em.Exists(root));
            Assert.IsFalse(em.Exists(weapon));
            using (var query = em.CreateEntityQuery(typeof(PartsCombatDemoLink)))
                Assert.AreEqual(0, query.CalculateEntityCount());
            Assert.IsNull(GameObject.Find("Weapon physics owner"));
            _demo = new GameObject("Respawned combat demo").AddComponent<PartsCombatDemo>();
            _demo.Atlas = atlas;
            _demo.ShowControls = false;
            yield return null;
            yield return null;
            Assert.IsTrue(_demo.Ready);
            Assert.AreNotEqual(root, _demo.Root);
        }

#if UNITY_EDITOR
        [UnityTest]
        public IEnumerator SavedProfileEditsDrivePlaybackAndReorderedSlots()
        {
            var source = _demo.Profile;
            Object.Destroy(_demo.gameObject);
            yield return null;
            string folder = "__PartsCombatTest_" + System.Guid.NewGuid().ToString("N");
            UnityEditor.AssetDatabase.CreateFolder("Assets", folder);
            _importedFolder = "Assets/" + folder;
            string path = _importedFolder + "/EditedProfile.asset";
            Assert.IsTrue(UnityEditor.AssetDatabase.CopyAsset(UnityEditor.AssetDatabase.GetAssetPath(source), path));
            var edited = UnityEditor.AssetDatabase.LoadAssetAtPath<ScriptableSpriteSheetProfile>(path);
            var idle = edited.Data.PartsClips.Find(c => c.Name == "Idle");
            idle.Duration = 2.4f;
            foreach (var key in idle.Tracks.Find(t => t.SlotId == "body").Keys)
                key.Position = new Vector2(0.35f, key.Position.y);
            edited.Data.PartsAppearances.Find(a => a.AppearanceId == "blaster").LogicalWorldSize = new Vector2(2f, 0.8f);
            var weapon = edited.Data.PartsSlots.Find(s => s.SlotId == "weapon");
            edited.Data.PartsSlots.Remove(weapon);
            edited.Data.PartsSlots.Insert(0, weapon);
            UnityEditor.EditorUtility.SetDirty(edited);
            UnityEditor.AssetDatabase.SaveAssets();
            UnityEditor.AssetDatabase.ImportAsset(path, UnityEditor.ImportAssetOptions.ForceUpdate);
            _demo = new GameObject("Edited profile demo").AddComponent<PartsCombatDemo>();
            _demo.Profile = UnityEditor.AssetDatabase.LoadAssetAtPath<ScriptableSpriteSheetProfile>(path);
            _demo.ShowControls = false;
            _demo.ViewCamera = _camera.GetComponent<Camera>();
            // No fallback atlas: the saved profile must supply all art and motion.
            yield return null;
            yield return null;
            Assert.IsTrue(_demo.Ready);
            var em = World.DefaultGameObjectInjectionWorld.EntityManager;
            var blob = em.GetComponentData<SpritePartsSetRef>(_demo.Root).Set;
            Assert.AreEqual(2.4f, blob.Value.Clips[0].Duration);
            Assert.AreEqual(0, em.GetComponentData<SpritePartSlot>(_demo.Weapon).SlotIndex);
            Assert.AreEqual(new float2(2f, 0.8f), em.GetComponentData<SpritePartAppearanceState>(_demo.Weapon).LogicalWorldSize);
            foreach (var link in em.GetBuffer<SpritePartLink>(_demo.Root))
                if (blob.Value.Slots[link.SlotIndex].SlotId.ToString() == "body")
                    Assert.AreEqual(0.35f, em.GetComponentData<LocalTransform>(link.Part).Position.x, 0.001f);
            _demo.Fire();
            yield return new WaitForSeconds(0.1f);
            Assert.AreEqual(1, _demo.ShotsFired);
            _demo.TogglePhysics();
            Assert.IsTrue(_demo.WeaponDetached);
            _demo.TogglePhysics();
            Assert.IsFalse(_demo.WeaponDetached);
            Assert.AreEqual(1.2f, source.Data.PartsClips[0].Duration, "Editing a copy must preserve the template.");
        }

        [UnityTest]
        public IEnumerator ImportedSampleSceneReloadsAndRenders()
        {
            Object.Destroy(_demo.gameObject);
            Object.Destroy(_camera);
            yield return null;
            yield return null;
            string name = "__PartsCombatTest_" + System.Guid.NewGuid().ToString("N");
            UnityEditor.AssetDatabase.CreateFolder("Assets", name);
            _importedFolder = "Assets/" + name;
            string importedScene = _importedFolder + "/PartsCombatExample.unity";
            System.IO.File.Copy("Packages/com.invertlab.spriteanimator/Samples~/Complete/PartsCombatExample/PartsCombatExample.unity", importedScene);
            UnityEditor.AssetDatabase.ImportAsset(importedScene);
            Entity previousRoot = Entity.Null;
            for (int pass = 0; pass < 2; pass++)
            {
                yield return UnityEditor.SceneManagement.EditorSceneManager.LoadSceneAsyncInPlayMode(importedScene,
                    new LoadSceneParameters(LoadSceneMode.Additive));
                _loadedScene = SceneManager.GetSceneByPath(importedScene);
                yield return null;
                yield return null;
                foreach (var go in _loadedScene.GetRootGameObjects())
                    if (go.TryGetComponent<PartsCombatDemo>(out var demo)) _demo = demo;
                Assert.IsNotNull(_demo);
                Assert.IsTrue(_demo.Ready);
                Assert.AreNotEqual(previousRoot, _demo.Root);
                Assert.IsNotNull(_demo.Atlas);
                Assert.IsNotNull(_demo.Profile, "The distributed scene must use the editable profile.");
                _demo.Fire();
                yield return new WaitForSeconds(0.1f);
                Assert.AreEqual(1, _demo.ShotsFired);
                if (pass == 0)
                {
                    // Keep PNG encoding in the Editor assembly: stripped player
                    // projects need not add the ImageConversion module for tests.
                    var capture = System.Type.GetType("InvertLab.Sprites.DOTS.Editor.PartsCombatDemoMenu, InvertLab.SpriteAnimator.Editor");
                    Assert.IsNotNull(capture);
                    var camera = _demo.ViewCamera;
                    var oldTarget = camera.targetTexture;
                    var rendered = new RenderTexture(1280, 720, 24);
                    try
                    {
                        camera.targetTexture = rendered;
                        // Let the normal player loop submit procedural draws and
                        // render the enabled camera, rather than a mid-update render.
                        yield return null;
                        yield return null;
                        yield return null;
                        int pixels = (int)capture.GetMethod("CapturePreview").Invoke(null, new object[] { camera, "Temp/PartsCombatPreview.png" });
                        Assert.Greater(pixels, 1000, "The actual camera output must contain visible demo content.");
                    }
                    finally
                    {
                        camera.targetTexture = oldTarget;
                        rendered.Release();
                        Object.Destroy(rendered);
                    }
                }
                previousRoot = _demo.Root;
                yield return SceneManager.UnloadSceneAsync(_loadedScene);
                yield return null;
                Assert.IsFalse(World.DefaultGameObjectInjectionWorld.EntityManager.Exists(previousRoot));
            }
        }
#endif
    }
}
