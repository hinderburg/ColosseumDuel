using ColosseumDuel.Core;
using UnityEngine;

namespace ColosseumDuel.Gameplay.View
{
    /// <summary>
    /// How big every piece of gear is, in world units, and which way round the models are.
    ///
    /// One place for both, because the same weapon is drawn twice - lying on the sand and in a
    /// gladiator's fist - and the two used to be sized by unrelated numbers. A weapon that shrinks
    /// when it is picked up is a weapon the player cannot recognise as the one they ran at.
    ///
    /// The lengths are set against the 3.75-unit figure the gladiator prefabs are built to: a
    /// shortsword at four tenths of a man and a two-handed hammer at six, which is about life.
    ///
    /// GearPrefabs guarantees every model here is centred on its own pivot and exactly one unit on
    /// its longest side, so a length below is the length that ends up on screen. Nothing here has to
    /// know how big the artist made anything.
    /// </summary>
    public static class GearSizes
    {
        // Grown with the man (GameConstants.GladiatorScale). Gear is given an absolute world size -
        // SetWorldSize divides out whatever the hand bone inherits - so a bigger figure does not make
        // bigger gear on its own. Left alone, a fifth-larger gladiator would be holding a sword a
        // fifth too small for him, and the weapon reaches, which scale with him, would outrun it.
        public const float SwordLength = 1.5f * GameConstants.GladiatorScale;
        public const float MaceLength = 2.3f * GameConstants.GladiatorScale;
        public const float ShieldHeight = 1.2f * GameConstants.GladiatorScale;
        public const float HelmetHeight = 0.62f * GameConstants.GladiatorScale;

        public const float SpearLength = 2.7f * GameConstants.GladiatorScale;
        public const float TridentLength = 2.5f * GameConstants.GladiatorScale;
        public const float ScutumHeight = 1.55f * GameConstants.GladiatorScale;
        public const float RoundShieldHeight = 1.0f * GameConstants.GladiatorScale;

        /// <summary>What goes in the weapon hand, and how long it is.</summary>
        public static float MainHandLength(WeaponKind kind)
        {
            switch (kind)
            {
                case WeaponKind.TwoHandedMace: return MaceLength;
                case WeaponKind.SpearAndShield: return SpearLength;
                case WeaponKind.Trident: return TridentLength;
                default: return SwordLength;
            }
        }

        /// <summary>What the off hand carries, and how big: a second sword, or one of three shields.</summary>
        public static float OffHandSize(WeaponKind kind)
        {
            switch (kind)
            {
                case WeaponKind.SwordAndShield: return ShieldHeight;
                case WeaponKind.ScutumAndGladius: return ScutumHeight;
                case WeaponKind.SpearAndShield: return RoundShieldHeight;
                default: return SwordLength;
            }
        }

        public static bool UsesMace(WeaponKind kind) => kind == WeaponKind.TwoHandedMace;

        /// <summary>Held in both fists, and so swung with the two-handed clips.</summary>
        public static bool TwoHanded(WeaponKind kind) => kind == WeaponKind.TwoHandedMace || kind == WeaponKind.Trident;

        /// <summary>
        /// How far along its own length a weapon is pushed so the fist closes on the grip.
        ///
        /// The models are centred on their geometry, so parenting one straight to a hand puts the
        /// middle of it in the fist and half of it out through the wrist. Which way to push depends
        /// on the model: the pack builds a blade running up from its grip, and the hammer the other
        /// way up. One sign for both left the gladiator holding the mace by its head with the haft
        /// trailing back over his shoulder.
        /// </summary>
        public static float GripAlong(WeaponKind kind)
        {
            switch (kind)
            {
                case WeaponKind.TwoHandedMace: return GripFromEnd;
                // Polearms are held nearer the butt than a sword is by its hilt, so the point is
                // well out in front of him.
                case WeaponKind.SpearAndShield:
                case WeaponKind.Trident: return -PolearmGripFromEnd;
                default: return -GripFromEnd;
            }
        }

        /// <summary>How far from the middle a polearm is held, as a share of its length.</summary>
        public const float PolearmGripFromEnd = 0.30f;

        /// <summary>How far from the middle the fist sits, as a share of the weapon's length.</summary>
        public const float GripFromEnd = 0.34f;

        /// <summary>
        /// Cool steel for what a gladiator brought with him, gold for the better copies on the sand.
        ///
        /// Tinted through a property block rather than built as two sets of materials: the models
        /// keep their own texture, only the wash over it changes, and a shield and a blade can be
        /// gilded by the same one line without either losing its own face.
        /// </summary>
        public static readonly Color CarriedTint = new Color(0.82f, 0.84f, 0.90f);

        public static readonly Color GildedTint = new Color(1.00f, 0.78f, 0.22f);

        /// <summary>
        /// How much bigger the red shell around an untrained weapon is drawn than the weapon.
        ///
        /// Wildly uneven on purpose. A blade is two hundredths of a unit thick against a whole unit
        /// of length, so a uniform swell big enough to see across the blade would add half a sword
        /// to its point. Each axis is pushed out by roughly the same absolute amount instead, which
        /// is what an outline is: a constant margin, not a constant ratio.
        /// </summary>
        public static readonly Vector3 UntrainedShell = new Vector3(4.5f, 1.05f, 1.5f);

        /// <summary>
        /// How a weapon or a shield lies on the sand: broad side up, long axis down the arena.
        ///
        /// Every model in the pack is built the same way - length along its own +Y, and the flat of
        /// it facing its own +X - so one rotation serves both. It maps the model's X to world up,
        /// which is what puts the blade's face under a camera looking down; tipping it over about X
        /// instead would stand a two-hundredth-of-a-unit edge towards the camera and draw a scratch
        /// on the sand.
        /// </summary>
        public static readonly Quaternion LyingDown =
            Quaternion.LookRotation(Vector3.right, Vector3.forward);

        /// <summary>
        /// How far to either side the two halves of a paired weapon are drawn, as a share of the
        /// main length. Twin swords lying on the sand are two swords, and stacked exactly they read
        /// as one.
        /// </summary>
        public const float PairSpread = 0.28f;

        /// <summary>
        /// How much bigger the gold glow round a blessed weapon is drawn than the weapon. Wider than
        /// the red outline of an untrained one, for the same reason that one is uneven: a blade is a
        /// sliver of a unit thick, so the glow is pushed out by about the same absolute amount on
        /// every axis rather than by one ratio.
        /// </summary>
        public static readonly Vector3 GlowShell = new Vector3(5f, 1.07f, 1.65f);

        /// <summary>
        /// The black outline round the blessing lying on the sand: gold on sand is gold on yellow, and
        /// without an edge it does not read from across the arena. Tighter than the glow, so the glow
        /// shows round it.
        /// </summary>
        public static readonly Vector3 OutlineShell = new Vector3(3.2f, 1.045f, 1.35f);

        /// <summary>Name of the inside-out copy that draws the untrained-weapon outline.</summary>
        public const string ShellName = "UntrainedShell";

        /// <summary>Name of the copy that draws the blessing's glow round a weapon.</summary>
        public const string GlowShellName = "GlowShell";

        /// <summary>Name of the copy that draws the black outline round the blessing on the sand.</summary>
        public const string OutlineShellName = "OutlineShell";

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        /// <summary>
        /// Washes every renderer under this object with one colour.
        ///
        /// Through a property block rather than by swapping materials: the models keep their own
        /// texture, and a shield and a blade can both be gilded by the same line without either
        /// losing its face. Touching the shared material instead would gild every copy at once.
        /// The shells round a weapon are left alone - they live inside it, and a blanket wash painted
        /// the warning and the glow the same colour as the weapon.
        /// </summary>
        public static void Tint(GameObject root, Color color)
        {
            var block = new MaterialPropertyBlock();
            block.SetColor(BaseColorId, color);
            foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                if (IsUnderShell(renderer.transform)) continue;
                renderer.SetPropertyBlock(block);
            }
        }

        /// <summary>Whether this sits inside either of the shells a weapon can carry.</summary>
        public static bool IsUnderShell(Transform t)
        {
            for (var walk = t; walk != null; walk = walk.parent)
                if (walk.name == ShellName || walk.name == GlowShellName || walk.name == OutlineShellName)
                    return true;
            return false;
        }

        /// <summary>
        /// A copy of a model drawn round it in one material - the outline and the glow are both this.
        /// Built hidden; whoever needs it shows it.
        /// </summary>
        public static GameObject MakeShell(GameObject model, Transform inside, string name, Vector3 scale,
            Material material)
        {
            var shell = Object.Instantiate(model, inside).transform;
            shell.name = name;
            shell.localPosition = Vector3.zero;
            shell.localRotation = Quaternion.identity;
            shell.localScale = scale;
            foreach (var renderer in shell.GetComponentsInChildren<Renderer>(true))
            {
                var slots = new Material[Mathf.Max(1, renderer.sharedMaterials.Length)];
                for (int i = 0; i < slots.Length; i++) slots[i] = material;
                renderer.sharedMaterials = slots;
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            shell.gameObject.SetActive(false);
            return shell.gameObject;
        }
    }
}
