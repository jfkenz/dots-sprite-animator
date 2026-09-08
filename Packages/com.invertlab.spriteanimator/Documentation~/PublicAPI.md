# Public API (recommended surface)

Use these entry points in gameplay. Prefer them over digging into systems or blob internals.

## Authoring (Inspector)

| Type | Role |
| --- | --- |
| `SpriteAnimSetAuthoring` | Sheet/profile, clips, size, tint, **PlaybackPath** |
| `SpriteAnimPlayerAuthoring` | Initial clip, speed, flip, queue helpers |
| `SpriteColliderAuthoring` | Scope + Method (Query / Unity2D / UnityPhysics) |
| `SpriteSocketAttachmentAuthoring` | Attach a transform to a named socket |
| `SpriteCrowdSpawnerAuthoring` | Dense spawn / GPU crowd |

## Playback

| API | Role |
| --- | --- |
| `SpriteAnims.Play` / `PlayFacing` / `PlayOneShot` / `PlayOrQueue` | Clip control |
| `SpriteAnims.SetSpeed` / `Pause` / `Resume` / `SeekFrame` | Clock control |
| `SpriteAnims.Hitstop` / `Hold` / `SetFlip` / `SetFacing` | Combat helpers |
| `SpriteAnims.TryToGpu` / `ToCpu` / `IsGpuDriven` | Optional GPU flipbook path |

## Colliders

| API | Role |
| --- | --- |
| `SpriteHitboxQuery` | Frame AABB from profile data |
| Unity 2D children via bake | When Method = Unity2D / Both |
| `SpriteUnityPhysicsHurtbox.Ensure` / `SyncTransform` / `Destroy` | Optional Physics (needs `com.unity.physics`) |

## Events / sockets

| API | Role |
| --- | --- |
| `SpriteAnimEvents` | ClipStarted / ClipCompleted + buffer readers |
| `SpriteSockets` / `SpriteSocketAttachment` | Live socket poses |

## Avoid as public contract

- Direct mutation of `SpriteAnimSetBlob` / baker internals
- `SpriteSheetToolWindow` editor types (editor-only)
- `SpriteGpuAnimSwitch` low-level fields except via `SpriteAnims.TryToGpu` / `ToCpu`

GPU path limits: single uniform sheet, Loop/Once, no sockets/events/crops. See Architecture.md.
