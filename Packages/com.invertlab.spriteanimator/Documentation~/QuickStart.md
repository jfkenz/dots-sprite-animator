# DOTS Sprite Animator Quick Start

Publisher: **Invert Lab**.

## 1) Install

Embed or reference the package:

- `Packages/com.invertlab.spriteanimator`

Required packages:

- `com.unity.entities`
- `com.unity.entities.graphics`
- `com.unity.render-pipelines.universal`

Use **Tools > DOTS Sprite Animator > Validate Installation** after import.

## 2) Create a profile

1. Open **Window > DOTS Sprite Animator**.
2. Click **New Profile**.
3. Assign your spritesheet texture, rows, and columns.
4. Add or load clips.
5. Click **Save Profile**.

The profile saves as:

- `<SheetName>_profile.asset`
- `<SheetName>_profile.json`

## 3) Author animation data

Per clip:

- Wrap mode (Loop / Once / Ping Pong / Reverse Loop)
- Frame order + hold durations
- Exact-time frame events
- Optional facing group + direction metadata

Per frame:

- Sheet column
- Position offset
- Scale
- Rotation
- TRS tween mode
- Colliders
- Sockets: click **Add Socket**, then click the preview frame to place a named attach point. The same name is one identity across frames; position and angle are keyed per frame.

## 4) Runtime setup

1. Add `SpriteAnimSetAuthoring` to a GameObject.
2. Assign `Profile`.
3. Bake to entities (SubScene or conversion flow).

Runtime calls:

```csharp
SpriteAnims.Play(entityManager, entity, "Run");
SpriteAnims.PlayFacing(entityManager, entity, "Walk", SpriteFacingDirection.Down);
```

## 5) GPU vs CPU path

Clip badge in inspector:

- **GPU clock OK**: simple uniform loop/once clips
- **CPU only**: clips using advanced channels (events, custom holds, reorder, offsets, sockets, TRS tween, ping-pong/reverse)

`SpriteGpuAnimSwitch.ToGpu(...)` only accepts eligible clips.
