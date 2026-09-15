# Cutout Parts v2 — Sprite keys (locked 2026-09-15)

Same-profile Parts authoring: `ScriptableSpriteSheetProfile` / `SpriteSheetProfile` owns Sheets, Frame clips, and Parts data (slots, appearances, Parts clips, skins). `AnimKind` selects Frame vs Parts. Appearances already bind **sheet index + cell** on the profile — no external random textures.

## Transform-default keys + optional sprite

Parts clip keys remain TRS (parent-local) by default. `SpritePartsKeyDef.AppearanceId` is optional:

- **Empty** on a key = hold the previous keyed appearance (do not change art).
- **Non-empty** = a profile `PartsAppearances` id (sheet/cell already on the profile).

Sampler: TRS interpolates as before. Appearance = last non-empty AppearanceId at/before wrapped time. **Loop** carries the last keyed appearance across the wrap when the playhead sits before the first sprite key; **Once** does not. While a keyed appearance is active it wins over skin; when none is active, skins/defaults own the slot. Geometry comes from the appearance resolver; joint TRS is unchanged by sprite swaps. Blob keys store `AppearanceIndex` (-1 = hold). Runtime player applies appearance when the sampled id changes and restores skin/default when keyed override ends.

Editor Animate: **Key Sprite** and optional **Incl. Sprite** on Key Pose write AppearanceId from a dropdown bound to profile Appearances. Timeline diamonds mark keys that carry a sprite change.

## Hierarchy menu (Rig)

Delete felt broken (Rig-only, no Delete key, lock blocks). Fix: bind Delete/Backspace when Parts selection is active and not renaming; context menu always lists Delete Subtree (disabled with a clear reason if not Rig / locked); status shows the failure reason.

Right-click Rig menu: Rename, Add Child, **Break from Parent** (→ Character root, preserve rest world pose), **Move Up One Level** (→ grandparent or root, preserve rest world pose), Delete Subtree, optional Move to Top/Bottom of siblings (SiblingOrder only). Animate/Skins: structural items disabled with “Switch to Rig…”.