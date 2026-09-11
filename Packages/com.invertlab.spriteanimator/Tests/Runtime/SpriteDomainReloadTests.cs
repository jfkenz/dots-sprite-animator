using System.Collections;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace InvertLab.Sprites.DOTS.Tests
{
    public sealed class SpriteDomainReloadTests
    {
        bool _optionsEnabled;
        EnterPlayModeOptions _options;
        Texture2D _texture;

        [SetUp]
        public void SaveOptions()
        {
            _optionsEnabled = EditorSettings.enterPlayModeOptionsEnabled;
            _options = EditorSettings.enterPlayModeOptions;
            EditorSettings.enterPlayModeOptionsEnabled = true;
            EditorSettings.enterPlayModeOptions = EnterPlayModeOptions.DisableDomainReload;
        }

        [UnityTearDown]
        public IEnumerator RestoreOptions()
        {
            if (EditorApplication.isPlaying) yield return new ExitPlayMode();
            EditorSettings.enterPlayModeOptions = _options;
            EditorSettings.enterPlayModeOptionsEnabled = _optionsEnabled;
            if (_texture != null) Object.DestroyImmediate(_texture);
        }

        [UnityTest]
        public IEnumerator ReenteringPlayWithoutDomainReloadResetsRenderResources()
        {
            _texture = new Texture2D(8, 8);
            for (int cycle = 0; cycle < 2; cycle++)
            {
                SpriteGpuAnimResources.EnsureCapacity(1);
                SpriteGpuAnimResources.EnsureObjects(_texture);
                SpriteRenderResources.EnsureCapacity(1);
                var material = SpriteGpuAnimResources.Material;
                yield return new EnterPlayMode(false);
                yield return null;
                Assert.IsNull(SpriteGpuAnimResources.Buffer);
                Assert.IsFalse(SpriteGpuAnimResources.Staging.IsCreated);
                Assert.IsFalse(SpriteRenderResources.Staging.IsCreated);
                Assert.IsTrue(material == null);
                Assert.IsFalse(SpriteGpuAnimResources.UseSharedClip);
                yield return new ExitPlayMode();
            }
        }
    }
}
