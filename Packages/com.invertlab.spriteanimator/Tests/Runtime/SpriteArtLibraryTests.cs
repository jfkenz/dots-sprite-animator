using System.Collections.Generic;
using NUnit.Framework;
using Unity.Collections;
using UnityEditor;
using UnityEngine;
using InvertLab.Sprites.DOTS;

namespace InvertLab.Sprites.DOTS.Tests
{
    public sealed class SpriteArtLibraryTests
    {
        Texture2D _tex;
        SpriteArtLibrary _library;
        SpriteArtLibrary _other;

        [SetUp]
        public void SetUp()
        {
            _tex = new Texture2D(8, 8, TextureFormat.RGBA32, false);
            _library = ScriptableObject.CreateInstance<SpriteArtLibrary>();
            _library.name = "WeaponsLib";
            _library.Sheets.Add(new SpriteSheetDef
            {
                Name = "Weapons",
                Texture = _tex,
                Columns = 4,
                Rows = 2,
                PixelsPerUnit = 32f,
                Pivot = new Vector2(0.5f, 0.5f),
            });
            _library.Appearances.Add(new SpritePartAppearanceDef
            {
                Name = "Iron Sword",
                AppearanceId = "weapon.iron.sword",
                SheetIndex = 0,
                CellIndex = 3,
                SemanticRole = SpritePartSemanticRole.Weapon,
            });
            _other = ScriptableObject.CreateInstance<SpriteArtLibrary>();
            _other.name = "ArmorLib";
        }

        [TearDown]
        public void TearDown()
        {
            if (_library != null) Object.DestroyImmediate(_library);
            if (_other != null) Object.DestroyImmediate(_other);
            if (_tex != null) Object.DestroyImmediate(_tex);
        }

        static SpriteSheetProfile Hero()
        {
            var dest = new SpriteSheetProfile();
            SpritePartsAuthoringOps.ApplyFloatingPartsTemplate(dest);
            dest.Sheets.Add(new SpriteSheetDef
            {
                Name = "Hero",
                Texture = new Texture2D(8, 8, TextureFormat.RGBA32, false),
                Columns = 2,
                Rows = 2,
                PixelsPerUnit = 32f,
                Pivot = new Vector2(0.5f, 0.5f),
            });
            dest.PartsAppearances.Add(new SpritePartAppearanceDef
            {
                Name = "Body Default",
                AppearanceId = "body.default",
                SheetIndex = 0,
                CellIndex = 0,
            });
            SpritePartsAuthoringOps.FindSlot(dest, "body").DefaultAppearanceId = "body.default";
            SpritePartsValidation.CanonicalizeIds(dest);
            return dest;
        }

        [Test]
        public void Attach_AddsLibraryLink_DetachRemoves()
        {
            var dest = Hero();
            var attach = SpriteArtLibraryOps.Attach(dest, _library, "lib-weapons", "mem:weapons");
            Assert.IsTrue(attach.Ok, attach.Reason);
            Assert.AreEqual(1, dest.ArtLibraries.Count);
            Assert.AreEqual("lib-weapons", dest.ArtLibraries[0].LibraryGuid);
            Assert.AreEqual("WeaponsLib", dest.ArtLibraries[0].LibraryName);

            var again = SpriteArtLibraryOps.Attach(dest, _library, "lib-weapons", "mem:weapons");
            Assert.IsTrue(again.Ok, again.Reason);
            Assert.AreEqual(1, dest.ArtLibraries.Count, "Attach is idempotent on the same GUID.");

            var detach = SpriteArtLibraryOps.Detach(dest, "lib-weapons");
            Assert.IsTrue(detach.Ok, detach.Reason);
            Assert.AreEqual(0, dest.ArtLibraries.Count);
        }

        [Test]
        public void Sync_CopiesLibraryArt_WithLibraryProvenance()
        {
            var dest = Hero();
            SpriteArtLibraryOps.Attach(dest, _library, "lib-weapons", "mem:weapons");
            int sheetsBefore = dest.Sheets.Count;
            string jsonBefore = dest.ToJson();

            var plan = SpriteArtLibraryOps.PlanSync(dest, _library, "lib-weapons");
            Assert.IsTrue(plan.Ok, plan.Reason);
            Assert.AreEqual(jsonBefore, dest.ToJson(), "Plan must stay read-only.");
            Assert.AreEqual(1, plan.SheetsAdded);
            Assert.AreEqual(1, plan.AppearancesAdded);

            var result = SpriteArtLibraryOps.ApplySync(
                plan, dest, _library,
                new SpriteProfileArtImport.ImportSourceInfo { Guid = "lib-weapons", Path = "mem:weapons" });
            Assert.IsTrue(result.Ok, result.Reason);
            Assert.AreEqual(sheetsBefore + 1, dest.Sheets.Count);
            var imported = SpritePartsAuthoringOps.FindAppearance(dest, "weapon.iron.sword");
            Assert.IsNotNull(imported);
            Assert.AreEqual(SpritePartSemanticRole.Weapon, imported.SemanticRole);
            Assert.IsTrue(SpriteArtLibraryOps.HasLibraryProvenance(imported.Import));
            Assert.AreEqual("lib-weapons", imported.Import.LibraryGuid);
            Assert.AreSame(_tex, dest.Sheets[imported.SheetIndex].Texture);
            Assert.IsTrue(SpriteArtLibraryOps.HasLibraryProvenance(dest.Sheets[imported.SheetIndex].Import));
        }

        [Test]
        public void Sync_SecondPass_UpdatesPreviouslyPulledArt()
        {
            var dest = Hero();
            SpriteArtLibraryOps.Attach(dest, _library, "lib-weapons");
            Assert.IsTrue(SpriteArtLibraryOps.Sync(dest, _library, default, "lib-weapons").Ok);

            _library.Appearances[0].CellIndex = 1;
            var plan = SpriteArtLibraryOps.PlanSync(dest, _library, "lib-weapons");
            Assert.IsTrue(plan.Ok, plan.Reason);
            Assert.AreEqual(1, plan.AppearancesUpdated);

            var result = SpriteArtLibraryOps.ApplySync(plan, dest, _library);
            Assert.IsTrue(result.Ok, result.Reason);
            Assert.AreEqual(1, SpritePartsAuthoringOps.FindAppearance(dest, "weapon.iron.sword").CellIndex);
            Assert.AreEqual(1, dest.PartsAppearances.FindAll(a =>
                a != null && SpritePartIdUtility.Canonical(a.AppearanceId) == "weapon.iron.sword").Count,
                "Sync replaces the pulled item; it does not append another copy.");
        }

        [Test]
        public void Attach_Cycle_IsRejected()
        {
            _library.NestedLibraries.Add(new SpriteArtLibraryLink { LibraryGuid = "lib-armor", LibraryName = "ArmorLib" });
            _other.NestedLibraries.Add(new SpriteArtLibraryLink { LibraryGuid = "lib-weapons", LibraryName = "WeaponsLib" });
            SpriteArtLibraryOps.LibraryResolver resolver = id =>
            {
                if (id == "lib-weapons") return _library;
                if (id == "lib-armor") return _other;
                return null;
            };

            var dest = Hero();
            var attach = SpriteArtLibraryOps.Attach(dest, _library, "lib-weapons", "mem:weapons", resolver);
            Assert.IsFalse(attach.Ok, attach.Summary);
            Assert.IsTrue(attach.Reason.ToLowerInvariant().Contains("cycle"), attach.Reason);
            Assert.AreEqual(0, dest.ArtLibraries.Count);
        }

        [Test]
        public void Bake_AfterSync_ResolvesWithoutLibraryAsset()
        {
            var dest = Hero();
            SpriteArtLibraryOps.Attach(dest, _library, "lib-weapons");
            Assert.IsTrue(SpriteArtLibraryOps.Sync(dest, _library, default, "lib-weapons").Ok);
            SpritePartsAuthoringOps.FindSlot(dest, "weapon").DefaultAppearanceId = "weapon.iron.sword";
            SpritePartsValidation.CanonicalizeIds(dest);

            Assert.IsTrue(SpriteArtLibraryOps.HasCompleteLocalArt(dest));
            Object.DestroyImmediate(_library);
            _library = null;

            bool built = SpritePartsClipConversion.TryBuildBlob(dest, Allocator.Temp, out var blob, out string error);
            Assert.IsTrue(built, error);
            try
            {
                Assert.Greater(blob.Value.Appearances.Length, 1);
                Assert.Greater(blob.Value.Slots.Length, 0);
            }
            finally
            {
                if (blob.IsCreated) blob.Dispose();
            }
        }

        [Test]
        public void ImportFromProfile_StillCopiesIndependentlyOfLibraries()
        {
            var source = new SpriteSheetProfile();
            source.Sheets.Add(new SpriteSheetDef
            {
                Name = "Other", Texture = _tex, Columns = 2, Rows = 2,
                PixelsPerUnit = 32f, Pivot = new Vector2(0.5f, 0.5f),
            });
            source.PartsAppearances.Add(new SpritePartAppearanceDef
            {
                Name = "Shield", AppearanceId = "offhand.shield", SheetIndex = 0, CellIndex = 0,
            });
            var dest = Hero();
            SpriteArtLibraryOps.Attach(dest, _library, "lib-weapons");

            var plan = SpriteProfileArtImport.PlanImport(
                source, dest, new List<int> { 0 }, new List<int> { 0 });
            Assert.IsTrue(plan.Ok, plan.Reason);
            var result = SpriteProfileArtImport.Apply(plan, source, dest);
            Assert.IsTrue(result.Ok, result.Reason);
            Assert.IsNotNull(SpritePartsAuthoringOps.FindAppearance(dest, "offhand.shield"));
            Assert.AreEqual(1, dest.ArtLibraries.Count, "Import-from-profile is additive to library refs.");
        }

        [Test]
        public void Sync_IsOneUndoTransaction()
        {
            var dest = Hero();
            var asset = ScriptableObject.CreateInstance<ScriptableSpriteSheetProfile>();
            asset.Data = dest;
            try
            {
                SpriteArtLibraryOps.Attach(dest, _library, "lib-weapons");
                Undo.RegisterCompleteObjectUndo(asset, "Sync Art Library");
                var result = SpriteArtLibraryOps.Sync(dest, _library, default, "lib-weapons");
                Assert.IsTrue(result.Ok, result.Reason);
                Assert.IsNotNull(SpritePartsAuthoringOps.FindAppearance(asset.Data, "weapon.iron.sword"));
                Undo.PerformUndo();
                Assert.IsNull(SpritePartsAuthoringOps.FindAppearance(asset.Data, "weapon.iron.sword"));
            }
            finally
            {
                Undo.ClearAll();
                Object.DestroyImmediate(asset);
            }
        }
    }
}
