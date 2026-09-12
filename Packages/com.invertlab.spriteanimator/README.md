# DOTS Sprite Animator

Portable Unity 6000.5 package for DOTS-first 2D flipbook animation authoring and runtime playback.

Current package version: **1.0.0**. Full guide: [Documentation](Documentation~/Documentation.md). Start with [Quick Start](Documentation~/QuickStart.md).

Published by **Invert Lab**.

License: see `LICENSE.md` (Unity Asset Store EULA).

## Mental model

See `Documentation~/Architecture.md` (Profile -> Play -> optional colliders) and `Documentation~/Samples.md`.

## Scope

- Publisher: **Invert Lab**.
- Package id: `com.invertlab.spriteanimator` (internal; not shown on the store).
- Runtime namespace: `InvertLab.Sprites.DOTS`.
- Runtime assembly: `InvertLab.SpriteAnimator.Runtime`.
- No gameplay-framework dependencies (no NZCore, Rukhanka, Trove, or project-only assemblies).
- Supported dependencies: Entities, Entities Graphics, and URP.

## Samples

Optional — not imported until you choose them in Package Manager (**Samples** tab). See `Documentation~/Samples.md`.

## Open the tools

- `Window > DOTS Sprite Animator`
- `Tools > DOTS Sprite Animator > Validate Installation`
- `Tools > DOTS Sprite Animator > Help`

## Toolbar highlights

- **New Profile** / **Load Profileâ€¦** / **Save Profile**
- Step-frame transport `|<  <  >  >|`
- Play/Pause, Stop, Loop, playback speed
- Undo/Redo text buttons (Windows-safe)
- Help and install validation shortcuts

## Timeline and preview

- Continuous playhead mapping for Loop, Once, Ping Pong, and Reverse Loop.
- Ruler scrubbing and drag-driven frame reorder/resize/events.
- Preview zoom + pan.
- Per-frame TRS keys:
  - Position offset
  - Scale
  - Rotation
  - Tween mode (`Linear`, `SmoothStep`, `EaseIn`, `EaseOut`, `Step`)

## GPU eligibility badge

Each clip shows either:

- **GPU clock OK** (uniform sequential loop/once clip), or
- **CPU only** (uses offsets, holds, events, reorder, ping-pong/reverse, TRS tween, sockets, etc.).

`SpriteGpuAnimSwitch.ToGpu` keeps refusing non-eligible clips.

## Colliders and sockets

- Author square/circle/polygon colliders per frame.
- Copy colliders to next frame or all frames.
- Click **Add Socket**, then click the preview to place a named attach point (`index:Name`). The same name is one identity across frames; position (px) and angle (deg) can differ per frame. Sockets stay CPU-only.

## Facing groups

Clips can be grouped under a logical facing group (for 4-way / 8-way sets) with explicit facing direction metadata.

## Runtime integration

1. Add `SpriteAnimSetAuthoring` and assign a profile or direct clip data.
2. Playback:
   - `SpriteAnims.Play(entityManager, entity, "Run")`
   - `SpriteAnims.PlayFacing(entityManager, entity, "Walk", SpriteFacingDirection.DownLeft)`

See:

- `Documentation~/QuickStart.md`
- `Documentation~/AnimationEvents.md`



## Validated release baseline

Unity 6000.5.2f1, Entities / Entities Graphics 6.5.0, URP 17.5.0, Windows x64 / Direct3D 11.
The Starter sample needs no optional input or physics package. Complete sample scripts
activate only when their optional dependencies are installed. See `Documentation~/ReleaseReadiness.md`
for release validation evidence and the supported CPU/GPU paths.
