# Architecture (mental model)

Publisher: **Invert Lab**. Package: `com.invertlab.spriteanimator`.

## Core animation pipeline

```
Profile (Window > DOTS Sprite Animator)
    -> clips / frames / TRS / events / sockets / collider boxes
SpriteAnimSetAuthoring (+ optional SpriteAnimPlayerAuthoring)
    -> bake (SubScene) or hybrid preview
Play (CPU player, or GPU for simple flipbooks)
    -> SpriteInstance / GPU render
```

Animation works with **no** collider method and **no** Unity Physics package.

## Optional collider layers

| Method | What it does | Needs |
|---|---|---|
| **Query** | Frame/Clip/Character boxes stay data; `SpriteHitboxQuery` AABB | nothing extra |
| **Unity2D** | Spawns Collider2D children | built-in 2D physics |
| **UnityPhysics** | Character body -> Unity Physics Box/Sphere/Convex; frame stays AABB query | optional `com.unity.physics` |

### Lifetime rules (Unity Physics)

- **Character** — persistent hurtbox. Bake Colliders (edit) or `SpriteUnityPhysicsHurtbox.Ensure` (Play / open scene).
- **Frame / Clip** — not baked into Physics. Read AABB via `SpriteHitboxQuery`, then optionally `OverlapAabb` against Character bodies.

```
Attack frame AABB  --OverlapAabb-->  Character PhysicsCollider (hurtbox)
```

## Optional Unity Physics assembly

- Assembly: `InvertLab.SpriteAnimator.UnityPhysics`
- Compiles only when `com.unity.physics` is installed (`INVERTLAB_UNITY_PHYSICS`).
- Core `InvertLab.SpriteAnimator.Runtime` never hard-depends on Physics.

## Two Unity Physics workflows

1. **Bake now** — Method = UnityPhysics → **Bake Colliders** → `UnityPhysicsColliders` children (Box/Sphere/Convex preview).
2. **Skip bake** — on Play, call `SpriteUnityPhysicsHurtbox.Ensure(transform, animSet)` (open-scene sprites are not SubScene-baked).


## PlaybackPath / GPU flipbook limits

`SpriteAnimSetAuthoring.PlaybackPath`:

- **Auto** — CPU default.
- **PreferGpu** — convert once after spawn when the clip is GPU-eligible.
- **ForceCpu** — never use the GPU clock.

The GPU path is a **simple flipbook** (shader clock on one legacy sheet):

- Single sheet only (multi-sheet characters stay on CPU).
- Uniform grid cells (Cropped / per-cell CropST stays on CPU).
- No sockets, animation events, frame reorder, custom holds, or per-frame TRS.

Use PreferGpu for dense crowds / simple loops. Keep heroes and VFX on CPU when you need events or sockets.
API: `SpriteAnims.TryToGpu` / `ToCpu` / `IsGpuDriven`.

## See also

- QuickStart.md
- Samples.md
- PureOverlap/PureOverlap.md

## Samples

Optional `Samples~` — import via Package Manager Samples tab. See Samples.md.

- PublicAPI.md

## Runtime folder layout (ECS tidy)

Under Packages/com.invertlab.spriteanimator/Runtime:

| Folder | Contents |
|--------|----------|
| Authoring/ | MonoBehaviour bakers (*Authoring) |
| Components/ | IComponentData / buffers / related runtime data |
| Systems/ | ISystem / SystemBase playback & events |
| Profile/ | Sheet profile ScriptableObject + authoring data helpers |
| Utility/ | Factories, shaders, playback helpers |
| Feature areas | Nested the same way: Instanced/{Components,Systems}/, Hitboxes/{Components,Systems,Utility}/, Sorting/{Authoring,Components,Systems}/, Spawn/{Authoring,Systems}/, Culling/Systems/, PureOverlap/Utility/, UnityPhysics/{Authoring,Components,Baking}/ |

### Naming convention

- Authoring: FooAuthoring (suffix)
- Systems: FooSystem (suffix)
- Data: prefer Foo for IComponentData; use FooData only if Foo collides with a MonoBehaviour; avoid FooComponent
