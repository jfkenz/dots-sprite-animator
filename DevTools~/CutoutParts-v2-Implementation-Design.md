# Cutout Parts: reviewed implementation design

DESIGN ONLY — 2026-09-14. Production feature code unchanged.

## 1. Verdict and scope

**Keep PR #2's architecture; correct its contracts before implementation.** Named parts, child entities, a separate Parts clock/blob, and CPU instanced rendering fit the goal. The PR does not yet define a safe, easy authoring workflow. Onion skin is required by the user, not optional.

Reviewed [PR #2](https://github.com/jfkenz/dots-sprite-animator/pull/2), head 7702088088b8245bd860cf36ce15c82c8c0eb2fd, including its complete 997-line design. Read local product brief, roadmap, undo rules, and relevant profile/render/sort code at v2 commit 756da0fad922b5651deba97adea2515997c7be48.

Sandbox: C:/Users/PC/Desktop/Unity Project/DOTS Sprite Animator 2.
Actual source: C:/Users/PC/Desktop/Unity Project/dots-sprite-animator-v2/Packages/com.invertlab.spriteanimator.
The sandbox manifest points to that worktree; it has no embedded package copy. Sandbox URP is 17.6.0; package baseline remains 17.5.0. Do not upgrade dependencies for this feature.

This document is the recommended correction to conflicting PR sections, not evidence that Parts already exists. Keep designs in DevTools~. Preserve the existing uncommitted brief/roadmap. Do not touch the original 1.0 store project or commit/merge without a separate request.

Goal: floating hands, bouncing body, swinging feet, and replaceable weapons, animated through named clips. Parts need not visually connect. “Flying body parts” means authored cutout motion here. Physics detachment, ragdolls, IK, mesh weights, Spine, and Unity Animator remain outside scope.

## 2. Findings the implementation agent must address

| Priority | PR section | Finding | Correction |
|---|---|---|---|
| P1 | 2.6 | Drag edits rest when no key exists, but edits a key otherwise. This can alter unrelated clips. | Explicit Rig/Animate modes; Animate never writes rest implicitly. |
| P1 | 2.6 | Onion optional, selected-track only. | Required whole-character pose ghosts, including parent motion. |
| P1 | 1.3 / 5 / 9.2 | Offset is initialized/reset to zero, while pivot section requires an offset and contradicts itself. | Shared appearance-to-geometry resolver; preserve joint while resolving pivot/size on swaps. |
| P1 | 4.1 / 9.3 | LocalTransform.Scale.x/y does not exist; Scale is scalar. Conditional PostTransformMatrix reset is undefined. | Fixed transform representation, described below. Never average X/Y scales. |
| P1 | 3.3 / 9.5 | Existing sorting writes local z, not world z. Parenting those values accumulates depth. | Render-only Parts depth, separate from joint hierarchy. |
| P1 | 1.2 / 3.3 | Profile mode and player-component presence compete over baker ownership. OnValidate removes components. | Exactly one bake path selected by mode; explicit Undoable conflict repair. |
| P1 | 2.5 | Skin preview may rewrite serialized base bindings. | Transient preview overrides; only Save Skin changes skin data. |
| P1 | 7 | Rendered tests excluded despite new geometry/parenting/depth. | Require small camera pixel tests alongside transform tests. |
| P2 | 1.1 / 1.4 | Authored clip Speed absent from blob. Invalid wrap may fail or silently coerce. | Store speed; reject unsupported values consistently. |
| P2 | 4.2 | Play ignores crossfade; other common API dispatch remains unclear. | Define supported control surface; reject unsupported requests explicitly. |
| P2 | 3.4 / 8 | First runtime result requires manual SubScene/ECS knowledge. | One-click working demo and configured character creation are release gates. |
| P2 | 7 | SmoothStep test avoids testing actual SmoothStep off its midpoint. | At t=0.25: Linear=0.25, SmoothStep=0.15625. |

## 3. Creative workflow

1. Existing window → Parts → New Parts Character. Create a NEW profile by default; conversion of an existing flipbook is Advanced and explicit.
2. Choose Floating Parts (default), Humanoid, or Empty. Floating Parts supplies Body, Hand L, Hand R, Weapon; user-defined parts remain supported.
3. Drag a sliced sprite, whole PNG, or existing sheet cell into the canvas. Whole PNG becomes a 1x1 sheet. New part appears at the drop point. Parent defaults to character root; Add Child is explicit.
4. Rig mode: place parts, move joint pivots, set parents through a simple list, order front/back.
5. Animate mode: choose Walk, move playhead, drag parts. Auto Key records poses. Onion ghosts show surrounding complete poses.
6. Play, optionally Match Loop End, Save.
7. Skins mode: replace Weapon art, save Iron Sword. Walk keys and current playback time remain unchanged.
8. Create Demo Scene runs the same profile through configured ECS authoring, without handwritten code.

A new user must complete this sequence without manually adding ECS components. Treat that as a usability acceptance target, not a performance claim.

### Layout

One existing IMGUI window; one Parts tab. No new animation window or graph.

    Profile: Hero*  [Clips] [Parts]                    Save Undo Redo
    [Rig] [Animate] [Skins]  Clip: Walk  Play/Pause Loop Speed
    +----------------+-------------------------------+---------------+
    | Clips          |                               | Selected part |
    | Idle / Walk    |         POSE CANVAS           | Position      |
    |----------------|       current + ghosts        | Rotation      |
    | Parts tree     |                               | Scale         |
    | Body           |                               | Key / Ease    |
    |   Hand L       |                               | Advanced      |
    |   Hand R       |                               | IDs / Units   |
    |     Weapon     | Onion On  Before 1  After 1    |               |
    +----------------+-------------------------------+---------------+
    | Duration 0.6s  Display 30fps  Auto Key ON  Key Pose             |
    | Body     <>-------------<>-----------------<>                  |
    | Hand L   <>-------------------<>-----------<>                  |
    | Weapon   (inherits; no keys)                                   |
    +---------------------------------------------------------------+
    Animate: Hand R at 0.20s. Drag records a key. Unsaved changes.

Resizable panels. At narrow widths, collapse inspector to a drawer before overlapping canvas controls. Keep transport and Auto Key visible. Technical CPU/GPU details belong in Advanced diagnostics.

### Mode contract

- Rig edits hierarchy, rest transforms, joint pivot and default appearance. No keys. Banner: Rig changes affect every clip. Existing keyed local poses are not silently rebased.
- Animate edits selected clip only. Auto Key ON by default. Drag creates/updates keys; NEVER rest pose.
- Skins edits an appearance patch. Preview substitutions are transient until Save Skin. Transform tools disabled here.
- Main Clips/Parts tab selects view, not profile animation mode. Existing frame data is preserved when mode is explicitly changed.

Auto Key OFF permits an unkeyed temporary pose with Key Pose / Discard actions. Scrub, play, or clip change requires resolving it first; do not silently discard work. On an empty track, first key created after t=0 also inserts a rest key at t=0 in the same Undo operation.

One key contains full local position/rotation/scale for one part. Timeline single-click selects/seeks; double-click or Key inserts. Drag moves, Alt-drag duplicates, box-select/Delete act on selected keys. Frame-step buttons move one display frame; separate previous/next-key buttons jump keys. Display FPS controls snapping, not animation speed.

Canvas: Move, Rotate, Scale, Frame Selection, Reset Selected Pose. Linked XY scale default. Pivot/reparent editing Rig-only. Reparent preserves rest world pose when representable; reject a transform requiring shear, and warn that existing clips use the old local coordinate basis. Group drag applies once to topmost selected ancestors, avoiding double motion of selected descendants.

One drag = one Undo entry. Escape cancels. Keyboard shortcuts respect text fields. Overlay controls consume input before canvas pan/selection. Save/reopen and Undo/Redo restore data, selection and preview together.

## 4. Onion skin — mandatory

Parts onion is visual comparison, not flipbook OnionOffsets motion data. Never reuse those arrays as Parts keys.

- Default ON while paused in Animate: one previous and one next pose; spacing two display frames at 30fps; opacity 0.30.
- Past blue, future orange, with -2/+2 frame badges so color is not the only cue.
- Whole character by default, including unkeyed parts. A Weapon with no keys still follows its animated Hand in every ghost.
- Options: before/after counts independently 0–3; spacing; opacity; Whole Character / Selected Subtree. Advanced mode samples neighboring distinct key times from the union of all clip tracks.
- Evaluate COMPLETE hierarchy at each ghost time using the same pure sampler as runtime. Never combine an old child with its parent's current pose.
- Use current preview skin, root facing, size, pivot and camera for every ghost. Ghosts show motion, not equipment history.
- Loop wraps across seam. Once omits out-of-range samples rather than piling up clamped duplicates. Deduplicate identical ghost times.
- Composite each ghost character in correct internal draw order, then tint/fade the composite once. Overlapping limbs should not darken just because a ghost has several quads. Reuse bounded preview render targets; release on window disable.
- Draw ghosts, then live character, then handles. Ghosts never occlude live depth, receive clicks, create entities, run physics, or emit events.
- Hide while playing by default; optional Show While Playing. Disabled in Rig mode.
- Cache by profile revision, clip/skin/time/settings/viewport. Parent edits, swap previews and Undo invalidate immediately. Never normalize, sort or save profile data during Repaint.

## 5. Authoring and runtime data

Retain same-profile AnimKind, defaulting old assets to Frame. New Parts lists and blob; SpriteAnimClipConversion stays frame-only.

| Record | Required data |
|---|---|
| Profile | AnimKind, Parts schema version, Slots, PartsClips, Appearances, SkinPatches, default clip/skin IDs. Preserve existing frame lists. |
| Slot | Stable SlotId, display Name, ParentSlotId, rest parent-local XY world units, Z rotation degrees, positive XY scale, default AppearanceId, stable draw rank. |
| Appearance | Stable AppearanceId, sheet/cell reference, logical world size, pivot source and optional override. Defines art relative to joint. |
| Clip | Stable ClipId, display Name, duration seconds, authored speed multiplier, Loop/Once, tracks. Only expose control fields actually implemented. |
| Track | SlotId and sorted pose keys; at most one track per slot per clip. |
| Key | Time seconds; absolute parent-local position/rotation/scale; outgoing ease Linear/SmoothStep. |
| Skin patch | Stable SkinId, display Name, SlotId → AppearanceId substitutions. Unlisted slots unchanged. |
| Editor session | Selection, preview time/skin overrides, Auto Key, onion, pan/zoom. Not baked runtime animation. |

Appearance metadata matters: a 32px sword and 96px spear with different PPU/pivots must stay attached to the same hand. Existing SpriteSheetDefinition carries grid/aspect, not all PPU/pivot information. Sheet+cell alone is insufficient.

Friendly swap resolves a pre-baked AppearanceId. Low-level SetSlotSheet must receive or resolve cell, logical world size and pivot alongside sheet entity. Reject missing metadata. Never zero frame offset/scale to make arbitrary art fit.

One resolver handles supported Grid/Cropped art for preview, bake and swaps. Preserve logical size and trim origin where applicable. Rotated packing is unsupported until renderer handling is proven by a test. Explain rejected art in the importer.

Stable IDs are not list positions. Rename display names without changing IDs. Reordering sheets/slots/clips repairs index references or retains IDs until bake. Canonicalize slot/appearance/skin IDs consistently: trim, lowercase, spaces to dots. Preserve existing case-sensitive clip-name Play lookup. Reject duplicate IDs and hash collisions.

Bake all rig nodes in first version. Eye/lock icons affect editor selection/preview, not runtime node existence. Do not omit a parent because an ambiguous Enabled flag is false. Runtime hiding and visibility keys remain later features.

### Sampling rules

Keys are absolute parent-local poses, not deltas added to rest. Missing/empty track uses rest. One key holds. Before first/after last key holds nearest key within duration. Never apply rest twice.

- Position/scale lerp with outgoing key's easing. Rotation shortest-angle interpolation, deterministic +180-degree tie. Full spins require intermediate keys below 180-degree increments; multi-turn curves later.
- Finite values only; duration >0; key times in [0,duration]; scales >0. Key beyond duration offers Extend Duration. Bake does not silently extend/coerce.
- Editor insertion at same snapped tick replaces key. Imported duplicate times resolve deterministically with a warning. Bake uses unique sorted times.
- Loop positive modulo, including negative time. Once clamps/stops at correct endpoint for playback direction.
- Effective advance = deltaSeconds × playerSpeed × authoredClipSpeed. Blob must include clip speed. Speed zero freezes.
- Runtime Loop at duration wraps to start. Editor can inspect terminal key at duration through an explicit endpoint-preview flag.
- No invented seam interpolation after last key. Match Loop End copies sampled t=0 poses to t=duration as one Undoable operation. Warn about mismatched seams.
- Play same running clip is idempotent unless Restart/force. Play same paused clip resumes; Play a completed Once clip restarts. A missing clip returns failure without state changes. Stop pauses and samples current clip at time zero; it keeps the selected clip. Restart samples the start (terminal endpoint for reverse Once) and plays. Seek changes pose without restarting or emitting completion. Completion occurs once per traversal; pause/resume does not repeat it.

Pure sampler accepts immutable definition, clip/time and output poses. Preview, onion, initial bake pose and runtime use the same math. Dense slot-to-track lookup avoids scanning all tracks for every slot. No per-frame managed allocation.

## 6. Runtime ownership and transforms

Gameplay root holds Parts player/blob, link/appearance tables, draw-group settings and game movement/optional physics. No frame player or drawn sprite on root.

One internal visual-root child holds whole-rig facing and visual scale. Negative facing must not scale the gameplay physics body.

One part entity per slot, Parent pointing to another part or visual root. Holds slot identity, pose, appearance, existing render components and render-only depth. Maximum 32 parts; visual root does not count toward that limit.

All owned entities belong to an explicit lifecycle group: prefab instantiation remaps references; root destruction removes parts. Shared sheet entities do not belong to individual character destruction groups. Bake-owned blobs follow Unity baking ownership; runtime-owned blobs need explicit lifetime/refcounts per world. Do not dispose a shared blob twice.

Blob contains slots, clips/keys/speed, dense track lookup, resolved appearance geometry and skin patches. Texture objects and world-specific sheet entities remain outside blobs, in registrations/entity buffers. Root link tables map stable IDs to remapped part entities; appearance tables map baked indices to sheets in that same world.

Use LocalTransform for joint translation/Z rotation, with scalar Scale=1. Allocate PostTransformMatrix for ALL parts at creation, including identity; store positive XY scale as diagonal(sx,sy,1). Never add/remove that component during playback. Visual root stores diagonal(signX,signY,1) for facing; children retain identity SpriteFlip flags. Root gameplay scale remains independent.

This removes invalid Scale.x writes, stale nonuniform matrices and double flips. Parent scale inheritance is intended. Linked XY scale is default; unlocked XY may produce inherited shear, handled by the existing CPU XY matrix-basis renderer. No explicit shear tool.

### Geometry contract

For normalized quad coordinate q in [-0.5,0.5]:

    visualPoint = (q + 0.5 - pivot) * logicalWorldSize
    worldPoint = jointWorldMatrix * visualPoint

For existing shader aspect A:

    frame.Scale = (logicalWidth / A, logicalHeight)
    frame.Offset = (0.5 - pivot) * logicalWorldSize
    frame.Rotation = 0

Shader already multiplies X by A: do not apply aspect twice. Joint TRS remains independent of artwork size/pivot. Crop selects UV; resolver must account for logical vs trimmed rectangle consistently. Test off-center pivots and different PPU, not only equal-size centered squares.

Swap changes sheet/cell and derived visual geometry only. Never joint TRS, keys, player time, clip, speed or queue state.

### Depth outside parent transforms

Existing SpriteSortDepthSystem writes LOCAL Position.z. Do not add SpriteSortDepth/SortPinPending to every part and call it world depth. Keep part-local z=0; gameplay-root z remains gameplay data.

Add a render-only Parts depth value. CPU packer uses it for PosScale.w after computing XY position. Bounds and culling use that same rendered depth. Resolve after transforms and before rendering.

First-release ordering uses explicit Character Order plus Part Order on the existing flat-index depth convention:

    drawIndex = characterOrder * 64 + stablePartRank
    renderedDepth = SpriteSortDepth.FromIndex(drawIndex)

Part ranks unique 0–31. Reserve 64 indices per root order, maintaining existing reliable 0.001 depth step. Do NOT subdivide that step into unreliable values. Validate integer overflow and camera depth range. Higher orders are in front for supported XY camera.

Character Order is a new GROUP order, not legacy OrderInLayer. Existing frame sprites retain current flat-index mapping; expose resolved flat index in Advanced for interoperability. This first version does not claim Unity SortingGroup or automatic Y sorting. Distinct character orders must keep two overlapping puppets internally grouped across mixed textures. Equal character orders intentionally share the group; explain that tie. Automatic Y ordering is later.

### Update and culling contract

Gameplay movement → Parts clock/pose → Unity TransformSystemGroup → Parts render-depth and fresh-transform culling → CPU instance packing/render.

Declare dependencies explicitly. If existing early culling cannot see current Parts pose, exclude Parts there and run a Parts-aware pass. No one-frame lag. Children can stop drawing off-screen while root clock keeps advancing; re-entry restores current pose immediately.

Parts uses CPU pose evaluation with GPU instanced drawing; it does not use GPU animation-clock playback. Reject root/children at all GPU promotion entry points without retry loops. Initial layout XY only; reject XZ instead of silently ignoring part rotation.

## 7. APIs and easy scene integration

API descriptions here are contracts, not production feature code.

- Common controls dispatch by player kind: Play name/index, Pause, Resume, Restart, Stop, SetSpeed, SeekNormalized, SetFacing/Flip, and playback/current-clip queries. Inventory existing overloads before editing; do not dispatch only the string Play overload.
- SpriteParts offers TryGetSlot, SetSlotAppearance, SetSlotSheet with explicit geometry metadata, ApplySkin, ResetSkin. Friendly equipment code names the slot and appearance, not registry internals.
- Play on Parts with nonzero crossfade returns a clear unsupported result without changing playback. Do not expose a crossfade inspector field that does nothing. Do not reinterpret frame-indexed combat/seek calls as seconds.
- ApplySkin is a patch. Unknown/unlisted slots remain unchanged; unknown skin fails without changes. Validate every applicable appearance before writes, so invalid art does not leave half-applied equipment. Preview Reset to Default is explicit; patches are not complete outfits.
- Pivot editing changes selected binding's artwork alignment around its joint, not motion keys. Display the joint separately from image center. If an appearance record is shared, copy it before editing the selected binding; avoid silently moving other parts. Move Joint is a separate Rig transform operation.

Exactly one baker owns the gameplay root, selected by Profile.AnimKind. Parts profile plus frame-only player is an explicit repairable authoring error. Both players present is an error with an Undoable Fix action; OnValidate does not delete user components. Old profiles default to Frame and retain existing behavior.

One visible Parts Character inspector groups Profile, Starting Clip, Starting Skin, Facing and Character Order. Existing set/profile host may remain internally. Stable IDs and low-level components stay Advanced.

Create Character builds configured authoring inside a loaded SubScene. Outside one, offer explicit Create ECS SubScene; do not create a normal GameObject that silently fails to animate. Create Demo Scene builds a dedicated sample scene with camera, authoring and controls; never overwrite the user's current scene. Full runtime GameObject bridge is later, not a second hidden spawn path.

Gameplay root may retain independently authored movement/body physics. Parts must not strip root colliders or force users back to flipbooks for basic movement. Animated per-part hurtboxes, attack events, damage logic and physics detachment remain later. TryGetSlot provides a documented attachment seam for external FX; no new socket catalog is required yet.

### Concrete type ownership for slice 1

Use PR names where their meaning remains valid; equivalent names are acceptable only with a clear mapping in that slice's report.

| Type | Essential runtime fields/meaning |
|---|---|
| SpritePartsSetBlob | Slots, Clips, Appearances, SkinPatches; immutable and shared. |
| SpritePartSlotBlob | Stable hash/ID, ParentSlotIndex, RestPosition float2, RestRotation float, RestScale float2, DefaultAppearanceIndex, DrawRank int. |
| SpritePartsClipBlob | Clip ID/hash, Duration float, SpeedMultiplier float, Wrap byte, dense slot→track indices, tracks/keys. |
| SpritePartsKeyBlob | Time float, Position float2, Rotation float, Scale float2, Ease byte. |
| SpritePartAppearanceBlob | ID/hash, SheetTableIndex int, Cell int, LogicalWorldSize float2, Pivot float2; resolved trim data if needed by supported crop mode. |
| SpritePartsSkinBlob | ID/hash and array of SlotIndex/AppearanceIndex substitutions. |
| SpritePartsPlayer | ClipIndex, TimeSeconds, SpeedMultiplier, Playing, completion state. No reserved unused crossfade/queue fields. |
| SpritePartsSetRef | Blob reference; ownership tracked separately according to baked/runtime source. |
| SpritePartLink | Part Entity plus stable SlotIndex/hash, stored on root. |
| SpritePartSlot | Root Entity, SlotIndex, ParentSlotIndex; marks child permanently CPU-pose. |
| SpritePartAppearanceState | Current resolved appearance index/override plus binding geometry. |
| SpritePartSheetEntry | World-local sheet Entity for each baked sheet-table index. |
| SpritePartRenderDepth | Final depth float consumed by render pack and bounds. |
| SpritePartsDrawGroup | CharacterOrder and validated mapping to flat depth interval. |

If queue/one-shot/hitstop parity is assigned, add the required state and define/test precedence; do not copy unused fields from frame player just to look complete. The initial common-control contract above is the minimum advertised feature set.

## 8. Files and build slices

| Area | Owner |
|---|---|
| Definitions/validation | Dedicated Runtime/Parts files; minimal new lists/mode on SpriteSheetProfile. |
| Canonicalization/geometry/sampling | Shared runtime utilities; SpritePartsClipConversion and SpritePartsSetBuilder. Frame conversion remains frame-only. |
| Bake | Parts baker and minimal exclusive dispatch in existing authoring pipeline. |
| Playback/links/lifetime | Runtime/Parts components and systems. |
| Common control | Existing SpriteAnims entry points; inventory all callers/overloads. |
| Rendering | Required depth override for Parts instances only in SpriteInstanceRenderSystem; frame instances retain current behavior. |
| GPU rejection | Existing eligibility and promotion entry points. |
| Window | Minimal StudioTab dispatch; .Parts.cs coordinator plus coherent preview/onion/timeline helpers if needed. |
| Example | Samples~/Complete/CutoutPartsExample. Generated art and pointer/touch controls. |

Keep four slices, each with observable completion:

1. **Data/evaluator:** records, validation, appearance resolver, pure sampler, blob conversion, ownership contract and unit tests. Freeze geometry/time rules first.
2. **Editor:** Parts tab, Rig/Animate/Skins, direct manipulation, Auto Key, required onion, timeline, Undo and save/reopen. Preview uses real shared evaluator, not a second dummy sampler.
3. **Runtime:** bake/hierarchy, clock/control dispatch, geometry/depth, culling, lifetime, GPU rejection and camera-rendered parity. Create Character produces working ECS authoring.
4. **Swaps/sample:** per-slot appearance and named patches, different-size/different-PPU weapons, safe errors, one-click demo, platform checks and frame-path regression.

Do not add IK, mesh deformation, curve graph, automatic rig detection, Animator state machine, runtime detachment, crossfade or per-part physics while these gates are incomplete. N puppets × P parts means N×P instances plus pose/hierarchy work. Existing flipbook crowd performance is not a Parts benchmark.

## 9. Acceptance tests

| Gate | Required evidence |
|---|---|
| Identity | Rename/reorder slots/sheets/clips without broken bindings; duplicates and hash collisions rejected. |
| Validation | Cycles, missing parent/art, unsupported packing, >32 slots, invalid time/scale and duplicate tracks report useful errors, no silent data loss. |
| Sampler | Missing/empty/one-key tracks, quarter-point SmoothStep, angle wrap, reverse speed, Loop/Once endpoints, paused seek and one-time completion. |
| Hierarchy | Rotated/scaled parent plus nested Weapon; verify full matrix multiplication, not parent+child translation only. |
| Geometry | 32px/32PPU sword vs 96px/48PPU spear; off-center grip, correct size, fixed joint before/after swap. |
| Facing | X/Y/both with rotated/scaled parent; no double flip or negative gameplay-physics scale. |
| Depth | Two overlapping mixed-atlas puppets at distinct group orders; correct internal order through parent changes, no accumulated z or fighting. |
| Swaps | Walk time/index/keys/pose unchanged; weapon patch leaves head; invalid applicable appearance fails atomically. |
| Onion | Parent-only keys move unkeyed weapon ghosts; Loop seam/Once bounds; current skin and pivot; ghost never pickable or mutating. |
| Editor | Unkeyed drag affects clip only; t=0 anchor; one Undo/drag; Escape; multi-select; key duplicate/delete; save/reopen and Undo/Redo; overlay input isolation. |
| Lifecycle | Instantiate two characters, verify reference remaps; destroy one without harming shared sheets/blob; world unload releases owned resources. |
| Controls | Every advertised common overload works; unsupported crossfade fails explicitly; old Frame behavior unchanged. |
| Culling | Parent motion/large weapon at viewport edge; off-screen clock continuity and immediate correct re-entry pose. |
| Camera pixels | Joint pivot, facing, swap and overlap order in real URP rendering; preview/runtime geometry parity. |
| Devices | Windows build/run and ARM64 Android run of NEW Parts sample. Earlier flipbook phone evidence does not validate Parts. |

Generated-art example: Floating Parts preset, Idle/Walk/Attack, wooden sword and larger spear on different sheets/PPU, nested weapon, two overlapping characters. Controls: Play/Pause, clip dropdown, swap weapon, facing, character order. Pointer/touch first; keyboard optional. Do not add optional Input System just to try the example.

Final acceptance: new user creates rig, poses/keys it, corrects motion using onion ghosts, saves/reopens, runs character, swaps equipment mid-Walk, and sees matching joint/size/depth in editor/runtime. No manual component repair. No shader errors. No frame-animation regression.

## 10. Decisions and later work

Keep: same profile with separate animation modes; named slots; child entities; new Parts clock/blob; one Parts tab; CPU pose; Linear/SmoothStep; Loop/Once; maximum 32 parts; patch skins; existing frame sockets unchanged.

Change from PR: required whole-pose onion; explicit editing modes; appearance geometry records; separate visual mirror root; fixed transform matrices; render-only group depth; explicit baker repair; non-silent unsupported controls; rendered tests and one-click scene setup.

Default first-release choices: tool-window editing plus working configured ECS demo; root physics composition allowed but no automatic part colliders; TryGetSlot for attachments; stable lowercase dot IDs and existing clip-name semantics. Do not decide release version until implementation and backward-compatibility evidence exist. “v2” remains a workstream name, not an automatic breaking-version bump.

Later: scene-view puppet editing, GameObject runtime bridge, runtime visibility, clip events, hitboxes, attachment-point editor, dynamic Y sorting, crossfade, multi-turn rotation, full skin/outfit replacement, GPU pose evaluation. Only add when a concrete workflow requires it.

## 11. Paste this prompt to the implementing agent

Read DevTools~/CutoutParts-v2-Implementation-Design.md, PR #2 at its pinned commit, the product brief, and applicable repo rules. Use this document's explicit corrections as the proposed contract; report actual code conflicts instead of silently inventing another architecture. Implement only the assigned slice on the v2 worktree. Preserve main/1.0 and existing uncommitted user files. Do not add Unity 2D Animation, Spine or Mecanim dependencies, or bump URP. Reuse existing IMGUI window and CPU instanced renderer. Onion skin is mandatory. Keep Rig, Animate and Skins edits separate. Share pose/geometry evaluation across editor/runtime. Swaps preserve joints and playback. Complete the slice's tests and show evidence. No commits or PR publication unless separately requested.

Recommended first assignment: slice 1, including sampling and mixed-size/pivot appearance tests. Design is ready for that assignment. Production implementation was intentionally not performed in this review.
