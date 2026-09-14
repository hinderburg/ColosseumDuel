using System.Collections.Generic;
using System.IO;
using ColosseumDuel.Core;
using UnityEditor;
using UnityEngine;

namespace ColosseumDuel.EditorTools
{
    /// <summary>
    /// Cuts the ability icons out of the sheet they arrived on: six rows, one per archetype in
    /// GladiatorDef.All's order, each with his three abilities in the order he offers them.
    ///
    /// Cut by a grid measured off the sheet rather than found by looking for tiles. The icons are
    /// painted art - several are mostly dark, and a detector that looked for bright tiles on the
    /// dark rows broke those in half - and this is one sheet, laid out once. The grid is kept as
    /// shares of the sheet's size, so the same sheet exported larger or smaller still cuts right;
    /// a sheet laid out differently needs the grid measured again.
    ///
    /// The sheet is optional: without it the ability button keeps its icon-pack glyph.
    /// </summary>
    public static class AbilityIconSheet
    {
        public const string SheetPath = "Assets/Art/AbilityIcons.png";
        private const string OutputDir = "Assets/Textures";

        /// <summary>Largest a cut icon is kept at. The button and the cards draw them under a hundred pixels.</summary>
        private const int MaxIconSize = 256;

        // Measured on the 1536 x 1024 sheet: where each tile's frame starts, and how big a tile is.
        private const float SheetWidth = 1536f, SheetHeight = 1024f;
        private static readonly float[] RowTops = { 14f, 178f, 342f, 506f, 671f, 838f };
        private static readonly float[] ColumnLefts = { 410f, 799f, 1172f };
        private const float TileWidth = 139f, TileHeight = 137f;

        /// <summary>Every ability's icon, or null when there is no sheet to cut them from.</summary>
        public static Dictionary<AbilityKey, Sprite> Ensure()
        {
            var importer = AssetImporter.GetAtPath(SheetPath) as TextureImporter;
            if (importer == null) return null;

            // Read back pixel for pixel: readable, full size, and not compressed on the way in.
            if (!importer.isReadable || importer.textureCompression != TextureImporterCompression.Uncompressed
                || importer.npotScale != TextureImporterNPOTScale.None || importer.mipmapEnabled)
            {
                importer.isReadable = true;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.npotScale = TextureImporterNPOTScale.None;
                importer.mipmapEnabled = false;
                importer.maxTextureSize = 8192;
                importer.SaveAndReimport();
            }

            var sheet = AssetDatabase.LoadAssetAtPath<Texture2D>(SheetPath);
            if (sheet == null) return null;

            float sx = sheet.width / SheetWidth, sy = sheet.height / SheetHeight;
            var icons = new Dictionary<AbilityKey, Sprite>();
            for (int row = 0; row < GladiatorDef.All.Count && row < RowTops.Length; row++)
            {
                var def = GladiatorDef.All[row];
                for (int column = 0; column < def.Abilities.Count && column < ColumnLefts.Length; column++)
                {
                    // Texture rows count up from the bottom; the measurements count down from the top.
                    int width = Mathf.RoundToInt(TileWidth * sx), height = Mathf.RoundToInt(TileHeight * sy);
                    int x = Mathf.RoundToInt(ColumnLefts[column] * sx);
                    int y = sheet.height - Mathf.RoundToInt(RowTops[row] * sy) - height;

                    var key = def.Abilities[column];
                    string path = $"{OutputDir}/Ability_{key}.png";
                    WriteCrop(sheet, new RectInt(x, y, width, height), path);
                    icons[key] = ImportSprite(path);
                }
            }
            return icons;
        }

        /// <summary>Writes one tile out as its own PNG - only when it has changed.</summary>
        private static void WriteCrop(Texture2D sheet, RectInt rect, string path)
        {
            var crop = new Texture2D(rect.width, rect.height, TextureFormat.RGBA32, false);
            try
            {
                crop.SetPixels(sheet.GetPixels(rect.x, rect.y, rect.width, rect.height));
                crop.Apply();
                var bytes = crop.EncodeToPNG();
                if (File.Exists(path) && ByteEquals(File.ReadAllBytes(path), bytes)) return;
                File.WriteAllBytes(path, bytes);
                AssetDatabase.ImportAsset(path);
            }
            finally
            {
                Object.DestroyImmediate(crop);
            }
        }

        private static Sprite ImportSprite(string path)
        {
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            if (importer.textureType != TextureImporterType.Sprite || importer.mipmapEnabled
                || importer.maxTextureSize != MaxIconSize)
            {
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.alphaIsTransparency = true;
                importer.mipmapEnabled = false;
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.maxTextureSize = MaxIconSize;
                importer.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }

        private static bool ByteEquals(byte[] a, byte[] b)
        {
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++)
                if (a[i] != b[i]) return false;
            return true;
        }
    }
}
