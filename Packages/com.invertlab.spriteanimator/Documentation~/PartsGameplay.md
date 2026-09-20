# Parts gameplay integration

Parts animates a hierarchy of sprite joints on the CPU. Move the character's root
with your gameplay controller. Use pose overrides for animated hands and weapons.

## Update order and ownership

1. Update root movement, physics-owned parts, and gameplay pose requests.
2. `SpritePartsPlayerSystem` advances clips, applies layers and overrides, writes
   animated joints, then exports socket positions and hitbox bounds.
3. Read `SpritePartSocketWorld` / `SpritePartHitboxWorld` for gameplay.
4. Unity updates transforms; Parts sorting and culling prepare rendering.

Put request systems in `SimulationSystemGroup` with
`[UpdateBefore(typeof(SpritePartsPlayerSystem))]`. Put socket consumers in the same
group with `[UpdateAfter(typeof(SpritePartsPlayerSystem))]`. An earlier read returns
the previous export. Physics must publish its transforms before the pose writer.

The immediate `SpriteParts` control methods can change entity structure. Call
initialization, playback, binding, and ownership changes outside entity query
iteration. Collect entities first or use an EntityCommandBuffer for component
changes, and play it back before the pose writer. Baked/factory roots already have
the override buffers; systems can update those existing buffers during iteration.

## Walk, aim, and recoil

The snippets assume a valid Parts `root`, its `EntityManager em`, and a `weapon`
slot in the profile. Initialize once:

```csharp
SpriteParts.Play(em, root, "Walk", crossfadeSeconds: 0.15f);
SpriteParts.BindSocket(em, root, "muzzle", "weapon", new float2(0.5f, 0f));
```

Write the aiming request before the pose system each update:

```csharp
if (SpriteParts.TryGetSlot(em, root, "weapon", out var weapon))
{
    int slot = em.GetComponentData<SpritePartSlot>(weapon).SlotIndex;
    SpriteParts.SetOverride(em, root, new SpritePartsPoseOverride
    {
        Id = 20001,
        SlotIndex = slot,
        Mode = (byte)SpritePartsPoseMode.LookAt,
        Channels = (byte)SpritePartsPoseChannel.Rotation,
        Space = (byte)SpritePartsPoseSpace.World,
        Target = targetWorldXY,
        Weight = 1f,
    });
    SpriteParts.SetOverride(em, root,
        SpritePartsMotion.Recoil(slot, new float2(-0.1f, 0f), recoilWeight));
}
```

`targetWorldXY` and `recoilWeight` come from your controller. Reduce the recoil
weight toward zero over time. `Recoil` is a parent-local additive offset; it does
not simulate a force or automatically decay. `Spring` accepts an offset and angle
from your own spring simulation. Reuse override IDs to replace requests; use a
different ID for each independently controlled part/effect. Clear an effect with
`SpriteParts.ClearOverride`, or set its weight to zero.

Read the muzzle after the pose system:

```csharp
if (SpriteParts.TryGetSocketWorld(em, root, "muzzle", out var xy, out var degrees))
{
    // Use xy and degrees to queue your projectile spawn.
}
```

Socket rotation follows its transformed positive X axis, including mirroring and
non-uniform parent scale. Hitboxes export world AABBs around the rotated authored
rectangle; they are bounds, not oriented collider shapes. Bind hitboxes once:
`BindHitbox` appends a binding.

## Playback and layers

- `Play` with a different clip and a positive fade blends transforms. Sprite images
  switch according to `SetSpriteSwitch`; this is not a texture-opacity fade.
- `Pause` freezes both clocks and the fade, including after the incoming Once clip
  completes. `Resume` can finish that fade without replaying the completed clip.
- `Restart` clears any fade and immediately samples the current clip at time zero.
- `SetLayer` blends a clip over the base pose before gameplay overrides. Its mask
  uses `1u << slotIndex`; mask zero means all parts. Layers share the base player's
  time and currently have no independent clock or additive-layer mode.

## Physics handoff

`SpritePartPhysicsOwned` grants your controller ownership of a joint's
`LocalTransform` and `PostTransformMatrix`. It does not add a collider, velocity, or
solver. While marked, animation and sorting preserve those transforms.

To detach a joint, add the marker, remove/change its `Parent`, and supply the
transform in the new parent's local space (world space when it has no parent).
Convert the old pose to that space first if it must remain visually stationary.
Sockets and hitboxes follow the actual ECS hierarchy, including animated children
of the physics-owned joint. A detached subtree no longer inherits character facing.

For a return to the rig, stop physics writes, restore the intended `Parent`, then
remove the marker. Animation takes ownership on its next update; this can snap to
the current clip pose. Automatic ragdoll recovery blending is not implemented.
`SpritePartFinalPose` remains the animation target and marks physics-skipped slots;
use the exported attachment buffers when you need the actual attachment position.

World/character overrides are evaluated against the authored animation rig. For
procedural aiming under a dynamically reparented or physics-driven joint, supply
parent-local controls from your physics controller until the joint returns to the
rig. Attachment export supports those hierarchies independently of pose evaluation.
