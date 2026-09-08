using System.Collections.Generic;
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
        /// A vignette: clear through the middle, opaque towards the edges.
        ///
        /// White, so the UI can tint it to whatever the moment calls for, and built with a soft
        /// falloff rather than a hard ring - a visible edge on a full-screen overlay reads as a
        /// rendering fault, not as an effect.
        /// </summary>
        public static Sprite EnsureVignette(string path)
        {
            var existing = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            if (existing != null) return existing;

            const int size = 256;
            var pixels = new Color32[size * size];

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    // Distance from the centre in each axis separately, then combined - a plain
                    // radial falloff on a portrait screen leaves the corners dark and the long edges
                    // untouched, which reads as a circle drawn on the screen rather than as a frame.
                    float dx = Mathf.Abs(x / (size - 1f) * 2f - 1f);
                    float dy = Mathf.Abs(y / (size - 1f) * 2f - 1f);
                    float edge = Mathf.Max(dx, dy);

                    // Nothing at all across the middle half, then easing in towards the border.
                    float t = Mathf.InverseLerp(0.45f, 1f, edge);
                    byte alpha = (byte)Mathf.RoundToInt(Mathf.Clamp01(t * t) * 255f);
                    pixels[y * size + x] = new Color32(255, 255, 255, alpha);
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
            importer.wrapMode = TextureWrapMode.Clamp; // or the gradient tiles back in at the seams
            importer.mipmapEnabled = false;
            importer.SaveAndReimport();

            return AssetDatabase.LoadAssetAtPath<Sprite>(path);
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
        /// One badge per weapon kind, for the green mark that says what a gladiator is trained in.
        ///
        /// Silhouettes rather than the weapon models: these are read at about thirty pixels on a
        /// roster card, and a rendered mace at that size is a grey smudge. Each is one solid mass
        /// for the same reason the archetype icons are - an interior gap collapses.
        /// </summary>
        public static Sprite EnsureWeaponIcon(string path, WeaponKind kind)
        {
            var existing = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            if (existing != null) return existing;

            return WriteBitmapSprite(path, WeaponIconRows(kind));
        }

        private static string[] WeaponIconRows(WeaponKind kind)
        {
            switch (kind)
            {
                // Twin swords: two blades crossed. The pair is the whole identity of the weapon.
                case WeaponKind.DualSwords:
                    return new[]
                    {
                        "................",
                        ".##..........##.",
                        "..##........##..",
                        "...##......##...",
                        "....##....##....",
                        ".....##..##.....",
                        "......####......",
                        ".......##.......",
                        "......####......",
                        ".....##..##.....",
                        "....##....##....",
                        "...##......##...",
                        "..##........##..",
                        ".##..........##.",
                        "................",
                        "................",
                    };

                // Sword and shield: a blade behind a heater shield.
                case WeaponKind.SwordAndShield:
                    return new[]
                    {
                        "................",
                        "............##..",
                        "...........####.",
                        "..#######...##..",
                        ".#########..##..",
                        ".#########..##..",
                        ".#########..##..",
                        ".#########..##..",
                        ".#########..##..",
                        "..#######...##..",
                        "..#######...##..",
                        "...#####...####.",
                        "....###....####.",
                        ".....#..........",
                        "................",
                        "................",
                    };

                // Two-handed mace: a heavy head on a long haft.
                default:
                    return new[]
                    {
                        "................",
                        "....########....",
                        "...##########...",
                        "..############..",
                        "..############..",
                        "...##########...",
                        "....########....",
                        "......####......",
                        "......####......",
                        "......####......",
                        "......####......",
                        "......####......",
                        "......####......",
                        ".....######.....",
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
        /// <summary>
        /// A white rounded rectangle set up for nine-slicing, which is what gives the HUD its
        /// corners.
        ///
        /// Nine-sliced rather than stretched: a HUD panel is anything from a 40-pixel square button
        /// to a full-width bar, and one stretched sprite would give each of them a differently
        /// shaped corner - an oval on the wide ones. The border is set to the corner radius so the
        /// four corners are carried through at their own size and only the flat middle stretches.
        ///
        /// Drawn at four samples per pixel rather than with a one-pixel alpha ramp. A corner this
        /// size is a long shallow curve, and a single-pixel ramp on it reads as a staircase; the
        /// disc above gets away with it because a small circle's edge is steep everywhere.
        /// </summary>
        public static Sprite EnsureRoundedRect(string path, int size = 48, int radius = 14)
        {
            var existing = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            if (existing != null) return existing;

            radius = Mathf.Clamp(radius, 1, size / 2);
            var pixels = new Color32[size * size];

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float covered = 0f;
                    for (int sy = 0; sy < 2; sy++)
                    {
                        for (int sx = 0; sx < 2; sx++)
                        {
                            float px = x + 0.25f + sx * 0.5f;
                            float py = y + 0.25f + sy * 0.5f;
                            if (InsideRoundedRect(px, py, size, radius)) covered += 0.25f;
                        }
                    }

                    pixels[y * size + x] = new Color32(255, 255, 255, (byte)(covered * 255f));
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

            // The border has to go through TextureImporterSettings; TextureImporter has no
            // spriteBorder of its own, and setting it anywhere else is silently dropped - which
            // leaves a sprite that looks right in the inspector and stretches its corners in game.
            var settings = new TextureImporterSettings();
            importer.ReadTextureSettings(settings);
            settings.spriteBorder = new Vector4(radius, radius, radius, radius);
            settings.spriteMeshType = SpriteMeshType.FullRect;
            importer.SetTextureSettings(settings);
            importer.SaveAndReimport();

            return AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }

        private static bool InsideRoundedRect(float x, float y, int size, int radius)
        {
            float near = radius;
            float far = size - radius;

            float cx = x < near ? near : (x > far ? far : x);
            float cy = y < near ? near : (y > far ? far : y);

            // Straight edges and the flat middle are inside by definition; only the four corner
            // squares actually need the distance test.
            if (cx == x && cy == y) return true;
            return (x - cx) * (x - cx) + (y - cy) * (y - cy) <= radius * radius;
        }

        /// <summary>
        /// A splat: one main pool with a ring of smaller drops thrown off it.
        ///
        /// White on transparent, so the material tints it - the same texture serves a fresh mark and
        /// an old dried one. Drawn rather than taken from the effects pack, which has bursts of
        /// particles and nothing that stays on the ground.
        ///
        /// Deliberately off-centre and uneven. A stain is spun to a random angle where it lands, and
        /// a symmetrical blob spun to a random angle is the same blob - the irregularity is the
        /// whole of what makes twenty of them look like twenty rather than one stamped over.
        /// </summary>
        public static Texture2D EnsureBloodStain(string path, int seed = 20260908)
        {
            var existing = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (existing != null) return existing;

            const int size = 128;
            var random = new System.Random(seed);
            var centre = new Vector2(size * 0.5f, size * 0.5f);

            // One broad pool with a few flecks thrown off its edge. The pool has to dominate: with
            // the drops anywhere near its size the whole thing reads as a starburst - a splash
            // frozen in mid-air rather than something that has already soaked into the ground.
            var blobs = new List<Vector3> { new Vector3(centre.x, centre.y, size * 0.34f) };

            // Three lobes close in, which is what makes the pool's edge uneven rather than round.
            for (int i = 0; i < 3; i++)
            {
                float angle = (float)(random.NextDouble() * Mathf.PI * 2f);
                float distance = size * (0.10f + (float)random.NextDouble() * 0.10f);
                blobs.Add(new Vector3(
                    centre.x + Mathf.Cos(angle) * distance,
                    centre.y + Mathf.Sin(angle) * distance,
                    size * (0.14f + (float)random.NextDouble() * 0.08f)));
            }

            // And a handful of small flecks further out.
            for (int i = 0; i < 6; i++)
            {
                float angle = (float)(random.NextDouble() * Mathf.PI * 2f);
                float distance = size * (0.28f + (float)random.NextDouble() * 0.14f);
                blobs.Add(new Vector3(
                    centre.x + Mathf.Cos(angle) * distance,
                    centre.y + Mathf.Sin(angle) * distance,
                    size * (0.020f + (float)random.NextDouble() * 0.035f)));
            }

            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float alpha = 0f;
                    foreach (var blob in blobs)
                    {
                        float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f),
                                                   new Vector2(blob.x, blob.y));

                        // Solid to two thirds of the way out, then falling off to nothing. A hard
                        // edge reads as a sticker and a soft one as smoke; this is neither.
                        float edge = Mathf.InverseLerp(blob.z, blob.z * 0.66f, d);
                        alpha = Mathf.Max(alpha, Mathf.Clamp01(edge));
                    }

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
            importer.textureType = TextureImporterType.Default;
            importer.alphaIsTransparency = true;
            importer.wrapMode = TextureWrapMode.Clamp;   // or the falloff tiles back in at the edges
            importer.filterMode = FilterMode.Bilinear;
            importer.SaveAndReimport();

            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        /// <summary>
        /// A hanging banner: deep red cloth, swallow-tailed at the foot, with a gold wreath on it.
        ///
        /// Both colours are baked in rather than tinted at the material, because there are two of
        /// them and a material tint has one. That also means the banner is the one prop in the arena
        /// whose colour is not a palette entry - it is here, in the drawing.
        ///
        /// The wreath is an open ring with leaf ticks around it. At the size a banner occupies on
        /// screen it is a gold mark on red, and any more detail than that is lost - but a mark that
        /// is roughly a wreath reads as Rome, and a plain gold disc reads as a coin.
        /// </summary>
        public static Texture2D EnsureBanner(string path)
        {
            var existing = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (existing != null) return existing;

            const int width = 128;
            const int height = 256;

            var cloth = new Color(0.62f, 0.13f, 0.11f);
            var clothEdge = new Color(0.44f, 0.09f, 0.08f);
            var gold = new Color(0.85f, 0.66f, 0.28f);

            // The wreath sits in the upper half, where the eye lands, not in the middle of a strip
            // whose lower half is mostly hidden behind the parapet at this camera angle.
            var wreath = new Vector2(width * 0.5f, height * 0.62f);
            const float wreathRadius = 30f;
            const float wreathThickness = 4.5f;

            var pixels = new Color32[width * height];
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    // Swallow tail: a notch cut up from the bottom edge, deepest in the middle.
                    float notch = (1f - Mathf.Abs(x / (width - 1f) * 2f - 1f)) * height * 0.13f;
                    if (y < notch)
                    {
                        pixels[y * width + x] = new Color32(0, 0, 0, 0);
                        continue;
                    }

                    // Shaded towards both long edges so the strip reads as cloth with a fold in it
                    // rather than as a flat rectangle.
                    float acrossEdge = Mathf.Abs(x / (width - 1f) * 2f - 1f);
                    var color = Color.Lerp(cloth, clothEdge, acrossEdge * acrossEdge);

                    var offset = new Vector2(x + 0.5f, y + 0.5f) - wreath;
                    float d = offset.magnitude;

                    // The ring itself, open at the top the way a laurel wreath is.
                    float angle = Mathf.Atan2(offset.y, offset.x) * Mathf.Rad2Deg;
                    bool inGap = angle > 62f && angle < 118f;
                    if (!inGap && Mathf.Abs(d - wreathRadius) < wreathThickness) color = gold;

                    // Leaf ticks, standing out from the ring.
                    if (!inGap && d > wreathRadius && d < wreathRadius + 9f)
                    {
                        float around = Mathf.Repeat(angle, 22f);
                        if (around < 9f) color = gold;
                    }

                    pixels[y * width + x] = color;
                }
            }

            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
            texture.SetPixels32(pixels);
            texture.Apply();

            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllBytes(path, texture.EncodeToPNG());
            Object.DestroyImmediate(texture);

            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);

            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = TextureImporterType.Default;
            importer.alphaIsTransparency = true;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.filterMode = FilterMode.Bilinear;
            importer.SaveAndReimport();

            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        /// <summary>
        /// The band a planned run is drawn as: a translucent lane with dashed rails down both sides
        /// and a chevron pointing along it.
        ///
        /// Drawn as one tile that repeats along the line, so U runs the length of the run and V runs
        /// across it. Everything about the shape is in the texture rather than in geometry, which is
        /// what lets a single LineRenderer carry it round a bounce off the wall - the tiling follows
        /// distance travelled, so the chevrons stay evenly spaced through a corner the preview's own
        /// points are not evenly spaced around.
        /// </summary>
        public static Texture2D EnsureTrajectoryBand(string path)
        {
            var existing = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (existing != null) return existing;

            const int along = 128;
            const int across = 64;

            // How much of the tile's length the dash occupies; the rest is the gap between dashes.
            const float dashShare = 0.62f;

            const float railThickness = 0.10f;   // of the width, at each edge
            const float bodyAlpha = 0.24f;
            const float railAlpha = 0.95f;
            const float chevronAlpha = 0.5f;

            var pixels = new Color32[along * across];
            for (int u = 0; u < along; u++)
            {
                float t = u / (along - 1f);        // along the run
                bool inDash = t < dashShare;

                for (int v = 0; v < across; v++)
                {
                    float w = v / (across - 1f);   // across the run, 0 and 1 at the rails
                    float fromEdge = Mathf.Min(w, 1f - w);

                    float alpha = bodyAlpha;

                    // Rails: dashed, so the lane reads as a route rather than as a painted road.
                    if (fromEdge < railThickness && inDash) alpha = railAlpha;

                    // A chevron, its point towards the far end. Built as the distance from a V
                    // shape: |w - 0.5| gives the sideways distance from the middle, and the tip
                    // leads the sides by that much.
                    float sideways = Mathf.Abs(w - 0.5f) * 2f;
                    float chevronTip = 0.72f - sideways * 0.34f;
                    if (Mathf.Abs(t - chevronTip) < 0.055f && sideways < 0.82f)
                        alpha = Mathf.Max(alpha, chevronAlpha);

                    pixels[v * along + u] = new Color32(255, 255, 255, (byte)(alpha * 255f));
                }
            }

            var texture = new Texture2D(along, across, TextureFormat.RGBA32, false);
            texture.SetPixels32(pixels);
            texture.Apply();

            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllBytes(path, texture.EncodeToPNG());
            Object.DestroyImmediate(texture);

            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);

            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = TextureImporterType.Default;
            importer.alphaIsTransparency = true;

            // Repeat along the run and clamp across it: the line tiles in U, and a wrap in V would
            // bleed the rail on one edge into the rail on the other.
            importer.wrapModeU = TextureWrapMode.Repeat;
            importer.wrapModeV = TextureWrapMode.Clamp;
            importer.filterMode = FilterMode.Bilinear;
            importer.mipmapEnabled = false;
            importer.SaveAndReimport();

            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        /// <summary>
        /// The head on the end of the planned run: a broad triangle, point forward, drawn on its
        /// own quad because a LineRenderer has one width and an arrow has two.
        /// </summary>
        public static Texture2D EnsureArrowHead(string path)
        {
            var existing = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (existing != null) return existing;

            const int size = 128;
            var pixels = new Color32[size * size];

            for (int y = 0; y < size; y++)
            {
                // The head points towards +V, which is where the quad's own up ends up once it is
                // laid on the ground and turned to the run's direction.
                float t = y / (size - 1f);
                float halfWidth = Mathf.Lerp(0.5f, 0.02f, t);

                for (int x = 0; x < size; x++)
                {
                    float sideways = Mathf.Abs(x / (size - 1f) - 0.5f);

                    // Antialiased over a couple of pixels, or the sloping edges of a triangle this
                    // size read as a staircase.
                    float alpha = Mathf.Clamp01((halfWidth - sideways) * size / 2.5f);
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
            importer.textureType = TextureImporterType.Default;
            importer.alphaIsTransparency = true;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.filterMode = FilterMode.Bilinear;
            importer.mipmapEnabled = false;
            importer.SaveAndReimport();

            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

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
