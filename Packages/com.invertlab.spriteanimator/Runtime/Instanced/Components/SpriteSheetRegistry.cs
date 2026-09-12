using System.Collections.Generic;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace InvertLab.Sprites.DOTS
{
    /// <summary>Grid description of one sprite sheet (per-sheet, not global).</summary>
    public struct SpriteSheetDefinition : IComponentData
    {
        public int Cols;
        public int Rows;
        /// <summary>Pixel width / height of one sheet cell. 0 or 1 = square.</summary>
        public float CellAspect;
        /// <summary>1 when a per-slot CropST buffer is installed (Cropped layout).</summary>
        public byte UseCellCrops;
    }

    /// <summary>Managed texture reference on a sheet-definition entity (few of these).</summary>
    public class SpriteSheetAsset : IComponentData
    {
        public Texture2D Texture;
    }

    /// <summary>
    /// Binds a sprite entity to its sheet-definition entity. Entity.Null (or
    /// missing component) = legacy default sheet (SpriteRenderResources.Sheet).
    /// </summary>
    public struct SpriteSheetBinding : IComponentData
    {
        public Entity Sheet;
    }

    /// <summary>Per-clip sheet entity (index = clip index). Written by the
    /// set baker; <see cref="SpriteClipSheetSystem"/> mirrors the active
    /// clip's entry into <see cref="SpriteSheetBinding"/> on clip change.</summary>
    public struct SpriteClipSheetBindingEntry : IBufferElementData
    {
        public Entity Sheet;
    }

    /// <summary>Written by the registration system once the managed record exists.</summary>
    public struct SpriteSheetRegistered : IComponentData
    {
        /// <summary>Index into SpriteSheetRegistry.Records. −1 = invalid texture.</summary>
        public int RegistryId;
    }

    /// <summary>Per-sheet GPU record: own material, buffer, staging slice, grid.</summary>
    public class SpriteSheetRecord
    {
        public Texture2D Texture;
        public Material Material;
        /// <summary>Only registry-created materials are destroyed with this record.</summary>
        public bool OwnsMaterial;
        public ComputeBuffer Buffer;
        public int Capacity;

        public int Count;
        public int Cols = 4;
        public int Rows = 4;
        public float CellAspect = 1f;
        public byte UseCellCrops;
        public NativeArray<float4> Crops;
        public ulong WorldSequence;

        public void EnsureCapacity(int need)
        {
            if (Buffer != null && need <= Capacity)
                return;
            int cap = math.max(4096, Capacity);
            while (cap < need) cap *= 2;
            Buffer?.Dispose();
            Buffer = new ComputeBuffer(cap, SpriteRenderResources.Stride);
            Capacity = cap;
        }

        public void SetCrops(float4[] crops)
        {
            UseCellCrops = crops != null && crops.Length > 0 ? (byte)1 : (byte)0;
            if (Crops.IsCreated) Crops.Dispose();
            if (UseCellCrops != 0)
            {
                Crops = new NativeArray<float4>(crops.Length, Allocator.Persistent);
                for (int i = 0; i < crops.Length; i++)
                    Crops[i] = crops[i];
            }
            else
            {
                Crops = new NativeArray<float4>(0, Allocator.Persistent);
            }
        }

        public void Dispose()
        {
            Buffer?.Dispose();
            Buffer = null;
            if (Crops.IsCreated) Crops.Dispose();
            Crops = default;
            if (OwnsMaterial && Material != null)
                SpriteRenderResourceLifetimeSystem.DestroyOwnedObject(Material);
            Material = null;
            OwnsMaterial = false;
            Texture = null;
            Capacity = 0;
            Count = 0;
        }
    }

    /// <summary>
    /// Managed registry of sheet GPU records, one per distinct world and layout. Baked
    /// sheet entities (SpriteSheetDef + SpriteSheetAsset) are registered here
    /// by <see cref="SpriteSheetRegistrationSystem"/>; the render system draws
    /// one instanced batch per record. Record 0 slot is typically the legacy
    /// default sheet seeded from SpriteRenderResources.Sheet.
    /// </summary>
    public static class SpriteSheetRegistry
    {
        public static readonly List<SpriteSheetRecord> Records = new List<SpriteSheetRecord>();

        /// <summary>
        /// Get or create the record for a texture. Deduplicates by texture
        /// entity id, so multiple sheet entities sharing one atlas land on
        /// one record (one draw call). Returns −1 for a null texture or a
        /// missing shader.
        /// </summary>
        public static int GetOrAdd(Texture2D texture)
            => GetOrAdd(texture, 4, 4, 1f, null);

        /// <summary>Batch identity includes world, grid, aspect and every crop, not just the texture.</summary>
        public static int GetOrAdd(Texture2D texture, int cols, int rows, float aspect,
            float4[] crops, ulong worldSequence = 0)
        {
            if (texture == null)
                return -1;
            cols = math.max(1, cols);
            rows = math.max(1, rows);
            aspect = math.isfinite(aspect) && aspect > 0.01f ? aspect : 1f;
            int cropCount = crops?.Length ?? 0;
            for (int i = 0; i < Records.Count; i++)
            {
                var candidate = Records[i];
                if (candidate.Texture != texture || candidate.Material == null ||
                    candidate.WorldSequence != worldSequence || candidate.Cols != cols ||
                    candidate.Rows != rows || candidate.CellAspect != aspect ||
                    (candidate.Crops.IsCreated ? candidate.Crops.Length : 0) != cropCount) continue;
                bool same = true;
                for (int c = 0; c < cropCount; c++)
                    if (!math.all(candidate.Crops[c] == crops[c])) { same = false; break; }
                if (same) return i;
            }

            var shader = Shader.Find(SpriteShaderLibrary.ActiveInstancedShader);
            if (shader == null)
                return -1;

            var record = new SpriteSheetRecord
            {
                Texture = texture,
                Cols = cols,
                Rows = rows,
                CellAspect = aspect,
                WorldSequence = worldSequence,
                OwnsMaterial = true,
                Material = new Material(shader),
            };
            record.Material.mainTexture = texture;
            record.Material.SetFloat("_Cutoff", 0.02f);
            record.SetCrops(crops);
            for (int i = 0; i < Records.Count; i++)
                if (Records[i].Material == null && Records[i].Texture == null)
                { Records[i] = record; return i; }
            Records.Add(record);
            return Records.Count - 1;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        public static void Reset()
        {
            foreach (var record in Records)
                record.Dispose();
            Records.Clear();
        }

        internal static void ReleaseWorld(ulong sequence)
        {
            // Keep indices stable for the remaining worlds' ECS registration components.
            foreach (var record in Records)
                if (record.WorldSequence == sequence) record.Dispose();
        }

        /// <summary>Test hook: drop every record without waiting for domain reload.</summary>
        public static void ClearForTests() => Reset();

        /// <summary>
        /// Switch cached materials in place, preserving their texture and buffers.
        /// </summary>
        public static void ResetMaterials()
        {
            var shader = Shader.Find(SpriteShaderLibrary.ActiveInstancedShader);
            if (shader == null)
                return;
            foreach (var record in Records)
            {
                if (record.Material != null)
                    record.Material.shader = shader;
            }
        }
    }

    /// <summary>
    /// Swaps a sprite's sheet binding when its current clip is authored on a
    /// different sheet (multi-atlas profiles). Cheap per-frame compare; only
    /// writes on actual clip-driven changes. Entities without the per-clip
    /// buffer (static sprites, single-sheet sets) never match the query.
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [BurstCompile]
    public partial struct SpriteClipSheetSystem : ISystem
    {
        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            foreach (var (binding, buffer, player) in
                     SystemAPI.Query<RefRW<SpriteSheetBinding>,
                                     DynamicBuffer<SpriteClipSheetBindingEntry>,
                                     RefRO<SpriteAnimPlayer>>())
            {
                if (buffer.Length == 0)
                    continue;
                int clip = player.ValueRO.ClipIndex;
                if ((uint)clip >= (uint)buffer.Length)
                    continue;
                var target = buffer[clip].Sheet;
                if (binding.ValueRO.Sheet != target)
                    binding.ValueRW.Sheet = target;
            }
        }
    }

    /// <summary>
    /// Registers baked sheet entities (SpriteSheetDef + SpriteSheetAsset) into
    /// the managed registry once, tagging them with SpriteSheetRegistered.
    /// Runs in the plain simulation bucket, before the OrderLast renderers.
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial class SpriteSheetRegistrationSystem : SystemBase
    {
        protected override void OnUpdate()
        {
            using var sheetQ = EntityManager.CreateEntityQuery(
                ComponentType.ReadOnly<SpriteSheetDefinition>(),
                ComponentType.ReadOnly<SpriteSheetAsset>(),
                ComponentType.Exclude<SpriteSheetRegistered>());
            var sheets = sheetQ.ToEntityArray(Allocator.Temp);
            if (sheets.Length == 0)
            {
                sheets.Dispose();
                return;
            }

            foreach (var sheetEntity in sheets)
            {
                var def = EntityManager.GetComponentData<SpriteSheetDefinition>(sheetEntity);
                var asset = EntityManager.GetComponentObject<SpriteSheetAsset>(sheetEntity);

                float4[] crops = null;
                if (def.UseCellCrops != 0 && EntityManager.HasBuffer<SpriteAnimCellCrop>(sheetEntity))
                {
                    var buf = EntityManager.GetBuffer<SpriteAnimCellCrop>(sheetEntity);
                    crops = new float4[buf.Length];
                    for (int c = 0; c < buf.Length; c++) crops[c] = buf[c].Value;
                }
                int id = SpriteSheetRegistry.GetOrAdd(asset.Texture, def.Cols, def.Rows,
                    def.CellAspect, crops, World.SequenceNumber);

                EntityManager.AddComponentData(sheetEntity, new SpriteSheetRegistered { RegistryId = id });
            }

            sheets.Dispose();
        }
    }
}
