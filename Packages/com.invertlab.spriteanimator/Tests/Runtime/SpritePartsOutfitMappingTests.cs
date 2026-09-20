using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using InvertLab.Sprites.DOTS;

namespace InvertLab.Sprites.DOTS.Tests
{
    public sealed class SpritePartsOutfitMappingTests
    {
        static Texture2D Tex() => new Texture2D(8, 8, TextureFormat.RGBA32, false);

        static SpriteSheetProfile ProfileWithRoles()
        {
            var p = new SpriteSheetProfile();
            SpritePartsAuthoringOps.ApplyFloatingPartsTemplate(p);
            p.Sheets.Add(new SpriteSheetDef
            {
                Name = "Hero", Texture = Tex(), Columns = 2, Rows = 2,
                PixelsPerUnit = 32f, Pivot = new Vector2(0.5f, 0.5f),
            });
            p.PartsAppearances.Add(new SpritePartAppearanceDef
            {
                Name = "Body A", AppearanceId = "body.a", SheetIndex = 0, CellIndex = 0,
                SemanticRole = SpritePartSemanticRole.Body,
            });
            p.PartsAppearances.Add(new SpritePartAppearanceDef
            {
                Name = "Head A", AppearanceId = "head.a", SheetIndex = 0, CellIndex = 1,
                SemanticRole = SpritePartSemanticRole.Head,
            });
            p.PartsAppearances.Add(new SpritePartAppearanceDef
            {
                Name = "Sword A", AppearanceId = "weapon.a", SheetIndex = 0, CellIndex = 2,
                SemanticRole = SpritePartSemanticRole.Weapon,
            });
            SpritePartsAuthoringOps.FindSlot(p, "body").SemanticRole = SpritePartSemanticRole.Body;
            SpritePartsAuthoringOps.FindSlot(p, "body").DefaultAppearanceId = "body.a";
            SpritePartsAuthoringOps.FindSlot(p, "weapon").SemanticRole = SpritePartSemanticRole.Weapon;
            SpritePartsAuthoringOps.FindSlot(p, "weapon").DefaultAppearanceId = "weapon.a";
            var head = SpritePartsAuthoringOps.FindSlot(p, "hand.l");
            head.Name = "Head";
            head.SlotId = "head";
            head.SemanticRole = SpritePartSemanticRole.Head;
            head.DefaultAppearanceId = "head.a";
            SpritePartsValidation.CanonicalizeIds(p);
            return p;
        }

        [Test]
        public void ApplyOutfit_MapsByRole()
        {
            var source = ProfileWithRoles();
            source.PartsAppearances.Add(new SpritePartAppearanceDef
            {
                Name = "Body B", AppearanceId = "body.b", SheetIndex = 0, CellIndex = 3,
                SemanticRole = SpritePartSemanticRole.Body,
            });
            source.PartsAppearances.Add(new SpritePartAppearanceDef
            {
                Name = "Sword B", AppearanceId = "weapon.b", SheetIndex = 0, CellIndex = 1,
                SemanticRole = SpritePartSemanticRole.Weapon,
            });
            source.PartsSkins.Add(new SpritePartsSkinDef
            {
                Name = "Alt", SkinId = "alt",
                Bindings = new List<SpritePartsSkinBindingDef>
                {
                    new SpritePartsSkinBindingDef { SlotId = "body", AppearanceId = "body.b" },
                    new SpritePartsSkinBindingDef { SlotId = "weapon", AppearanceId = "weapon.b" },
                },
            });
            SpritePartsValidation.CanonicalizeIds(source);

            var dest = ProfileWithRoles();
            int skinIndex = source.PartsSkins.Count - 1;
            var plan = SpritePartsOutfitMapping.PlanApply(source, skinIndex, dest);
            Assert.IsTrue(plan.Ok, plan.Reason);

            var result = SpritePartsOutfitMapping.Apply(plan, source, dest);
            Assert.IsTrue(result.Ok, result.Reason);
            Assert.AreEqual("body.b", SpritePartsAuthoringOps.FindSlot(dest, "body").DefaultAppearanceId);
            Assert.AreEqual("weapon.b", SpritePartsAuthoringOps.FindSlot(dest, "weapon").DefaultAppearanceId);
        }

        [Test]
        public void ApplyOutfit_ManyToOne_Blocks()
        {
            var dest = ProfileWithRoles();
            dest.PartsAppearances.Add(new SpritePartAppearanceDef
            {
                Name = "Body Extra", AppearanceId = "body.extra", SheetIndex = 0, CellIndex = 3,
                SemanticRole = SpritePartSemanticRole.Body,
            });
            dest.PartsSkins.Add(new SpritePartsSkinDef
            {
                Name = "Conflict", SkinId = "conflict",
                Bindings = new List<SpritePartsSkinBindingDef>
                {
                    new SpritePartsSkinBindingDef { SlotId = "body", AppearanceId = "body.a" },
                    new SpritePartsSkinBindingDef { SlotId = "hand.r", AppearanceId = "body.extra" },
                },
            });
            SpritePartsAuthoringOps.FindSlot(dest, "hand.r").SemanticRole = SpritePartSemanticRole.None;
            dest.PartsAppearances[dest.PartsAppearances.Count - 1].SemanticRole = SpritePartSemanticRole.Body;
            SpritePartsValidation.CanonicalizeIds(dest);

            var plan = SpritePartsOutfitMapping.PlanApply(dest, dest.PartsSkins.Count - 1, dest);
            Assert.IsFalse(plan.Ok, "Two Body-role appearances must block many-to-one.");
            Assert.IsTrue(plan.Reason.ToLowerInvariant().Contains("many-to-one") ||
                          plan.Conflicts.Count > 0, plan.Reason);
            Assert.AreEqual("body.a", SpritePartsAuthoringOps.FindSlot(dest, "body").DefaultAppearanceId);
        }

        [Test]
        public void ApplyOutfit_Unmapped_LeavesDestinationUnchanged()
        {
            var source = ProfileWithRoles();
            source.PartsSkins.Add(new SpritePartsSkinDef
            {
                Name = "NoHead", SkinId = "nohead",
                Bindings = new List<SpritePartsSkinBindingDef>
                {
                    new SpritePartsSkinBindingDef { SlotId = "body", AppearanceId = "body.a" },
                    new SpritePartsSkinBindingDef { SlotId = "weapon", AppearanceId = "weapon.a" },
                },
            });
            source.PartsAppearances.RemoveAll(a =>
                a != null && SpritePartIdUtility.Canonical(a.AppearanceId) == "head.a");
            SpritePartsValidation.CanonicalizeIds(source);

            var dest = ProfileWithRoles();
            string headBefore = SpritePartsAuthoringOps.FindSlot(dest, "head").DefaultAppearanceId;
            dest.PartsAppearances.Add(new SpritePartAppearanceDef
            {
                Name = "Body C", AppearanceId = "body.c", SheetIndex = 0, CellIndex = 3,
                SemanticRole = SpritePartSemanticRole.Body,
            });
            source.PartsAppearances.Add(new SpritePartAppearanceDef
            {
                Name = "Body C", AppearanceId = "body.c", SheetIndex = 0, CellIndex = 3,
                SemanticRole = SpritePartSemanticRole.Body,
            });
            source.PartsSkins[source.PartsSkins.Count - 1].Bindings[0].AppearanceId = "body.c";
            SpritePartsValidation.CanonicalizeIds(source);

            var plan = SpritePartsOutfitMapping.PlanApply(source, source.PartsSkins.Count - 1, dest);
            Assert.IsTrue(plan.Ok, plan.Reason);
            Assert.Greater(plan.Unmapped.Count, 0);

            var result = SpritePartsOutfitMapping.Apply(plan, source, dest);
            Assert.IsTrue(result.Ok, result.Reason);
            // These profiles have different textures. Safe import must retain
            // the destination art and bind a renamed copy of the source art.
            string appliedId = SpritePartsAuthoringOps.FindSlot(dest, "body").DefaultAppearanceId;
            Assert.AreNotEqual("body.c", appliedId);
            var applied = SpritePartsAuthoringOps.FindAppearance(dest, appliedId);
            Assert.IsNotNull(applied);
            Assert.AreSame(source.Sheets[0].Texture, dest.Sheets[applied.SheetIndex].Texture);
            Assert.AreEqual(3, applied.CellIndex);
            var original = SpritePartsAuthoringOps.FindAppearance(dest, "body.c");
            Assert.AreEqual(0, original.SheetIndex);
            Assert.AreEqual(3, original.CellIndex);
            Assert.AreEqual(headBefore, SpritePartsAuthoringOps.FindSlot(dest, "head").DefaultAppearanceId,
                "Unmapped Head slot must stay unchanged.");
        }
    }
}
