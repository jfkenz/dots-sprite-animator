using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;

namespace InvertLab.Sprites.DOTS
{
    /// <summary>Factory bookkeeping. Do not remove or dispose the blob manually.</summary>
    public struct SpriteAnimOwnedBlob : ICleanupComponentData
    {
        public BlobAssetReference<SpriteAnimSetBlob> Blob;
    }

    /// <summary>Removed automatically by entity destruction, leaving the cleanup record.</summary>
    public struct SpriteAnimBlobOwnerAlive : IComponentData
    {
        public BlobAssetReference<SpriteAnimSetBlob> Blob;
    }

    /// <summary>
    /// Owns factory-created blobs within one world. Instantiated copies share
    /// ownership; the blob is released after the last copy is destroyed.
    /// Baked blobs and caller-built blobs without ownership records are untouched.
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup), OrderLast = true)]
    public partial class SpriteAnimBlobLifetimeSystem : SystemBase
    {
        EntityQuery _owners;
        EntityQuery _newOwners;
        EntityQuery _alive;
        EntityQuery _destroyed;
        readonly HashSet<BlobAssetReference<SpriteAnimSetBlob>> _live = new();
        readonly HashSet<BlobAssetReference<SpriteAnimSetBlob>> _released = new();
        readonly HashSet<BlobAssetReference<SpriteAnimSetBlob>> _worldOwned = new();

        /// <summary>Crowd prototypes retain shared blobs until their entire world is disposed.</summary>
        internal void OwnUntilWorldDisposal(BlobAssetReference<SpriteAnimSetBlob> blob) => _worldOwned.Add(blob);

        protected override void OnCreate()
        {
            _owners = GetEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<SpriteAnimOwnedBlob>() },
                Options = EntityQueryOptions.IncludeDisabledEntities | EntityQueryOptions.IncludePrefab,
            });
            _destroyed = GetEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<SpriteAnimOwnedBlob>() },
                None = new[] { ComponentType.ReadOnly<SpriteAnimBlobOwnerAlive>() },
                Options = EntityQueryOptions.IncludeDisabledEntities | EntityQueryOptions.IncludePrefab,
            });
            _alive = GetEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<SpriteAnimBlobOwnerAlive>() },
                Options = EntityQueryOptions.IncludeDisabledEntities | EntityQueryOptions.IncludePrefab,
            });
            _newOwners = GetEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<SpriteAnimBlobOwnerAlive>() },
                None = new[] { ComponentType.ReadOnly<SpriteAnimOwnedBlob>() },
                Options = EntityQueryOptions.IncludeDisabledEntities | EntityQueryOptions.IncludePrefab,
            });
        }

        protected override void OnUpdate()
        {
            if (_destroyed.IsEmptyIgnoreFilter && _newOwners.IsEmptyIgnoreFilter) return;
            EntityManager.CompleteAllTrackedJobs();
            // Instantiate omits cleanup components. The normal ownership marker
            // survives cloning, so adopt copies before releasing destroyed owners.
            using var newOwners = _newOwners.ToEntityArray(Allocator.Temp);
            foreach (var entity in newOwners)
                EntityManager.AddComponentData(entity, new SpriteAnimOwnedBlob
                {
                    Blob = EntityManager.GetComponentData<SpriteAnimBlobOwnerAlive>(entity).Blob,
                });
            _live.Clear();
            _released.Clear();
            using var alive = _alive.ToComponentDataArray<SpriteAnimBlobOwnerAlive>(Allocator.Temp);
            foreach (var owner in alive) _live.Add(owner.Blob);

            using var destroyed = _destroyed.ToEntityArray(Allocator.Temp);
            foreach (var entity in destroyed)
            {
                var blob = EntityManager.GetComponentData<SpriteAnimOwnedBlob>(entity).Blob;
                if (!_live.Contains(blob) && !_worldOwned.Contains(blob) && _released.Add(blob) && blob.IsCreated)
                    blob.Dispose();
                EntityManager.RemoveComponent<SpriteAnimOwnedBlob>(entity);
            }
        }

        protected override void OnDestroy()
        {
            EntityManager.CompleteAllTrackedJobs();
            _released.Clear();
            foreach (var owned in _worldOwned)
            {
                var blob = owned;
                if (_released.Add(blob) && blob.IsCreated) blob.Dispose();
            }
            _worldOwned.Clear();
            using var owners = _owners.ToComponentDataArray<SpriteAnimOwnedBlob>(Allocator.Temp);
            foreach (var owner in owners)
            {
                var blob = owner.Blob;
                if (_released.Add(blob) && blob.IsCreated) blob.Dispose();
            }
            using var alive = _alive.ToComponentDataArray<SpriteAnimBlobOwnerAlive>(Allocator.Temp);
            foreach (var owner in alive)
            {
                var blob = owner.Blob;
                if (_released.Add(blob) && blob.IsCreated) blob.Dispose();
            }
            _live.Clear();
            _released.Clear();
        }
    }
}
