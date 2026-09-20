using System.Reflection;
using NUnit.Framework;
using Unity.Entities;
using UnityEngine;

namespace InvertLab.Sprites.DOTS.Tests
{
    public sealed class CutoutPartsDemoTests
    {
        const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;

        static void Set(CutoutPartsDemoBootstrap demo, string name, object value)
            => typeof(CutoutPartsDemoBootstrap).GetField(name, PrivateInstance).SetValue(demo, value);

        static object Invoke(CutoutPartsDemoBootstrap demo, string name, params object[] args)
            => typeof(CutoutPartsDemoBootstrap).GetMethod(name, PrivateInstance).Invoke(demo, args);

        [Test]
        public void CleanupRemovesOwnedSheetsAndKeepsUnrelatedEntities()
        {
            using var world = new World("demo sheet lifetime");
            var em = world.EntityManager;
            Entity unrelated = em.CreateEntity();
            var go = new GameObject("demo cleanup test");
            var texture = new Texture2D(4, 4);
            try
            {
                var demo = go.AddComponent<CutoutPartsDemoBootstrap>();
                Set(demo, "_world", world);
                Set(demo, "_em", em);
                Entity sheet = (Entity)Invoke(demo, "CreateSheetEntity", texture, 1, 1);
                Assert.IsTrue(em.Exists(sheet));
                // This EditMode fixture initializes ownership without Start.
                // Exercise the cleanup callback explicitly.
                Invoke(demo, "OnDestroy");
                Object.DestroyImmediate(go);
                Assert.IsFalse(em.Exists(sheet), "Demo-created sheet must not survive the demo.");
                Assert.IsTrue(em.Exists(unrelated));
            }
            finally
            {
                if (go != null) Object.DestroyImmediate(go);
                Object.DestroyImmediate(texture);
            }
        }

        [Test]
        public void CallbacksAfterWorldShutdownDoNotAccessDisposedManager()
        {
            var world = new World("demo shutdown");
            var go = new GameObject("demo shutdown test");
            try
            {
                var demo = go.AddComponent<CutoutPartsDemoBootstrap>();
                Set(demo, "_world", world);
                Set(demo, "_em", world.EntityManager);
                Set(demo, "_root", world.EntityManager.CreateEntity());
                world.Dispose();
                Assert.DoesNotThrow(() => Invoke(demo, "Update"));
                Assert.DoesNotThrow(() => Invoke(demo, "OnGUI"));
                Assert.DoesNotThrow(() => Object.DestroyImmediate(go));
            }
            finally
            {
                if (go != null) Object.DestroyImmediate(go);
                if (world.IsCreated) world.Dispose();
            }
        }

#if !ENABLE_LEGACY_INPUT_MANAGER
        [Test]
        public void UpdateWithoutLegacyInputDoesNotPollDisabledBackend()
        {
            using var world = new World("demo new input");
            var go = new GameObject("demo new input test");
            try
            {
                var demo = go.AddComponent<CutoutPartsDemoBootstrap>();
                Set(demo, "_world", world);
                Set(demo, "_em", world.EntityManager);
                Set(demo, "_root", world.EntityManager.CreateEntity());
                Assert.DoesNotThrow(() => Invoke(demo, "Update"));
            }
            finally { Object.DestroyImmediate(go); }
        }
#endif
    }
}
