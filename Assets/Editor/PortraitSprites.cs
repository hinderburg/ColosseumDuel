using System.Collections.Generic;
using System.IO;
using ColosseumDuel.Core;
using ColosseumDuel.Gameplay.View;
using UnityEditor;
using UnityEngine;

namespace ColosseumDuel.EditorTools
{
    /// <summary>
    /// Cuts the gladiator portraits out of the sheet they arrived on, one sprite each, and reads the
    /// colour each of them is painted on. PortraitSheet decides where the cells are and whose each
    /// one is; this only moves pixels between files.
    ///
    /// The sheet is optional. Without it every card keeps its icon-pack glyph, tinted with the
    /// colour the portrait was described as having - so the colours are right before the art is in.
    /// </summary>
    public static class PortraitSprites
    {
        public const string SheetPath = "Assets/Art/GladiatorPortraits.png";
        private const string OutputDir = "Assets/Textures";

        /// <summary>Largest a cut portrait is kept at. Cards draw them at under a hundred pixels.</summary>
        private const int MaxPortraitSize = 256;

        public sealed class Portrait
        {
            public Sprite Sprite;
            public Color Background;
        }

        /// <summary>Every gladiator's portrait and colour, or null when there is no sheet to cut them from.</summary>
        public static Dictionary<GladiatorId, Portrait> Ensure()
        {
            var importer = AssetImporter.GetAtPath(SheetPath) as TextureImporter;
            if (importer == null) return null;

            // Read back pixel for pixel: readable, full size, and not compressed on the way in -
            // block compression averages the very corners the colour is read from.
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

            var cells = PortraitSheet.Slice(sheet.GetPixels32(), sheet.width, sheet.height);
            var portraits = new Dictionary<GladiatorId, Portrait>();
            foreach (var cell in cells)
            {
                string path = $"{OutputDir}/Portrait_{cell.Id}.png";
                var inside = new Vector2Int(cell.Frame.x + cell.Frame.width / 2 - cell.Rect.x,
                                            cell.Frame.y + cell.Frame.height / 2 - cell.Rect.y);
                WriteCrop(sheet, cell.Rect, inside, path);
                portraits[cell.Id] = new Portrait { Sprite = ImportSprite(path), Background = cell.Background };
                Debug.Log($"[Colosseum] Portrait for {cell.Id} cut from {cell.Rect}, painted on {cell.Background}.");
            }
            return portraits;
        }

        /// <summary>
        /// Writes one cell out as its own PNG - only when it has changed, so running the bootstrap
        /// again does not touch a file nobody edited.
        /// </summary>
        private static void WriteCrop(Texture2D sheet, RectInt rect, Vector2Int inside, string path)
        {
            var crop = new Texture2D(rect.width, rect.height, TextureFormat.RGBA32, false);
            try
            {
                // The sheet's white ground round the frame comes out transparent, so on the dark HUD
                // the avatar is its framed portrait and not a white square with one inside it.
                var source = sheet.GetPixels(rect.x, rect.y, rect.width, rect.height);
                var pixels = new Color32[source.Length];
                for (int i = 0; i < source.Length; i++) pixels[i] = source[i];
                PortraitSheet.CutOut(pixels, rect.width, rect.height, inside);

                crop.SetPixels32(pixels);
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
                || importer.maxTextureSize != MaxPortraitSize)
            {
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.alphaIsTransparency = true;
                importer.mipmapEnabled = false;
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.maxTextureSize = MaxPortraitSize;
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
