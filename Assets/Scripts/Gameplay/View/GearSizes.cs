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
        public const float SwordLength = 1.5f;
        public const float MaceLength = 2.3f;
        public const float ShieldHeight = 1.2f;
        public const float HelmetHeight = 0.62f;

        /// <summary>What goes in the weapon hand, and how long it is.</summary>
        public static float MainHandLength(WeaponKind kind)
            => kind == WeaponKind.TwoHandedMace ? MaceLength : SwordLength;

        public static bool UsesMace(WeaponKind kind) => kind == WeaponKind.TwoHandedMace;

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
            => kind == WeaponKind.TwoHandedMace ? GripFromEnd : -GripFromEnd;

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
    }
}
