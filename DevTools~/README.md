# DevTools~ (sandbox only — not for Asset Store)

This folder is for Invert Lab / AI / internal tooling while developing
DOTS Sprite Animator in this Unity project.

**Never ship this folder with the Asset Store or UPM package.**
The published product is only:

`Packages/com.invertlab.spriteanimator`

Unity ignores folders ending in `~`, and this path is outside the package,
so a normal package export will not include it.

## Contents

| Path | Purpose |
|------|---------|
| `cursor/rules/` | Cursor AI project rules (editor undo/redo checklist, etc.) |
| `Roadmap.md` | Internal feature backlog (not customer docs) |
| `patches/` | One-off local patch scripts (optional) |

## Use Cursor rules from here

Cursor reads `.cursor/rules` at the project root. To keep one source of truth:

1. Prefer a directory junction from `.cursor` → `DevTools~/cursor` (created when this layout was set up), or
2. Copy `DevTools~/cursor/rules/*.mdc` into `.cursor/rules/` after pulls.

## Related (also not packaged)

- `graft/` — local graph/cache + scratch scripts (gitignored)
