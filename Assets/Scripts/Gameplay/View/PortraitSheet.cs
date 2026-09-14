using System.Collections.Generic;
using ColosseumDuel.Core;
using UnityEngine;

namespace ColosseumDuel.Gameplay.View
{
    /// <summary>
    /// Finds the six portraits on the sheet the art arrived as, and which gladiator each of them is.
    ///
    /// Pure arithmetic on pixels, so it is tested without an image on disk; the bootstrap reads the
    /// sheet and cuts the sprites (see PortraitSprites). Each portrait is painted on a flat field of
    /// its own colour, and that colour is the gladiator's colour everywhere else in the game - the
    /// body on the arena and the card in the menu - so the one sheet decides both.
    ///
    /// Cells are matched to gladiators by that colour rather than by where they sit on the sheet:
    /// the layout of a sheet is whatever the artist chose, the colours are what was asked for.
    /// </summary>
    public static class PortraitSheet
    {
        public const int PortraitCount = 6;

        public struct Cell
        {
            /// <summary>Where the portrait is, in texture pixels - origin bottom left, as Unity reads them.</summary>
            public RectInt Rect;

            /// <summary>The flat colour the portrait is painted on.</summary>
            public Color Background;

            public GladiatorId Id;
        }

        /// <summary>
        /// The colour each portrait was painted on, as far as it could be read by eye: the fallback
        /// while the sheet is not in the project, and the key its cells are matched against once it is.
        /// </summary>
        public static Color ExpectedBackground(GladiatorId id)
        {
            switch (id)
            {
                case GladiatorId.Brutius: return new Color(0.49f, 0.33f, 0.76f);    // purple, the mace
                case GladiatorId.Barbarius: return new Color(0.98f, 0.52f, 0.15f);  // orange, the twin blades
                case GladiatorId.Hilius: return new Color(0.78f, 0.20f, 0.18f);     // red, sword and round shield
                case GladiatorId.Scutarius: return new Color(0.20f, 0.45f, 0.85f);  // blue, the tall shield
                case GladiatorId.Hastarius: return new Color(0.33f, 0.60f, 0.43f);  // green, spear and shield
                case GladiatorId.Retiarius: return new Color(0.93f, 0.72f, 0.20f);  // yellow, trident and net
                default: return Color.white;
            }
        }

        /// <summary>
        /// How the six sit on a sheet this shape: three by two, two by three, or all in a row. The
        /// one whose cells come out nearest to square, since a portrait is.
        /// </summary>
        public static Vector2Int Grid(int width, int height)
        {
            var options = new[]
            {
                new Vector2Int(3, 2), new Vector2Int(2, 3), new Vector2Int(6, 1), new Vector2Int(1, 6),
            };

            var best = options[0];
            float bestError = float.MaxValue;
            foreach (var grid in options)
            {
                float cellAspect = (width / (float)grid.x) / (height / (float)grid.y);
                float error = Mathf.Abs(Mathf.Log(cellAspect));
                if (error < bestError)
                {
                    bestError = error;
                    best = grid;
                }
            }
            return best;
        }

        /// <summary>
        /// Every portrait on the sheet, cut a little inside its cell so the gutter between them does
        /// not come along, with the gladiator it belongs to.
        /// </summary>
        public static List<Cell> Slice(Color32[] pixels, int width, int height)
        {
            var grid = Grid(width, height);
            int cellWidth = width / grid.x;
            int cellHeight = height / grid.y;
            int insetX = Mathf.RoundToInt(cellWidth * CropInset);
            int insetY = Mathf.RoundToInt(cellHeight * CropInset);

            var cells = new List<Cell>();
            for (int row = 0; row < grid.y; row++)
            {
                // Read top row first, the way the sheet is looked at; texture rows count from the bottom.
                int y = height - (row + 1) * cellHeight;
                for (int column = 0; column < grid.x; column++)
                {
                    var whole = new RectInt(column * cellWidth, y, cellWidth, cellHeight);
                    cells.Add(new Cell
                    {
                        Rect = new RectInt(whole.x + insetX, whole.y + insetY,
                            whole.width - insetX * 2, whole.height - insetY * 2),
                        Background = SampleBackground(pixels, width, whole),
                    });
                }
            }

            AssignByColour(cells);
            return cells;
        }

        /// <summary>How much of each cell's edge is cut away with the gutter.</summary>
        private const float CropInset = 0.02f;

        /// <summary>
        /// The colour a portrait is painted on: the median of four patches just inside the corners
        /// of its cell. The corners because the figure is in the middle; the median because a crest
        /// or a shoulder can still reach one of them, and one patch out of four should not shift it.
        /// </summary>
        public static Color SampleBackground(Color32[] pixels, int width, RectInt cell)
        {
            int patch = Mathf.Max(1, Mathf.RoundToInt(Mathf.Min(cell.width, cell.height) * 0.05f));
            int insetX = Mathf.RoundToInt(cell.width * 0.08f);
            int insetY = Mathf.RoundToInt(cell.height * 0.08f);

            var r = new List<float>();
            var g = new List<float>();
            var b = new List<float>();

            foreach (var corner in new[]
            {
                new Vector2Int(cell.xMin + insetX, cell.yMin + insetY),
                new Vector2Int(cell.xMax - insetX - patch, cell.yMin + insetY),
                new Vector2Int(cell.xMin + insetX, cell.yMax - insetY - patch),
                new Vector2Int(cell.xMax - insetX - patch, cell.yMax - insetY - patch),
            })
            {
                for (int y = corner.y; y < corner.y + patch; y++)
                for (int x = corner.x; x < corner.x + patch; x++)
                {
                    var p = pixels[y * width + x];
                    r.Add(p.r / 255f);
                    g.Add(p.g / 255f);
                    b.Add(p.b / 255f);
                }
            }

            return new Color(Median(r), Median(g), Median(b));
        }

        /// <summary>
        /// Gives each cell the gladiator whose colour it is nearest to, nearest pairs first, so a
        /// colour that is close to two of them goes to the one it is closest to and the other finds
        /// its own.
        /// </summary>
        private static void AssignByColour(List<Cell> cells)
        {
            var ids = new List<GladiatorId>();
            foreach (var def in GladiatorDef.All) ids.Add(def.Id);

            var pairs = new List<(float distance, int cell, GladiatorId id)>();
            for (int i = 0; i < cells.Count; i++)
                foreach (var id in ids)
                    pairs.Add((Distance(cells[i].Background, ExpectedBackground(id)), i, id));
            pairs.Sort((a, b) => a.distance.CompareTo(b.distance));

            var cellTaken = new bool[cells.Count];
            var idTaken = new HashSet<GladiatorId>();
            foreach (var pair in pairs)
            {
                if (cellTaken[pair.cell] || idTaken.Contains(pair.id)) continue;
                cellTaken[pair.cell] = true;
                idTaken.Add(pair.id);

                var cell = cells[pair.cell];
                cell.Id = pair.id;
                cells[pair.cell] = cell;
            }
        }

        private static float Distance(Color a, Color b)
            => new Vector3(a.r - b.r, a.g - b.g, a.b - b.b).sqrMagnitude;

        private static float Median(List<float> values)
        {
            values.Sort();
            return values[values.Count / 2];
        }
    }
}
