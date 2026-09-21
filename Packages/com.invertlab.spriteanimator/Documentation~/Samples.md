# Samples

**Starter**: generated demo art, CPU/GPU playback, pause/restart/flip controls. No Input System or Unity Physics dependency.

**Complete**: full playback, events, sockets, colliders, crowd, and showcase examples. Interactive scripts require Input System. Unity Physics scripts compile only when Unity Physics is installed. Editor builders locate their imported folder without hard-coding a package version.

# Samples map

## Parts Combat Playground

Use **Tools > DOTS Sprite Animator > Create Parts Combat Example**, or import
Complete and open `PartsCombatExample/PartsCombatExample.unity`. Demonstrates
Idle/Walk blending, independent aiming, recoil, skin swaps, socket-fired shots,
pause, and Unity 2D weapon handoff. Includes generated cutout art and PlayMode
integration tests. The menu creates an editable `PartsCombatProfile` in
`Assets/PartsCombatExample` and opens it in the animator. Edit Idle/Walk, save the
profile, then restart Play to see the changes. The scene Inspector also provides
**Open Profile in Animator** and **Create Editable Profile Copy**. See the sample
README for controls and implementation details.

## Cutout Parts demo

Add **DOTS Sprite Animator / Cutout Parts Demo** to an empty GameObject in a DOTS
project to create a small generated-art character at runtime. Its buttons pause
playback, swap weapons, reset skins, and flip facing. Buttons are available with
either input backend; background mouse/touch shortcuts require the legacy Input
Manager. Clicking the control panel does not trigger those background shortcuts.

For gameplay code, see [Parts gameplay integration](PartsGameplay.md).

Samples ship inside the package as an optional `Samples~` folder. They are **not** in your project until you import them.

## How to import

1. Window → Package Manager
2. Select **DOTS Sprite Animator**
3. Open the **Samples** tab
4. Import **Complete** (one click — all examples)

Imported path (Unity default):

`Assets/Samples/DOTS Sprite Animator/<version>/Complete/`

On some embedded installs the `<version>` segment may be omitted:
`Assets/Samples/DOTS Sprite Animator/1.0.0/Complete/`

## What’s inside Complete

| Folder | Proves | Needs |
|---|---|---|
| PlaybackApiExample | Play / one-shot / facing | — |
| EventsExample | Frame events | — |
| Sockets | Socket attach points | — |
| CrowdGpuExample | GPU / crowd scale | — |
| PureColliderEventExample | Pure overlap hit queries | — |
| ColliderEventExample | Unity 2D collider children | — |
| UnityPhysicsExample | Character Physics body + frame AABB OverlapAabb | com.unity.physics |
| Showcase | Art/profiles demo (Clembod) | credit Clembod |

## Unity Physics note

Scripts live under **UnityPhysicsExample**; related event scenes may be under **ColliderEventExample**. Open-scene actors use `SpriteUnityPhysicsHurtbox.Ensure` (not SubScene bake).
