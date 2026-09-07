# Samples map

Samples ship inside the package as **optional** ``Samples~`` folders. They are **not** downloaded into your project until you import them.

## How to import

1. Window → Package Manager
2. Select **DOTS Sprite Animator** (In Project / My Registries / embedded)
3. Open the **Samples** tab
4. Import only what you need

Imported path (Unity default):

``Assets/Samples/DOTS Sprite Animator/<version>/<Sample display name>/``

## What each sample proves

| Sample (Package Manager name) | Proves | Needs |
|---|---|---|
| Playback API | Play / one-shot / facing | — |
| Events | Frame events | — |
| Sockets | Socket attach points | — |
| Crowd GPU | GPU / crowd scale | — |
| Collider Query | Query + AABB hits | — |
| Collider Unity 2D Events | Unity 2D collider children | — |
| Unity Physics | Character Physics body + frame AABB OverlapAabb | `com.unity.physics` |
| Showcase (Clembod) | Art/profiles demo | credit Clembod |

## Unity Physics note

Scripts live in the **Unity Physics** sample; related scenes may be under **Collider Unity 2D Events** until fully split. Open-scene actors use ``SpriteUnityPhysicsHurtbox.Ensure`` (not SubScene bake).
