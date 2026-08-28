# Sockets runtime sample

Open `SocketsExample.unity`, then enter Play mode.

- Profile: `Assets/Samples/Sockets/SocketsProfile.asset`
- Character art: `Assets/Samples/Showcase/Sword Character Prototype_All Frames.png`
- Attached item: `Assets/Samples/Showcase/sword_angles.png`
- Socket: `Weapon`
- Clips: `Idle`, `Attack`
- Keys: `[` / `]` cycle clips; `1` selects Idle; `2` selects Attack

`SpriteAnimSetAuthoring` and `SpriteAnimPlayerAuthoring` live on the Quad in
`SocketsExample_SubScene.unity`. Sockets force CPU animation playback; this sample
does not use `SpriteCrowdSpawnerAuthoring` or `SpriteGpuDriven`.

`SpriteAnimSetAuthoring` bakes authored socket pixels with each clip sheet's PPU.
`SocketsExampleController` reads the current `SpriteSocketBuffer` every frame and
applies its position and angle to the sword. Missing frame keys retain the last pose.

Rebuild generated profile/scenes with **Tools > DOTS Sprite Animator > Build Sockets Sample**.
