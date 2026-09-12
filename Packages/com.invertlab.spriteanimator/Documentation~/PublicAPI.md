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

## Factory and atlas contracts

`SpriteEntityFactory.Create` accepts unpacked, equal-size cells aligned to a grid.
You may supply any subset or order of cells; the grid comes from full texture dimensions.
Different factory textures receive independent `SpriteSheetBinding` entities.
Use profile Cropped layouts for irregular atlas rectangles.

Factory blobs belong to the creating world. Clone with `EntityManager.Instantiate`
in that world; the last owner releases the blob. Do not manually dispose factory blobs
or copy their raw references to another world. Caller-built blobs remain caller-owned.

Pause, seek, speed, and gameplay controls on a GPU entity restore CPU playback at
its displayed phase. Low-level `SpriteGpuAnimSwitch.ToCpu` restores the parked state;
`ToCpuAtTime` restores the running GPU phase.

GPU draw buffers/materials are owned per world. The GPU path still requires one
compatible sheet layout across active GPU sprites; conflicting sheet changes are rejected.
CPU rendering supports independent sheet layouts on one texture and multiple textures.

For explicit non-default physics worlds, use `UnityPhysicsOverlapBridge.Queue(world, ...)`
and that world's bridge `Hit` event. Static callbacks target the default world.
