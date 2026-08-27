# Changelog

## 0.7.0 - 2026-08-27

- Renamed package display branding to **DOTS Sprite Animator** (package id unchanged:
  `com.ballforge.spriteanimator`) and updated menu paths to:
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
