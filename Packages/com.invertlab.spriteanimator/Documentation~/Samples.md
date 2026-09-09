# Samples map

Samples ship inside the package as an optional `Samples~` folder. They are **not** in your project until you import them.

## How to import

1. Window → Package Manager
2. Select **DOTS Sprite Animator**
3. Open the **Samples** tab
4. Import **Complete** (one click — all examples)

Imported path (Unity default):

`Assets/Samples/DOTS Sprite Animator/<version>/Complete/`

On some embedded installs the `<version>` segment may be omitted:
`Assets/Samples/DOTS Sprite Animator/0.8.1/Complete/`

## What’s inside Complete

| Folder | Proves | Needs |
|---|---|---|
| PlaybackApiExample | Play / one-shot / facing | — |
| EventsExample | Frame events | — |
| Sockets | Socket attach points | — |
| CrowdGpuExample | GPU / crowd scale | — |
| ColliderQueryExample | Query + AABB hits | — |
| ColliderEventExample | Unity 2D collider children | — |
| UnityPhysicsExample | Character Physics body + frame AABB OverlapAabb | com.unity.physics |
| Showcase | Art/profiles demo (Clembod) | credit Clembod |

## Unity Physics note

Scripts live under **UnityPhysicsExample**; related event scenes may be under **ColliderEventExample**. Open-scene actors use `SpriteUnityPhysicsHurtbox.Ensure` (not SubScene bake).
