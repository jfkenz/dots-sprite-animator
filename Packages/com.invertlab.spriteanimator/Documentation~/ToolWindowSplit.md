# SpriteSheetToolWindow split plan

`Editor/SpriteSheetToolWindow.cs` is ~1 MB and blocks maintainability. Split without behavior change.

## Rules
- Keep the same Window menu path and public editor entry points.
- Prefer `partial class SpriteSheetToolWindow` + private helper types in `Editor/`.
- One coherent extraction per PR; compile after each.
- Do not mix feature work into a split PR.

## Suggested slices (order)
1. **IO / import helpers** — file pickers, sprite-sheet load, texture decode.
2. **Drawing / Gizmo paint** — frame overlay, rect draw utilities.
3. **Clip / frame data UI** — lists, rename, reorder (self-contained panels).
4. **Collider / hitbox panel** — if still inlined in the same file.
5. **GPU / preview strip** leftovers last.

## Status
- 2026-09-07: plan only. Cloud agent launch blocked on Cursor on-demand usage.
- Local hygiene (queries, PublicAPI, collider help) landed separately.
