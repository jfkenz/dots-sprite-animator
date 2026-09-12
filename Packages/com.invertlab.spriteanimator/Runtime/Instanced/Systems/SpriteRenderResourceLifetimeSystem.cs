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
        public ComputeBuffer GpuBuffer { get; private set; }
        public Material GpuMaterial { get; private set; }
        public Bounds GpuBounds;
        int gpuCapacity;

        public void EnsureGpuBatch(int count, Texture2D sheet)
        {
            if (GpuBuffer == null || count > gpuCapacity)
            {
                GpuBuffer?.Dispose();
                gpuCapacity = Mathf.NextPowerOfTwo(Mathf.Max(4096, count));
                GpuBuffer = new ComputeBuffer(gpuCapacity, SpriteGpuAnimResources.Stride);
            }
            var shader = Shader.Find(SpriteShaderLibrary.ActiveGpuAnimShader);
            if (GpuMaterial == null) GpuMaterial = new Material(shader);
            else if (GpuMaterial.shader != shader) GpuMaterial.shader = shader;
            GpuMaterial.mainTexture = sheet;
            GpuMaterial.SetFloat("_Cutoff", .02f);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetOwners() => Owners.Clear();

        protected override void OnCreate() => Owners.Add(World);
        protected override void OnUpdate() { }

        protected override void OnDestroy()
        {
            EntityManager.CompleteAllTrackedJobs();
            GpuBuffer?.Dispose();
            GpuBuffer = null;
            DestroyOwnedObject(GpuMaterial);
            GpuMaterial = null;
            SpriteSheetRegistry.ReleaseWorld(World.SequenceNumber);
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
