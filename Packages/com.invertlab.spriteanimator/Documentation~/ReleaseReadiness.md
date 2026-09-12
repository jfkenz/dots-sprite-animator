# 1.0 release preparation

Status: **1.0.0 prepared for release on the validated Windows / URP baseline.**

The exported UPM archive was installed through Unity Package Manager in the clean
project and passed its full applicable suite. Final archive changes after that run
contain only documentation and validation evidence; executable content is unchanged.

- Full suite with imported samples and Unity Physics: **133 passed, 0 failed, 0 skipped**.
- Fresh registry-resolved install without Unity Physics or Input System: **128 passed, 0 failed, 0 skipped**.
- Windows x64 player compilation: **0 errors, 0 warnings**.
- Windows player camera pixels: CPU and GPU sprites visible in unlit and lit modes; zero-intensity 2D light produces black sprites.
- Four regression cases verify that world-space instance positions ignore an unrelated object matrix.

Results: `Validation-Full-2026-09-12.xml`, `Validation-Clean-2026-09-12.xml`, and `Validation-Player-2026-09-12.txt`.

## Supported baseline

- Unity 6000.5; current validation editor: 6000.5.2f1.
- URP 17.5.0, Entities 6.5.0, Entities Graphics 6.5.0.
- Windows x64 / Direct3D 11 is the validated player target.
- Built-in rendering, HDRP, WebGL, mobile, and older Unity releases are not certified by these checks.

The previous Unity 6000.0 package declaration conflicted with URP 17.5.0's own
Unity 6000.5 requirement. The package now declares the actual dependency baseline.

## Implemented fixes

- Atlas records include texture, grid, aspect, crop values, and world identity.
- Crop indices cannot read into another atlas record. Invalid frames are skipped.
- CPU batch uploads start at the beginning of the sorted batch.
- Factory grids use full texture dimensions, support frame subsets, and keep independent sheet bindings.
- GPU buffers and materials are owned per world; dirty versions reach every world.
- Dynamic draw bounds follow sprites beyond the old fixed boundary.
- XY transforms preserve matrix basis vectors, including reflection and shear; offsets follow the entity transform.
- Culling uses camera frustum planes and current parent transforms.
- Baking and runtime crowd clip conversion share one implementation.
- Runtime preview/socket/collider APIs are no longer hidden by an editor-only guard.
- Procedural shaders project world-space instance positions directly, avoiding a second object transform.
- Runtime shader variants are retained through a collection in Resources.
- Starter sample uses generated geometric art and no optional input or physics package.

## Explicit limits

GPU playback still uses one compatible sheet layout and an optional shared crowd
recipe. Per-world GPU buffers do not imply independently configurable GPU atlases
or shared crowd clocks. Use CPU sheet bindings for multiple layouts.

Factory input must contain unpacked, aligned, equal-size cells. Irregular or packed
SpriteAtlas rectangles must use profile Cropped layouts; factory input validation
rejects them instead of guessing UVs.

Frame sockets and event payloads share clip conversion. Independent socket clocks
and hitbox construction use normal authoring/baking; the crowd tool is not a
replacement for baking complete gameplay entities.

## Distribution gates

- Clembod separate terms confirmed by the publisher; preserve the supplied license files.
- Fresh-install, optional-physics-off, player-build, and actual rendering checks passed.
- Exported file inventory hashes, sample paths, and declared dependencies verified.
- Package manifest and editor version are 1.0.0.
- Marketplace submission, pricing, and account actions remain publisher decisions.

Unity's current [submission guidelines](https://assetstore.unity.com/publishing/submission-guidelines)
cover documentation, third-party notices, supported versions, and package quality.
Consult them for the chosen submission route; this report does not imply store approval.

## Release recommendation

Ready for an initial 1.0 sale with the supported baseline and GPU limitations stated
in the listing. These checks establish a working release, not a guarantee that every
project configuration is covered. Do not advertise additional Unity versions or
platforms until tested there.

Prioritize bug fixes and user feedback after release. Independent GPU atlases,
per-world crowd clocks, and wider platform coverage are useful later additions;
they are not required to ship the documented current feature set.
