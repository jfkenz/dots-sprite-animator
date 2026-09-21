# Parts Combat Playground

An interactive example of a floating-parts character with independent aiming,
additive recoil, a fixed weapon slot, muzzle sockets, and physics ownership.

## Open it

- Recommended: **Tools > DOTS Sprite Animator > Create Parts Combat Example**. This creates a
  unique editable profile in `Assets/PartsCombatExample/`, assigns it to the new scene,
  and opens it in the DOTS Sprite Animator. Save the scene, then press Play.
- From Package Manager: import **Complete**, open `PartsCombatExample.unity` in
  its `PartsCombatExample` folder, then press Play.

The distributed scene references `Runtime/DemoArt/PartsCombatProfile.asset` in the package.
To customize an imported scene, select **Parts Combat Playground**, click **Create Editable
Profile Copy** in the Inspector, and save the scene. The copy is assigned automatically.
**Open Profile in Animator** returns to its editor. You can also use **Load Profile** and
select `PartsCombatProfile` directly. The profile references the package's generated atlas.
It requires the package's normal Entities/URP setup and Unity 2D Physics module.
It does not require the Input System or optional Unity Physics package. When the
Input System is installed and enabled, the optional demo bridge polls its keyboard;
legacy projects poll Unity's legacy keyboard instead. Movement does not depend on
GUI key events. `SetMove` remains available for a custom controller.

## Edit it without writing animation code

1. Open the assigned profile and select **Parts**. Expand `body` to see both hands and
   the weapon parented to `hand.r`.
2. Select **Idle** or **Walk**, scrub the timeline, turn on onion skin, and change a
   body pose/key. Save with **Save Profile**.
3. Restart Play. The example rebuilds from your saved asset, including transforms,
   keys, appearance sizes/pivots, sheet textures/crops, and weapon skin bindings.
4. Select the `weapon` slot to change its assigned appearance. Gameplay keeps that
   assignment and never swaps skins. The template's optional skin patches remain
   available for editor experimentation. Keep slot IDs `hand.r` and `weapon`, and
   clip names `Idle` and `Walk` for this particular gameplay controller.

Animation is profile-driven; aiming overrides the right hand's keyed rotation/scale and recoil
adds to the weapon pose during gameplay. The demo muzzle is positioned at normalized
weapon-art coordinates (0.94, 0.55), and its collider follows the appearance size/pivot.
Changing to completely different weapon artwork may require adjusting that gameplay muzzle.
Edits are read when entering Play, not hot-reloaded during a session. Do not add a second
animation authoring component to the demo object: it already spawns its own ECS character.

## Controls

| Action | Control |
| --- | --- |
| Move | WASD or keyboard arrow keys |
| Aim | Enable practice-target aim, or disable it and point inside the arena |
| Fire | Click the arena, press Space, or hold FIRE |
| Physics handoff | Drop weapon, let it bounce, then Return weapon to rig |
| Pause | Pause / Resume freezes animation, projectiles, and the dropped weapon |

## Walkthrough

1. Hold FIRE while moving. The body plays Walk while the hand aims and the weapon recoils.
2. Watch **Clip**, playback status, elapsed clip time, and movement speed in the panel.
3. Aim across the character. The weapon stays parented to the right hand; aiming does
   not mirror the rig or move it to the other hand. The aiming hand's local X scale
   mirrors for leftward aim, keeping rotation within an upright half-turn. The barrel,
   muzzle socket, and recoil follow that same transform, so shots still travel left.
4. Drop the weapon. It becomes an unparented entity controlled by a Rigidbody2D proxy.
   The body remains animated. Returning restores the original parent and animation
   ownership; this example intentionally snaps back to the clip pose.
5. Pause during movement or a drop. Resume should preserve the current state.

Idle/Walk follows actual movement speed after arena limits are applied. Holding a
direction against a boundary becomes Idle; moving along that boundary remains Walk.
Direction changes while moving continue the current Walk clip without restarting it.

## Code map

- `PartsCombatProfile.asset`: editable slots, cropped atlas rectangles, Idle/Walk clips, and skins.
- `PartsCombatDemoProfile`: creates the initial template once and provides editor shortcuts.
- `PartsCombatDemoRig`: optional code-only API example, used only when Profile is unassigned.
- `PartsCombatDemo`: controls, entity lifetime, projectile queries, and the Unity 2D bridge.
- `PartsCombatDemoInputSystem`: movement and pose requests before `SpritePartsPlayerSystem`.
- `PartsCombatDemoFireSystem`: socket reads and shot spawning after the pose writer.
- `PartsCombatDemoPlayModeTests`: movement/fire/upright aim/pause/physics integration and respawn cleanup.
- `PartsCombatKeyboardPlayModeTests`: actual Input System key state changes without GUI events.

Character, targets, arena lines, reticle, and projectiles use the package's DOTS
instance renderer. The dropped weapon's solver is Unity 2D Physics on a non-rendering
GameObject proxy. Projectile hits use swept segment-vs-target-distance queries in
this sample; they are not a full combat/damage framework.

This is a readable one-character integration example, not a crowd benchmark.
Runtime scripts and art remain in the package so the menu works before sample import.

See `Documentation~/PartsGameplay.md` for the underlying gameplay API and limitations.
