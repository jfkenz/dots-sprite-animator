# DOTS Sprite Animator Quick Start

Publisher: **Invert Lab**. Package version **0.8.1**.

## 1) Install

Embed or reference the package:

- Packages/com.invertlab.spriteanimator

Required packages:

- com.unity.entities
- com.unity.entities.graphics
- com.unity.render-pipelines.universal

Use **Tools > DOTS Sprite Animator > Validate Installation** after import.

## 2) Create a profile

1. Open **Window > DOTS Sprite Animator**.
2. Click **New Profile**.
3. Assign your spritesheet texture, rows, and columns.
4. Add or load clips (accordion UI; edit FPS, wrap, interrupt, priority).
5. Click **Save Profile**.

The profile saves as:

- <SheetName>_profile.asset
- <SheetName>_profile.json

## 3) Author animation data

Per clip:

- Wrap mode (Loop / Once / Ping Pong / Reverse Loop / **ReverseOnce**)
- Interrupt (Always / Never / AfterTime) + Priority + optional OnCompleteClipIndex
- Frame order + hold durations + FPS
- Exact-time frame events (EVENT TYPES; multi-select delete; marker RMB menu)
- Optional facing group + direction metadata
- Combo window fields for combat follow-ups

Per frame:

- Sheet column
- Position offset, Scale, Rotation, TRS tween
- Colliders (Square / Circle / Polygon; Frame / Clip / Character; Unity bake/gizmos)
- Sockets: **Add Socket**, click preview; inventories; Independent Motion layers

Inspector: **Show Sprite** toggles scene MeshRenderer preview (ShowSpriteInScene).

## 4) Runtime setup

1. Add SpriteAnimSetAuthoring to a GameObject.
2. Assign Profile.
3. Bake to entities (SubScene or conversion flow).
4. Optional: SpriteAnimPlayerAuthoring for managed Play / Hitstop / queue APIs.

Runtime calls:

`csharp
SpriteAnims.Play(entityManager, entity, "Run");
SpriteAnims.PlayFacing(entityManager, entity, "Walk", SpriteFacingDirection.Down);
player.PlayOneShot("Attack");
player.Hitstop(0.12f);
`

## 5) GPU vs CPU path

The **GPU clock** is a simple flipbook path: the shader picks the frame from a global clock. It is **not** a full feature substitute for the CPU player.

Clip / inspector eligibility:

- **GPU clock OK**: simple uniform sequential **Loop** / **Once** clips
- **CPU only**: events, custom holds, reorder, offsets, sockets, TRS tween, ping-pong / reverse / ReverseOnce

`SpriteGpuAnimSwitch.ToGpu` / `SpriteAnims.TryToGpu` only accept eligible clips.

### Playback Path (SpriteAnimSetAuthoring)

| Mode | Behavior |
| --- | --- |
| **Auto** | CPU default (same as before). |
| **PreferGpu** | After bake, convert once when eligible. Promotes a single uniform sheet onto the legacy GPU `SetSheet` path. |
| **ForceCpu** | Stay on (or return to) the CPU clock. |

PreferGpu stays on CPU when:

- the clip is not GPU-eligible
- the profile uses **multiple sheets**
- the sheet uses **Cropped** cell layout (needs CPU CropST)

## 6) Samples

Samples are **optional**. Import from Package Manager → DOTS Sprite Animator → Samples.

See Samples.md. Do not expect `Assets/Samples` until you import.
## Architecture

See **Architecture.md** for the Profile -> Play -> optional collider diagram.

See **Samples.md** for which sample to open for each feature.

## Optional: Unity Physics

Install com.unity.physics only if you need Method = UnityPhysics. Core animation and Query/Unity2D do not require it.

