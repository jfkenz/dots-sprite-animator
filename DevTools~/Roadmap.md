# DOTS Sprite Animator — Roadmap

Feature backlog, tracked per version. Items move up when a concrete use
case appears.

> This file lives in `DevTools~/` (project sandbox only). It is **not** part of
> the Asset Store / UPM package under `Packages/com.invertlab.spriteanimator`.

## Next version (collider damage pipeline)

The collider system currently detects; these make it combat-ready.

1. **Damage + knockback on the box** — `FrameBoxDef` gains `Damage`
   (and optional knockback force/direction), baked into the hitbox set.
   Heavy vs light attacks become data, not code.
2. **Built-in hit-once tracking** — `SpriteHitboxQuery.TryHit(...)` with
   internal per-attack dedup (no more `LastHitAttackId` boilerplate in
   gameplay code).
3. **Hit results carry box identity** — box Id, lifetime, and damage
   returned with each target, so per-hitbox effects (slash VFX vs impact)
   are trivial.
4. **Authoring QoL** — copy/paste boxes between clips, mirror-paste.

## Later (on demand)

- **Swept overlap** for fast projectiles (tunneling through thin targets
  between frames).
- **Factions** — team byte + mask on boxes for the pure/query path
  (Unity Physics path already has CollisionFilter).
- **Normal-map lighting** — per-instance normal sheets for the lit shader
  (lights are currently flat).
- **Per-clip sheet binding inside one profile** (profiles whose clips span
  multiple atlases; single-sheet and per-sheet-set workflows work today).
- **9-slicing** — border data + 9-region shader variant (for world-space
  framed bars over crowds; uGUI already covers normal UI).
- **Tilemap baking** — Unity Tilemap chunks to static sprite entities.
- **Sprite masks** — stencil-free mask-texture approach.
- **Netcode determinism / rollback helpers** — snapshot API over
  SpriteAnimPlayer + clock discipline; build when a transport exists to
  verify against.

## Known limitations (documented scope)

- 2D lights: flat (no normal maps) — URP 2D Renderer required for the
  lit toggle.
- Cell Editor is Grid-layout only (Cropped layout renders but is not
  cell-editable yet).
- Unity Physics detection uses 3D colliders flattened onto the sprite
  plane (com.unity.physics has no 2D shapes).
- Polygon colliders are convex-hulled in Unity Physics (concave shapes
  decompose visually via Box2D on the Unity 2D path).
