# DOTS Sprite Animator Quick Start

Publisher: **Invert Lab**. Release baseline: **Unity 6000.5 / Entities 6.5 / URP 17.5**.

## 1) Install

Embed or reference the package:

- Packages/com.invertlab.spriteanimator

Required packages:

- com.unity.entities
- com.unity.entities.graphics
- com.unity.render-pipelines.universal

Use **Tools > DOTS Sprite Animator > Validate Installation** after import.

For a self-contained first run, import **Starter** from Package Manager Samples,
add `SpriteAnimatorStarter` to an empty GameObject, assign a URP asset, and enter Play.
For the full examples, import **Complete**; interactive examples use Input System,
and the Unity Physics example additionally requires `com.unity.physics`.

## 2) Create a profile

1. Open **Window > DOTS Sprite Animator**.
2. Click **New Profile**, then choose **Static Sprite**, **Frame Animation**, or **Parts Character**.
3. Assign your image. Static starts with **Whole Image**; choose **Sheet Cell** only for a sliced spritesheet.
4. For Frames, configure rows/columns and add clips. For Parts, build the rig and key its poses. Static needs no clips.
5. Click **Save Profile**.

The profile saves as:

- <SheetName>_profile.asset
- <SheetName>_profile.json

### Place a static sprite

1. Open an ECS SubScene for editing. If several are open, select an object inside the target SubScene.
2. In **Static**, choose an image, then **Whole Image** or a sheet cell.
3. Click **Create Scene Object**. This saves the profile and creates configured static authoring in the SubScene.
4. Position the object in Scene view. Its preview remains visible while SubScene live baking hides ordinary authoring renderers.
5. Enter Play to use the baked sprite. **Show Sprite In Scene** controls the editing preview, not runtime visibility.

Use **Apply to Selected Object** to convert an existing object with Undo. Workspace tabs only change the editing view; an inactive workspace offers **Use Static for Character** to change the profile's runtime mode.

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

```csharp
SpriteAnims.Play(entityManager, entity, "Run");
SpriteAnims.PlayFacing(entityManager, entity, "Walk", SpriteFacingDirection.Down);
player.PlayOneShot("Attack");
player.Hitstop(0.12f);
```

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

