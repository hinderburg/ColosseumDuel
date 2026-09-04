namespace ColosseumDuel.Core
{
    /// <summary>
    /// All balance/timing numbers, ported 1:1 from the original web prototype (game.html).
    /// Keep this the single source of truth so gameplay feel stays identical after the port.
    /// </summary>
    public static class GameConstants
    {
        // --- phase timing (seconds) ---
        public const float PlanningTime = 3.0f;
        public const float ActionTime = 2.0f; // reduced from the original 4.0s per design iteration
        public const float RevealTime = 1.0f;   // picks stay on screen this long before the round's first Planning
        public const float RoundEndTime = 1.0f; // pause after a death so the knockout reads on screen

        // --- arena geometry, in "virtual" 2D simulation units (top-down plane) ---
        // The simulation runs entirely in this 2D virtual space; only the presentation layer
        // (3D models/camera) needs to convert virtual -> world units. Keep that conversion in
        // one place (see ArenaView / a VirtualToWorld scale constant) rather than scattering it.
        public const float ArenaRadius = 300f;

        /// <summary>
        /// How much longer the arena is along Y than across X. The camera looks down the Y axis, so
        /// this is the axis that runs up the screen - stretching it is what lets the arena fill a
        /// 9:16 frame instead of leaving a band of empty space above and below.
        /// See ArenaShape for the geometry everything else goes through.
        /// </summary>
        public const float ArenaElongation = 2.0f;
        public const float GladiatorRadius = 16f;
        public const float ItemRadius = 12f;

        public const float CollideDistance = GladiatorRadius * 2f - 4f;   // 28
        public const float PassByDistance = GladiatorRadius * 2f + 34f;  // 66
        public const float PickupDistance = GladiatorRadius + ItemRadius + 6f; // 34

        // Both fighters are placed on opposite sides of the arena at the start of every round, this
        // far from the center (as a fraction of ArenaRadius). Only HP and carried items persist for
        // a round winner - position does not, so no round starts from an arbitrary leftover spot.
        public const float SpawnDistanceFraction = 0.6f;

        public const float SpeedScale = 7.5f;
        public const float MaxDragVirtual = 90f; // max pull-back distance for the slingshot move

        // Knockback after a direct collision: ~1 body length, increased 30% per a later design pass.
        public const float KnockbackDistance = GladiatorRadius * 1.3f * 1.3f;
        public const float CollisionEarlyEndDelay = 0.25f; // cut the action phase short this long after a collision

        public const int ActionSubsteps = 6; // subdivide stepAction(dt) to avoid tunneling through fast-moving gladiators

        // --- rage / ability system ---
        public const float RagePerCyclePassive = 0.15f;
        public const float RageBonusOnDealDamage = 0.15f;
        public const float RageBonusOnTakeDamage = 0.10f;
        public const float RageMax = 1.0f;
        public const int AbilityLockCycles = 1; // cycles rage cannot charge after activating an ability

        // --- combat modifiers ---
        public const float DefendDamageMult = 0.70f;      // -30% incoming damage while defending
        public const float ShieldDamageMult = 0.50f;      // -50% incoming damage, multiplicative with defend
        public const float OneHandedWeaponMult = 1.5f;    // axe
        public const float PassByDamageMult = 0.50f;      // normal pass-by damage share
        public const float TwoHandedPassByDamageMult = 1.0f; // trident: full damage on a pass-by

        // --- items ---
        public const int ItemCountOnArena = 3; // always exactly 1 weapon + 1 shield + 1 random

        // --- arena hazard (shrinking rings) ---
        public const float HazardDamageFraction = 0.15f; // 15% of max HP per tick in an active danger ring
        public const int HazardSafeCycles = 6;            // arena is fully safe for the first 6 full cycles

        // --- squads ---
        public const int SquadSize = 3;
    }
}
