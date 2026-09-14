# v2 Cutout Parts — technical design

Status: **technical design for implementers**. Product is locked (JFKENZ brief).  
Do **not** implement Runtime/Editor feature code from this file in the same PR.  
Sandbox only: `DevTools~/CutoutParts-v2-Design.md`. **Not** package `Documentation~/`.

Package: `com.invertlab.spriteanimator` · Unity 6000.5 · Entities 6.5 · URP **17.5.0** (do not bump casually).  
1.0.0 (Astra) is flipbook frame clips. This document is the cutout/puppet layer.

---

## Vocabulary (do not re-litigate)

| Term | What it is | What it is not |
|------|------------|----------------|
| **Cutout / puppet / paper-doll** | Named body **slots** in a parent hierarchy. Each slot draws one sheet cell. Tween **parts clips** move those slots. | Unity 2D Animation, Sprite Skin, mesh deformation, IK, Spine, Mecanim. |
| **Slot** | One body part: `Head`, `Torso`, `ArmL`, `ArmR`, `LegL`, `LegR`, `Weapon`, plus user-defined. Has parent, rest TRS, pivot, sheet+cell binding, sort order. | A 1.0 **socket**. |
| **Socket** | 1.0 attach point on a **frame** clip (or independent socket motion). FX / muzzle / floating icon. Keep as-is. | A body part. Do not stretch sockets into a rig. |
| **Parts clip** | Named clip (`Walk`, `Idle`) of **keyed local TRS per slot**. Same play feel as 1.0: loop, duration, speed, `Play("Walk")`. | A flipbook of cells on one entity. |
| **Frame clip** | 1.0 flipbook: cells over time on **one** sprite entity. | A puppet rig. |
| **Skin / slot skin** | Named map of **slot → sheet+cell** (equipment). Writes bindings only. Motion keys stay. | Rewriting `Walk`. Not a Mecanim avatar mask. |
| **Mode rule** | One character is **frame-clip XOR parts-clip**. Never both on one entity. | Hybrid frame+parts on one entity (non-goal). |
| **GPU parts** | Non-goal. Cutout is **CPU-only**. | PreferGpu crowds. |

One-sentence goal: Brotato-style characters — a cutout rig of named slots, tween clips that move those slots, and sheet swaps on a slot for equipment without changing the motion.

---

## Locked product (from the brief)

- One new SpriteSheetToolWindow tab named **Parts**. No graph, no IK gizmos, no Unity Animation window.
- Default slot names: Head, Torso, ArmL, ArmR, LegL, LegR, Weapon + user-defined.
- Rest pose: pos / rot / scale / pivot. Binding = sheet + cell. Sort order per slot.
- Parts clip tracks: keys of `time + local TRS + ease`. v1 ease = **Linear** and **SmoothStep** only (reuse `SpriteEaseMode`). Missing track = rest pose.
- Loop / duration / speed like the current player. `Play("Walk")`.
- Swap: `SetSlotSheet` or a named skin set. **Unknown/unlisted slot = leave current** (see §10).
- Sockets stay for FX/sword *attach* on **frame** characters. On a parts character, parent FX to a **slot entity** (slice 5 can add sockets-on-slots).
- Slices: (1) data (2) Parts tab (3) DOTS play + tests (4) swap + tiny two-weapon sample (5) later events / hide API / IK — **not now**.
- Non-goals: Unity 2D Animation, IK, Spine, Mecanim, GPU parts, hybrid frame+parts on one entity.

---

## Architecture snapshot (what 1.0 already is)

```
Profile (Window > DOTS Sprite Animator)
  → SpriteSheetToolWindow partials: .Clips .Sockets .Events .Timeline .Preview
SpriteAnimSetAuthoring + SpriteAnimPlayerAuthoring
  → baker → SpriteAnimClipConversion → SpriteAnimSetBlob
SpriteAnimPlayerSystem writes SpriteAnimFrame.Slot
SpriteInstanceRenderSystem draws one quad per entity (CPU)
SpriteGpuEligibility refuses sockets / events / extra TRS / Parent
```

Cutout **reuses** CPU instance rendering and sheet bindings. It does **not** extend `SpriteAnimSetBlob.Frames` or `SpriteAnimClipConversion`.

Proposed folders (when implementing):

```
Runtime/Parts/Authoring/     SpritePartsPlayerAuthoring.cs
Runtime/Parts/Components/    SpritePartsContracts.cs, SpritePartsSet.cs
Runtime/Parts/Systems/       SpritePartsPlayerSystem.cs
Runtime/Utility/             SpritePartsClipConversion.cs, SpritePartsPlayback.cs
Editor/                      SpriteSheetToolWindow.Parts.cs
Tests/Runtime/               SpriteParts*.cs
Samples~/Complete/CutoutPartsExample/   slice 4 only
```

---

## Decisions made here (open brief choices)

| Choice | Decision | Why |
|--------|----------|-----|
| Child entities vs TRS buffer | **One child entity per slot**, `Parent`ed. Root does not draw. | Brief already says “one entity per part, parented.” CPU renderer is per-entity (`SpriteAnimFrame` + `SpriteSheetBinding` + `LocalToWorld`). A TRS buffer would need a new multi-quad renderer (= GPU-parts-shaped). Swap writes binding on the child. Sort is `SpriteSortDepth` on the child. |
| `SpriteAnimPlayer` vs `SpritePartsPlayer` | **New `SpritePartsPlayer`** on the root. No `SpriteAnimPlayer` / `SpriteAnimSetRef` on that entity. | 1.0 clock is **frames**. Parts clock is **seconds**. Player system writes `SpriteAnimFrame` on self; parts writes `LocalTransform` on children. Hybrid on one entity is a non-goal. |
| `Play("Walk")` | `SpriteParts.Play` is the parts API. **`SpriteAnims.Play` dispatches** to it when the entity has `SpritePartsPlayer`, so existing gameplay style keeps working. | Brief: easy `Play("Walk")`. One call site for both modes. |
| Unknown-slot skin | **Leave-current** (patch). Unlisted slots are untouched. Hide is **not** implied. | Brotato: a `RustySword` skin that only mentions `Weapon` must not hide `Head`. Explicit hide is slice 5 (`SetSlotVisible`). |
| Asset layout | Same `ScriptableSpriteSheetProfile`. New lists on `SpriteSheetProfile`. **`AnimKind`** Frame vs Parts. Do **not** reuse `SpriteClipDef.Frames`. | Brief: save clip on the same profile/set. One asset, exclusive bake path. |

---

# 1. Types (authoring + runtime + blobs)

All new types live in `InvertLab.Sprites.DOTS`. Structs follow 1.0 rules: **no field initializers** on runtime structs (C# 9 / Entities). Blobs are `Allocator.Persistent`, released by the existing blob lifetime pattern (`SpriteAnimBlobLifetimeSystem` analog or the same owner-count helper).

## 1.1 Profile authoring (`SpriteSheetProfile`)

Add to `Runtime/Profile/SpriteSheetProfile.cs` (serialized on `ScriptableSpriteSheetProfile.Data`):

```csharp
public enum SpriteAnimKind : byte
{
    Frame = 0, // 1.0 flipbook — default for existing assets
    Parts = 1, // v2 cutout
}

// on SpriteSheetProfile:
public SpriteAnimKind AnimKind;          // bake switch
public List<SpritePartSlotDef> PartsSlots;
public List<SpritePartsClipDef> PartsClips;
public List<SpritePartsSkinDef> PartsSkins;
public string PartsDefaultSkinId;        // optional; empty = use each slot's rest binding
```

`EnsurePartsRig()` (call from tool EnsureProfile / baker):

- null-coalesce the three lists
- canonicalise SlotIds (see 1.2)
- reject duplicate SlotIds
- if `AnimKind == Parts` and `PartsSlots.Count == 0`, baker no-ops (same as missing sheet today)

Existing `Clips` / `Events` / `Hitboxes` / `SocketCatalog` **stay on the asset**. Bake ignores frame clips when `AnimKind == Parts`. Do not delete them (user can switch back).

### `SpritePartSlotDef` (rig, not keyed)

Identity is **SlotId** (gameplay contract). **Name** is the editor label.

```csharp
[Serializable]
public class SpritePartSlotDef
{
    public string Name = "Torso";          // editor label
    public string SlotId = "torso";        // stable; hashed at bake
    public string ParentSlotId;            // empty = parented to character root
    public Vector2 RestPosition;           // local XY, world units (already / PPU)
    public float RestRotation;             // local Z degrees, CCW, same as 1.0 frame rot
    public Vector2 RestScale = Vector2.one;
    public Vector2 Pivot = new(0.5f, 0.5f); // normalized cell UV; (0.5,0.5)=center, (0.5,0)=feet
    public int SheetIndex;                 // into profile.Sheets
    public int CellIndex;                  // row * cols + col (same as 1.0 Slot)
    public int SortOrder;                  // higher = on top (SpriteSortDepth.OrderInLayer)
    public bool Enabled = true;            // authoring-only; disabled slots are not baked
}
```

Preset SlotIds (Add default humanoid):

| Name | SlotId | Default parent |
|------|--------|----------------|
| Torso | `torso` | (root) |
| Head | `head` | `torso` |
| ArmL | `arm.l` | `torso` |
| ArmR | `arm.r` | `torso` |
| LegL | `leg.l` | `torso` |
| LegR | `leg.r` | `torso` |
| Weapon | `weapon` | `arm.r` |

Canonicalisation: trim, lower-case, spaces → `.` (reuse `SpriteSocketIdUtility.Canonical` or a `SpritePartIdUtility` copy so sockets and slots cannot collide in code reviews). **Renaming Name does not change SlotId.** Changing SlotId is a breaking gameplay change (skins, clips, `SetSlotSheet("weapon", …)`).

Limits: soft 16, hard 32 slots. Baker fails above 32.

Parent graph: must be a forest under the character root. Bake fails on cycles or unknown `ParentSlotId`.

### Why world units, not pixels

1.0 sockets / onion offsets are **pixels**, divided by PPU at bake. Slots may bind **different sheets with different PPU**. Pixel locals would be ambiguous across a Head@50ppu parented to Torso@100ppu. Store **world units**. The Parts inspector may show a read-only px convenience using *that slot's* sheet PPU.

### `SpritePartsClipDef`

```csharp
[Serializable]
public class SpritePartsClipDef
{
    public string Name = "Walk";
    public float Duration = 1f;            // seconds; bake uses max(Duration, last key time)
    public byte WrapMode;                  // SpriteAnimWrap.Loop or Once only in v1
    public float Speed = 1f;               // authored default; player may override
    public byte Interrupt;                 // SpriteClipInterrupt.*; same as 1.0
    public float CancelAfter;
    public int Priority;
    public int OnCompleteClipIndex = -1;
    public List<SpritePartsTrackDef> Tracks;
}
```

v1 wrap: **Loop** and **Once** only. PingPong / Reverse* are a bake-time error (or coerced to Loop/Once with a Check warning). Combo windows are frame-indexed — **omit on parts clips** until slice 5.

### `SpritePartsTrackDef` + `SpritePartsKeyDef`

```csharp
[Serializable]
public class SpritePartsTrackDef
{
    public string SlotId;                  // must match a baked slot
    public List<SpritePartsKeyDef> Keys;   // sorted by Time on Ensure/bake
}

[Serializable]
public class SpritePartsKeyDef
{
    public float Time;                     // seconds from clip start, >= 0
    public Vector2 Position;               // local XY, world units
    public float Rotation;                 // local Z degrees
    public Vector2 Scale = Vector2.one;
    public byte EaseMode;                  // SpriteEaseMode.Linear or SmoothStep in the Parts UI
}
```

Sampling rules (lock these):

1. No track for a slot → that slot stays at **rest pose** for the whole clip.
2. One key → hold that pose (no interpolate).
3. Time `<` first key → hold first key (do **not** lerp from rest unless a key exists at t=0). Pose-from-rest is an authoring choice: insert a key at 0.
4. Time `>` last key → hold last key until clip duration / wrap.
5. Between keys i and i+1: `u = Ease(keys[i].EaseMode, (t - t0) / (t1 - t0))`.  
   Position/Scale = lerp. Rotation = **shortest-path** lerp in degrees (not Unity Quaternion slerp of 3D).
6. Duplicate times: last writer wins after a stable sort (`Time`, then list index).
7. `EaseMode` other than Linear/SmoothStep: bake accepts any `SpriteEaseMode` (`SpriteEase.Evaluate`) so the blob need not change later; **Parts tab UI only offers Linear and SmoothStep**.

### `SpritePartsSkinDef`

```csharp
[Serializable]
public class SpritePartsSkinDef
{
    public string Name = "Default";        // "Knight", "RustySword"
    public string SkinId;                  // stable id; default = canonical Name
    public List<SpritePartsSkinBindingDef> Bindings;
}

[Serializable]
public class SpritePartsSkinBindingDef
{
    public string SlotId;
    public int SheetIndex;
    public int CellIndex;
    // Hidden reserved for slice 5; do not author or bake in slices 1–4
}
```

Apply is a **patch**: only listed slots change. Unknown `SlotId` in a skin: **ignore** (do not hide, do not throw at runtime). Check() warns.

## 1.2 Scene authoring

### `SpritePartsPlayerAuthoring`

New MonoBehaviour, menu `DOTS Sprite Animator/Sprite Parts Player`.

```csharp
public class SpritePartsPlayerAuthoring : MonoBehaviour
{
    public bool PlayOnEnable = true;
    public float Speed = 1f;
    public bool Playing = true;
    public int ClipIndex;
    public bool FlipX;                     // whole-rig mirror; see §9
    public bool FlipY;
    public float CrossfadeDuration;        // unused in v1 (hard cuts); keep field for API parity
    // Play / Pause / Resume / SetSpeed / SeekNormalized — same shape as SpriteAnimPlayerAuthoring
    public bool Play(string clipName, bool force = false);
    public bool Play(int clipIndex, bool force = false);
}
```

Mutual exclusion (extend `SpriteAuthoringBundle`):

| On the GO | Allowed |
|-----------|---------|
| `SpriteAnimSetAuthoring` + `SpriteAnimPlayerAuthoring` | Frame character (1.0) |
| `SpriteAnimSetAuthoring` + `SpritePartsPlayerAuthoring` | Parts character |
| Both players | **Forbidden** — OnValidate removes the one that does not match `Profile.Data.AnimKind` |
| `SpriteStaticAuthoring` + either | **Forbidden** (existing rule) |

`SpriteAnimSetAuthoring` stays the profile/tint/size host. When `Profile.AnimKind == Parts`:

- Inspector shows a help box: “Parts rig — flipbook clips on this profile do not bake.”
- `PlaybackPath` is forced to **ForceCpu** in the inspector (PreferGpu is a lie for puppets).
- `ShowSpriteInScene` host quad is **off by default** for parts (the host is not a single cell). Optional later: preview-only composite; bake still `DisableRendering` on the root.
- `BakeUnitySockets` / colliders: **no-op** for parts in slices 1–4 (frame-clip features). Check() says so.

Baker lives on `SpriteAnimSetAuthoring` **or** a dedicated `SpritePartsPlayerAuthoring.Baker` that requires the set. Prefer **one baker on `SpritePartsPlayerAuthoring`** that reads `GetComponent<SpriteAnimSetAuthoring>()` so frame bake and parts bake cannot both run. Frame baker early-outs if `SpritePartsPlayerAuthoring` is present.

## 1.3 Runtime components

Root entity (character):

```csharp
public struct SpritePartsPlayer : IComponentData
{
    public int ClipIndex;
    public float Time;                     // seconds (NOT 1.0's frame phase)
    public float Speed;
    public byte Playing;
    public int QueuedClipIndex;            // -1 empty; Once drain, same as 1.0
    public byte QueuedForce;
    public int ResumeClipIndex;
    public byte OneShotActive;
    public float HitstopRemaining;
    public float HitstopRestoreSpeed;
    public byte HitstopActive;
}

public struct SpritePartsSetRef : IComponentData
{
    public BlobAssetReference<SpritePartsSetBlob> Set;
}

public struct SpritePartsEnabled : IComponentData, IEnableableComponent { }

public struct SpritePartsCompleted : IComponentData { }  // Once finished; Play() removes

[InternalBufferCapacity(8)]
public struct SpritePartLink : IBufferElementData
{
    public Entity Part;
    public ulong SlotIdHash;
    public int SlotIndex;                  // index into rig blob Slots
}
```

Do **not** add `SpriteAnimPlayer`, `SpriteAnimSetRef`, or `SpriteAnimFrame` on the root. Root gets `DisableRendering` (same as today’s preview-strip). Root **may** have `LocalTransform` + `SpriteFlip` (FlipY / documentation) and `SpriteTint` as the default tint pushed to children at bake.

Child entity (one slot):

```csharp
public struct SpritePartSlot : IComponentData
{
    public Entity Root;
    public ulong SlotIdHash;
    public int SlotIndex;
    public int ParentSlotIndex;            // -1 = character root
}

public struct SpritePartRest : IComponentData
{
    public float3 LocalPosition;           // z unused (sort owns z)
    public float RotationZ;                // degrees
    public float2 Scale;
    public float2 Pivot;
}
```

Plus the **existing** render stack on each child:

- `LocalTransform` (pose; z from `SpriteSortDepth`)
- `Parent` → parent slot entity, or character root
- `SpriteAnimFrame` { Slot = cell, Offset = 0, Scale = 1, Rotation = 0 } — **cell only**; pose is transform
- `SpriteSheetBinding`
- `SpriteTint`, `SpriteFlip` (identity; rig flip is root scale, §9)
- `SpriteSortDepth` + `SpriteSortPinPending`
- `SpriteAnimEnabled` so frustum culling can hide the quad without stopping the root clock

Swap writes `SpriteAnimFrame.Slot` and/or `SpriteSheetBinding`. It must **not** write `LocalTransform`.

## 1.4 Blobs

One blob per character type (`SpritePartsSetBlob`), analogous to `SpriteAnimSetBlob`.

```csharp
public struct SpritePartsSetBlob
{
    public BlobArray<SpritePartSlotBlob> Slots;     // bake order = SlotIndex
    public BlobArray<SpritePartsClipBlob> Clips;
    public BlobArray<SpritePartsSkinBlob> Skins;
}

public struct SpritePartSlotBlob
{
    public FixedString64Bytes Name;
    public FixedString64Bytes SlotId;
    public ulong SlotIdHash;               // FNV1a64, same helper as clip names
    public int ParentSlotIndex;            // -1 root
    public float2 RestPosition;
    public float RestRotation;
    public float2 RestScale;
    public float2 Pivot;
    public int SheetIndex;                 // into baker sheet-entity table
    public int CellIndex;
    public int SortOrder;
}

public struct SpritePartsClipBlob
{
    public ulong NameHash;
    public float Duration;
    public byte WrapMode;
    public byte Interrupt;
    public float CancelAfter;
    public int Priority;
    public int OnCompleteClipIndex;
    public BlobArray<SpritePartsTrackBlob> Tracks;
}

public struct SpritePartsTrackBlob
{
    public int SlotIndex;                  // -1 = drop (should not happen if bake validates)
    public ulong SlotIdHash;
    public BlobArray<SpritePartsKeyBlob> Keys;
}

public struct SpritePartsKeyBlob
{
    public float Time;
    public float2 Position;
    public float Rotation;
    public float2 Scale;
    public byte EaseMode;
}

public struct SpritePartsSkinBlob
{
    public ulong SkinIdHash;
    public BlobArray<SpritePartsSkinBindingBlob> Bindings;
}

public struct SpritePartsSkinBindingBlob
{
    public int SlotIndex;
    public ulong SlotIdHash;
    public int SheetIndex;
    public int CellIndex;
}
```

Builder: `SpritePartsSetBuilder.Build(Allocator, RigInput, ClipInput[], SkinInput[])` — same style as `SpriteAnimSetBuilder` (optional arrays, `Fnv` name hashes).

Sheet entities: reuse the 1.0 pattern (`CreateAdditionalEntity` + `SpriteSheetDefinition` + `SpriteSheetAsset`). Build a `sheetIndex → Entity` map from **union of** slot rest sheets and skin bindings (and any `SetSlotSheet` runtime-registered sheets). Children start bound to rest (or `PartsDefaultSkinId` if set).

---

# 2. Parts tab hook into `SpriteSheetToolWindow`

## 2.1 Where it lives

New partial **only**: `Packages/com.invertlab.spriteanimator/Editor/SpriteSheetToolWindow.Parts.cs`.

Do **not** dump Parts UI into the 1MB `SpriteSheetToolWindow.cs`. Follow `DevTools~/ToolWindowSplit.md`: one coherent extraction, compile after.

Existing partials stay owners of frame-clip UI:

`.Clips` `.Sockets` `.Events` `.Timeline` `.Preview` `.Colliders` `.Browser` `.Persistence` `.Styles`

## 2.2 The one tab named Parts

There is no studio-mode tab today (inspector is one scroll; timeline has Frames | Sockets). Add a **window-level** mode next to the title cluster in `DrawToolbar`:

```
[ Clips ] [ Parts ]
```

```csharp
enum StudioTab { Clips = 0, Parts = 1 }

[SerializeField] StudioTab _studioTab;
```

- **Clips** = today’s window (frame clips, sockets, events, colliders, frame timeline).
- **Parts** = puppet authoring. Timeline Frames | Sockets **hidden**.

This is the single Parts tab the brief asked for. Do **not** add a third timeline sub-tab.

Switching tabs does not convert the profile. A banner on Parts when `AnimKind != Parts`:

> This profile is a flipbook. Create Parts Rig to author a cutout (frame clips stay on the asset but will not bake).

Button **Create Parts Rig** → `RecordProfileUndo`, `AnimKind = Parts`, optional **Add default humanoid**.  
Button **Revert to Flipbook** on the Clips tab when `AnimKind == Parts` (confirm dialog).

## 2.3 `OnGUI` hook (exact)

Today (`SpriteSheetToolWindow.cs` ~711–717):

```csharp
DrawClipBrowser(clipsRect);
DrawInspector(inspectorRect);
DrawPreview(previewRect);
DrawTimeline(timelineRect, timelineControlId);
```

Change to:

```csharp
if (_studioTab == StudioTab.Parts)
{
    DrawPartsBrowser(clipsRect);       // .Parts.cs
    DrawPartsInspector(inspectorRect);
    DrawPartsPreview(previewRect);
    DrawPartsTimeline(timelineRect, timelineControlId);
}
else
{
    DrawClipBrowser(clipsRect);
    DrawInspector(inspectorRect);
    DrawPreview(previewRect);
    DrawTimeline(timelineRect, timelineControlId);
}
```

Transport (Play / Pause / Stop / Loop / Speed) stays in `DrawToolbar`. When `_studioTab == Parts`, Play/Loop drive `_partsPreviewTime` / `_partsPlaying` instead of `_previewTime` / `_playing`. Step `|< < > >|` snaps to nearest key (or 1/30s if no keys).

## 2.4 Parts browser (left)

List **`PartsClips`**, not `Clips`. Same row chrome as clip browser (rename, delete, reorder).  
`+ Clip` adds a `SpritePartsClipDef` with Duration=1, Wrap=Loop, empty Tracks.  
Selecting a clip does not select a slot.

Below clips, a **Slots** foldout: list `PartsSlots` in hierarchy indent order (not bake order). Click = select slot for pose.

## 2.5 Parts inspector (right)

Sections, in order:

1. **RIG** — AnimKind badge; Create/Revert; Add slot; Add default humanoid.
2. **SLOT** (when one selected) — Name, SlotId, Parent (popup of other SlotIds + “Character root”), Rest Position/Rotation/Scale, Pivot, Sheet+cell (object field + cell picker reuse `_showSheetCellPicker`), Sort order, Enabled.
3. **POSE / KEY** — **Key at playhead** (writes/replaces a key on the selected slot’s track of the current parts clip at `_partsPreviewTime`). Ease popup Linear | SmoothStep. Delete key.
4. **SKIN** — dropdown of `PartsSkins` + `+ Skin`. Applying a skin in preview **rewrites slot bindings only** (SheetIndex/CellIndex). It does **not** rewrite rest TRS or clip keys. “Save as skin from current bindings”.
5. No Events / Sockets / Colliders blocks in this tab (those remain Clips-tab / frame-clip).

**Add slot from cell:** with the sheet cell picker open, “Add Slot from Cell” creates a slot named from the cell index (`Part 3`), binding that sheet+cell, parent = currently selected slot or root, rest TRS = identity, pivot = sheet default.

## 2.6 Parts preview (center)

Reuse the preview **canvas** (zoom/pan, background). Do **not** draw the 1.0 single-cell quad as the character.

Draw back-to-front by `SortOrder`, then hierarchy for stable ties:

For each enabled slot: compute world TRS by walking parents (rest ⊕ sampled clip), draw that cell with the slot pivot. Selected slot: translate/rotate handles in the canvas (no IK). Dragging updates **rest** when the clip has no key at the playhead, or updates **the key at the playhead** when one exists (status line says which). Option-drag always writes a key.

Onion: optional ghost of previous/next keys on the selected track only (v1 can ship without onion).

## 2.7 Parts timeline (bottom)

One **horizontal track per slot** (not per channel). Keys are diamonds on the track. Playhead is the same visual language as 1.0 (`SpriteAnimPlayback.PlayheadX` analog in `SpritePartsPlayback`).

- Click empty track at playhead → Key at playhead (rest pose if no drag).
- Drag key in time. Alt-drag duplicates.
- Box select keys. Delete.
- No curve editor. No graph.

Clip Duration field on the timeline header. Loop toggle is the toolbar Loop (preview) **and** clip `WrapMode` in the inspector.

## 2.8 Persistence / undo

Reuse `RecordProfileUndo` / `SaveDirty` / auto-save. Parts lists live on `_profile`, so Save Profile already serializes them. Add `EnsurePartsRig()` next to `EnsureSocketCatalog` in `SpriteSheetToolWindow.Persistence.cs` (tiny call; logic in `SpriteSheetProfile`).

Editor undo checklist: `DevTools~/cursor/rules/editor-undo-redo.mdc`.

---

# 3. Bake vs `SpriteAnimClipConversion`

## 3.1 `SpriteAnimClipConversion` does not change

`SpriteAnimClipConversion.CreateInput` stays **frame-clip only**. Do not add parts tracks to `SpriteAnimSetBuilder.ClipInput`. Do not store slot TRS in `SpriteAnimSetBlob.Frames`.

If a future intern “just adds a flag” to `SpriteClipDef`, reject the PR. Parts data is a different blob.

## 3.2 New `SpritePartsClipConversion`

```csharp
public static class SpritePartsClipConversion
{
    public static SpritePartsSetBuilder.RigInput CreateRig(SpriteAnimSetAuthoring authoring);
    public static SpritePartsSetBuilder.ClipInput CreateClip(SpriteAnimSetAuthoring authoring, int i);
    public static SpritePartsSetBuilder.SkinInput CreateSkin(SpriteAnimSetAuthoring authoring, int i);
}
```

Responsibilities:

- Resolve SlotId hashes, parent indices, cycle check.
- Copy rest / keys **as world units** (no extra PPU divide).
- Sort keys by Time.
- Drop tracks whose SlotId is missing (**and** log a bake error so Check() can fail the profile).
- `Duration = max(authored Duration, last key time, 1e-3)`.
- WrapMode Loop/Once only.
- `DependsOn` every referenced texture (same as frame baker).

## 3.3 Baker algorithm (`SpritePartsPlayerAuthoring.Baker`)

1. If `Profile == null` or `AnimKind != Parts` or no slots → return.
2. `DependsOn(Profile)` + each slot/skin texture.
3. `var root = GetEntity(TransformUsageFlags.Dynamic)` (not Renderable-as-mesh). `DisableRendering` on root.
4. Build sheet-index map (`CreateAdditionalEntity` per distinct `SpriteSheetDef`, clone of the 1.0 loop in `SpriteAnimSetAuthoring.Baker`).
5. `SpritePartsSetBuilder.Build` → `SpritePartsSetRef` on root. Add `SpritePartsPlayer` from authoring (clip, speed, playing, flip).
6. Topo-sort slots (parents before children). For each slot: `CreateAdditionalEntity(TransformUsageFlags.Renderable)` (needed for `LocalToWorld`). Add `Parent`, `SpritePartSlot`, `SpritePartRest`, render stack, `SpriteSortDepth.FromLayerOrder(host.SortLayer, slot.SortOrder, host.DepthOffset)`.
7. `SpritePartLink` buffer on root, same order as blob `Slots`.
8. Initial pose: sample clip 0 at t=0 (or rest if missing track) → `LocalTransform`. Cell from rest (or default skin).
9. Do **not** add `SpritePlaybackPreference.PreferGpu`. Write `ForceCpu` if the field exists, or omit GPU preference entirely.
10. Do **not** add `SpriteSocketBuffer` / hitbox blobs / `SpriteAnimEventBuffer` in slices 1–4.

Frame baker (`SpriteAnimSetAuthoring.Baker`): if `GetComponent<SpritePartsPlayerAuthoring>() != null` **return immediately**.

## 3.4 Runtime spawn (no SubScene)

`SpriteEntityFactory` stays flipbook. Slice 4 sample uses authoring + SubScene **or** a `SpritePartsFactory.Create(em, setBlob, sheetEntities)` if tests need it. Tests may build blobs + entities directly (see `SpriteGpuControlTests.Create`).

---

# 4. Playback API snippets

Clock: **seconds**. `Time += dt * Speed` (negative Speed rewinds; 0 freezes). Hitstop/Hold copied from 1.0 (`HitstopActive` / `HitstopRemaining`).

System: `SpritePartsPlayerSystem`

```
[UpdateInGroup(typeof(SimulationSystemGroup))]
[UpdateBefore(typeof(TransformSystemGroup))]
```

Must write `LocalTransform` **before** Unity composes `LocalToWorld`, or the render pack lags one frame. Do **not** `[UpdateAfter(TransformSystemGroup)]`.

`SpriteSortDepthSystem` already pins z before TransformSystemGroup; parts writes xy/rot/scale and **leaves z** so sort still owns depth.

Query: `SpritePartsPlayer` + `SpritePartsSetRef` + `DynamicBuffer<SpritePartLink>`, none `SpriteGpuDriven`, respect `SpritePartsEnabled`.

### 4.1 Sample one slot

```csharp
public static class SpritePartsPlayback
{
    public static void SampleSlot(
        ref SpritePartsSetBlob set, int clipIndex, int slotIndex, float time,
        out float2 position, out float rotation, out float2 scale)
    {
        ref var slot = ref set.Slots[slotIndex];
        position = slot.RestPosition;
        rotation = slot.RestRotation;
        scale = slot.RestScale;
        if (clipIndex < 0 || clipIndex >= set.Clips.Length) return;
        ref var clip = ref set.Clips[clipIndex];
        float t = WrapTime(time, clip.Duration, clip.WrapMode); // Loop: repeat; Once: clamp
        for (int i = 0; i < clip.Tracks.Length; i++)
        {
            ref var track = ref clip.Tracks[i];
            if (track.SlotIndex != slotIndex) continue;
            EvaluateTrack(ref track, t, out position, out rotation, out scale);
            return;
        }
    }
}
```

`EvaluateTrack`: rules in §1.1. Use `SpriteEase.Evaluate((SpriteEaseMode)key.EaseMode, u)`.

Apply:

```csharp
var lt = em.GetComponentData<LocalTransform>(child);
lt.Position.x = position.x;
lt.Position.y = position.y;
// lt.Position.z unchanged (SpriteSortDepth)
lt.Rotation = quaternion.RotateZ(math.radians(rotation));
lt.Scale = /* see §9: uniform-ish; use average or x-scale with y from scale.y */;
em.SetComponentData(child, lt);
```

`LocalTransform.Scale` is a **float** (uniform) in Entities 1.x/6 transforms. **Lock:** store non-uniform scale via `PostTransformMatrix` on the child **only when** `|scale.x - scale.y| > 1e-4`; otherwise `lt.Scale = scale.x`. Rest default is `(1,1)`. This is the same constraint 1.0 GPU already has; CPU instance pack already reads `LocalToWorld` so non-uniform via `PostTransformMatrix` works. GPU eligibility already refuses `PostTransformMatrix` — parts children may have it; they stay CPU.

### 4.2 `SpriteParts` + `SpriteAnims` dispatch

```csharp
public static class SpriteParts
{
    public static bool Play(EntityManager em, Entity root, string clipName, bool force = false)
    {
        if (!em.HasComponent<SpritePartsPlayer>(root) || !em.HasComponent<SpritePartsSetRef>(root))
            return false;
        ref var set = ref em.GetComponentData<SpritePartsSetRef>(root).Set.Value;
        ulong hash = SpriteAnims.Fnv(clipName);
        for (int i = 0; i < set.Clips.Length; i++)
            if (set.Clips[i].NameHash == hash)
                return Play(em, root, i, force);
        return false;
    }

    public static bool Play(EntityManager em, Entity root, int clipIndex, bool force = false) { /* … */ }
    public static void Pause(EntityManager em, Entity root) { /* Playing = 0 */ }
    public static void Resume(EntityManager em, Entity root) { /* Playing = 1 */ }
    public static void SetSpeed(EntityManager em, Entity root, float speed) { /* Hitstop-aware like 1.0 */ }
    public static void SeekNormalized(EntityManager em, Entity root, float t01) { /* Time = t01 * Duration */ }
    public static bool TryGetSlot(EntityManager em, Entity root, string slotId, out Entity part) { /* links */ }
}
```

`Play` interrupt/priority: copy 1.0 (`SpriteClipInterrupt`, `Priority`). No combo window in v1. Once completion: `SpritePartsCompleted`, drain queue / `OnCompleteClipIndex` / one-shot resume — same order as `SpriteAnimPlayerAuthoring.TryDrainCompletion`.

Dispatch in `SpriteAnims.Play(string)` **before** the `SpriteAnimSetRef` requirement:

```csharp
public static bool Play(EntityManager em, Entity e, string clipName, bool force = false,
                        float crossfadeSeconds = 0f)
{
    if (em.HasComponent<SpritePartsPlayer>(e))
        return SpriteParts.Play(em, e, clipName, force); // v1: ignore crossfadeSeconds (hard cut)
    // existing frame path …
}
```

Crossfade of slot TRS is **slice 5+**. `crossfadeSeconds` is ignored on parts (documented, not an error).

Authoring `SpritePartsPlayerAuthoring.Play("Walk")` samples in edit mode into preview children **or** (simpler for v1) only drives the tool-window preview, while Play Mode GO with a SubScene uses ECS. Match 1.0 if cheap: edit-mode tick that writes child `transform` under a `SpriteParts` preview folder. If that fights DontSave preview meshes, **ECS-only Play Mode is acceptable for slice 3**; the **tool window** must still play/loop.

---

# 5. Swap API (Brotato: new sheet on a slot, Walk stays Walk)

Invariant: **swap writes binding, never keys, never rest, never clip index.**

```csharp
public static class SpriteParts
{
    /// <summary>
    /// Bind this slot to a sheet entity already in the world (same contract as
    /// SpriteSheetBinding) and a cell index. Unknown slot → false, no change.
    /// </summary>
    public static bool SetSlotSheet(EntityManager em, Entity root, string slotId,
                                    Entity sheetEntity, int cellIndex)
    {
        if (!TryGetSlot(em, root, slotId, out var part))
            return false;
        if (sheetEntity != Entity.Null)
            em.SetComponentData(part, new SpriteSheetBinding { Sheet = sheetEntity });
        var frame = em.GetComponentData<SpriteAnimFrame>(part);
        frame.Slot = cellIndex;
        frame.Offset = float2.zero;
        frame.Scale = new float2(1f, 1f);
        frame.Rotation = 0f;
        em.SetComponentData(part, frame);
        return true;
    }

    /// <summary>
    /// Patch bindings from a named skin. Unlisted / unknown slots stay as they are.
    /// </summary>
    public static bool ApplySkin(EntityManager em, Entity root, string skinId)
    {
        if (!em.HasComponent<SpritePartsSetRef>(root)) return false;
        ref var set = ref em.GetComponentData<SpritePartsSetRef>(root).Set.Value;
        ulong hash = SpriteAnims.Fnv(skinId);
        int si = -1;
        for (int i = 0; i < set.Skins.Length; i++)
            if (set.Skins[i].SkinIdHash == hash) { si = i; break; }
        if (si < 0) return false;
        ref var skin = ref set.Skins[si];
        var links = em.GetBuffer<SpritePartLink>(root);
        // sheet entity table: store SpritePartSheetMap buffer on root at bake (index = SheetIndex)
        var sheets = em.GetBuffer<SpritePartSheetEntry>(root);
        for (int b = 0; b < skin.Bindings.Length; b++)
        {
            var bind = skin.Bindings[b];
            if (bind.SlotIndex < 0 || bind.SlotIndex >= links.Length) continue;
            var part = links[bind.SlotIndex].Part;
            Entity sheet = bind.SheetIndex >= 0 && bind.SheetIndex < sheets.Length
                ? sheets[bind.SheetIndex].Sheet : Entity.Null;
            SetSlotSheet(em, root, /* via entity */ part, sheet, bind.CellIndex);
        }
        return true;
    }
}

[InternalBufferCapacity(4)]
public struct SpritePartSheetEntry : IBufferElementData
{
    public Entity Sheet; // SpriteSheetDefinition entity
}
```

Gameplay (two weapons, Walk still Walk):

```csharp
// baked skins "sword.wood" and "sword.iron" each list only SlotId "weapon"
SpriteAnims.Play(em, hero, "Walk");
SpriteParts.ApplySkin(em, hero, "sword.iron");
// Head/Torso/arms unchanged; Walk keys still move ArmR; the child now samples the iron cell
```

Or without skins:

```csharp
SpriteParts.SetSlotSheet(em, hero, "weapon", ironSheetEntity, cell: 0);
```

Registering a **brand-new** texture at runtime (not in the profile): caller creates a sheet-definition entity the same way 1.0 multi-sheet bake does (`SpriteSheetDefinition` + `SpriteSheetAsset` + let `SpriteSheetRegistrationSystem` assign a registry id), then `SetSlotSheet`. Slice 4 sample keeps both weapons **in the profile** so no runtime sheet spawn is required.

Unknown slot id: `SetSlotSheet` / `ApplySkin` return **false** / skip that binding. **Leave-current.** Do not disable the child.

---

# 6. `SpriteGpuEligibility` — parts are CPU-only

PreferGpu already refuses sockets, events, extra TRS, **Parent**. Cutout children are parented, so they would fail today — but the **root** might not have `Parent` or `SpriteAnimPlayer`, and `SpritePlaybackApplySystem` only considers `SpriteAnimPlayer`. Close every hole.

### 6.1 Entity overload

At the top of `IsGpuEligible(EntityManager, Entity, …)`, after existence checks:

```csharp
if (em.HasComponent<SpritePartsPlayer>(entity) ||
    em.HasComponent<SpritePartsSetRef>(entity) ||
    em.HasComponent<SpritePartSlot>(entity))
{
    reason = "Cutout parts require CPU playback.";
    return false;
}
```

Keep the existing `Parent` refusal (parts children). Do not special-case parts as eligible if someone strips `Parent`.

### 6.2 Blob / clip overloads

`IsGpuEligible(ref SpriteAnimSetBlob, …)` stays frame-only. Parts entities should not have that blob.

Add:

```csharp
public static bool IsGpuEligible(ref SpritePartsSetBlob set, int clipIndex,
                                out FixedString128Bytes reason)
{
    reason = "Cutout parts require CPU playback.";
    return false;
}
```

### 6.3 Authoring clip overload

If someone passes a dummy `SpriteClipDef` for a parts profile, existing frame checks do not apply. Tool-window GPU badge on Parts tab: always **CPU only**.

### 6.4 `SpritePlaybackApplySystem` / `SpriteGpuAnimSwitch`

- `TryToGpu` on a parts root or part child → false, no structural change (mirror `SpriteGpuControlTests`).
- PreferGpu leftover on a misconfigured GO: `TryFinish` treats `SpritePartsPlayer` as **permanent CPU** (`return true` so `SpritePlaybackApplied` sticks; never retry).
- Tests: see §7.

### 6.5 Culling / crowds

`SpriteCrowdSpawnerAuthoring` stays flipbook. Do not spawn parts rigs through the GPU crowd path. Culling: children already use `SpriteAnimEnabled`; root clock uses `SpritePartsEnabled`. When the camera culls a child, the quad skips pack; the root **keeps ticking** so off-screen Walk stays in phase (same idea as 1.0: disable skips **that entity’s** tick — root is the ticker).

---

# 7. Test plan

Editor assembly: `InvertLab.SpriteAnimator.Tests` (existing). New files under `Tests/Runtime/`. No playmode scenes required if entities are created like `SpriteGpuControlTests`.

| Test | Asserts |
|------|---------|
| `SpritePartsIdTests.CanonicalAndStable` | Rename Name, SlotId unchanged; duplicate SlotIds fail `EnsurePartsRig` |
| `SpritePartsBakeTests.CycleRejected` | Parent A→B→A → conversion throws / baker no-add |
| `SpritePartsBakeTests.FrameBakerSkipsPartsGo` | GO with both would be invalid; parts baker wins; no `SpriteAnimSetRef` |
| `SpritePartsBakeTests.MissingTrackIsRest` | Clip with only `arm.l` track; `torso` pose equals rest at t=0.5 |
| `SpritePartsPlaybackTests.LerpLinear` | Two keys t=0 pos 0 and t=1 pos 10; t=0.5 → 5 |
| `SpritePartsPlaybackTests.SmoothStepNotLinear` | Same keys, SmoothStep; t=0.5 still 5 (SmoothStep is symmetric) — use EaseIn on a **unit test calling `SpriteEase`** plus a parts key with Linear vs a mid-ease that differs (e.g. `EaseIn` in blob even if UI hides it) |
| `SpritePartsPlaybackTests.LoopWraps` | Duration 1, t=1.25 samples like t=0.25 |
| `SpritePartsPlaybackTests.OnceStops` | After duration, `Playing=0`, `SpritePartsCompleted`, pose = last key |
| `SpritePartsPlaybackTests.PlayByName` | `SpriteAnims.Play(em, root, "Walk")` sets `ClipIndex` on `SpritePartsPlayer` |
| `SpritePartsPlaybackTests.WritesLocalTransformNotFramePose` | After tick, child `LocalTransform.xy` changed; `SpriteAnimFrame.Scale` still `(1,1)` |
| `SpritePartsSwapTests.SetSlotSheetKeepsMotion` | Play Walk; `SetSlotSheet(weapon, sheetB, 0)`; next tick still animates `arm.r`; weapon `SpriteAnimFrame.Slot` is 0 on sheetB |
| `SpritePartsSwapTests.SkinIsPatch` | Skin lists only `weapon`; `head` binding unchanged |
| `SpritePartsSwapTests.UnknownSlotLeavesCurrent` | `SetSlotSheet(..., "nope", …)` false; all slots unchanged |
| `SpriteGpuControlTests.CutoutRootRejectsGpu` | Root with `SpritePartsPlayer` → `ToGpu` false, player remains |
| `SpriteGpuControlTests.CutoutChildRejectsGpu` | Child with `SpritePartSlot` + `Parent` → `ToGpu` false |
| `SpriteGpuEligibility.PartsBlobAlwaysFalse` | `IsGpuEligible(ref partsBlob, 0, out reason)` contains `"parts"` or `"Cutout"` |

Slice 3 = bake + playback + GPU tests. Slice 4 adds swap tests + sample.

Do **not** require image-diff / GPU pixel tests for parts (CPU transforms). Optional: one `LocalToWorld` parent-chain test (child world pos = parent + local).

---

# 8. Tiny sample sketch (slice 4)

Path: `Samples~/Complete/CutoutPartsExample/`  
Do **not** add BallForge. Do **not** bump URP.

**Art:** generated (Starter-style), one texture `CutoutParts.png`:

- 4 columns × 2 rows, 32×32 cells, PPU 32  
  Row 0: `torso`, `head`, `arm`, `leg` (arm/leg mirrored via FlipX on left slots — **no**: left/right use the same cell; facing is root FlipX).  
  Row 1: `sword.wood`, `sword.iron`, empty, empty.

**Profile** (`CutoutPartsProfile.asset`, `AnimKind = Parts`):

- Slots: Torso (root), Head → torso, ArmL/ArmR → torso, LegL/LegR → torso, Weapon → arm.r  
- Rest: small offsets so the puppet reads as a person (head above torso, etc.).  
- Clip `Walk` (loop, 0.6s): ArmL/ArmR rotate ±20°, LegL/LegR opposite, Torso slight bob. **No Weapon track.**  
- Skins: `sword.wood` { weapon → row1 col0 }, `sword.iron` { weapon → row1 col1 }.

**Scene:** SubScene with `SpriteAnimSetAuthoring` + `SpritePartsPlayerAuthoring`, Play on enable = Walk.

**Driver** (`CutoutPartsDriver`, Input System like other Complete samples):

```
1 → ApplySkin("sword.wood")
2 → ApplySkin("sword.iron")
Space → Play("Walk")   // already looping; Space can Restart
```

README (package `Samples.md` row, when implementing): “Cutout parts: sheet swap on Weapon, Walk keys unchanged.”

---

# 9. Risks

## 9.1 Draw calls

Each slot is a `SpriteInstance` in `SpriteInstanceRenderSystem`. Batching key = **sheet record** (texture + grid). Same atlas → one instanced draw for all parts of all characters on that atlas. **A unique equipment texture is a unique draw.**

Mitigation (document in sample README, do not “fix” in v1): put weapons on the **same** sheet as the body (row 1 in the tiny sample). Do not promise GPU instancing of mixed atlases.

Crowd of N puppets × 7 slots = 7N instances. Fine for heroes; **not** a PreferGpu soldier swarm.

## 9.2 Pivot

Slot `Pivot` is the cell UV origin of that quad (1.0 already bakes per-cell pivot into `SpriteAnimFrame.Offset` for flipbooks). For parts, **do not** stuff pivot into `SpriteAnimFrame.Offset` if `LocalTransform` is the joint. Two layers:

1. **Joint** = child `LocalTransform` (rest + clip). This is where the parent chain attaches.
2. **Sprite pivot** = where the cell sits on that joint. Bake as `SpriteAnimFrame.Offset` from `(0.5,0.5) → Pivot` using **that slot’s** cell size / PPU (copy the 1.0 baker math in `SpriteAnimClipConversion` lines 49–64).

If joint and sprite pivot are collapsed into one, rotating an arm will orbit the wrong point. Implementers: **joint = transform, cell pivot = frame offset.** Preview must use the same split.

Changing Pivot at rest should not require re-keying Walk, but it **will** change how the cell sits on the joint (expected).

## 9.3 Flip

1.0 `SpriteFlip` is **UV mirror around pivot** on a single quad. Mirroring UV on every part independently leaves the left arm on the left (wrong).

**Lock for parts:**

- `SpritePartsPlayerAuthoring.FlipX` → root `LocalTransform.Scale.x` sign (or `PostTransformMatrix` if uniform scale must stay positive). Whole hierarchy mirrors through Unity.Transforms. Children keep `SpriteFlip.Identity`.
- `SpriteAnims.SetFlip` on a parts root maps to that scale sign, **not** `SpriteFlip.X` on children.
- `FlipY` is supported the same way (`Scale.y`) but rarely used; document.

Do **not** UV-flip children in v1 (double-flip with scale). If art is authored facing right, Scale.x = -1 is Brotato’s facing.

Socket-style `SpriteFlipUtility.LocalPosition` is for frame sockets; parts do not use it for slot joints.

## 9.4 Scale inheritance

Unity `Parent` composes scale. A Torso rest scale of `(1.2,1.2)` scales Head even if Head’s rest is `(1,1)`. **That is intended** (paper-doll).

Risks:

- Non-uniform parent scale shears children. v1 UI should prefer **uniform** rest/key scale (single float in the inspector, written to both axes). Blob still has `float2` for later.
- Negative slot scale as a “local flip” fights root FlipX. **Forbid** negative authored scale in Check() (`scale.x > 0 && scale.y > 0`).
- `LocalTransform.Scale` is uniform — non-uniform uses `PostTransformMatrix` (§4.1). GPU already ineligible.

## 9.5 Other

| Risk | Mitigation |
|------|------------|
| Sort vs parent z | `SpriteSortDepth` pins world z on each child every refresh. Parent translation must not clobber z (system writes xy only). |
| Baker creates many additional entities | Same pattern as socket bake; tests check link count = slot count. |
| Profile with both `Clips` and `PartsClips` | `AnimKind` chooses bake. Check() warns the unused list is ignored. |
| `SpriteAnimCullingSystem` on children | Good. Do not add `SpriteAnimEnabled` on the root (root has no `SpriteAnimFrame`; pack job would skip/fail). |
| Edit-mode Scene quad on the host | Hide for parts; preview lives in the tool window. |
| URP 17.6 template vs package 17.5.0 | Do not bump `package.json`. |

---

# 10. Decisions vs still-open

## Locked in this design (implementers must follow)

1. Child entities + `Parent`, not a TRS buffer on one entity.  
2. `SpritePartsPlayer` + `SpritePartsSetBlob`; no `SpriteAnimPlayer` on that entity.  
3. `SpriteAnims.Play` dispatches to `SpriteParts.Play`.  
4. Unknown / unlisted skin slots **leave-current** (patch). Unknown `SetSlotSheet` id → false, no hide.  
5. Same `ScriptableSpriteSheetProfile`; `AnimKind`; new lists; no reuse of `SpriteClipDef.Frames`.  
6. `SpriteAnimClipConversion` untouched; new `SpritePartsClipConversion`.  
7. Parts tab = window `StudioTab.Parts` + partial `.Parts.cs`; not a timeline Frames\|Sockets sibling.  
8. CPU-only; GPU eligibility hard-fails parts root and children.  
9. Ease UI: Linear + SmoothStep; blob stores `SpriteEaseMode`.  
10. Wrap v1: Loop + Once.  
11. FlipX = root transform scale, not per-slot UV.  
12. Joint = `LocalTransform`; cell pivot = `SpriteAnimFrame.Offset`.  
13. Sockets / events / colliders / IK / hide API / TRS crossfade = **not slices 1–4**.  
14. Weapon is a **slot** (sheet swap). A 1.0 **socket** is still an attach point on frame clips.  
15. Store rest/keys in **world units**.  
16. Max 32 slots. Default humanoid preset table in §1.1.  
17. URP stays 17.5.0. No BallForge. No `Documentation~/` in the design PR.

## Still open — needs JFKENZ (do not block slices 1–3)

These are product/art choices, not type-system holes. Implementers should use the default in **italics**.

1. **Edit-mode Scene puppet** (GO children under the authoring object) vs **tool-window-only** preview? *Default: tool-window-only; SubScene for Play Mode.* Say if you need a Scene-view puppet while animating.
2. **Character collider** on a parts hero (one AABB on the root vs per-slot boxes)? *Default: none in v2.0; use 1.0 frame characters for combat until slice 5.*
3. **Sockets on slots** (muzzle on `weapon` after a swap) — slice 5 scope, or a must-have for 2.0 ship? *Default: parent an entity to the slot child; no socket catalog on parts yet.*
4. **Public clip names vs SkinId** style (`Walk` vs `walk`, `sword.iron` vs `SwordIron`)? *Default: display Name as typed; hash is case-sensitive FNV like 1.0 clip names. Recommend `sword.iron` in the sample.*
5. **Package version** for the feature ship (1.1.0 vs 2.0.0) and whether `AnimKind` belongs in customer `Documentation~/` at ship time. *Default: 1.1.0 minor; docs when implementing, not in this sandbox file.*

If JFKENZ does not answer, implementers take the italics defaults.

---

## Slice checklist (implementation PRs later)

| Slice | Deliver | Out |
|-------|---------|-----|
| 1 Data | Types, `EnsurePartsRig`, builder, conversion, baker, GPU eligibility, tests without UI | Tool window, sample |
| 2 Parts tab | `.Parts.cs`, StudioTab, pose/key/play/loop, save on profile, skin dropdown | Runtime play can still be stubbed if slice 1 landed |
| 3 DOTS play | `SpritePartsPlayerSystem`, `SpriteAnims.Play` dispatch, tests | Swap sample |
| 4 Swap + sample | `SetSlotSheet` / `ApplySkin`, CutoutPartsExample | Events, hide, IK |
| 5 Later | Events, `SetSlotVisible`, sockets-on-slots, IK, crossfade, PingPong | — |

Success: an implementer can execute slices 1–4 from this file without asking what cutout vs socket vs skin means, and without editing `SpriteAnimClipConversion` into a chimera.
