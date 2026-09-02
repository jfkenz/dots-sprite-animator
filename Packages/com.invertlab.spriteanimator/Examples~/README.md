# DOTS Sprite Animator Examples

Publisher: **Invert Lab**. Samples live in the project under Assets/Samples/ (not only inside this package folder).

| Sample | Path | Open |
| --- | --- | --- |
| Collider + events | Assets/Samples/ColliderEventExample | Open scene in Scenes/ |
| Animation events | Assets/Samples/EventsExample | **Tools > DOTS Sprite Animator > Build Events Sample** |
| Playback API | Assets/Samples/PlaybackApiExample | **Tools > DOTS Sprite Animator > Build Playback API Sample** |
| Crowd / GPU | Assets/Samples/CrowdGpuExample | **Tools > DOTS Sprite Animator > Open Crowd GPU Sample** |
| Sockets | Assets/Samples/Sockets | **Tools > DOTS Sprite Animator > Build Sockets Sample** |
| Showcase art | Assets/Samples/Showcase/Clembod | Clembod license — credit appreciated |

Typical integration pieces:

1. A spritesheet texture
2. A ScriptableSpriteSheetProfile asset
3. A scene with SpriteAnimSetAuthoring (+ optional SpriteAnimPlayerAuthoring)
4. Scripts calling SpriteAnims.Play, PlayOneShot, PlayOrQueue, Hitstop, etc.

Docs: Documentation~/QuickStart.md, Documentation~/Documentation.md, Documentation~/DOTS-Sprite-Animator-User-Guide-v0.8.0.pdf.
