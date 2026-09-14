using System.Linq;
using ColosseumDuel.Core;
using ColosseumDuel.Gameplay.View;
using NUnit.Framework;
using UnityEngine;

namespace ColosseumDuel.Tests
{
    /// <summary>
    /// The portrait sheet is cut by arithmetic, so it is tested on a sheet made of arithmetic: six
    /// flat fields a little off the described colours, each with a dark figure in the middle and a
    /// white gutter round it - which is what the real one is, less the painting.
    /// </summary>
    public class PortraitSheetTests
    {
        private const int Cell = 60;

        /// <summary>As the sheet was laid out: red, blue, orange along the top, green, purple, yellow below.</summary>
        private static readonly GladiatorId[] SheetOrder =
        {
            GladiatorId.Hilius, GladiatorId.Scutarius, GladiatorId.Barbarius,
            GladiatorId.Hastarius, GladiatorId.Brutius, GladiatorId.Retiarius,
        };

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
            var pixels = Sheet(out int width, out int height, figureOverCorner: false);
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
        public void ACropLeavesTheGutterBehind()
        {
            var pixels = Sheet(out int width, out int height, figureOverCorner: false);
            foreach (var cell in PortraitSheet.Slice(pixels, width, height))
            {
                var corner = pixels[cell.Rect.yMin * width + cell.Rect.xMin];
                Assert.AreNotEqual(new Color32(255, 255, 255, 255), corner, "the crop took the white gutter with it");
            }
        }

        [Test]
        public void AFigureReachingIntoOneCornerDoesNotChangeTheColour()
        {
            // A crest or a shield rim can reach a corner. One corner of four is outvoted.
            var pixels = Sheet(out int width, out int height, figureOverCorner: true);
            var cells = PortraitSheet.Slice(pixels, width, height);

            for (int i = 0; i < cells.Count; i++)
                AssertClose(Painted(SheetOrder[i]), cells[i].Background, $"cell {i}'s colour");
        }

        /// <summary>A little off what was described, the way a painted field is.</summary>
        private static Color Painted(GladiatorId id)
        {
            var c = PortraitSheet.ExpectedBackground(id);
            return new Color(Mathf.Clamp01(c.r + 0.05f), Mathf.Clamp01(c.g - 0.04f), Mathf.Clamp01(c.b + 0.03f));
        }

        private static Color32[] Sheet(out int width, out int height, bool figureOverCorner)
        {
            width = Cell * 3;
            height = Cell * 2;
            var pixels = new Color32[width * height];

            for (int i = 0; i < SheetOrder.Length; i++)
            {
                int column = i % 3;
                int row = i / 3;
                int x0 = column * Cell;
                int y0 = height - (row + 1) * Cell;
                Color32 field = Painted(SheetOrder[i]);

                for (int y = 0; y < Cell; y++)
                for (int x = 0; x < Cell; x++)
                {
                    bool gutter = x < 1 || y < 1 || x >= Cell - 1 || y >= Cell - 1;
                    float dx = (x - Cell * 0.5f) / Cell;
                    float dy = (y - Cell * 0.5f) / Cell;
                    bool figure = dx * dx + dy * dy < 0.12f
                                  || (figureOverCorner && x > Cell * 0.8f && y > Cell * 0.8f);

                    pixels[(y0 + y) * width + x0 + x] = gutter ? new Color32(255, 255, 255, 255)
                        : figure ? new Color32(40, 30, 25, 255)
                        : field;
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
