# Samples map

Each sample proves one thing. Prefer these over ad-hoc scenes.

| Sample | Proves | Open |
|---|---|---|
| **PlaybackApiExample** | Play / one-shot / facing API | `Assets/Samples/PlaybackApiExample` |
| **EventsExample** | Frame events | `Assets/Samples/EventsExample` |
| **Sockets** | Socket attach points | `Assets/Samples/Sockets` |
| **CrowdGpuExample** | GPU / crowd scale | `Assets/Samples/CrowdGpuExample` |
| **ColliderQueryExample** | Query method + AABB hits (no Physics package) | `Assets/Samples/ColliderQueryExample` |
| **ColliderEventExample** | Unity 2D collider children + events | `Assets/Samples/ColliderEventExample` (use `ColliderEventExample 2` / hybrid scenes) |
| **UnityPhysicsExample** | Method = UnityPhysics; frame AABB × Character body; Bake or runtime Ensure | Scripts under `Assets/Samples/UnityPhysicsExample`; scene `ColliderUnityPhysicEventExample 1` |

## Unity Physics scene note

Player/enemy often live in the **open scene** (not the SubScene). SubScene bakers do not run on them — use **Bake Colliders** for edit preview and/or `SpriteUnityPhysicsHurtbox.Ensure` at Play.

## Showcase

`Assets/Samples/Showcase` — art/profile demos (Clembod), not the minimal integration path.
