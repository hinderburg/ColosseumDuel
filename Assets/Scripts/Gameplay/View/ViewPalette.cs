using UnityEngine;

namespace ColosseumDuel.Gameplay.View
{
    /// <summary>
    /// Every material the presentation layer uses, in one asset.
    ///
    /// Views are built from primitives at runtime rather than from prefabs, which means nothing in
    /// a scene would reference their shaders - and Unity strips shaders that no scene or Resources
    /// asset points at, so a WebGL build would come out with pink gladiators. Routing the materials
    /// through this asset (referenced by GameController, which lives in the scene) keeps that chain
    /// intact. ProjectBootstrap creates and fills it.
    /// </summary>
    [CreateAssetMenu(fileName = "ViewPalette", menuName = "Colosseum/View Palette")]
    public sealed class ViewPalette : ScriptableObject
    {
        [Header("Gladiators")]
        public Material PlayerBody;
        public Material BotBody;
        public Material PlayerHelmet;   // blue - the player, per the design doc
        public Material BotHelmet;      // red - the opponent

        [Header("Items")]
        public Material Weapon;
        public Material Shield;
        public Material RandomItem;

        [Header("Bars")]
        public Material BarBackground;
        public Material BarHp;
        public Material BarRage;

        [Header("Input")]
        /// <summary>Dashed white line: where the gladiator will run if released now.</summary>
        public Material Trajectory;

        /// <summary>Solid white line: the pull itself, from the gladiator back to the pointer.</summary>
        public Material PullLine;

        /// <summary>The red wedge on the sand: the ground his weapon covers, and so who he can hit.</summary>
        public Material StrikeZone;

        /// <summary>
        /// The pillar the columns on the sand are built from. Low Poly Trim Sheet, so absent in a
        /// clean clone - ArenaView falls back to a stone cylinder, which is the same obstacle to the
        /// simulation and only less handsome.
        /// </summary>
        [Header("Obstacles")]
        public GameObject ColumnModel;

        /// <summary>Stone for the fallback column, when there is no pillar model to build it from.</summary>
        public Material ColumnStone;

        /// <summary>Planked wood for the crates. Drawn in code, so it is always there.</summary>
        public Material CrateWood;

        /// <summary>
        /// Transparent unlit material for the expanding impact/ability rings. Shared, with per-ring
        /// alpha driven through a MaterialPropertyBlock.
        /// </summary>
        [Header("Effects")]
        public Material Burst;

        /// <summary>
        /// Font for every HUD label. Must be a real asset, not Unity's built-in font: the built-in
        /// one carries no Cyrillic glyphs, so in a WebGL build (where there are no OS fonts to fall
        /// back on) every Russian caption renders as nothing at all.
        /// </summary>
        [Header("HUD")]
        public Font HudFont;

        /// <summary>Marker drawn over a gladiator who is out of the match.</summary>
        public Sprite Skull;

        /// <summary>Filled circle - backing for the round action buttons.</summary>
        public Sprite Disc;

        /// <summary>Ring - the radial rage gauge around the ability button.</summary>
        public Sprite Ring;

        /// <summary>
        /// The mark a blow leaves on the sand. One splat texture on a transparent material; each
        /// stain is the same quad turned and sized differently where it lands.
        /// </summary>
        public Material BloodStain;

        /// <summary>
        /// The red banner hung on the arena wall. Its colours are in its texture rather than in this
        /// material's tint - there are two of them, cloth and gold, and a tint has one.
        /// </summary>
        public Material Banner;

        /// <summary>The head on the end of the planned run. Its own quad; see PlayerInputController.</summary>
        public Material TrajectoryHead;

        /// <summary>
        /// Rounded rectangle, nine-sliced: the shape every panel, button and bar in the HUD is cut
        /// to. One sprite for all of them rather than a radius per widget, so the corners match
        /// across the whole interface without anything having to agree on a number.
        ///
        /// Null is survivable - a widget without it falls back to a square quad, which is what the
        /// HUD was before - so a palette from an older bootstrap run still renders.
        /// </summary>
        public Sprite RoundedPanel;

        /// <summary>
        /// One silhouette per archetype, indexed by GladiatorId. Left white so the UI can tint each
        /// with its archetype colour - the same one the body carries on the arena, so the card and
        /// the fighter read as the same character.
        /// </summary>
        public Sprite[] ArchetypeIcons;

        /// <summary>
        /// One badge per weapon kind, in WeaponDef.All order. Drawn white so the UI can paint it -
        /// the roster cards want it green, as the mark of what a gladiator is trained in.
        /// </summary>
        public Sprite[] WeaponIcons;

        public Sprite IconFor(ColosseumDuel.Core.WeaponKind kind)
        {
            if (WeaponIcons == null) return null;
            for (int i = 0; i < ColosseumDuel.Core.WeaponDef.All.Count && i < WeaponIcons.Length; i++)
                if (ColosseumDuel.Core.WeaponDef.All[i].Kind == kind) return WeaponIcons[i];
            return null;
        }

        public Sprite IconFor(ColosseumDuel.Core.GladiatorId id)
        {
            int index = (int)id;
            return ArchetypeIcons != null && index >= 0 && index < ArchetypeIcons.Length
                ? ArchetypeIcons[index]
                : null;
        }

        /// <summary>
        /// Flame played beside the gladiator while the ability is charged and ready.
        /// Comes from an Asset Store pack that is not in the repository, so a clean clone will find
        /// this null - everything else has to keep working without it.
        /// </summary>
        public GameObject AbilityReadyFire;

        /// <summary>Flame set along the arena wall. Same caveat: absent in a clean clone.</summary>
        public GameObject Torch;

        /// <summary>Iron for the jaws of the traps.</summary>
        public Material TrapIron;

        /// <summary>Burst played on a gladiator taking a hit. Same caveat.</summary>
        public GameObject BloodHit;

        /// <summary>
        /// Pieces of the modular stone kit the arena is dressed with. All four are null in a clean
        /// clone, and ArenaView falls back to painted blocks with no gallery.
        /// </summary>
        [Header("Arena decor")]
        public GameObject WallBlock;
        public GameObject WallPost;
        public GameObject GallerySlab;
        public GameObject GalleryRail;

        /// <summary>Stone the fallback wall is painted with, when there is no kit to build it from.</summary>
        public Material WallStone;

        /// <summary>
        /// The gear models, used both for the pickups lying on the sand and for what a gladiator is
        /// carrying or wearing. Built by GearPrefabs from the imported weapon pack, and normalised
        /// there: each is centred on its own geometry and one unit on its longest side, so the views
        /// can ask for a size in world units instead of carrying a measurement per model.
        ///
        /// A one-handed and a two-handed weapon are separate models rather than one at two scales -
        /// the silhouettes differ as well as the size, which is what the player is reading when they
        /// decide whether running at it will cost them their shield.
        ///
        /// Null in a clean clone, where ItemView falls back to primitives.
        /// </summary>
        [Header("Gear")]
        public GameObject SwordModel;
        public GameObject MaceModel;
        public GameObject ShieldModel;

        /// <summary>Worn on the head, tinted with the owning side's colour.</summary>
        public GameObject HelmetModel;

        /// <summary>
        /// Flat red, drawn inside-out, for the shell around a weapon its holder was never trained
        /// in. Front faces are culled, so what is left is the far side of a slightly larger copy -
        /// which from any angle is a margin around the silhouette.
        /// </summary>
        public Material GearUntrained;

        /// <summary>
        /// Screen-edge tint for the planning phase, transparent in the middle. Generated, so it is
        /// always present.
        /// </summary>
        public Sprite Vignette;

        /// <summary>
        /// One figure per archetype, indexed by GladiatorId. Built from the imported humanoid; a
        /// clean clone without the model pack gets nulls and falls back to primitive bodies.
        /// </summary>
        [Header("Gladiator figures")]
        public GameObject[] GladiatorFigures;

        /// <summary>One opaque body material per archetype, indexed by GladiatorId.</summary>
        public Material[] ArchetypeBodies;

        public Material BodyMaterialFor(ColosseumDuel.Core.GladiatorId id)
        {
            int index = (int)id;
            return ArchetypeBodies != null && index >= 0 && index < ArchetypeBodies.Length
                ? ArchetypeBodies[index]
                : null;
        }

        public GameObject FigureFor(ColosseumDuel.Core.GladiatorId id)
        {
            int index = (int)id;
            return GladiatorFigures != null && index >= 0 && index < GladiatorFigures.Length
                ? GladiatorFigures[index]
                : null;
        }

        /// <summary>Body tint that identifies the archetype, regardless of which side owns it.</summary>
        public Color ArchetypeColor(ColosseumDuel.Core.GladiatorId id)
        {
            switch (id)
            {
                case ColosseumDuel.Core.GladiatorId.Brutius: return new Color(0.85f, 0.62f, 0.30f);
                case ColosseumDuel.Core.GladiatorId.Barbarius: return new Color(0.62f, 0.40f, 0.80f);
                case ColosseumDuel.Core.GladiatorId.Hilius: return new Color(0.40f, 0.78f, 0.48f);
                default: return Color.white;
            }
        }

        /// <summary>
        /// Unity's built-in primitive meshes, referenced as assets rather than fetched at runtime.
        ///
        /// GameObject.CreatePrimitive would be the obvious way to build these views, but it always
        /// attaches a collider, and engine-code stripping removes collider classes that nothing in
        /// the scene references - so a WebGL build spat "Can't add component because class
        /// 'MeshCollider' doesn't exist" for every single view. The simulation does its own collision
        /// maths anyway, so the right fix is to never ask for a collider.
        /// </summary>
        [Header("Primitive meshes")]
        public Mesh Cube;
        public Mesh Sphere;
        public Mesh Capsule;
        public Mesh Cylinder;
        public Mesh Quad;

        public Mesh MeshFor(PrimitiveType type)
        {
            switch (type)
            {
                case PrimitiveType.Cube: return Cube;
                case PrimitiveType.Sphere: return Sphere;
                case PrimitiveType.Capsule: return Capsule;
                case PrimitiveType.Cylinder: return Cylinder;
                case PrimitiveType.Quad: return Quad;
                default: return Cube;
            }
        }
    }
}
