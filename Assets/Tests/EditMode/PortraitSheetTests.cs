using System.Linq;
using ColosseumDuel.Core;
using ColosseumDuel.Gameplay.View;
using NUnit.Framework;
using UnityEngine;

namespace ColosseumDuel.Tests
{
    /// <summary>
    /// The portrait sheet is cut by arithmetic, so it is tested on a sheet made of arithmetic, laid out
    /// the way the real one is: six framed squares on a white ground, each a dark border round a field
    /// a little off its described colour, a figure in the lower middle - and a crest reaching up out of
    /// the frame, which is where the real portraits break every simple rule about where things are.
    /// </summary>
    public class PortraitSheetTests
    {
        private const int Cell = 100;
        private const int FrameFrom = 12, FrameTo = 88, Border = 3;

        /// <summary>As the sheet is laid out: red, blue, orange along the top, green, purple, yellow below.</summary>
        private static readonly GladiatorId[] SheetOrder =
        {
            GladiatorId.Hilius, GladiatorId.Scutarius, GladiatorId.Barbarius,
            GladiatorId.Hastarius, GladiatorId.Brutius, GladiatorId.Retiarius,
        };

        private static readonly Color32 White = new Color32(255, 255, 255, 255);
        private static readonly Color32 Ink = new Color32(20, 18, 16, 255);
        private static readonly Color32 Crest = new Color32(150, 30, 25, 255);

        [Test]
        public void TheLayoutIsTheOneWithTheSquarestCells()
        {
            Assert.AreEqual(new Vector2Int(3, 2), PortraitSheet.Grid(1536, 1024));
            Assert.AreEqual(new Vector2Int(2, 3), PortraitSheet.Grid(1024, 1536));
            Assert.AreEqual(new Vector2Int(6, 1), PortraitSheet.Grid(1200, 200));
            Assert.AreEqual(new Vector2Int(1, 6), PortraitSheet.Grid(200, 1200));
        }

        [Test]
        public void EachPortraitGoesToTheGladiatorWhoseColourItIsPaintedOn()
        {
            var pixels = Sheet(out int width, out int height, silhouette: false);
            var cells = PortraitSheet.Slice(pixels, width, height);

            Assert.AreEqual(PortraitSheet.PortraitCount, cells.Count);
            CollectionAssert.AreEquivalent(GladiatorDef.All.Select(d => d.Id), cells.Select(c => c.Id),
                "every gladiator should have exactly one portrait");

            for (int i = 0; i < cells.Count; i++)
            {
                Assert.AreEqual(SheetOrder[i], cells[i].Id, $"cell {i} went to the wrong gladiator");
                AssertClose(Painted(SheetOrder[i]), cells[i].Background, $"cell {i}'s colour");
            }
        }

        [Test]
        public void OnATransparentGroundTheyAreFoundAndMatchedJustTheSame()
        {
            // The sheet as it actually arrived: saved with its alpha, so the ground is not white but
            // nothing - and a transparent pixel stores black, which read by colour alone is ink.
            var pixels = Sheet(out int width, out int height, silhouette: false, ground: new Color32(0, 0, 0, 0));
            var cells = PortraitSheet.Slice(pixels, width, height);

            for (int i = 0; i < cells.Count; i++)
            {
                Assert.AreEqual(SheetOrder[i], cells[i].Id, $"cell {i} went to the wrong gladiator");
                AssertClose(Painted(SheetOrder[i]), cells[i].Background, $"cell {i}'s colour");
            }

            var frame = cells[0].Frame;
            Assert.AreEqual(FrameFrom, frame.xMin, 1, "the transparent ground was read as the frame's border");
        }

        [Test]
        public void TheFrameIsFoundThoughACrestRisesOutOfIt()
        {
            var pixels = Sheet(out int width, out int height, silhouette: false);
            var cells = PortraitSheet.Slice(pixels, width, height);

            // The first cell, top left: its frame in texture pixels, rows counted from the bottom.
            var frame = cells[0].Frame;
            int y0 = height - Cell;
            Assert.AreEqual(FrameFrom, frame.xMin, 1, "left edge");
            Assert.AreEqual(FrameTo - 1, frame.xMax - 1, 1, "right edge");
            Assert.AreEqual(y0 + FrameFrom, frame.yMin, 1, "bottom edge");
            Assert.AreEqual(y0 + FrameTo - 1, frame.yMax - 1, 1,
                "top edge - the crest over the middle of it should have been outvoted");
        }

        [Test]
        public void ThingsPaintedIntoTheFieldDoNotChangeItsColour()
        {
            // The real fields carry arena silhouettes and leaves in a darker shade of their own
            // colour. The commonest shade is the field; the average would drift towards the paint.
            var pixels = Sheet(out int width, out int height, silhouette: true);
            var cells = PortraitSheet.Slice(pixels, width, height);

            for (int i = 0; i < cells.Count; i++)
                AssertClose(Painted(SheetOrder[i]), cells[i].Background, $"cell {i}'s colour");
        }

        [Test]
        public void ACutPortraitKeepsItsOwnCrest_AndLosesTheGroundAndItsNeighboursCrest()
        {
            // The top-left portrait. Its cut runs a little past its cell, down into the one below -
            // whose crest rises out of its frame and across the line, the way the real sheet's does.
            var pixels = Sheet(out int width, out int height, silhouette: false);
            var cell = PortraitSheet.Slice(pixels, width, height)[0];
            Assert.Less(cell.Rect.yMin, height - Cell, "the cut should reach past the cell's own edge");

            var crop = new Color32[cell.Rect.width * cell.Rect.height];
            for (int y = 0; y < cell.Rect.height; y++)
            for (int x = 0; x < cell.Rect.width; x++)
                crop[y * cell.Rect.width + x] = pixels[(cell.Rect.y + y) * width + cell.Rect.x + x];

            var inside = new Vector2Int(cell.Frame.x + cell.Frame.width / 2 - cell.Rect.x,
                                        cell.Frame.y + cell.Frame.height / 2 - cell.Rect.y);
            PortraitSheet.CutOut(crop, cell.Rect.width, cell.Rect.height, inside);

            // In the cell's own coordinates, rows counted up from its bottom edge.
            int offsetY = (height - Cell) - cell.Rect.y;
            byte AlphaAt(int x, int y) => crop[(y + offsetY) * cell.Rect.width + x - cell.Rect.x].a;

            Assert.AreEqual(0, AlphaAt(0, Cell - 1), "the ground in the corner of the cut should be transparent");
            Assert.AreEqual(0, AlphaAt(FrameFrom - 2, Cell / 2), "the ground beside the frame should be transparent");
            Assert.AreEqual(255, AlphaAt(Cell / 2, Cell / 2 - 10), "the portrait itself should be untouched");
            Assert.AreEqual(255, AlphaAt(FrameFrom + Border + 4, FrameTo - Border - 4),
                "the field just inside the frame should be untouched");
            Assert.AreEqual(255, AlphaAt(Cell / 2, FrameTo + 4), "his own crest standing out over the frame should be kept");
            Assert.AreEqual(0, AlphaAt(Cell / 2, -5),
                "the crest of the portrait below, reaching up into the margin, should have been cleared");
        }

        /// <summary>A little off what was described, the way a painted field is.</summary>
        private static Color Painted(GladiatorId id)
        {
            var c = PortraitSheet.ExpectedBackground(id);
            return new Color(Mathf.Clamp01(c.r + 0.04f), Mathf.Clamp01(c.g - 0.03f), Mathf.Clamp01(c.b + 0.02f));
        }

        private static Color32[] Sheet(out int width, out int height, bool silhouette, Color32? ground = null)
        {
            width = Cell * 3;
            height = Cell * 2;
            var plain = ground ?? White;
            var pixels = new Color32[width * height];
            for (int i = 0; i < pixels.Length; i++) pixels[i] = plain;

            for (int i = 0; i < SheetOrder.Length; i++)
            {
                int x0 = (i % 3) * Cell;
                int y0 = height - (i / 3 + 1) * Cell;
                Color32 field = Painted(SheetOrder[i]);
                Color32 shade = Color.Lerp(Painted(SheetOrder[i]), Color.black, 0.2f);

                for (int y = 0; y < Cell; y++)
                for (int x = 0; x < Cell; x++)
                {
                    Color32 p = plain;
                    bool inFrame = x >= FrameFrom && x < FrameTo && y >= FrameFrom && y < FrameTo;
                    if (inFrame)
                    {
                        bool border = x < FrameFrom + Border || x >= FrameTo - Border
                                      || y < FrameFrom + Border || y >= FrameTo - Border;
                        float dx = (x - Cell * 0.5f) / Cell, dy = (y - Cell * 0.35f) / Cell;
                        bool figure = dx * dx + dy * dy < 0.06f;
                        bool painted = silhouette && x < Cell * 0.45f && y > Cell * 0.62f;
                        p = border ? Ink : figure ? new Color32(200, 140, 90, 255) : painted ? shade : field;
                    }

                    // A crest from the middle of the head up through the frame's top edge and past it.
                    if (x >= Cell / 2 - 4 && x < Cell / 2 + 4 && y >= Cell / 2 && y < FrameTo + 8) p = Crest;

                    pixels[(y0 + y) * width + x0 + x] = p;
                }
            }
            return pixels;
        }

        private static void AssertClose(Color expected, Color actual, string what)
        {
            Assert.AreEqual(expected.r, actual.r, 0.01f, $"{what}: red");
            Assert.AreEqual(expected.g, actual.g, 0.01f, $"{what}: green");
            Assert.AreEqual(expected.b, actual.b, 0.01f, $"{what}: blue");
        }
    }
}
