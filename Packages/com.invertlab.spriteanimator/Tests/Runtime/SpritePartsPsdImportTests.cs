using System.Collections.Generic;
using System.IO;
using System.Text;
using NUnit.Framework;
using Unity.Mathematics;
using UnityEngine;

namespace InvertLab.Sprites.DOTS.Tests
{
    /// <summary>PSD reading and layers-to-parts import.</summary>
    public sealed class SpritePartsPsdImportTests
    {
        // ------------------------------------------------------------------ a tiny PSD writer

        sealed class L
        {
            public string Name, Unicode;
            public int Top, Left, Bottom, Right;
            public Color32[] TopDown; // rows from the top, like Photoshop
            public bool Hidden, Clipped, Rle;
            public int Divider; // 0 layer, 1 open folder, 3 group end marker
        }

        static void U16(BinaryWriter w, int v) { w.Write((byte)(v >> 8)); w.Write((byte)v); }
        static void U32(BinaryWriter w, int v) { w.Write((byte)(v >> 24)); w.Write((byte)(v >> 16)); w.Write((byte)(v >> 8)); w.Write((byte)v); }
        static void Ascii(BinaryWriter w, string s) => w.Write(Encoding.ASCII.GetBytes(s));

        static byte[] Channel(L l, int c)
        {
            int w = l.Right - l.Left, h = l.Bottom - l.Top;
            var ms = new MemoryStream();
            var bw = new BinaryWriter(ms);
            if (l.Divider != 0 || w == 0)
            {
                U16(bw, 0);
                return ms.ToArray();
            }
            byte Get(int x, int y)
            {
                var p = l.TopDown[y * w + x];
                return c == 0 ? p.r : c == 1 ? p.g : c == 2 ? p.b : p.a;
            }
            if (!l.Rle)
            {
                U16(bw, 0);
                for (int y = 0; y < h; y++)
                    for (int x = 0; x < w; x++)
                        bw.Write(Get(x, y));
                return ms.ToArray();
            }
            U16(bw, 1);
            for (int y = 0; y < h; y++)
                U16(bw, w + 1); // one literal run per row
            for (int y = 0; y < h; y++)
            {
                bw.Write((byte)(w - 1));
                for (int x = 0; x < w; x++)
                    bw.Write(Get(x, y));
            }
            return ms.ToArray();
        }

        static byte[] Psd(int width, int height, params L[] layers)
        {
            var info = new MemoryStream();
            var iw = new BinaryWriter(info);
            U16(iw, layers.Length);
            var data = new List<byte[]>();
            foreach (var l in layers)
            {
                U32(iw, l.Top); U32(iw, l.Left); U32(iw, l.Bottom); U32(iw, l.Right);
                U16(iw, 4);
                int[] ids = { -1, 0, 1, 2 };
                foreach (int id in ids)
                {
                    var bytes = Channel(l, id < 0 ? 3 : id);
                    data.Add(bytes);
                    U16(iw, id & 0xFFFF);
                    U32(iw, bytes.Length);
                }
                Ascii(iw, "8BIM"); Ascii(iw, "norm");
                iw.Write((byte)255);
                iw.Write((byte)(l.Clipped ? 1 : 0));
                iw.Write((byte)(l.Hidden ? 2 : 0));
                iw.Write((byte)0);
                var extra = new MemoryStream();
                var ew = new BinaryWriter(extra);
                U32(ew, 0); U32(ew, 0);
                var name = Encoding.ASCII.GetBytes(l.Name);
                ew.Write((byte)name.Length);
                ew.Write(name);
                for (int p = (name.Length + 1) % 4; p != 0 && p < 4; p++)
                    ew.Write((byte)0);
                if (l.Unicode != null)
                {
                    Ascii(ew, "8BIM"); Ascii(ew, "luni");
                    int len = 4 + l.Unicode.Length * 2;
                    U32(ew, len);
                    U32(ew, l.Unicode.Length);
                    foreach (char ch in l.Unicode)
                        U16(ew, ch);
                }
                if (l.Divider != 0)
                {
                    Ascii(ew, "8BIM"); Ascii(ew, "lsct");
                    U32(ew, 4);
                    U32(ew, l.Divider);
                }
                U32(iw, (int)extra.Length);
                iw.Write(extra.ToArray());
            }
            foreach (var d in data)
                iw.Write(d);
            if (info.Length % 2 != 0)
                iw.Write((byte)0);

            var file = new MemoryStream();
            var fw = new BinaryWriter(file);
            Ascii(fw, "8BPS"); U16(fw, 1); fw.Write(new byte[6]);
            U16(fw, 4); U32(fw, height); U32(fw, width); U16(fw, 8); U16(fw, 3);
            U32(fw, 0); U32(fw, 0);
            U32(fw, (int)info.Length + 4 + 4); // layer and mask section
            U32(fw, (int)info.Length);
            fw.Write(info.ToArray());
            U32(fw, 0); // global mask
            return file.ToArray();
        }

        static Color32[] Fill(int n, Color32 c)
        {
            var a = new Color32[n];
            for (int i = 0; i < n; i++)
                a[i] = c;
            return a;
        }

        static readonly Color32 Red = new Color32(255, 0, 0, 255), Green = new Color32(0, 255, 0, 255),
            Yellow = new Color32(255, 255, 0, 255), Blue = new Color32(0, 0, 255, 255), White = new Color32(255, 255, 255, 255);

        // Bottom to top: Back, [group start], Hand (RLE), Glove (clipped to Hand), Arm (folder), Hidden.
        static SpritePsdReader.Document Sample()
        {
            var hand = new Color32[6];
            for (int i = 0; i < 3; i++) { hand[i] = Green; hand[3 + i] = Yellow; } // top row green, bottom row yellow
            var bytes = Psd(8, 6,
                new L { Name = "Back", Top = 0, Left = 0, Bottom = 6, Right = 8, TopDown = Fill(48, Red) },
                new L { Name = "</Layer group>", Divider = 3 },
                new L { Name = "Hand", Unicode = "Hånd", Top = 1, Left = 2, Bottom = 3, Right = 5, TopDown = hand, Rle = true },
                new L { Name = "Glove", Top = 1, Left = 2, Bottom = 3, Right = 5, TopDown = Fill(6, Blue), Clipped = true },
                new L { Name = "Arm", Divider = 1 },
                new L { Name = "Hidden", Top = 0, Left = 0, Bottom = 2, Right = 2, TopDown = Fill(4, White), Hidden = true });
            Assert.IsTrue(SpritePsdReader.TryRead(bytes, out var doc, out string error), error);
            return doc;
        }

        // ------------------------------------------------------------------ tests

        [Test]
        public void Reader_Reads_Layers_Groups_Names_And_Pixels()
        {
            var doc = Sample();
            Assert.AreEqual(8, doc.Width);
            Assert.AreEqual(6, doc.Height);
            Assert.AreEqual(5, doc.Layers.Count, "Back, Arm (group), Hand, Glove, Hidden.");
            var arm = doc.Layers[1];
            Assert.IsTrue(arm.IsGroup);
            Assert.AreEqual("Arm", arm.Name, "The group takes the folder record's name.");
            var hand = doc.Layers[2];
            Assert.AreEqual("Hånd", hand.Name, "The Unicode name wins.");
            Assert.AreEqual(1, hand.Parent);
            Assert.AreEqual(new RectInt(2, 1, 3, 2), hand.Rect);
            Assert.AreEqual(Yellow, hand.Pixels[0], "Rows are flipped: the bottom row comes first.");
            Assert.AreEqual(Green, hand.Pixels[3]);
            Assert.IsTrue(doc.Layers[3].Clipped);
            Assert.IsFalse(doc.Layers[4].Visible);
            Assert.AreEqual(-1, doc.Layers[4].Parent);
        }

        [Test]
        public void Bones_Mode_Places_Parts_And_Keeps_Clipping()
        {
            var plan = SpritePartsPsdImport.BuildPlan(Sample(), new SpritePartsPsdImport.Options { PixelsPerUnit = 1f });
            Assert.IsNull(plan.Error);
            Assert.AreEqual(5, plan.Items.Count);
            var bone = plan.Items[0];
            Assert.IsTrue(bone.IsBone);
            var back = plan.Items.Find(i => i.Name == "Back");
            var hand = plan.Items.Find(i => i.Name == "Hånd");
            var glove = plan.Items.Find(i => i.Name == "Glove");
            Assert.AreEqual(new float2(0f, 3f), back.Root, "Centre of the canvas, above a bottom-centre root.");
            Assert.AreEqual(new float2(-0.5f, 4f), hand.Root);
            Assert.AreEqual(hand.Root, bone.Root, "A bone sits at the centre of what it holds.");
            Assert.AreEqual(0, hand.Parent);
            Assert.AreEqual(plan.Items.IndexOf(hand), glove.ClipBase);
            Assert.IsFalse(plan.Items.Find(i => i.Name == "Hidden").Visible);
        }

        [Test]
        public void Apply_Builds_The_Rig_And_Reimport_Updates_In_Place()
        {
            var options = new SpritePartsPsdImport.Options { PixelsPerUnit = 1f };
            var plan = SpritePartsPsdImport.BuildPlan(Sample(), options);
            var profile = new SpriteSheetProfile();
            profile.EnsurePartsRig();
            int before = profile.PartsSlots.Count;
            var atlas = new Texture2D(plan.AtlasWidth, plan.AtlasHeight);
            try
            {
                var result = SpritePartsPsdImport.Apply(profile, plan, "hero", atlas, options);
                Assert.IsTrue(result.Ok, result.Reason);
                Assert.AreEqual(5, result.Created);
                Assert.AreEqual(before + 5, profile.PartsSlots.Count);
                var arm = profile.PartsSlots.Find(s => s.Name == "Arm");
                var hand = profile.PartsSlots.Find(s => s.Name == "Hånd");
                var glove = profile.PartsSlots.Find(s => s.Name == "Glove");
                var back = profile.PartsSlots.Find(s => s.Name == "Back");
                Assert.IsTrue(arm.IsBone);
                Assert.AreEqual(SpritePartIdUtility.Canonical(arm.SlotId), SpritePartIdUtility.Canonical(hand.ParentSlotId));
                Assert.AreEqual(Vector2.zero, hand.RestPosition, "In the bone's space: the bone is right there.");
                Assert.AreEqual(new Vector2(0f, 3f), back.RestPosition);
                Assert.AreEqual(SpritePartIdUtility.Canonical(hand.SlotId), glove.ClipMaskSlotId);
                Assert.IsFalse(profile.PartsSlots.Find(s => s.Name == "Hidden").Enabled);
                Assert.Less(back.DrawRank, hand.DrawRank);
                Assert.Less(hand.DrawRank, glove.DrawRank);
                var sheet = profile.Sheets[result.SheetIndex];
                Assert.AreEqual(SpriteSheetCellLayoutMode.Cropped, sheet.CellLayoutMode);
                Assert.AreEqual(4, sheet.CroppedCellRects.Length);
                var handApp = SpritePartsAuthoringOps.FindAppearance(profile, hand.DefaultAppearanceId);
                int cell = handApp.CellIndex;

                hand.RestPosition = new Vector2(9f, 9f); // posed by hand after the first import
                var again = SpritePartsPsdImport.Apply(profile, SpritePartsPsdImport.BuildPlan(Sample(), options), "hero", atlas, options);
                Assert.IsTrue(again.Ok, again.Reason);
                Assert.AreEqual(0, again.Created);
                Assert.AreEqual(5, again.Updated);
                Assert.AreEqual(before + 5, profile.PartsSlots.Count);
                Assert.AreEqual(result.SheetIndex, again.SheetIndex, "Same sheet.");
                Assert.AreEqual(cell, SpritePartsAuthoringOps.FindAppearance(profile, hand.DefaultAppearanceId).CellIndex, "Same cell.");
                Assert.AreEqual(new Vector2(9f, 9f), hand.RestPosition, "Updating keeps the pose.");
            }
            finally
            {
                Object.DestroyImmediate(atlas);
            }
        }

        [Test]
        public void Merge_Groups_Makes_One_Part_Per_Group()
        {
            var plan = SpritePartsPsdImport.BuildPlan(Sample(), new SpritePartsPsdImport.Options
            {
                PixelsPerUnit = 1f, Groups = SpritePsdGroupMode.MergeGroups, IncludeHidden = false,
            });
            Assert.IsNull(plan.Error);
            Assert.AreEqual(2, plan.Items.Count, "Back and the merged Arm; the hidden layer is left out.");
            var arm = plan.Items.Find(i => i.Name == "Arm");
            Assert.AreEqual(3, arm.Width);
            Assert.AreEqual(2, arm.Height);
            Assert.AreEqual(Blue, arm.Pixels[0], "The clipped glove is on top where the hand is.");
        }
    }
}
