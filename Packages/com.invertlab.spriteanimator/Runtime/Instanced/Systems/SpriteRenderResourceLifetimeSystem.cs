using System.Collections.Generic;
using Unity.Entities;
using UnityEngine;

namespace InvertLab.Sprites.DOTS
{
    /// <summary>
    /// Shared render resources remain alive while a rendering world exists.
    /// The last world releases them; subsystem registration also resets them
    /// when entering play mode with domain reload disabled.
    /// </summary>
    [UpdateInGroup(typeof(InitializationSystemGroup))]
    public partial class SpriteRenderResourceLifetimeSystem : SystemBase
    {
        static readonly HashSet<World> Owners = new();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetOwners() => Owners.Clear();

        protected override void OnCreate() => Owners.Add(World);
        protected override void OnUpdate() { }

        protected override void OnDestroy()
        {
            EntityManager.CompleteAllTrackedJobs();
            Owners.Remove(World);
            if (Owners.Count != 0) return;
            SpriteSheetRegistry.Reset();
            SpriteGpuAnimResources.Reset();
            SpriteRenderResources.Reset();
        }

        internal static void DestroyOwnedObject(Object value)
        {
            if (value == null) return;
            if (Application.isPlaying) Object.Destroy(value);
            else Object.DestroyImmediate(value);
        }
    }
}
