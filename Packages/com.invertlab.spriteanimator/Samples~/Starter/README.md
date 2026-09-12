# Starter

Use a Unity 6000.5 project with a Universal Render Pipeline asset assigned.
Add `SpriteAnimatorStarter` to an empty GameObject in an empty scene, then enter Play.
The sample creates its camera, atlas, sprites, and playback controls.

Left sprite uses CPU animation. Right sprite starts on the GPU clock.
Pause, restart, flip, and switch CPU/GPU with the on-screen buttons.
The sample uses generated geometric pixels and requires no third-party art,
Input System package, Unity Physics package, or keyboard configuration.

The factory accepts unpacked, equally sized, aligned cells from one grid texture.
For irregular rectangles, use profile Cropped layouts and CPU playback.
