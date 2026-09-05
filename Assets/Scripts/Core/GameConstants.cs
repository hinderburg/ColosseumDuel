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
        // How long the gladiators actually move. Cut from 4.0 to 2.0 and then to 1.0 across design
        // passes - a short burst reads as a charge, a long one as a jog.
        //
        // It sets how far a dash carries, and so how many cycles it takes two fighters to meet:
        // halving it halves the distance covered per phase. At 1.0 the slowest pair charging head-on
        // covers 150 units against the 512 between them, so first contact is three or four cycles
        // out; the fastest pair does it in two. SpawnDistanceFraction is the other end of that
        // trade if the approach starts to drag.
        public const float ActionTime = 1.0f;
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

        // Both fighters are placed at opposite ends of the arena at the start of every round, this
        // far from the centre as a fraction of the LONG semi-axis - the one they are spread along.
        // Only HP and carried items persist for a round winner; position does not, so no round
        // starts from an arbitrary leftover spot.
        //
        // The cost of spreading them is measured in cycles, not in units: two fighters charging at
        // full power close the gap between them at twice their own speed, and at this distance the
        // slowest pair (two Brutius, 75/s each) needs two action phases to meet where they used to
        // need one. The fastest still meets inside a single phase. Lower this if the wait shows.
        public const float SpawnDistanceFraction = 0.45f;

        // Virtual units per second per point of a gladiator's Speed stat.
        //
        // Paired with ActionTime, and the pair is what matters: how far a dash carries is
        // Speed * SpeedScale * ActionTime, so halving the phase halves the reach unless this
        // doubles to match. It did, when the action phase went from 2.0s to 1.0s - the dash covers
        // the same ground as before and covers it twice as fast, which is the point of the shorter
        // phase. Change one of the two and the reach moves; DashCarriesTheSameGround pins it.
        public const float SpeedScale = 15f;
        public const float MaxDragVirtual = 90f; // max pull-back distance for the slingshot move

        // How far apart a direct collision leaves the two fighters, measured centre to centre.
        //
        // It has to clear their own bodies, and for a long time it did not: it was written as a
        // multiple of one radius and came out at 27, against a collide threshold of 28 and two
        // bodies 32 wide. The "knockback" placed them closer than the distance at which they had
        // just collided, still overlapping - so on screen nothing was thrown back at all, the two
        // simply stopped in each other.
        //
        // Expressed against the width of the pair, which is what it actually has to beat.
        public const float KnockbackDistance = GladiatorRadius * 2f * 1.4f;
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
        // Share of max HP the danger zone burns through over one action phase. Doubled in a balance
        // pass: at 15% the fire was a nuisance to be walked through, which is the opposite of what a
        // closing arena is for.
        public const float HazardDamageFraction = 0.30f;
        public const int HazardSafeCycles = 6;            // arena is fully safe for the first 6 full cycles

        // --- squads ---
        public const int SquadSize = 3;
    }
}
