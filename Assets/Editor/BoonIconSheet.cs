using System.Collections.Generic;
using System.IO;
using ColosseumDuel.Core;
using UnityEditor;
using UnityEngine;

namespace ColosseumDuel.EditorTools
{
    /// <summary>
    /// Cuts the boon pictures out of the card sheet they arrived on: two rows of four cards, in
    /// BoonDef.All's order, each card a painting over a name plate and a line of numbers. Only the
    /// painting is taken - the game writes its own name and numbers, which change more often than
    /// the art does.
    ///
    /// Cut by a grid measured off the sheet, the way the ability icons are (see AbilityIconSheet):
    /// the cards are not quite the same width, so each column is measured on its own, and the grid
    /// is kept as shares of the sheet's size so the same sheet at another resolution cuts the same.
    /// A sheet laid out differently needs the grid measured again.
    ///
    /// The sheet is optional: without it the cards keep their name and text and no picture.
    /// </summary>
    public static class BoonIconSheet
    {
        public const string SheetPath = "Assets/Art/BoonCards.png";
        private const string OutputDir = "Assets/Textures";

        /// <summary>Largest a cut picture is kept at, on its long side. The cards draw them at a third of that.</summary>
        private const int MaxIconSize = 256;

        // Measured on the 1536 x 1024 sheet: where each card's frame starts and ends across, how far
        // in from the frame the painting is, and where each row's painting starts and ends down.
        private const float SheetWidth = 1536f, SheetHeight = 1024f;
        private static readonly float[] ColumnLefts = { 21f, 397f, 776f, 1156f };
        private static readonly float[] ColumnRights = { 363f, 758f, 1139f, 1516f };
        private const float Inset = 14f;
        private static readonly float[] RowTops = { 28f, 524f };
        private static readonly float[] RowBottoms = { 234f, 726f };

        /// <summary>Every boon's picture, or null when there is no sheet to cut them from.</summary>
        public static Dictionary<BoonKey, Sprite> Ensure()
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
            var icons = new Dictionary<BoonKey, Sprite>();
            for (int i = 0; i < BoonDef.All.Count; i++)
            {
                int row = i / ColumnLefts.Length, column = i % ColumnLefts.Length;
                if (row >= RowTops.Length) break;

                // Texture rows count up from the bottom; the measurements count down from the top.
                int x = Mathf.RoundToInt((ColumnLefts[column] + Inset) * sx);
                int width = Mathf.RoundToInt((ColumnRights[column] - ColumnLefts[column] - 2f * Inset) * sx);
                int height = Mathf.RoundToInt((RowBottoms[row] - RowTops[row]) * sy);
                int y = sheet.height - Mathf.RoundToInt(RowBottoms[row] * sy);

                var key = BoonDef.All[i].Key;
                string path = $"{OutputDir}/Boon_{key}.png";
                WriteCrop(sheet, new RectInt(x, y, width, height), path);
                icons[key] = ImportSprite(path);
            }
            return icons;
        }

        /// <summary>Writes one picture out as its own PNG - only when it has changed.</summary>
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
