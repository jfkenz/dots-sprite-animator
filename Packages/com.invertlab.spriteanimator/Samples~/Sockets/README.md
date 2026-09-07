# Sockets runtime sample

Open `SocketsExample.unity`, then enter Play mode.

- Profile: `Assets/Samples/Sockets/SocketsProfile.asset`
- Character art: `Assets/Samples/Showcase/Sword Character Prototype_All Frames.png`
- Attached item: a sample-local Quad using `SocketsSword.mat`
- Socket: `Weapon`
- Clips: `Idle`, `Attack`
- Keys: `[` / `]` cycle clips; `1` selects Idle; `2` selects Attack

KEEP The weapon is a baked child with
`SpriteSocketAttachmentAuthoring`. Sockets force CPU animation playback; this
sample does not use `SpriteCrowdSpawnerAuthoring` or `SpriteGpuDriven`.

`SpriteAnimSetAuthoring` bakes authored socket pixels with each clip sheet's PPU.
`SpriteSocketAttachmentSystem` runs after `SpriteAnimPlayerSystem`, reads the current
`SpriteSocketBuffer`, and applies local position, Z rotation, and socket scale to the
weapon entity. It compensates for the source Quad's render-size scale, so baked
pixel/PPU offsets and the weapon's authored size stay in world units. Missing frame
keys retain the last pose. The sample controller only switches clips and configures
the CPU renderer.

`SpriteAnimPlayerAuthoring` is useful here for explicit initial clip, speed, and
preview controls, but is not required by the attachment itself:
`SpriteAnimSetAuthoring` already bakes the runtime `SpriteAnimPlayer`.

Show Sprite is off here: play mode draws via `SpriteInstanceRenderSystem`, not the MeshRenderer edit preview.

Rebuild generated profile/scenes with **Tools > DOTS Sprite Animator > Build Sockets Sample**.
