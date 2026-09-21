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
