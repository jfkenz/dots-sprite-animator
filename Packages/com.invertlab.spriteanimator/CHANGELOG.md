# Changelog

## [0.8.0] - 2026-09-02

- Scene sockets: convert pivot-relative pixel poses to bottom-center mesh local so independent/frame sockets (e.g. HealthBarSocket) match the animator preview after the cell pivot change. Formula: meshLocal = ((pivot - (0.5,0)) * cellSize + pixels) / PPU.
- Added Bake Pivot (default on) — empty Pivot child at local (0,0) — and Show Scene Pivot gizmo (crosshair + label).

- Editor: renamed inspector toggle **Show Scene Preview** -> **Show Sprite** (`ShowSpriteInScene`, FormerlySerializedAs).
- Combat/playback pack: `Hitstop`/`Hold`/`HoldAtFrame` (shared `HitstopRemaining`/`HitstopRestoreSpeed`/`HitstopActive` timer; simulation delta), combo window (`ComboWindowStartFrame`/`EndFrame`/`PriorityBoost` on clip + blob; `InComboWindow`/`TryComboPlay`; interrupt Always + priority boost in window), facing helpers (`SetFacing`, `PlayMirrored`, `Play`/`PlayFacing` flipX overloads), `PlayRandomStart`, `PlayWeighted` (NativeArray + managed + up to 4 pairs).
- Clip extras: `SetSpeed`/`GetSpeed` (negative = rewind, 0 = freeze clock), `Pause`/`Resume`/`Freeze`/`Unfreeze`, `SeekFrame`/`SeekNormalized`/`SetTime` on `SpriteAnims` and `SpriteAnimPlayerAuthoring`.
- Lifecycle: `SpriteAnimEvents.ClipStarted` / `ClipCompleted` (+ reserved Ids 250/251). Once + negative speed completes at phase 0.
- Playback control: clip `Interrupt` modes `Always` / `Never` / `AfterTime`, plus clip `Priority` gating for `Play`.
- Queue helpers: `PlayOrQueue` and `PlayOneShot` on the authoring player.
- Clip `OnCompleteClipIndex` chaining when a once-shot finishes.
- Crossfade: `Blend` weight (1->0) exposed for gameplay / shaders during fade.
- Sockets: `SyncUnitySockets` keeps GameObject socket transforms aligned with the current frame.
- Editor: rename UX control IDs cleaned up; frame-delete now persists correctly on Save Profile.
- Package cleanup: removed broken BallForge/legacy Examples scene; control focus names use InvertLab prefixes.

## 0.7.0 - 2026-08-27

- Rebranded package ownership under **InvertLab**:
  - Package id: `com.invertlab.spriteanimator`
  - Namespace: `InvertLab.Sprites.DOTS`
  - Assemblies: `InvertLab.SpriteAnimator.*`

- Renamed package display branding to **DOTS Sprite Animator** and updated menu paths to:
  - `Window > DOTS Sprite Animator`
  - `Tools > DOTS Sprite Animator > *`
- Added per-frame sockets (named local attach points) to clip data and runtime blob.
- Click-to-place sockets in the preview (Add Socket → balloon → click frame). Same
  name is one identity across frames; `LocalPosition` and `LocalAngle` are per-frame
  keys copied into `SpriteSocketBuffer` (CPU playback, no socket tween).
- Added collider copy workflows:
  - Copy current frame colliders to next frame
  - Copy current frame colliders to all frames
- Added facing metadata for 4-way / 8-way clip grouping (`Facing Group` + `Facing`).
- Added preview zoom + pan controls.
- Added `Documentation~/QuickStart.md` and updated package docs for 0.7 workflows.

## 0.6.0 - 2026-08-27

- Added toolbar profile actions: **New Profile** and **Load Profile…**.
- Added per-frame TRS authoring channels and blob/runtime support:
  - Position offset
  - Scale
  - Rotation
  - Tween mode
- Added built-in easing helper (`Linear`, `SmoothStep`, `EaseIn`, `EaseOut`, `Step`).
- Added GPU eligibility checks and inspector badge:
  - **GPU clock OK** for simple sequential Loop/Once clips
  - **CPU only** for clips using advanced channels (offsets, holds, events, reorder,
    ping-pong/reverse, TRS tween, sockets)
- Extended `SpriteGpuAnimSwitch.ToGpu` gating to keep refusing non-eligible clips.
- Added UV flip support on both instanced and GPU-anim shader paths (no 180° transform hack).
- Added step-frame transport controls (`|<`, `<`, `>`, `>|`) and left/right shortcuts
  when no onion ghost is selected.

## 0.5.1 - 2026-08-27

- Fixed timeline playhead mapping so the red needle is a continuous function of preview
  time across frame boundaries and wrap modes (Loop, Once, Ping Pong, Reverse Loop).
- Kept pointer-follow behavior only during active scrub/reorder/resize/event drags.
- Defaulted preview playback to paused on open.
- Disabled Play when no clip is available.
- Updated toolbar Undo/Redo labels to plain text for Windows compatibility.
- Disabled Save Profile until a sheet is assigned.
- Added installation validator dialog feedback on both success and failure (with Console logs).
- Added Help entry points:
  - `Tools > DOTS Sprite Animator > Help`
  - Toolbar Help button opening QuickStart / README.
