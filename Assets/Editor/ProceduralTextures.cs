using ColosseumDuel.Core;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace ColosseumDuel.EditorTools
{
    /// <summary>
    /// Generates the arena's sand and wall textures, matching the design doc's "procedural canvas
    /// textures" from the web prototype.
    ///
    /// Baked to PNG assets at bootstrap rather than generated at runtime: a Texture2D built in
    /// Awake would be invisible to the build's asset pipeline (no mipmaps, no compression, and one
    /// more thing that only exists if some script happens to run), whereas a committed asset is
    /// just an asset.
    /// </summary>
    public static class ProceduralTextures
    {
        private const int WallSize = 512;

        /// <summary>
        /// The sand is drawn across the whole floor once, so it is generated large and NOT tiled.
        /// Tiling it needed seamless noise, and the usual four-copy blend that makes Perlin seamless
        /// trades the seams for a repeating diamond pattern that is just as obvious on a flat floor.
        /// The arena is one object of a fixed size - there was never a reason to repeat it.
        /// </summary>
        private const int SandSize = 1024;

        public static Texture2D EnsureSand(string path, Color baseColor)
        {
            var existing = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (existing != null) return existing;

            var pixels = new Color32[SandSize * SandSize];
            var rng = new System.Random(20260904);

            for (int y = 0; y < SandSize; y++)
            {
                for (int x = 0; x < SandSize; x++)
                {
                    // Broad drifts from fractal noise, fine grain from per-pixel noise, plus a sparse
                    // scatter of darker specks so it reads as sand rather than as a gradient.
                    float broad = Fbm(x * 0.006f, y * 0.006f, 4);
                    float grain = (float)rng.NextDouble();

                    float shade = 0.86f + broad * 0.22f + (grain - 0.5f) * 0.07f;
                    if (rng.NextDouble() < 0.012) shade *= 0.82f;

                    pixels[y * SandSize + x] = Tint(baseColor, shade);
                }
            }

            return Write(path, pixels, SandSize);
        }

        public static Texture2D EnsureWall(string path, Color baseColor)
        {
            var existing = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (existing != null) return existing;

            var pixels = new Color32[WallSize * WallSize];
            var rng = new System.Random(70131);

            // Both divide Size exactly, so the courses meet cleanly where the texture wraps.
            const int courseHeight = 64;  // stone course
            const int blockWidth = 64;
            const int mortar = 4;

            for (int y = 0; y < WallSize; y++)
            {
                int course = y / courseHeight;
                // Every other course is offset half a block, the way stonework is actually laid.
                int offset = (course % 2) * (blockWidth / 2);

                for (int x = 0; x < WallSize; x++)
                {
                    int withinCourse = y % courseHeight;
                    int withinBlock = ((x + offset) % WallSize) % blockWidth;

                    bool isMortar = withinCourse < mortar || withinBlock < mortar;

                    float shade = isMortar
                        ? 0.62f
                        : 0.92f + TileableFbm(x * 0.02f, y * 0.02f, WallSize * 0.02f, 2) * 0.22f
                                + (float)(rng.NextDouble() - 0.5) * 0.05f;

                    pixels[y * WallSize + x] = Tint(baseColor, shade);
                }
            }

            return Write(path, pixels, WallSize);
        }

        /// <summary>
        /// One icon per archetype, drawn as a silhouette and left white so the UI can tint it with
        /// the archetype's own colour - the same colour the gladiator's body carries on the arena,
        /// so the card and the fighter are recognisably the same character.
        ///
        /// Silhouettes rather than detailed art: these are read at 36 pixels on a roster card, where
        /// anything with interior detail turns to mush. Each shape says what the archetype is for -
        /// a shield for the one who soaks damage, an axe for the one who deals it, a double chevron
        /// for the one who outruns both.
        ///
        /// Like every generator here it keeps whatever is already on disk, so redrawing the art
        /// means deleting the PNG first and running the bootstrap again.
        /// </summary>
        public static Sprite EnsureArchetypeIcon(string path, GladiatorId id)
        {
            var existing = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            if (existing != null) return existing;

            return WriteBitmapSprite(path, IconRows(id));
        }

        private static string[] IconRows(GladiatorId id)
        {
            switch (id)
            {
                // Brutius: 200 HP and the slowest of the three - a tower shield.
                case GladiatorId.Brutius:
                    return new[]
                    {
                        "................",
                        "..############..",
                        ".##############.",
                        "################",
                        "################",
                        "################",
                        "################",
                        "################",
                        ".##############.",
                        ".##############.",
                        "..############..",
                        "...##########...",
                        "....########....",
                        "......####......",
                        ".......##.......",
                        "................",
                    };

                // Barbarius: the hardest hitter - a maul.
                //
                // One solid mass, like the other two. An axe was tried twice and failed both times
                // for the same reason: at the size these are actually read, a shape with an interior
                // gap between blade and haft collapses into a smudge with a stick next to it.
                // Everything here has to survive being about forty pixels across.
                case GladiatorId.Barbarius:
                    return new[]
                    {
                        "................",
                        "................",
                        "...##########...",
                        "..############..",
                        "..############..",
                        "..############..",
                        "...##########...",
                        "......####......",
                        "......####......",
                        "......####......",
                        "......####......",
                        "......####......",
                        "......####......",
                        "......####......",
                        "................",
                        "................",
                    };

                // Hilius: twice the speed of Brutius, and two attacks a cycle - a double chevron.
                default:
                    return new[]
                    {
                        "................",
                        "................",
                        ".##.....##......",
                        "..##.....##.....",
                        "...##.....##....",
                        "....##.....##...",
                        ".....##.....##..",
                        "......##.....##.",
                        "......##.....##.",
                        ".....##.....##..",
                        "....##.....##...",
                        "...##.....##....",
                        "..##.....##.....",
                        ".##.....##......",
                        "................",
                        "................",
                    };
            }
        }

        /// <summary>
        /// The "this gladiator is out" marker, as a sprite.
        ///
        /// Drawn from a bitmap rather than taken from a font glyph: Inter has no skull character, and
        /// a missing glyph renders as nothing at all - the same silent failure that made the whole
        /// HUD's Cyrillic disappear in the first WebGL build.
        /// </summary>
        public static Sprite EnsureSkull(string path)
        {
            var existing = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            if (existing != null) return existing;

            string[] rows =
            {
                "................",
                "....########....",
                "..############..",
                ".##############.",
                ".##############.",
                ".###..####..###.",
                ".##....##....##.",
                ".##....##....##.",
                ".###..####..###.",
                ".##############.",
                "..############..",
                "...##########...",
                "...#.#.##.#.#...",
                "...##########...",
                "....########....",
                "................",
            };

            return WriteBitmapSprite(path, rows);
        }

        /// <summary>
        /// Turns a square block of '#' and '.' into a white sprite with a transparent background.
        ///
        /// The row check is not ceremony: these are hand-drawn in a string array, and a row one
        /// character short does not throw on its own - it silently shifts every pixel after it,
        /// producing a sheared icon that looks like a bad drawing rather than a typo.
        /// </summary>
        private static Sprite WriteBitmapSprite(string path, string[] rows)
        {
            foreach (var row in rows)
                if (row.Length != rows.Length)
                    throw new System.ArgumentException(
                        $"{path}: the art is {rows.Length} rows but one of them is {row.Length} " +
                        $"characters ('{row}'). Every row must be as long as the block is tall.");

            const int scale = 4;
            int size = rows.Length * scale;
            var pixels = new Color32[size * size];

            for (int y = 0; y < size; y++)
            {
                // Bitmap rows read top-down, texture rows read bottom-up.
                string row = rows[rows.Length - 1 - y / scale];
                for (int x = 0; x < size; x++)
                {
                    bool solid = row[x / scale] == '#';
                    pixels[y * size + x] = solid
                        ? new Color32(255, 255, 255, 255)
                        : new Color32(255, 255, 255, 0);
                }
            }

            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            texture.SetPixels32(pixels);
            texture.Apply();

            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllBytes(path, texture.EncodeToPNG());
            Object.DestroyImmediate(texture);

            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);

            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.alphaIsTransparency = true;
            importer.filterMode = FilterMode.Point; // keep the pixel edges crisp
            importer.mipmapEnabled = false;
            importer.SaveAndReimport();

            return AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }

        /// <summary>
        /// The dash pattern for the trajectory preview: an opaque run followed by a transparent gap,
        /// tiled along the line by LineRenderer.
        ///
        /// A LineRenderer cannot draw dashes on its own. Splitting the polyline into separate
        /// segments would work on a straight run but makes dash length wander at every bounce,
        /// because the points are not evenly spaced. Tiling a texture keys the dashes to distance
        /// travelled instead, so they stay even all the way round a bounce.
        /// </summary>
        public static Texture2D EnsureDash(string path)
        {
            var existing = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (existing != null) return existing;

            const int width = 64;
            const int height = 8;
            const int dashLength = 38; // the rest of the width is the gap

            var pixels = new Color32[width * height];
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                    pixels[y * width + x] = x < dashLength
                        ? new Color32(255, 255, 255, 255)
                        : new Color32(255, 255, 255, 0);

            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
            texture.SetPixels32(pixels);
            texture.Apply();

            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllBytes(path, texture.EncodeToPNG());
            Object.DestroyImmediate(texture);

            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);

            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = TextureImporterType.Default;
            importer.wrapMode = TextureWrapMode.Repeat; // required, or tiling clamps to one dash
            importer.filterMode = FilterMode.Point;     // crisp dash ends
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.SaveAndReimport();

            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        /// <summary>
        /// A white circle, or a ring when innerFraction is above zero, as a sprite.
        ///
        /// Needed as an actual sprite because the radial rage gauge uses Image.Type.Filled, and
        /// fillAmount is ignored outright on an Image with no sprite - it draws full and says
        /// nothing about it.
        /// </summary>
        public static Sprite EnsureDisc(string path, float innerFraction = 0f)
        {
            var existing = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            if (existing != null) return existing;

            const int size = 128;
            const float outer = size * 0.5f - 1f;
            float inner = outer * innerFraction;
            var centre = new Vector2(size * 0.5f, size * 0.5f);

            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), centre);

                    // Antialiased over a one-pixel band, otherwise a circle this size looks jagged
                    // against the arena behind it.
                    float alpha = Mathf.Clamp01(outer - d);
                    if (inner > 0f) alpha = Mathf.Min(alpha, Mathf.Clamp01(d - inner));

                    pixels[y * size + x] = new Color32(255, 255, 255, (byte)(alpha * 255f));
                }
            }

            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            texture.SetPixels32(pixels);
            texture.Apply();

            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllBytes(path, texture.EncodeToPNG());
            Object.DestroyImmediate(texture);

            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);

            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.alphaIsTransparency = true;
            importer.filterMode = FilterMode.Bilinear;
            importer.mipmapEnabled = false;
            importer.SaveAndReimport();

            return AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }

        // ------------------------------------------------------------------

        private static Color32 Tint(Color baseColor, float shade)
            => new Color(
                Mathf.Clamp01(baseColor.r * shade),
                Mathf.Clamp01(baseColor.g * shade),
                Mathf.Clamp01(baseColor.b * shade),
                1f);

        /// <summary>
        /// Seamless fractal noise. Perlin is not periodic, so tiling its output leaves a visible
        /// grid on a floor this large - the arena tiles the sand 6x6 and every seam shows. Blending
        /// four copies of the field, each offset by one period, makes opposite edges agree.
        /// </summary>
        private static float TileableFbm(float x, float y, float period, int octaves)
        {
            float u = Mathf.Repeat(x, period) / period;
            float v = Mathf.Repeat(y, period) / period;

            float v00 = Fbm(x, y, octaves);
            float v10 = Fbm(x - period, y, octaves);
            float v01 = Fbm(x, y - period, octaves);
            float v11 = Fbm(x - period, y - period, octaves);

            return v00 * (1f - u) * (1f - v)
                 + v10 * u * (1f - v)
                 + v01 * (1f - u) * v
                 + v11 * u * v;
        }

        /// <summary>Fractal value noise in 0..1, built on Unity's Perlin so there is no table to ship.</summary>
        private static float Fbm(float x, float y, int octaves)
        {
            float value = 0f, amplitude = 0.5f, frequency = 1f, total = 0f;
            for (int i = 0; i < octaves; i++)
            {
                value += Mathf.PerlinNoise(x * frequency, y * frequency) * amplitude;
                total += amplitude;
                amplitude *= 0.5f;
                frequency *= 2.1f;
            }
            return total > 0f ? value / total : 0f;
        }

        private static Texture2D Write(string path, Color32[] pixels, int size)
        {
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            texture.SetPixels32(pixels);
            texture.Apply();

            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllBytes(path, texture.EncodeToPNG());
            Object.DestroyImmediate(texture);

            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);

            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.wrapMode = TextureWrapMode.Repeat;
            importer.filterMode = FilterMode.Bilinear;
            importer.anisoLevel = 4;
            importer.SaveAndReimport();

            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }
    }
}
