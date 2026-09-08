# System run order (Unity + Invert Lab + Physics)

Publisher: **Invert Lab**. Package: com.invertlab.spriteanimator.

This page shows **where our systems sit inside Unity Entities' default groups**, how **Unity Physics** nests under FixedStep, and **where you should put your own systems** (before / after ours).

Attributes in code are the source of truth: [UpdateInGroup], [UpdateBefore], [UpdateAfter], OrderFirst, OrderLast.

## Unity default frame (simplified)

Top-level groups (Unity Entities). Timescale / fixed step apply as usual.

`
InitializationSystemGroup
SimulationSystemGroup          ← most Invert Lab gameplay / animation systems
  ├─ (early / OrderFirst systems)
  ├─ TransformSystemGroup
  └─ (late / OrderLast systems)
FixedStepSimulationSystemGroup ← Unity Physics lives here
  └─ PhysicsSystemGroup
PresentationSystemGroup
`

Invert Lab animation clocks and CPU/GPU flipbook updates run in **SimulationSystemGroup**.
Unity Physics (optional package) runs in **FixedStepSimulationSystemGroup → PhysicsSystemGroup**.

Those are **different clocks**. Frame hitboxes update with the anim player (Simulation). Overlaps against Character Physics bodies should run **after physics exports the world** (AfterPhysics), typically via UnityPhysicsOverlapBridge.

---

## Invert Lab — SimulationSystemGroup

### Ordered chain (explicit edges)

`
OrderFirst
  SpriteFlipBootstrapSystem
  SpriteAnimEventBootstrapSystem
        │
        ▼
  SpriteAnimEventClearSystem ──────────────┐
  SpriteSocketEventClearSystem             │
  SpritePlaybackApplySystem ──┐            │
  SpriteAnimCullingSystem ────┤            │
                              ▼            ▼
                      SpriteAnimPlayerSystem
                              │
          ┌───────────────────┼───────────────────┐
          ▼                   ▼                   ▼
 SpriteHitboxActivation   SpriteAnimEvent    SpriteSocketMotion
        System             DispatchSystem         System
                                                  │
                                    ┌─────────────┴─────────────┐
                                    ▼                           ▼
                         SpriteSocketAttachment      SpriteSocketEvent
                                System                DispatchSystem

OrderLast (upload / draw)
  SpriteGpuAnimRenderSystem
  SpriteInstanceRenderSystem
`

### Also in Simulation (weaker / no edge to Player)

| System | Notes |
|--------|--------|
| SpriteSortDepthSystem | [UpdateBefore(typeof(TransformSystemGroup))] |
| SpriteTintTweenSystem | Simulation only |
| SpriteClipSheetSystem | Simulation only |
| SpriteSheetRegistrationSystem | Simulation only (before OrderLast renderers by convention) |
| SpriteBatchPlaceDriver / SpriteCrowdGpuClipDriver | Managed helpers, default Simulation order |

### Baking (edit / bake world, not play loop)

| System | Group |
|--------|--------|
| SpriteAnimSetPreviewRenderStripBakingSystem | PostBakingSystemGroup |

---

## Unity Physics — FixedStep (optional com.unity.physics)

When the Physics package is installed, Unity registers:

`
FixedStepSimulationSystemGroup
  └─ PhysicsSystemGroup
       ├─ BeforePhysicsSystemGroup     ← write velocities / forces before build
       ├─ PhysicsInitializeGroup       ← BuildPhysicsWorld
       ├─ PhysicsSimulationGroup       ← broadphase → contacts → solve
       ├─ ExportPhysicsWorld
       └─ AfterPhysicsSystemGroup      ← OverlapAabb / read PhysicsWorldSingleton
`

### Invert Lab Physics bridge

| System | Group | Why |
|--------|--------|-----|
| UnityPhysicsOverlapBridge | **AfterPhysicsSystemGroup** | Runs after ExportPhysicsWorld so OverlapAabb hits the rebuilt PhysicsWorldSingleton |

Character hurtboxes are authored/baked (SpriteUnityPhysicsHurtbox / baker). Frame/Clip attack boxes stay **Query AABB** (SpriteHitboxQuery) after SpriteHitboxActivationSystem, then you queue an overlap for the bridge.

Recommended attack flow:

1. Simulation: anim advances → hitboxes activate (SpriteHitboxActivationSystem).
2. Gameplay reads AABB (SpriteHitboxQuery) and calls UnityPhysicsOverlapBridge.Queue(...).
3. FixedStep AfterPhysics: bridge runs OverlapAabb against Character bodies.

---

## Where to put YOUR systems

Use these hooks so you stay correct without fighting our attributes.

### Animation / sockets / events (Simulation)

| You want to… | Put your system… |
|---|---|
| Change playback / force clip before the clock ticks | [UpdateBefore(typeof(SpriteAnimPlayerSystem))] (and usually after SpriteAnimEventClearSystem) |
| Read current frame, events, or live hitboxes | [UpdateAfter(typeof(SpriteAnimPlayerSystem))] — and after SpriteHitboxActivationSystem if you need active boxes |
| Move things after Independent Motion sockets settle | [UpdateAfter(typeof(SpriteSocketAttachmentSystem))] |
| React to socket triggers | [UpdateAfter(typeof(SpriteSocketEventDispatchSystem))] |
| Draw / upload after our GPU packs | [UpdateAfter(typeof(SpriteInstanceRenderSystem))] or Presentation group |
| Adjust sort depth before LocalToWorld | [UpdateBefore(typeof(TransformSystemGroup))] near SpriteSortDepthSystem |

Example:

`csharp
[UpdateInGroup(typeof(SimulationSystemGroup))]
[UpdateAfter(typeof(SpriteHitboxActivationSystem))]
public partial struct MyMeleeResolveSystem : ISystem { /* ... */ }
`

### Unity Physics (FixedStep)

| You want to… | Put your system… |
|---|---|
| Set velocities before the world builds | [UpdateInGroup(typeof(BeforePhysicsSystemGroup))] |
| Overlap / raycast the live world | [UpdateInGroup(typeof(AfterPhysicsSystemGroup))] (same bucket as UnityPhysicsOverlapBridge) |
| Run after our queued overlaps | [UpdateInGroup(typeof(AfterPhysicsSystemGroup))] [UpdateAfter(typeof(UnityPhysicsOverlapBridge))] |

Example:

`csharp
using Unity.Physics.Systems;

[UpdateInGroup(typeof(AfterPhysicsSystemGroup))]
[UpdateAfter(typeof(UnityPhysicsOverlapBridge))]
public partial struct MyDamageApplySystem : ISystem { /* ... */ }
`

### Query-only colliders (no Physics package)

Stay in Simulation after SpriteHitboxActivationSystem and call SpriteHitboxQuery — no FixedStep required.

---

## Quick mental model

1. **Bootstrap / clear** event buffers (OrderFirst + clears).
2. **Playback apply + cull**, then **AnimPlayer** advances the clock.
3. **Hitboxes activate**, **clip events dispatch**, **sockets move / attach / socket events**.
4. **OrderLast** packs GPU/instance draw data.
5. **FixedStep Physics** (optional) builds & simulates; **AfterPhysics** runs UnityPhysicsOverlapBridge and your overlap/damage systems.

Namespaces: InvertLab.Sprites.DOTS (core), Unity.Physics.Systems (Physics groups).
