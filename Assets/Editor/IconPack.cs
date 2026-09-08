using ColosseumDuel.Core;
using UnityEditor;
using UnityEngine;

namespace ColosseumDuel.EditorTools
{
    /// <summary>
    /// The interface icons, taken out of the imported icon atlas.
    ///
    /// The bright set, not the dark one: every icon in this game is tinted where it is used - the
    /// archetype's own colour on a roster card, green on a weapon badge - and tint multiplies, so a
    /// black glyph stays black whatever colour it is given.
    ///
    /// Each lookup is by name and each caller keeps its own fallback to the drawn-in-code icons, so
    /// a project without the pack still shows something rather than three blank squares. The pack is
    /// Asset Store content and is not in the repository; see PROJECT_CONTEXT.md.
    /// </summary>
    public static class IconPack
    {
        private const string AtlasPath =
            "Assets/Modern GDR - Free icons pack/00_Atlas/BrightIcons.png";

        /// <summary>
        /// What each archetype is, in one glyph.
        ///
        /// Read off the stat that makes each of them worth taking rather than off what they look
        /// like, because that is the question the card is being scanned to answer: Brutius is the
        /// one who does not die, Barbarius the one who hits, Hilius the one who is already
        /// somewhere else.
        /// </summary>
        public static string ForArchetype(GladiatorId id)
        {
            switch (id)
            {
                case GladiatorId.Brutius: return "Heart02_Bright";     // 200 HP, twice anyone else
                case GladiatorId.Barbarius: return "Thunder_Bright";   // 13 damage, the hardest hitter
                default: return "Skip_Bright";                         // 20 speed, and the chevrons he already wore
            }
        }

        /// <summary>
        /// The weapon badges. A blade and a shield are in the pack as themselves; there is no mace
        /// in it, so the hammer takes the shattering-impact glyph - which is what it does.
        /// </summary>
        public static string ForWeapon(WeaponKind kind)
        {
            switch (kind)
            {
                case WeaponKind.DualSwords: return "Sword_Bright";
                case WeaponKind.SwordAndShield: return "Shield_Bright";
                case WeaponKind.TwoHandedMace: return "ThunderStrike_Bright";
                default: return null;
            }
        }

        /// <summary>
        /// One named sprite out of the atlas, or null if the pack is not imported.
        ///
        /// Through the sub-assets rather than LoadAssetAtPath, which on a sliced sheet hands back
        /// the texture and none of the sprites cut out of it.
        /// </summary>
        public static Sprite Sprite(string spriteName)
        {
            if (string.IsNullOrEmpty(spriteName)) return null;

            foreach (var asset in AssetDatabase.LoadAllAssetRepresentationsAtPath(AtlasPath))
            {
                var sprite = asset as Sprite;
                if (sprite != null && sprite.name == spriteName) return sprite;
            }

            Debug.LogWarning($"[Colosseum] Icon '{spriteName}' not found in {AtlasPath} - " +
                             "falling back to the drawn-in-code icon. Import the icon pack to get it.");
            return null;
        }
    }
}
