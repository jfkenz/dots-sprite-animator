using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using InvertLab.Sprites.DOTS;

namespace InvertLab.Sprites.DOTS.Tests
{
    public sealed class SpriteProfileArtImportTests
    {
        static Texture2D Tex() => new Texture2D(8, 8);

        /// <summary>SwordLibrary-style source: one sheet + one weapon appearance.</summary>
        static SpriteSheetProfile MakeSource(Texture2D tex)
        {
            var source = new SpriteSheetProfile();
            source.Sheets.Add(new SpriteSheetDef
            {
                Name = "Weapons",
                Texture = tex,
                Columns = 4,
                Rows = 2,
                PixelsPerUnit = 32f,
                Pivot = new Vector2(0.5f, 0.5f),
                CroppedCellRects = new[] { new RectInt(0, 0, 8, 8), new RectInt(8, 0, 8, 8) },
            });
            source.PartsAppearances.Add(new SpritePartAppearanceDef
            {
                Name = "Iron Sword",
                AppearanceId = "weapon.iron.sword",
                SheetIndex = 0,
                CellIndex = 3,
            });
            return source;
        }

        /// <summary>Hero-style destination: Floating Parts rig with its own body sheet.</summary>
        static SpriteSheetProfile MakeDestination(Texture2D tex)
        {
            var destination = new SpriteSheetProfile();
            SpritePartsAuthoringOps.ApplyFloatingPartsTemplate(destination);
            destination.Sheets.Add(new SpriteSheetDef
            {
                Name = "Hero",
                Texture = tex,
                Columns = 2,
                Rows = 2,
                PixelsPerUnit = 32f,
                Pivot = new Vector2(0.5f, 0.5f),
            });
            destination.PartsAppearances.Add(new SpritePartAppearanceDef
            {
                Name = "Body Default",
                AppearanceId = "body.default",
                SheetIndex = 0,
                CellIndex = 0,
            });
            SpritePartsValidation.CanonicalizeIds(destination);
            return destination;
        }

        [Test]
        public void Import_CopiesArtAsDeepLocalCopy_SourceUnchanged()
        {
            var sourceTex = Tex();
            var source = MakeSource(sourceTex);
            string sourceJsonBefore = source.ToJson();

            var destination = MakeDestination(Tex());
            var plan = SpriteProfileArtImport.PlanImport(
                source, destination, new List<int> { 0 }, new List<int> { 0 });
            Assert.IsTrue(plan.Ok, plan.Reason);
            Assert.AreEqual(1, plan.SheetsAdded);
            Assert.AreEqual(0, plan.SheetsReused);
            Assert.AreEqual(1, plan.AppearancesAdded);

            var result = SpriteProfileArtImport.Apply(plan, source, destination);
            Assert.IsTrue(result.Ok, result.Reason);

            // Texture asset reference reused, never duplicated.
            Assert.AreEqual(2, destination.Sheets.Count);
            Assert.AreSame(sourceTex, destination.Sheets[1].Texture);
            Assert.AreEqual("Weapons", destination.Sheets[1].Name);
            Assert.AreEqual(32f, destination.Sheets[1].PixelsPerUnit);

            // Definition copy is deep: arrays are cloned, not shared.
            Assert.AreNotSame(source.Sheets[0].CroppedCellRects, destination.Sheets[1].CroppedCellRects);
            Assert.AreEqual(source.Sheets[0].CroppedCellRects[1], destination.Sheets[1].CroppedCellRects[1]);

            // Appearance remapped onto the copied sheet.
            var imported = SpritePartsAuthoringOps.FindAppearance(destination, "weapon.iron.sword");
            Assert.IsNotNull(imported);
            Assert.AreEqual(1, imported.SheetIndex);
            Assert.AreEqual(3, imported.CellIndex);

            // Editing the destination copy never mutates the source.
            destination.Sheets[1].Name = "Renamed";
            destination.Sheets[1].CroppedCellRects[0] = new RectInt(1, 2, 3, 4);
            Assert.AreEqual("Weapons", source.Sheets[0].Name);
            Assert.AreEqual(new RectInt(0, 0, 8, 8), source.Sheets[0].CroppedCellRects[0]);
            Assert.AreEqual(sourceJsonBefore, source.ToJson());
        }

        [Test]
        public void Import_SameTextureDifferentGridPpuPivotOrCrop_DoesNotDedupe()
        {
            var tex = Tex();
            var source = MakeSource(tex);
            source.Sheets[0].CroppedCellRects = null;
            source.Sheets[0].CellLayoutMode = SpriteSheetCellLayoutMode.Grid;

            var variants = new List<SpriteSheetDef>
            {
                new SpriteSheetDef { Name = "V", Texture = tex, Columns = 4, Rows = 2, PixelsPerUnit = 100f, Pivot = new Vector2(0.5f, 0.5f) },
                new SpriteSheetDef { Name = "V", Texture = tex, Columns = 4, Rows = 4, PixelsPerUnit = 32f, Pivot = new Vector2(0.5f, 0.5f) },
                new SpriteSheetDef { Name = "V", Texture = tex, Columns = 4, Rows = 2, PixelsPerUnit = 32f, Pivot = new Vector2(0.3f, 0.7f) },
                new SpriteSheetDef
                {
                    Name = "V", Texture = tex, Columns = 4, Rows = 2, PixelsPerUnit = 32f, Pivot = new Vector2(0.5f, 0.5f),
                    CellLayoutMode = SpriteSheetCellLayoutMode.Cropped,
                    CroppedCellRects = new[] { new RectInt(0, 0, 8, 8), new RectInt(8, 0, 8, 8) },
                },
            };

            for (int v = 0; v < variants.Count; v++)
            {
                var destination = MakeDestination(Tex());
                destination.Sheets[0] = variants[v];
                var plan = SpriteProfileArtImport.PlanImport(
                    source, destination, new List<int> { 0 }, null);
                Assert.IsTrue(plan.Ok, plan.Reason);
                Assert.AreEqual(1, plan.SheetsAdded,
                    $"Variant {v} must add a separate sheet copy, not deduplicate.");
                Assert.AreEqual(0, plan.SheetsReused);
            }
        }

        [Test]
        public void Import_ExactDefinitionMatch_ReusesDestinationIdentity()
        {
            var tex = Tex();
            var source = MakeSource(tex);
            var destination = MakeDestination(tex);
            // Destination sheet matches the source definition exactly (name differs —
            // names are not identity), and destination already owns the same
            // appearance id bound to the same cell.
            destination.Sheets[0] = new SpriteSheetDef
            {
                Name = "Existing",
                Texture = tex,
                Columns = 4,
                Rows = 2,
                PixelsPerUnit = 32f,
                Pivot = new Vector2(0.5f, 0.5f),
            };
            destination.PartsAppearances.Add(new SpritePartAppearanceDef
            {
                Name = "Existing Sword",
                AppearanceId = "weapon.iron.sword",
                SheetIndex = 0,
                CellIndex = 3,
            });
            int sheetInstance = destination.Sheets.Count;
            int appearanceInstance = destination.PartsAppearances.Count;

            var plan = SpriteProfileArtImport.PlanImport(
                source, destination, new List<int> { 0 }, new List<int> { 0 });
            Assert.IsTrue(plan.Ok, plan.Reason);
            Assert.AreEqual(1, plan.SheetsReused);
            Assert.AreEqual(0, plan.SheetsAdded);
            Assert.AreEqual(1, plan.AppearancesReused);
            Assert.AreEqual(0, plan.AppearancesAdded);

            var result = SpriteProfileArtImport.Apply(plan, source, destination);
            Assert.IsTrue(result.Ok, result.Reason);
            Assert.AreEqual(sheetInstance, destination.Sheets.Count);
            Assert.AreEqual(appearanceInstance, destination.PartsAppearances.Count);
            var remap = result.AppearanceIdRemaps.Find(kv => kv.Key == "weapon.iron.sword");
            Assert.AreEqual("weapon.iron.sword", remap.Value);
        }

        [Test]
        public void Import_IdCollisionWithDifferentDefinition_CreatesUniqueId()
        {
            var source = MakeSource(Tex());
            var destination = MakeDestination(Tex());
            destination.PartsAppearances.Add(new SpritePartAppearanceDef
            {
                Name = "Other Sword",
                AppearanceId = "weapon.iron.sword",
                SheetIndex = 0,
                CellIndex = 1, // different cell: different definition, no replace
            });

            var plan = SpriteProfileArtImport.PlanImport(
                source, destination, new List<int> { 0 }, new List<int> { 0 });
            Assert.IsTrue(plan.Ok, plan.Reason);
            Assert.AreEqual(1, plan.AppearancesAdded);

            var result = SpriteProfileArtImport.Apply(plan, source, destination);
            Assert.IsTrue(result.Ok, result.Reason);
            var imported = SpritePartsAuthoringOps.FindAppearance(destination, "weapon.iron.sword2");
            Assert.IsNotNull(imported, "Collision must produce a unique id, not overwrite.");
            Assert.AreEqual(3, imported.CellIndex);
            var existing = SpritePartsAuthoringOps.FindAppearance(destination, "weapon.iron.sword");
            Assert.AreEqual(1, existing.CellIndex, "Existing content must remain untouched.");
        }

        [Test]
        public void Import_AppearancePullsItsSheetAlong()
        {
            var source = MakeSource(Tex());
            var destination = MakeDestination(Tex());

            // Only the appearance is selected; its sheet is a dependency.
            var plan = SpriteProfileArtImport.PlanImport(
                source, destination, new List<int>(), new List<int> { 0 });
            Assert.IsTrue(plan.Ok, plan.Reason);
            Assert.AreEqual(1, plan.Sheets.Count);
            Assert.IsTrue(plan.Sheets[0].RequiredByAppearance);

            var result = SpriteProfileArtImport.Apply(plan, source, destination);
            Assert.IsTrue(result.Ok, result.Reason);
            Assert.AreEqual(2, destination.Sheets.Count);
            var imported = SpritePartsAuthoringOps.FindAppearance(destination, "weapon.iron.sword");
            Assert.AreEqual(1, imported.SheetIndex);
        }

        [Test]
        public void Import_InvalidRequest_FailsAndChangesNothing()
        {
            var source = MakeSource(Tex());
            var destination = MakeDestination(Tex());
            // The editor window always holds a normalized profile; snapshot after
            // normalization so only real import mutations can change the JSON.
            destination.EnsureSheets();
            string jsonBefore = destination.ToJson();

            var plan = SpriteProfileArtImport.PlanImport(
                source, destination, new List<int>(), new List<int> { 7 });
            Assert.IsFalse(plan.Ok);
            Assert.IsNotEmpty(plan.Reason);

            var result = SpriteProfileArtImport.Apply(plan, source, destination);
            Assert.IsFalse(result.Ok);
            Assert.AreEqual(jsonBefore, destination.ToJson());

            // Null / same-profile requests are rejected outright.
            Assert.IsFalse(SpriteProfileArtImport.PlanImport(null, destination, null, null).Ok);
            Assert.IsFalse(SpriteProfileArtImport.PlanImport(destination, destination, null, null).Ok);
            Assert.AreEqual(jsonBefore, destination.ToJson());
        }

        [Test]
        public void PlanImport_DoesNotMutateDestination()
        {
            var source = MakeSource(Tex());
            var destination = new SpriteSheetProfile();
            destination.Clips.Add(new SpriteClipDef { Name = "Idle", Frames = new[] { 0 } });
            string jsonBefore = destination.ToJson();

            var plan = SpriteProfileArtImport.PlanImport(
                source, destination, new List<int> { 0 }, new List<int> { 0 });
            Assert.IsTrue(plan.Ok, plan.Reason);
            Assert.AreEqual(jsonBefore, destination.ToJson(),
                "Planning (and the import picker GUI) must not write the open profile.");
        }

        [Test]
        public void Import_TexturelessSourceSheet_IsRejectedWithReason()
        {
            var source = MakeSource(Tex());
            source.Sheets[0].Texture = null;
            var destination = MakeDestination(Tex());

            var plan = SpriteProfileArtImport.PlanImport(
                source, destination, new List<int> { 0 }, null);
            Assert.IsFalse(plan.Ok);
            StringAssert.Contains("no texture", plan.Reason);
        }

        [Test]
        public void Import_RemappedBinding_SurvivesSheetRename()
        {
            var source = MakeSource(Tex());
            var destination = MakeDestination(Tex());
            var result = SpriteProfileArtImport.Apply(
                SpriteProfileArtImport.PlanImport(source, destination, new List<int> { 0 }, new List<int> { 0 }),
                source, destination);
            Assert.IsTrue(result.Ok, result.Reason);

            // Rename (and conceptually reorder — indices are managed by the sheet
            // list ops) must not silently rebind the imported appearance.
            var imported = SpritePartsAuthoringOps.FindAppearance(destination, "weapon.iron.sword");
            int sheetIndex = imported.SheetIndex;
            destination.Sheets[sheetIndex].Name = "Renamed Weapons";
            Assert.AreEqual(sheetIndex, imported.SheetIndex);
            Assert.AreSame(destination.Sheets[sheetIndex],
                destination.Sheets[imported.SheetIndex]);
            Assert.IsTrue(SpritePartsValidation.Validate(destination).Ok,
                "Imported art must keep the profile valid.");
        }

        [Test]
        public void Import_IsOneUndoTransaction()
        {
            var source = MakeSource(Tex());
            var destination = MakeDestination(Tex());
            var asset = ScriptableObject.CreateInstance<ScriptableSpriteSheetProfile>();
            asset.Data = destination;
            try
            {
                Undo.RegisterCompleteObjectUndo(asset, "Import Art from SwordLibrary");
                var result = SpriteProfileArtImport.Apply(
                    SpriteProfileArtImport.PlanImport(source, destination, new List<int> { 0 }, new List<int> { 0 }),
                    source, destination);
                Assert.IsTrue(result.Ok, result.Reason);
                Assert.AreEqual(2, asset.Data.Sheets.Count);
                Assert.AreEqual(2, asset.Data.PartsAppearances.Count);

                // The editor records the SO snapshot; one undo removes the whole import.
                Undo.PerformUndo();
                Assert.AreEqual(1, asset.Data.Sheets.Count);
                Assert.AreEqual(1, asset.Data.PartsAppearances.Count);
                Assert.IsNull(SpritePartsAuthoringOps.FindAppearance(asset.Data, "weapon.iron.sword"));
            }
            finally
            {
                Undo.ClearAll();
                UnityEngine.Object.DestroyImmediate(asset);
            }
        }

        [Test]
        public void Import_ReplacePolicy_KeepsDestinationIdAndName_ReplacesDefinition()
        {
            var source = MakeSource(Tex());
            string sourceJson = source.ToJson();
            var destination = MakeDestination(Tex());
            destination.PartsAppearances.Add(new SpritePartAppearanceDef
            {
                Name = "Old Sword", AppearanceId = "weapon.iron.sword", SheetIndex = 0, CellIndex = 1,
            });
            SpritePartsAuthoringOps.FindSlot(destination, "weapon").DefaultAppearanceId =
                "weapon.iron.sword";
            string destJson = destination.ToJson();

            var plan = SpriteProfileArtImport.PlanImport(
                source, destination, new List<int> { 0 }, new List<int> { 0 },
                SpriteProfileImportConflictPolicy.ReplaceExisting);
            Assert.IsTrue(plan.Ok, plan.Reason);
            Assert.AreEqual(0, plan.AppearancesAdded);
            Assert.AreEqual(1, plan.AppearancesReplaced);
            Assert.AreEqual(destJson, destination.ToJson(), "Plan must stay read-only.");
            var action = plan.Appearances[0];
            Assert.AreEqual(SpriteProfileArtImport.AppearanceResolution.ReplaceExisting, action.Resolution);

            var result = SpriteProfileArtImport.Apply(plan, source, destination);
            Assert.IsTrue(result.Ok, result.Reason);
            Assert.AreEqual(2, destination.PartsAppearances.Count, "Replace keeps the slot; nothing is appended.");
            var replaced = SpritePartsAuthoringOps.FindAppearance(destination, "weapon.iron.sword");
            Assert.AreEqual("Old Sword", replaced.Name, "Destination display name is stable identity.");
            Assert.AreEqual(3, replaced.CellIndex, "Content (cell) comes from the source.");
            Assert.AreEqual(1, replaced.SheetIndex, "Definition remaps onto the imported sheet copy.");
            Assert.AreEqual(2, destination.Sheets.Count);
            Assert.AreEqual(
                "weapon.iron.sword",
                SpritePartsAuthoringOps.FindSlot(destination, "weapon").DefaultAppearanceId,
                "Slot references keep resolving to the same id.");
            Assert.IsTrue(SpritePartsValidation.Validate(destination).Ok);
            Assert.AreEqual(sourceJson, source.ToJson(), "Source must stay unchanged.");
        }

        [Test]
        public void Import_ReplacePolicy_PreviewsAffectedUses()
        {
            var source = MakeSource(Tex());
            var destination = MakeDestination(Tex());
            destination.PartsAppearances.Add(new SpritePartAppearanceDef
            {
                Name = "Old Sword", AppearanceId = "weapon.iron.sword", SheetIndex = 0, CellIndex = 1,
            });
            SpritePartsAuthoringOps.FindSlot(destination, "weapon").DefaultAppearanceId =
                "weapon.iron.sword";
            destination.PartsSkins[0].Bindings.Add(new SpritePartsSkinBindingDef
            {
                SlotId = "weapon", AppearanceId = "weapon.iron.sword",
            });
            int walk = SpritePartsAuthoringOps.FindClipIndex(destination, "walk");
            Assert.IsTrue(SpritePartsAuthoringOps.WriteKeySprite(
                destination, walk, "weapon", 0.2f, "weapon.iron.sword",
                new SpritePartsAuthoringOps.PoseEdit
                {
                    Position = Vector2.zero, Rotation = 0f, Scale = Vector2.one,
                }).WroteAppearance);

            var plan = SpriteProfileArtImport.PlanImport(
                source, destination, new List<int> { 0 }, new List<int> { 0 },
                SpriteProfileImportConflictPolicy.ReplaceExisting);
            Assert.IsTrue(plan.Ok, plan.Reason);
            string usedBy = string.Join(";", plan.Appearances[0].UsedBy.ToArray());
            StringAssert.Contains("slot 'Weapon' default art", usedBy);
            StringAssert.Contains("skin 'Default' binding 'weapon'", usedBy);
            StringAssert.Contains("keyed sprite", usedBy);
        }

        [Test]
        public void Import_ReplacePolicy_DefaultPolicyStillUniqueCopy()
        {
            var source = MakeSource(Tex());
            var destination = MakeDestination(Tex());
            destination.PartsAppearances.Add(new SpritePartAppearanceDef
            {
                Name = "Old Sword", AppearanceId = "weapon.iron.sword", SheetIndex = 0, CellIndex = 1,
            });

            var plan = SpriteProfileArtImport.PlanImport(
                source, destination, new List<int> { 0 }, new List<int> { 0 },
                SpriteProfileImportConflictPolicy.UniqueCopy);
            Assert.IsTrue(plan.Ok, plan.Reason);
            Assert.AreEqual(1, plan.AppearancesAdded);
            Assert.AreEqual(0, plan.AppearancesReplaced);
            Assert.AreEqual(SpriteProfileArtImport.AppearanceResolution.AddCopy, plan.Appearances[0].Resolution);
        }

        [Test]
        public void Import_ReplacePolicy_ExactDefinitionMatchStillReuses()
        {
            var tex = Tex();
            var source = MakeSource(tex);
            var destination = MakeDestination(tex);
            destination.Sheets[0] = new SpriteSheetDef
            {
                Name = "Existing",
                Texture = tex,
                Columns = 4,
                Rows = 2,
                PixelsPerUnit = 32f,
                Pivot = new Vector2(0.5f, 0.5f),
            };
            destination.PartsAppearances.Add(new SpritePartAppearanceDef
            {
                Name = "Existing Sword", AppearanceId = "weapon.iron.sword", SheetIndex = 0, CellIndex = 3,
            });

            var plan = SpriteProfileArtImport.PlanImport(
                source, destination, new List<int> { 0 }, new List<int> { 0 },
                SpriteProfileImportConflictPolicy.ReplaceExisting);
            Assert.IsTrue(plan.Ok, plan.Reason);
            Assert.AreEqual(1, plan.AppearancesReused);
            Assert.AreEqual(0, plan.AppearancesReplaced,
                "Identical content is reused; there is nothing to replace.");
        }

        [Test]
        public void Import_Provenance_StampedOnCopies_DisplayOnly()
        {
            var source = MakeSource(Tex());
            var destination = MakeDestination(Tex());
            string destJsonBefore = destination.ToJson();

            var plan = SpriteProfileArtImport.PlanImport(
                source, destination, new List<int> { 0 }, new List<int> { 0 });
            Assert.IsTrue(plan.Ok, plan.Reason);
            Assert.AreEqual(destJsonBefore, destination.ToJson(), "Plan must stay read-only.");

            var sourceInfo = new SpriteProfileArtImport.ImportSourceInfo
            {
                Guid = "deadbeefguid0000",
                Path = "Assets/SwordLibrary.profile.asset",
            };
            var result = SpriteProfileArtImport.Apply(plan, source, destination,
                sourceInfo, importedUtc: "2026-09-17T00:00:00Z");
            Assert.IsTrue(result.Ok, result.Reason);

            var sheet = destination.Sheets[1];
            Assert.IsNotNull(sheet.Import);
            Assert.AreEqual("deadbeefguid0000", sheet.Import.SourceGuid);
            Assert.AreEqual("Assets/SwordLibrary.profile.asset", sheet.Import.SourcePath);
            StringAssert.Contains("Weapons", sheet.Import.SourceItem);
            Assert.AreEqual("2026-09-17T00:00:00Z", sheet.Import.ImportedUtc);

            var app = SpritePartsAuthoringOps.FindAppearance(destination, "weapon.iron.sword");
            Assert.IsNotNull(app.Import);
            StringAssert.Contains("weapon.iron.sword", app.Import.SourceItem);
            Assert.AreEqual("deadbeefguid0000", app.Import.SourceGuid);

            // Display-only: a GUID that points nowhere breaks nothing, and the
            // provenance survives a JSON round-trip as plain strings.
            Assert.IsTrue(SpritePartsValidation.Validate(destination).Ok);
            var reloaded = JsonUtility.FromJson<SpriteSheetProfile>(destination.ToJson());
            var reloadedApp = SpritePartsAuthoringOps.FindAppearance(reloaded, "weapon.iron.sword");
            Assert.IsNotNull(reloadedApp);
            Assert.IsNotNull(reloadedApp.Import);
            Assert.AreEqual("deadbeefguid0000", reloadedApp.Import.SourceGuid);
        }

        [Test]
        public void Import_Provenance_UpdatedOnReplacement()
        {
            var source = MakeSource(Tex());
            var destination = MakeDestination(Tex());
            destination.PartsAppearances.Add(new SpritePartAppearanceDef
            {
                Name = "Old Sword", AppearanceId = "weapon.iron.sword", SheetIndex = 0, CellIndex = 1,
            });

            var plan = SpriteProfileArtImport.PlanImport(
                source, destination, new List<int> { 0 }, new List<int> { 0 },
                SpriteProfileImportConflictPolicy.ReplaceExisting);
            Assert.IsTrue(plan.Ok, plan.Reason);
            var result = SpriteProfileArtImport.Apply(plan, source, destination,
                new SpriteProfileArtImport.ImportSourceInfo { Guid = "replace.guid" });
            Assert.IsTrue(result.Ok, result.Reason);

            var replaced = SpritePartsAuthoringOps.FindAppearance(destination, "weapon.iron.sword");
            Assert.IsNotNull(replaced.Import, "Replacement updates provenance on the written item.");
            Assert.AreEqual("replace.guid", replaced.Import.SourceGuid);
            StringAssert.Contains("replaced", replaced.Import.SourceItem);
            Assert.IsNotEmpty(replaced.Import.ImportedUtc, "Time defaults when not supplied.");
        }
    }
}
