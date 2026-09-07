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

        /// <summary>
        /// How far apart a weapon's own reach lets it strike, drawn as the ring under the fighter.
        ///
        /// Converted from simulation units by the caller; this is only the shape of the reading.
        /// </summary>
        public static bool UsesMace(WeaponKind kind) => kind == WeaponKind.TwoHandedMace;

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
