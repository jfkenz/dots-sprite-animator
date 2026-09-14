# v2 Cutout Parts — product brief for Astra

**Status:** locked product draft. **Next:** technical design. **Do not implement yet.**
**Audience:** Astra (and any agent picking up v2). JFKENZ signed this off 2026-09-14.
**Branch:** `v2` at `756da0f` (1.0.0). 1.0 store project on `main` stays untouched.
**Sandbox project:** `C:\Users\PC\Desktop\Unity Project\DOTS Sprite Animator 2` (file: this worktree package).
**Package worktree:** `C:\Users\PC\Desktop\Unity Project\dots-sprite-animator-v2`

This file lives in `DevTools~/` (sandbox only). It is **not** part of the shipped UPM/Asset Store package.

---

## One-sentence goal

Brotato-style characters: a **cutout (puppet) rig** of named body slots, **tween clips** that move those slots, and **sheet swaps** on a slot for equipment/skins **without changing the motion**.

## What the user asked for (keep this)

- Tween-like **separate body movement**, saved **like an animation clip**.
- **One new tab** on the existing DOTS Sprite Animator tool (`SpriteSheetToolWindow`), not a new window and not Unity Animator.
- Easy: pose parts, key them, play, loop, play clip by name like today.
- Equipment: **change the part with a new sheet**; **Walk stays Walk**.
- Skip (explicit): mesh deformation / Unity 2D Animation Sprite Skin, IK, Spine-style constraints, Mecanim/Unity Animator.

## Industry names (use these in design)

| User words | Correct name | We ship |
|---|---|---|
| tween body parts | **Cutout animation** (also puppet) | yes |
| bones + mesh squash | **2D skeletal / Sprite Skin** | no |
| swap gun/hat | **Slot skin / paper-doll** | yes |
| attach VFX to a point | **Socket** (already in 1.0) | keep as-is |

Do **not** depend on `com.unity.2d.animation`, Spine, or DragonBones.

---

## How this sits on 1.0 (do not break)

1.0 is **flipbook / frame clips** on an atlas (timeline, events, sockets, colliders, optional GPU clock). That stays.

| 1.0 | v2 cutout |
|---|---|
| Clip = ordered cells on a sheet | Clip = keyed local TRS per **slot** |
| One sprite entity (or GPU instance) | One entity per **part**, parented |
| Socket = attach point on the sprite | Slot = **the body piece itself** |
| `PreferGpu` refuses sockets / events / extra TRS | Cutout is **CPU path first**. Do not block v2 on GPU parts. |
| `SpriteSheetToolWindow` tabs: browser, clips, timeline, events, sockets, colliders, preview | Add **one tab: Parts**. Reuse preview/timeline feel. |

**Mode rule (v1 of this feature):** a character is either frame-clip **or** parts-clip, not both on the same player. Hybrid (frame body + tweened overlay) is later.

**Sockets vs slots:** sockets remain for sword/FX attach. Do not stretch sockets into a rig.

Relevant 1.0 types to extend or sibling, not overload:

- Editor: `SpriteSheetToolWindow` (+ `.Clips`, `.Sockets`, `.Events`, `.Timeline`, `.Preview`)
- Authoring: `SpriteAnimPlayerAuthoring`, `SpriteAnimSetAuthoring`, `SpriteSocketAttachmentAuthoring`
- Runtime: `SpriteAnimPlayerSystem`, `SpriteAnimClipConversion`, `SpriteSocketAttachmentSystem`, `SpriteGpuEligibility`

---

## Product shape (locked)

### Rig

- Named **slots**: `Head`, `Torso`, `ArmL`, `ArmR`, `LegL`, `LegR`, `Weapon`, user-defined.
- Hierarchy: parents (Torso root, Head/arms under Torso). Local rest pose: pos / rot / scale / pivot.
- Each slot has a **binding**: which sheet + cell (or sprite) draws that part right now.
- Sort order / overlay per slot (arm in front of torso).

### Parts clip

- Same mental model as Idle / Walk / Attack.
- Per slot, a track of keys: time + local pos + rot + scale + ease (lerp/smoothstep is enough for v1).
- Missing track = rest pose (slot can be omitted).
- Loop, duration, playback speed — match existing player API as closely as possible (`Play("Walk")`).
- Events on the clip timeline can come later; do not require them for v1 play.

### Equipment / skin swap (Brotato)

- **Motion is on the slot name, not the art.**
- `SetSlotSheet(entity, slot: ArmR, sheet, cell)` or swap a whole **skin set** (`Skin_Default`, `Skin_Gold`) that maps every slot to cells.
- Walk clip does not change when the gun sprite changes.
- Unknown slot on a skin = leave current binding (or hide — Astra: pick one and document).

### Authoring UX (easy)

One **Parts** tab:

1. Create rig / add slot from a sheet cell.
2. Parent in a simple list (not a node graph).
3. Pose in the existing preview; **Key** writes current local TRS at the playhead.
4. Timeline like current clips; Play / loop.
5. Save as a parts clip on the same profile/set as frame clips (Astra: exact asset layout).
6. Skin/equipment: dropdown or assign sheet per slot; preview must update without rewriting keys.

No graph, no IK gizmos, no Unity Animation window.

---

## Runtime sketch (for Astra to turn into a real design)

- Bake rig + clips + default skin into Blob / baker data (follow existing clip conversion style in `SpriteAnimClipConversion`).
- Spawn child entities (or a linked entity group) per slot; each is a normal CPU instanced sprite.
- System samples the playing parts clip, writes local `LocalTransform` (and sprite cell if the clip ever keys a cell — v1 can skip cell keys and only TRS).
- Swap API writes binding only (sheet/cell/UV), not transforms.
- Sorting: respect slot order; keep 2D overlay stable.
- GPU: ineligible. `SpriteGpuEligibility` should refuse parts-rig characters the same way it refuses sockets/TRS. Crowd GPU stays frame-clip.

Astra must specify: parented child entities vs a custom TRS buffer on one entity; how the player component looks; how Play() is shared or split (`SpriteAnimPlayer` vs new `SpritePartsPlayer`); bake path from authoring.

---

## Non-goals (do not design these in)

- Mesh weights, Sprite Skin, PSD bone import
- IK (hand/foot reach)
- Spine constraints, shear, free-form deform
- Unity Animator / Mecanim / AnimationClip
- GPU parts / compute skinning
- Mixing frame clips + parts clips on one entity
- Netcode determinism for parts (later, same as 1.0 roadmap)

---

## Suggested build slices (design must fit this order)

1. **Data** — rig, slot, rest pose, parts clip keys, skin map. Authoring Scriptable/Mono that bakes.
2. **Editor Parts tab** — add slot, parent, key, timeline, play in preview.
3. **DOTS play** — bake, spawn parts, sample clip, `Play("Walk")`. Tests.
4. **Swap** — per-slot sheet/cell + named skin set. Tests + tiny Brotato-style sample (one walk, two weapons).
5. Later: clip events, hide slot, simple IK — not now.

---

## Design deliverable (what Astra should produce)

A **technical design** markdown in `DevTools~/CutoutParts-v2-Design.md` (still not shipped in the package):

1. Types (authoring + runtime + blobs) with field lists.
2. How Parts tab hooks `SpriteSheetToolWindow` (new partial, tab enum).
3. Bake/conversion vs 1.0 `SpriteAnimClipConversion`.
4. Playback API (C# snippets, no full implementation).
5. Swap API.
6. Eligibility / GPU interaction.
7. Test plan (EditMode + PlayMode).
8. Sample sketch for the v2 Unity project.
9. Risks (draw calls per part, pivot space, flip/mirror, scale inheritance).
10. Open questions you resolved, and any that still need JFKENZ.

**Do not write production Runtime/Editor feature code in this pass.** Types-in-the-design-doc only. No Unity 2D Animation package. No commits on `main`. Work on `v2` if a PR is opened.

---

## Constraints

- Unity 6000.5, Entities 6.5, URP (sandbox template is 17.6; package.json still 17.5.0 — do not casually bump package.json).
- JFKENZ: no git commits unless they explicitly ask. Design file in `DevTools~/` is OK; do not dump this into `Packages/com.invertlab.spriteanimator/Documentation~` until a public version ships.
- Do not mix work into BallForge. Store 1.0 stays `C:\Users\PC\Desktop\Unity Project\DOTS Sprite Animator` on `main`.
- Android IL2CPP/ARM64/Vulkan is already set on the v2 sandbox; not part of this feature.

## Success for this design pass

Someone can implement slice 1-4 from the design doc without re-asking what cutout vs socket vs skin means, and without pulling in Sprite Skin or Animator.
