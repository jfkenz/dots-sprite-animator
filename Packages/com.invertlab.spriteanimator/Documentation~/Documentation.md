# Invert Lab DOTS Sprite Animator — User Guide

**Product:** Invert Lab DOTS Sprite Animator  
**Package id:** com.invertlab.spriteanimator  
**Version:** 0.8.0  
**Publisher:** Invert Lab  
**Namespace:** InvertLab.Sprites.DOTS  
**Unity:** 6000.0+ (Entities, Entities Graphics, URP)

This guide covers the shipped 0.8.0 feature set: playback control, combat helpers, wrap modes, editor UX, sockets, events, crowd/GPU overview, and samples.

---

## 1. Install and open tools

Embed or reference:

- Packages/com.invertlab.spriteanimator

Required packages:

- com.unity.entities
- com.unity.entities.graphics
- com.unity.render-pipelines.universal

Menus:

| Menu | Purpose |
| --- | --- |
| Window > DOTS Sprite Animator | Main timeline / sheet authoring window |
| Tools > DOTS Sprite Animator > Open Window | Same window |
| Tools > DOTS Sprite Animator > Validate Installation | Dependency check |
| Tools > DOTS Sprite Animator > Help | Opens QuickStart / README |
| Tools > DOTS Sprite Animator > Build Sockets Sample | Rebuild sockets sample |
| Tools > DOTS Sprite Animator > Build Events Sample | Rebuild events sample |
| Tools > DOTS Sprite Animator > Build Playback API Sample | Rebuild playback API sample |
| Tools > DOTS Sprite Animator > Open Crowd GPU Sample | Open crowd/GPU sample scene |
| Tools > DOTS Sprite Animator > Setup Authoring Example Scene | Authoring scene helper |

After import, run **Validate Installation**.

---

## 2. Quick authoring workflow

1. Open **Window > DOTS Sprite Animator**.
2. **New Profile**, assign spritesheet texture, rows, columns.
3. Add / edit clips (accordion UI), frames, FPS, events, colliders, sockets.
4. **Save Profile** → <SheetName>_profile.asset + .json.
5. On a GameObject: add SpriteAnimSetAuthoring, assign Profile, bake (SubScene / conversion).
6. Optional: SpriteAnimPlayerAuthoring for managed playback helpers (Play, Hitstop, queue, etc.).

Runtime basics:

`csharp
using InvertLab.Sprites.DOTS;

SpriteAnims.Play(entityManager, entity, "Run");
SpriteAnims.PlayFacing(entityManager, entity, "Walk", SpriteFacingDirection.Down);
`

---

## 3. Playback API (0.8)

APIs exist on both SpriteAnims (ECS) and SpriteAnimPlayerAuthoring (managed). Prefer orce: true only when you intentionally bypass interrupt / priority gates.

### 3.1 Interrupt and Priority

Per clip:

| Field | Meaning |
| --- | --- |
| **Interrupt** Always | Any Play may replace this clip (idle / walk). |
| **Interrupt** Never | Locked until Once/ReverseOnce completes, or Stop / orce. |
| **Interrupt** AfterTime | Cancelable only when normalized time ≥ CancelAfter (0–1). |
| **Priority** | Higher (or equal) priority may interrupt the current clip when orce is false. |

Combo windows temporarily lower the *effective* current priority via ComboWindowPriorityBoost, so follow-up attacks can land during the window.

### 3.2 Queue / One-shot / chaining

| API | Behavior |
| --- | --- |
| PlayOrQueue(...) | Tries Play; if blocked by interrupt/priority and queueIfBlocked, stores a queued clip that plays when the current clip finishes. |
| PlayOneShot(...) | Plays a clip once, then resumes the previous locomotion/idle clip. |
| OnCompleteClipIndex | When a Once/ReverseOnce clip finishes, automatically starts this clip index (-1 = none). |

### 3.3 Speed, seek, freeze, blend

| API | Behavior |
| --- | --- |
| SetSpeed / GetSpeed | Playback rate. **Negative** rewinds; **0** freezes the clock. |
| Pause / Resume | Pause without losing speed restore intent. |
| Freeze / Unfreeze | Aliases of Pause / Resume. |
| SeekFrame / SeekNormalized / SetTime | Jump within the current clip. |
| Blend | Crossfade weight **1 → 0** during fade (gameplay / shader readable; no dual draw). |

### 3.4 Lifecycle callbacks

| Callback | When |
| --- | --- |
| SpriteAnimEvents.ClipStarted / authoring ClipStarted | Clip begins (also reserved event Ids 250). |
| SpriteAnimEvents.ClipCompleted / authoring ClipCompleted | Once-style clip ends (Id 251). Completes at phase 0 when finishing with negative speed. |

Managed:

`csharp
player.ClipStarted += clipIndex => { /* ... */ };
player.ClipCompleted += clipIndex => { /* ... */ };
`

ECS / static:

`csharp
SpriteAnimEvents.ClipStarted += (entity, clipIndex) => { };
SpriteAnimEvents.ClipCompleted += (entity, clipIndex) => { };
SpriteAnimEvents.Raised += (entity, evt) => { /* frame markers */ };
`

---

## 4. Combat helpers

| API | Behavior |
| --- | --- |
| Hitstop(seconds) | Freeze the clock for a duration, then restore previous speed. Shares timer with Hold. |
| Hold(seconds) | Same shared freeze timer. |
| HoldAtFrame(frame, seconds) | SeekFrame then Hold. |
| Combo window | Clip fields ComboWindowStartFrame / EndFrame / PriorityBoost. Query with InComboWindow(); TryComboPlay uses Always interrupt + priority boost in-window. |
| SetFacing / PlayMirrored / Play/PlayFacing flipX overloads | Facing / mirror helpers for 2D combat. |
| PlayRandomStart | Play clip and seek to a random frame (crowd variety). |
| PlayWeighted | Weighted random among clip indices (NativeArray, managed arrays, or up to 4 pairs). |

Hitstop / Hold use simulation / authoring delta time (HitstopRemaining, HitstopRestoreSpeed, HitstopActive).

---

## 5. Wrap modes

| Mode | Behavior |
| --- | --- |
| Loop | Repeats forward. |
| Once | Plays forward once, then completes. |
| Ping Pong | Forward then reverse, repeating. |
| Reverse Loop | Loops while playing backward. |
| **ReverseOnce** (0.8) | Plays once from last frame toward first (positive Speed; not a Speed=-1 hack), then completes. Supports OnCompleteClipIndex / queue drain like Once. |

Clips using ping-pong, reverse, custom holds, events, sockets, or TRS tween stay **CPU only**.

---

## 6. Editor UX (0.8)

- **Clip accordion** — compact clip list with expand/collapse.
- **FPS edits** — per-clip frame rate authoring.
- **EVENT TYPES** — multi-select and delete for profile event type rows.
- **Event-marker RMB menu** — right-click markers for edit / type / delete actions.
- **Rename UX** — cleaned control IDs; frame-delete persists correctly on Save Profile.
- **Show Sprite** — scene preview toggle (renamed from Show Scene Preview; serialized as ShowSpriteInScene). Draws via MeshRenderer edit preview when on; play mode still uses SpriteInstanceRenderSystem when applicable.
- **Sockets** — named attach points; click-to-place in preview; inventories; Independent Motion layers + triggers.
- **Unity collider bake / gizmos** — square / circle / polygon colliders with Frame / Clip / Character lifetime; Physics Query AABB / Unity 2D / Both; scene gizmos and bake into runtime data.

Toolbar still includes New / Load / Save Profile, step transport |< < > >|, Play/Pause, Loop, speed, Undo/Redo, Help.

---

## 7. Sockets, independent motion, inventory

- Socket **Name** is an editor label; **ID** (prefer dotted ids like equipment.head, combat.muzzle) is the stable gameplay contract.
- **Frame-attached** sockets follow the current frame key (position px, angle, scale).
- **Independent Motion** sockets run their own timeline; RMB on the row adds triggers that fire SpriteSocketEventBuffer / SpriteSocketEvents.Raised.
- **Inventory** patterns help organize equipment / VFX attach points across clips.
- SyncUnitySockets() keeps GameObject socket transforms aligned with the current frame (authoring / preview path).
- Attach with SpriteSocketAttachmentAuthoring on a child; baker resolves stable ID.
- Runtime lookup: SpriteSockets.TryGetWorldPose / TryGetPose.
- Sockets force **CPU** playback (not GPU clock).

See also Documentation~/SocketGameplayAPI.md.

---

## 8. Animation events (Footstep / Attack story)

Author markers in the timeline window. Each marker has:

- Byte **Id** (profile EVENT TYPES)
- Exact in-frame timestamp
- Fire mode **Loop** or **Once**
- Optional typed payloads (Int/Float vectors, Bool, Color, Text, Asset GUID hash, …)

**Story pattern:** put a Footstep marker on walk contact frames and an Attack hit marker on the impact frame. Receivers spawn dust / SFX / hitboxes from SpriteAnimEventBuffer or SpriteAnimEvents.Raised.

EventMarkers is the source of truth (multiple markers per frame). EventIds[] keeps the first marker per frame for older profiles / GPU eligibility.

Clips with any event markers stay on the CPU player.

See Documentation~/AnimationEvents.md for payload / receiver details.

---

## 9. Crowd / GPU path overview

Two playback paths:

| Path | Use when |
| --- | --- |
| **CPU** (SpriteAnimPlayerSystem) | Events, sockets, custom holds, reorder, TRS tween, ping-pong / reverse / ReverseOnce, combat helpers needing precise markers. |
| **GPU clock** | Simple uniform sequential **Loop** / **Once** clips. Inspector badge **GPU clock OK**. |

SpriteGpuAnimSwitch.ToGpu(...) refuses non-eligible clips.

For crowds:

- SpriteCrowdSpawnerAuthoring (and sample AuthoringCrowdDemo) primes instance scale / material so GPU sprites render at the intended size.
- Use GPU-eligible idle/walk clips for density; keep hero / VFX on CPU when you need events or sockets.
- Sample: Assets/Samples/CrowdGpuExample — open via **Tools > DOTS Sprite Animator > Open Crowd GPU Sample**.

Preview / edit mesh uses SpriteUnlit2DPreview (separate from instanced runtime shaders).

---

## 10. Samples

Shipped under Assets/Samples/:

| Folder | What it shows | How to open |
| --- | --- | --- |
| **ColliderEventExample** | Frame / clip / character colliders + event-driven reactions | Open scene under ColliderEventExample/Scenes/ |
| **EventsExample** | Footstep / Attack markers, ClipStarted / ClipCompleted | **Tools > DOTS Sprite Animator > Build Events Sample**, then open scene |
| **PlaybackApiExample** | Play, PlayOneShot, PlayOrQueue, Priority, Hitstop, Hold (keys 1–6) | **Tools > … > Build Playback API Sample** |
| **CrowdGpuExample** | Dense GPU / crowd spawn path | **Tools > … > Open Crowd GPU Sample** |
| **Sockets** | Weapon socket attach, Idle/Attack, CPU path | **Tools > … > Build Sockets Sample**; open SocketsExample.unity |
| **Showcase/Clembod** | Warrior / Bringer Of Death art for demos | See License.txt / Contact.txt — credit Clembod appreciated |

Showcase art © Clembod (personal/commercial use OK; do not redistribute/resell the art alone). Credit not required but appreciated: [@Clembod](https://clembod.itch.io).

---

## 11. Runtime integration checklist

1. Profile authored and saved.
2. SpriteAnimSetAuthoring on entity root with Profile assigned.
3. Optional SpriteAnimPlayerAuthoring for MonoBehaviour-driven gameplay.
4. For ECS receivers: systems [UpdateAfter(typeof(SpriteAnimPlayerSystem))] reading SpriteAnimEventBuffer with SpriteAnimEventsPending.
5. Choose CPU vs GPU per clip eligibility; do not force GPU on event/socket clips.
6. Validate Installation before packaging builds.

---

## 12. Related docs in this package

- Documentation~/QuickStart.md — short install path
- Documentation~/AnimationEvents.md — markers, payloads, receivers
- Documentation~/SocketGameplayAPI.md — socket IDs, attachments, independent triggers
- CHANGELOG.md — version history
- README.md — package overview

---

*© Invert Lab. DOTS Sprite Animator 0.8.0.*
