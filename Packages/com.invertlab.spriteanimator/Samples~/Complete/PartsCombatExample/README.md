# Parts Combat Playground

An interactive example of a floating-parts character with independent aiming,
additive recoil, weapon skins, muzzle sockets, and physics ownership.

## Open it

- Without importing samples: **Tools > DOTS Sprite Animator > Create Parts Combat Example**, then press Play.
- From Package Manager: import **Complete**, open `PartsCombatExample.unity` in
  its `PartsCombatExample` folder, then press Play.

The scene references the package's generated atlas and runtime example components.
It requires the package's normal Entities/URP setup and Unity 2D Physics module.
It does not require the Input System or optional Unity Physics package.

## Controls

| Action | Control |
| --- | --- |
| Move | WASD, arrow keys, or hold the on-screen direction buttons |
| Aim | Enable practice-target aim, or disable it and point inside the arena |
| Fire | Click the arena, press Space, or hold FIRE |
| Swap art | Swap weapon button: blaster / rifle |
| Physics handoff | Drop weapon, let it bounce, then Return weapon to rig |
| Pause | Pause / Resume freezes animation, projectiles, and the dropped weapon |

## Walkthrough

1. Hold FIRE while moving. The body plays Walk while the hand aims and the weapon recoils.
2. Swap weapons. The skin changes and the muzzle binding updates for the new barrel.
3. Aim across the character to exercise mirrored facing.
4. Drop the weapon. It becomes an unparented entity controlled by a Rigidbody2D proxy.
   The body remains animated. Returning restores the original parent and animation
   ownership; this example intentionally snaps back to the clip pose.
5. Pause during movement or a drop. Resume should preserve the current state.

## Code map

- `PartsCombatDemoRig`: slots, cropped atlas rectangles, Idle/Walk clips, and skins.
- `PartsCombatDemo`: controls, entity lifetime, projectile queries, and the Unity 2D bridge.
- `PartsCombatDemoInputSystem`: movement and pose requests before `SpritePartsPlayerSystem`.
- `PartsCombatDemoFireSystem`: socket reads and shot spawning after the pose writer.
- `PartsCombatDemoPlayModeTests`: movement/fire/skin/pause/physics integration and respawn cleanup.

Character, targets, arena lines, reticle, and projectiles use the package's DOTS
instance renderer. The dropped weapon's solver is Unity 2D Physics on a non-rendering
GameObject proxy. Projectile hits use swept segment-vs-target-distance queries in
this sample; they are not a full combat/damage framework.

This is a readable one-character integration example, not a crowd benchmark.
Runtime scripts and art remain in the package so the menu works before sample import.

See `Documentation~/PartsGameplay.md` for the underlying gameplay API and limitations.
