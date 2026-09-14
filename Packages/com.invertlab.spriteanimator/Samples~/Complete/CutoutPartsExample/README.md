# Cutout Parts Example

Pure DOTS cutout Parts sample for Invert Lab DOTS Sprite Animator.

## One-click demo (does not overwrite your scene)

In the Unity editor:

1. **Tools → DOTS Sprite Animator → Create Parts Demo Scene**
2. Enter Play Mode

That opens a **new** empty scene with `CutoutPartsDemoBootstrap` (generated art, Walk clip, `sword.iron` / `spear.oak` skins). It never writes over your previously open scene asset.

## Controls

- **OnGUI**: Pause/Resume, swap weapon, flip facing, reset skin
- **Pointer / touch**: tap left half = swap weapon; right half = flip facing
- No Input System package required

## Runtime APIs exercised

- `SpriteParts.TryGetSlot`
- `SpriteParts.SetSlotAppearance` / `SetSlotSheet`
- `SpriteParts.ApplySkin` / `ResetSkin`

Swap changes sheet/cell + derived visual geometry only — Walk time/index/keys/joint pose stay put.

## Note

This folder is part of the Complete sample. The demo bootstrap lives in the package Runtime so Create Demo Scene works even before importing Samples.
