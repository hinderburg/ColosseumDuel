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
        public Material Body;
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
        public Material Trajectory;

        /// <summary>
        /// Font for every HUD label. Must be a real asset, not Unity's built-in font: the built-in
        /// one carries no Cyrillic glyphs, so in a WebGL build (where there are no OS fonts to fall
        /// back on) every Russian caption renders as nothing at all.
        /// </summary>
        [Header("HUD")]
        public Font HudFont;

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
