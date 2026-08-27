using Unity.Entities;
using UnityEngine;

namespace InvertLab.Sprites.DOTS
{
    /// <summary>
    /// Sample-scene crowd driver. Same as <see cref="SpriteCrowdSpawnerAuthoring"/>
    /// plus number-key clip switching and SoldierDemo suppression.
    /// </summary>
    public sealed class AuthoringCrowdDemo : SpriteCrowdSpawnerAuthoring { }

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

            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (kb == null)
                return;

            if (kb.digit1Key.wasPressedThisFrame) spawner.SetAllClips(0);
            if (kb.digit2Key.wasPressedThisFrame) spawner.SetAllClips(1);
            if (kb.digit3Key.wasPressedThisFrame) spawner.SetAllClips(2);
            if (kb.digit4Key.wasPressedThisFrame) spawner.SetAllClips(3);
            if (kb.digit5Key.wasPressedThisFrame) spawner.SetAllClips(4);
            if (kb.digit6Key.wasPressedThisFrame) spawner.SetAllClips(5);
            if (kb.digit7Key.wasPressedThisFrame) spawner.SetAllClips(6);
            if (kb.digit8Key.wasPressedThisFrame) spawner.SetAllClips(7);
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
