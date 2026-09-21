using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.TestTools;

namespace InvertLab.Sprites.DOTS.Tests
{
    public class PartsCombatKeyboardPlayModeTests
    {
        Keyboard _keyboard;
        PartsCombatDemo _demo;
        InputSettings _originalSettings, _testSettings;

        [UnityTest]
        public IEnumerator PolledKeyboardChangesFromHorizontalToVerticalWithoutGuiEvents()
        {
            // Batchmode has no focused Game view. Route the virtual keyboard to
            // gameplay explicitly; never persist this override to project settings.
            _originalSettings = InputSystem.settings;
            _testSettings = Object.Instantiate(_originalSettings);
            _testSettings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
#if UNITY_EDITOR
            _testSettings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
#endif
            InputSystem.settings = _testSettings;
            _keyboard = InputSystem.AddDevice<Keyboard>();
            PartsCombatInputSystem.Install();
            _demo = new GameObject("Polled keyboard combat test").AddComponent<PartsCombatDemo>();
            _demo.Atlas = Texture2D.whiteTexture;
#if UNITY_EDITOR
            _demo.Profile = UnityEditor.AssetDatabase.LoadAssetAtPath<ScriptableSpriteSheetProfile>(
                "Packages/com.invertlab.spriteanimator/Runtime/DemoArt/PartsCombatProfile.asset");
#endif
            _demo.ReadKeyboard = true;
            _demo.ShowControls = false;
            yield return null;
            yield return null;
            Assert.IsTrue(_demo.Ready);
            foreach (var key in new[] { Key.LeftArrow, Key.RightArrow, Key.UpArrow, Key.DownArrow, Key.W, Key.S })
            {
                InputSystem.QueueStateEvent(_keyboard, new KeyboardState(key));
                InputSystem.Update();
                Assert.AreSame(_keyboard, Keyboard.current, "Simulated keyboard must be the active device.");
                Assert.IsTrue(_keyboard[key].isPressed, "Queued key state must reach the keyboard.");
                yield return new WaitForSeconds(0.06f);
                Assert.IsTrue(_keyboard[key].isPressed, "Held input must survive the player loop.");
                Assert.AreEqual("Walk", _demo.CurrentClipName, "Held " + key + " must play Walk without OnGUI key events.");
                Assert.Greater(Unity.Mathematics.math.length(_demo.Velocity), 2f);
            }
            InputSystem.QueueStateEvent(_keyboard, new KeyboardState(Key.LeftArrow, Key.RightArrow, Key.UpArrow));
            InputSystem.Update();
            yield return new WaitForSeconds(0.06f);
            Assert.AreEqual("Walk", _demo.CurrentClipName);
            Assert.AreEqual(0f, _demo.Velocity.x, 0.001f);
            Assert.Greater(_demo.Velocity.y, 2f);
            InputSystem.QueueStateEvent(_keyboard, new KeyboardState());
            InputSystem.Update();
            yield return new WaitForSeconds(0.06f);
            Assert.AreEqual("Idle", _demo.CurrentClipName);
        }

        [UnityTearDown]
        public IEnumerator Cleanup()
        {
            if (_demo != null) Object.Destroy(_demo.gameObject);
            if (_keyboard != null && _keyboard.added) InputSystem.RemoveDevice(_keyboard);
            if (_originalSettings != null) InputSystem.settings = _originalSettings;
            if (_testSettings != null) Object.Destroy(_testSettings);
            yield return null;
            yield return null;
        }
    }
}
