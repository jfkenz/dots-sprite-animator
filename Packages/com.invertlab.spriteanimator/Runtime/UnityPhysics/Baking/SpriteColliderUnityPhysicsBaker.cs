using System.Collections.Generic;
using Unity.Entities;
using Unity.Physics;
using UnityEngine;

namespace InvertLab.Sprites.DOTS
{
    /// <summary>Bakes Character body boxes when Method = UnityPhysics.</summary>
    sealed class SpriteColliderUnityPhysicsBaker : Baker<SpriteColliderAuthoring>
    {
        public override void Bake(SpriteColliderAuthoring authoring)
        {
            if (authoring.Method != SpriteColliderMethod.UnityPhysics)
                return;

            var entity = GetEntity(TransformUsageFlags.Dynamic);
            var set = authoring.GetComponent<SpriteAnimSetAuthoring>();
            if (set != null && set.Profile != null)
                DependsOn(set.Profile);

            var data = set != null && set.Profile != null ? set.Profile.Data : null;
            var sheet = data != null ? SpriteSocketWorld.DisplaySheet(data, null) : null;
            var pivot = data != null && sheet != null
                ? SpriteSocketWorld.ResolvePivot(data, sheet)
                : new Vector2(0.5f, 0.5f);
            float sizeUnits = set != null ? set.SizeUnits : 1f;

            var bodies = new List<FrameBoxDef>();
            SpriteUnityPhysicsShape.CollectCharacterBodyBoxes(data, bodies);
            var blob = SpriteUnityPhysicsShape.CreateBodyCollider(
                bodies, pivot, sizeUnits, false, false);

            AddComponent(entity, new PhysicsCollider { Value = blob });
            AddComponent(entity, new SpriteHurtbox());
            AddComponent(entity, PhysicsMass.CreateKinematic(blob.Value.MassProperties));
            AddComponent(entity, new PhysicsVelocity());
            AddComponent(entity, new PhysicsGravityFactor { Value = 0f });
            AddSharedComponent(entity, new PhysicsWorldIndex { Value = 0 });
        }
    }
}
