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
