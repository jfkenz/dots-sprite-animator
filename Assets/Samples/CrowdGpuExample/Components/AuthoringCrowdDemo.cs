using Unity.Entities;
using UnityEngine;
using UnityEngine.InputSystem;

namespace InvertLab.Sprites.DOTS
{
    /// <summary>
    /// Crowd GPU sample driver. Same as <see cref="SpriteCrowdSpawnerAuthoring"/>
    /// plus number-key clip switching, SoldierDemo suppression, and on-screen help.
    /// </summary>
    public sealed class AuthoringCrowdDemo : SpriteCrowdSpawnerAuthoring
    {
        void OnGUI()
        {
            if (!Application.isPlaying)
                return;

            const float w = 320f;
            var box = new Rect(Screen.width - w - 8f, 8f, w, 132f);
            GUI.Box(box, "Crowd GPU Sample");
            GUILayout.BeginArea(new Rect(box.x + 8f, box.y + 22f, box.width - 16f, box.height - 28f));
            GUILayout.Label("Sprites: " + LiveCount.ToString("N0")
                            + "   GPU: " + (UseGpuAnim ? "ON" : "OFF"));
            GUILayout.Label("1-9 / 0 / [ ]  switch clips");
            GUILayout.Label("Inspector: Spawn Batch, +10k, Despawn");
            GUILayout.Label("SpawnOnStart=" + SpawnOnStartCount
                            + "  Batch=" + BatchSize
                            + (Grid ? " grid" : " random"));
            GUILayout.Label("Toggle GPU + Burst on the Crowd Spawner.");
            GUILayout.EndArea();
        }
    }

    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateBefore(typeof(SoldierDemoInputSystem))]
    public partial class AuthoringCrowdDemoInputSystem : SystemBase
    {
        static bool _soldierDisabled;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            _soldierDisabled = false;
        }

        protected override void OnUpdate()
        {
            var spawner = Object.FindFirstObjectByType<SpriteCrowdSpawnerAuthoring>();
            if (spawner == null)
                return;

            DisableSoldierDemo();

            if (!spawner.NumberKeysSwitchClips)
                return;

            var authoring = spawner.Source != null
                ? spawner.Source
                : Object.FindFirstObjectByType<SpriteAnimSetAuthoring>();
            int clipCount = authoring != null && authoring.Clips != null
                ? authoring.Clips.Length
                : 0;
            if (clipCount <= 0)
                return;

            var kb = Keyboard.current;
            if (kb == null)
                return;

            int current = 0;
            var player = authoring.GetComponent<SpriteAnimPlayerAuthoring>();
            if (player != null)
                current = Mathf.Clamp(player.ClipIndex, 0, clipCount - 1);

            if (kb.leftBracketKey.wasPressedThisFrame)
                spawner.SetAllClips((current - 1 + clipCount) % clipCount);
            if (kb.rightBracketKey.wasPressedThisFrame)
                spawner.SetAllClips((current + 1) % clipCount);

            if (clipCount >= 1 && kb.digit1Key.wasPressedThisFrame) spawner.SetAllClips(0);
            if (clipCount >= 2 && kb.digit2Key.wasPressedThisFrame) spawner.SetAllClips(1);
            if (clipCount >= 3 && kb.digit3Key.wasPressedThisFrame) spawner.SetAllClips(2);
            if (clipCount >= 4 && kb.digit4Key.wasPressedThisFrame) spawner.SetAllClips(3);
            if (clipCount >= 5 && kb.digit5Key.wasPressedThisFrame) spawner.SetAllClips(4);
            if (clipCount >= 6 && kb.digit6Key.wasPressedThisFrame) spawner.SetAllClips(5);
            if (clipCount >= 7 && kb.digit7Key.wasPressedThisFrame) spawner.SetAllClips(6);
            if (clipCount >= 8 && kb.digit8Key.wasPressedThisFrame) spawner.SetAllClips(7);
            if (clipCount >= 9 && kb.digit9Key.wasPressedThisFrame) spawner.SetAllClips(8);
            if (clipCount >= 10 && kb.digit0Key.wasPressedThisFrame) spawner.SetAllClips(9);
        }

        static void DisableSoldierDemo()
        {
            if (_soldierDisabled)
                return;

            var world = World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated)
                return;

            var sys = world.GetExistingSystemManaged<SoldierDemoInputSystem>();
            if (sys != null)
                sys.Enabled = false;

            _soldierDisabled = true;
        }
    }
}