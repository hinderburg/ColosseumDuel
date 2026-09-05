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

        [Header("Arena hazard")]
        public Material HazardActive;
        public Material HazardTelegraph;

        [Header("Bars")]
        public Material BarBackground;
        public Material BarHp;
        public Material BarRage;

        [Header("Input")]
        /// <summary>Dashed white line: where the gladiator will run if released now.</summary>
        public Material Trajectory;

        /// <summary>Solid white line: the pull itself, from the gladiator back to the pointer.</summary>
        public Material PullLine;

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
        /// Flame played beside the gladiator while the ability is charged and ready.
        /// Comes from an Asset Store pack that is not in the repository, so a clean clone will find
        /// this null - everything else has to keep working without it.
        /// </summary>
        public GameObject AbilityReadyFire;

        /// <summary>Flame set along the arena wall. Same caveat: absent in a clean clone.</summary>
        public GameObject Torch;

        /// <summary>Ground flame marking the edge of the danger zone. Same caveat.</summary>
        public GameObject HazardFire;

        /// <summary>Burst played on a gladiator taking a hit. Same caveat.</summary>
        public GameObject BloodHit;

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
