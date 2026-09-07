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

## See also

- QuickStart.md
- Samples.md
- PureOverlap/PureOverlap.md
