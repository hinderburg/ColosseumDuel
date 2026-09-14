using System.Collections.Generic;
using ColosseumDuel.Core;
using UnityEngine;

namespace ColosseumDuel.Gameplay.View
{
    /// <summary>
    /// Finds the six portraits on the sheet the art arrived as, and which gladiator each of them is.
    ///
    /// Pure arithmetic on pixels, so it is tested without an image on disk; the bootstrap reads the
    /// sheet and cuts the sprites (see PortraitSprites). Each portrait is a framed square - a dark
    /// border, rounded at the corners, round a field of its own colour - standing on the sheet's
    /// plain white ground, and the colour of that field is the gladiator's colour everywhere else in
    /// the game: his body on the arena and his card in the menu.
    ///
    /// Cells are matched to gladiators by that colour rather than by where they sit on the sheet:
    /// the layout of a sheet is whatever the artist chose, the colours are what was asked for.
    /// </summary>
    public static class PortraitSheet
    {
        public const int PortraitCount = 6;

        public struct Cell
        {
            /// <summary>What is cut out for the avatar, in texture pixels - origin bottom left.</summary>
            public RectInt Rect;

            /// <summary>The portrait's frame inside its cell: the dark border drawn round it.</summary>
            public RectInt Frame;

            /// <summary>The colour the portrait's field is painted in.</summary>
            public Color Background;

            public GladiatorId Id;
        }

        /// <summary>
        /// The colour each portrait's field was described as, and read off the sheet as about: the
        /// fallback while the sheet is not in the project, and the key its cells are matched against.
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

        /// <summary>Every portrait on the sheet, with its frame, its colour and the gladiator it belongs to.</summary>
        public static List<Cell> Slice(Color32[] pixels, int width, int height)
        {
            var grid = Grid(width, height);
            int cellWidth = width / grid.x;
            int cellHeight = height / grid.y;

            var cells = new List<Cell>();
            for (int row = 0; row < grid.y; row++)
            {
                // Read top row first, the way the sheet is looked at; texture rows count from the bottom.
                int y = height - (row + 1) * cellHeight;
                for (int column = 0; column < grid.x; column++)
                {
                    var whole = new RectInt(column * cellWidth, y, cellWidth, cellHeight);
                    var frame = FindFrame(pixels, width, whole);
                    int margin = Mathf.RoundToInt(Mathf.Min(cellWidth, cellHeight) * CutMargin);
                    cells.Add(new Cell
                    {
                        // The whole cell and a little past it, not just the frame: a crest or a spear
                        // reaching out over the frame is part of the picture, and on this sheet some
                        // of them cross into the next cell. CutOut drops whatever is a neighbour's.
                        Rect = Expand(whole, width, height, margin),
                        Frame = frame,
                        Background = SampleBackground(pixels, width, frame),
                    });
                }
            }

            AssignByColour(cells);
            return cells;
        }

        /// <summary>How far past its own cell a portrait is cut, as a share of the cell.</summary>
        private const float CutMargin = 0.06f;

        private static RectInt Expand(RectInt r, int width, int height, int by)
        {
            int xMin = Mathf.Max(0, r.xMin - by), yMin = Mathf.Max(0, r.yMin - by);
            int xMax = Mathf.Min(width, r.xMax + by), yMax = Mathf.Min(height, r.yMax + by);
            return new RectInt(xMin, yMin, xMax - xMin, yMax - yMin);
        }

        /// <summary>
        /// Cuts one portrait out of its piece of the sheet: the ground made transparent, and then
        /// everything not joined to the portrait itself cleared too - a neighbour's crest or spear
        /// point reaching over into the margin is somebody else's. <paramref name="inside"/> is any
        /// point on the portrait, in the crop's own pixels.
        /// </summary>
        public static void CutOut(Color32[] crop, int width, int height, Vector2Int inside)
        {
            ClearOutside(crop, width, height);
            KeepJoinedTo(crop, width, height, inside);
        }

        private static void KeepJoinedTo(Color32[] crop, int width, int height, Vector2Int seed)
        {
            int start = seed.y * width + seed.x;
            if (start < 0 || start >= crop.Length || crop[start].a == 0) return;

            var kept = new bool[crop.Length];
            var queue = new Queue<int>();
            kept[start] = true;
            queue.Enqueue(start);

            while (queue.Count > 0)
            {
                int i = queue.Dequeue();
                int x = i % width, y = i / width;
                for (int n = 0; n < 4; n++)
                {
                    int nx = x + (n == 0 ? -1 : n == 1 ? 1 : 0);
                    int ny = y + (n == 2 ? -1 : n == 3 ? 1 : 0);
                    if (nx < 0 || ny < 0 || nx >= width || ny >= height) continue;
                    int j = ny * width + nx;
                    if (kept[j] || crop[j].a == 0) continue;
                    kept[j] = true;
                    queue.Enqueue(j);
                }
            }

            for (int i = 0; i < crop.Length; i++)
                if (!kept[i]) crop[i].a = 0;
        }

        /// <summary>
        /// The portrait's frame inside its cell: where the dark border is met walking in from each
        /// side. Along three lines per side, taking the middle answer, so a crest over the top of the
        /// frame or a spear out past its edge - either of which one line can run into first - is
        /// outvoted by the other two.
        /// </summary>
        public static RectInt FindFrame(Color32[] pixels, int width, RectInt cell)
        {
            var fractions = new[] { 0.35f, 0.5f, 0.65f };
            var lefts = new List<int>();
            var rights = new List<int>();
            var bottoms = new List<int>();
            var tops = new List<int>();

            foreach (float f in fractions)
            {
                int y = cell.yMin + Mathf.RoundToInt(cell.height * f);
                int x = cell.xMin + Mathf.RoundToInt(cell.width * f);
                lefts.Add(FirstDark(pixels, width, cell.xMin, y, 1, 0, cell.width));
                rights.Add(FirstDark(pixels, width, cell.xMax - 1, y, -1, 0, cell.width));
                bottoms.Add(FirstDark(pixels, width, x, cell.yMin, 0, 1, cell.height));
                tops.Add(FirstDark(pixels, width, x, cell.yMax - 1, 0, -1, cell.height));
            }

            int left = Median(lefts), right = Median(rights), bottom = Median(bottoms), top = Median(tops);
            if (left < 0 || right < 0 || bottom < 0 || top < 0 || right <= left || top <= bottom) return cell;
            return new RectInt(left, bottom, right - left + 1, top - bottom + 1);
        }

        /// <summary>The first dark pixel from a point in a direction, or -1 if there is none within reach.</summary>
        private static int FirstDark(Color32[] pixels, int width, int x, int y, int dx, int dy, int reach)
        {
            for (int i = 0; i < reach; i++)
            {
                int px = x + dx * i, py = y + dy * i;
                if (IsDark(pixels[py * width + px])) return dx != 0 ? px : py;
            }
            return -1;
        }

        /// <summary>
        /// The frame's ink. Opaque only: the sheet's ground may be transparent, and a transparent
        /// pixel usually stores black - read by colour alone, the whole ground is one dark border.
        /// </summary>
        private static bool IsDark(Color32 p) => p.a > 127 && p.r + p.g + p.b < 3 * 70;

        /// <summary>
        /// The colour the portrait's field is painted in: the commonest strongly coloured shade in the
        /// top third of the frame, where the field shows round the head. The commonest rather than an
        /// average, because a crest, a helmet and whatever is painted into the field sit in the same
        /// band, and an average of all of them is a colour none of them is.
        /// </summary>
        public static Color SampleBackground(Color32[] pixels, int width, RectInt frame)
        {
            int insetX = Mathf.Max(2, Mathf.RoundToInt(frame.width * 0.06f));
            int insetY = Mathf.Max(2, Mathf.RoundToInt(frame.height * 0.06f));
            int yFrom = frame.yMax - Mathf.RoundToInt(frame.height * 0.35f);
            int yTo = frame.yMax - insetY;

            var counts = new Dictionary<int, int>();
            var sums = new Dictionary<int, Vector3>();
            for (int y = yFrom; y < yTo; y++)
            for (int x = frame.xMin + insetX; x < frame.xMax - insetX; x++)
            {
                var p = pixels[y * width + x];
                if (p.a < 128) continue;
                Color.RGBToHSV(p, out _, out float saturation, out float value);
                if (saturation < 0.3f || value < 0.3f) continue;

                int key = ((p.r >> 5) << 6) | ((p.g >> 5) << 3) | (p.b >> 5);
                counts.TryGetValue(key, out int n);
                counts[key] = n + 1;
                sums.TryGetValue(key, out var sum);
                sums[key] = sum + new Vector3(p.r, p.g, p.b);
            }

            if (counts.Count == 0) return Color.gray;

            int best = -1, bestCount = 0;
            foreach (var entry in counts)
                if (entry.Value > bestCount)
                {
                    best = entry.Key;
                    bestCount = entry.Value;
                }

            var mean = sums[best] / (bestCount * 255f);
            return new Color(mean.x, mean.y, mean.z);
        }

        /// <summary>
        /// Makes the sheet's plain ground round a cut portrait transparent: every near-white pixel that
        /// can be reached from the edge of the cut without crossing anything else. The frame's dark
        /// border closes the portrait off, so nothing inside it is touched, and anything reaching out
        /// over the frame - a crest, a trident - is kept, against a transparent ground.
        /// </summary>
        public static void ClearOutside(Color32[] crop, int width, int height)
        {
            var queue = new Queue<int>();
            var seen = new bool[crop.Length];

            void Seed(int x, int y)
            {
                int i = y * width + x;
                if (seen[i] || !IsGround(crop[i])) return;
                seen[i] = true;
                queue.Enqueue(i);
            }

            for (int x = 0; x < width; x++)
            {
                Seed(x, 0);
                Seed(x, height - 1);
            }
            for (int y = 0; y < height; y++)
            {
                Seed(0, y);
                Seed(width - 1, y);
            }

            while (queue.Count > 0)
            {
                int i = queue.Dequeue();
                crop[i].a = 0;
                int x = i % width, y = i / width;
                if (x > 0) Seed(x - 1, y);
                if (x < width - 1) Seed(x + 1, y);
                if (y > 0) Seed(x, y - 1);
                if (y < height - 1) Seed(x, y + 1);
            }
        }

        /// <summary>
        /// The sheet's ground: transparent, or plain white and the pale edge it softens into along the
        /// frame. The same art has come both ways - a sheet saved flat has a white ground, one saved
        /// with its alpha has none.
        /// </summary>
        private static bool IsGround(Color32 p) => p.a < 128 || (p.r > 215 && p.g > 215 && p.b > 215);

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

        private static int Median(List<int> values)
        {
            values.Sort();
            return values[values.Count / 2];
        }
    }
}
