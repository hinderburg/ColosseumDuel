namespace ColosseumDuel.Core
{
    /// <summary>
    /// All balance/timing numbers, ported 1:1 from the original web prototype (game.html).
    /// Keep this the single source of truth so gameplay feel stays identical after the port.
    /// </summary>
    public static class GameConstants
    {
        // --- phase timing (seconds) ---
        // How long the player gets to decide, in real seconds - the phase is timed on unscaled time
        // while the world runs at a third speed, so this is four real seconds, not four slowed ones.
        //
        // It has been 3.0 and then 2.0. Back up to 4.0 now that the order is given by a swipe: a
        // gesture takes longer to make than a tap, and two seconds was long enough to decide but not
        // long enough to decide and then draw the decision.
        public const float PlanningTime = 4.0f;
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
        // Pause after a death, in real seconds. Long enough that the knockout plays out at the
        // slowed rate the camera comes in on: at DeathTimeScale this is roughly half a second of
        // animation, which is what a fall takes.
        public const float RoundEndTime = 1.4f;

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
        // Reach for picking something off the sand: the two radii, a little slack, and 15% on top
        // of the lot after a pass where running over an item and not getting it was too common.
        public const float PickupDistance = (GladiatorRadius + ItemRadius + 6f) * 1.15f;

        // Both fighters are placed at opposite ends of the arena at the start of every round, this
        // far from the centre as a fraction of the LONG semi-axis - the one they are spread along.
        // Only HP and carried items persist for a round winner; position does not, so no round
        // starts from an arbitrary leftover spot.
        //
        // The cost of spreading them is measured in cycles, not in units: two fighters charging at
        // full power close the gap between them at twice their own speed, and at this distance the
        // slowest pair (two Brutius, 75/s each) needs two action phases to meet where they used to
        // need one. The fastest still meets inside a single phase. Lower this if the wait shows.
        public const float SpawnDistanceFraction = 0.55f;

        // Virtual units per second per point of a gladiator's Speed stat.
        //
        // Paired with ActionTime, and the pair is what matters: how far a dash carries is
        // Speed * SpeedScale * ActionTime, so halving the phase halves the reach unless this
        // doubles to match. It did, when the action phase went from 2.0s to 1.0s - the dash covers
        // the same ground as before and covers it twice as fast, which is the point of the shorter
        // phase. Change one of the two and the reach moves; DashCarriesTheSameGround pins it.
        public const float SpeedScale = 15f;
        public const float MaxDragVirtual = 90f; // max pull-back distance for the slingshot move

        /// <summary>
        /// How fast a collision sends the two back off each other, in virtual units per second.
        ///
        /// A step back, not a shove: they used to be teleported to opposite ends of a fixed
        /// distance the instant they touched, which on screen is not a collision but a cut - two
        /// fighters meet and are suddenly somewhere else, on exactly the frame their swings were
        /// meant to be playing. Setting them moving instead lets the rest of the phase carry them,
        /// and the whole thing reads as one beat.
        ///
        /// Modest against a charge: the slowest gladiator runs at 150 and the fastest at 300, so
        /// this is a recoil rather than a second dash in the other direction.
        /// </summary>
        public const float BounceSpeed = 90f;
        /// <summary>
        /// How long the action phase runs on after a collision before it is cut short.
        ///
        /// Long enough for the swings to play out and the recoil to carry, which it was not: at a
        /// quarter of a second the phase ended mid-swing and the rest of it finished during the
        /// planning slow-motion, at a third speed and seconds after the two had met.
        /// </summary>
        public const float CollisionEarlyEndDelay = 0.45f;

        public const int ActionSubsteps = 6; // subdivide stepAction(dt) to avoid tunneling through fast-moving gladiators

        // --- rage / ability system ---
        public const float RagePerCyclePassive = 0.15f;
        public const float RageBonusOnDealDamage = 0.15f;
        public const float RageBonusOnTakeDamage = 0.10f;
        public const float RageMax = 1.0f;
        public const int AbilityLockCycles = 1; // cycles rage cannot charge after activating an ability

        // --- combat modifiers ---
        public const float DefendDamageMult = 0.70f;      // -30% incoming damage while defending
        public const float ShieldDamageMult = 0.50f;      // what the sword-and-shield wielder takes

        // --- how far and how sharply a gladiator may be ordered to turn ---

        /// <summary>
        /// The arc of ground a gladiator may be ordered onto, at the slowest speed in the game and
        /// at the fastest. See MoveEnvelope, which interpolates between them.
        ///
        /// The wide end is deliberately most of a circle: a heavy man is slow, and taking his
        /// choices away as well as his ground would be two punishments for one stat. The narrow end
        /// is where the trade lives - a quick gladiator covers twice the distance and commits to a
        /// heading while he does it, which is what gives an opponent a back to get round behind.
        /// </summary>
        public const float MoveArcWideDegrees = 250f;

        public const float MoveArcNarrowDegrees = 130f;

        /// <summary>
        /// What a blow is worth for landing behind a gladiator, and for landing on his flank.
        ///
        /// This is what makes the way a man is facing a decision. He turns to where he is running,
        /// so ordering a run is also ordering which of him is exposed for the rest of the cycle -
        /// and charging past somebody to end up behind them is worth almost half a blow again.
        /// </summary>
        public const float BackAttackMult = 1.40f;

        public const float FlankAttackMult = 1.20f;

        /// <summary>
        /// How much better the gilded copies on the sand are than the weapon a gladiator arrives
        /// with. It is the entire reason to break off and cross a mined arena for one, so it has to
        /// be worth a trap and a stretch of danger zone - but it is a better version of the same
        /// weapon, not a different tier of weapon.
        /// </summary>
        public const float GildedWeaponMult = 1.35f;

        /// <summary>How far one mace blow throws its target, in virtual units - most of a body.</summary>
        public const float MaceKnockback = GladiatorRadius * 1.6f;

        /// <summary>
        /// Share of the blow that opened it which a bleed deals, each cycle, for BleedCycles.
        ///
        /// Taken off the raw blow rather than off what landed: a wound is a wound, and the shield
        /// that softened the hit is not still in the way of it afterwards. Over its two cycles a
        /// bleed is therefore worth half of one more blow - enough that the twin swords' lower
        /// damage per hit is a trade rather than a straight loss.
        /// </summary>
        public const float BleedFraction = 0.25f;

        /// <summary>How many cycles a bleed runs for. A fresh one starts the count again.</summary>
        public const int BleedCycles = 2;

        // --- items ---
        // One of each weapon kind, always on the floor. See ItemSystem.
        public const int ItemCountOnArena = 3;

        // --- traps ---
        // Laid out fresh every round, half in each fighter's end. Even, so neither side starts the
        // round with more holes to worry about than the other - which is why this stays a multiple
        // of two. Enough that crossing the arena is a decision rather than a formality, few enough
        // that a dash is not a dice roll.
        public const int TrapCount = 6;
        public const float TrapRadius = 14f;

        // --- arena hazard (shrinking rings) ---

        /// <summary>
        /// What the spikes cost a gladiator who spends a whole action phase in them.
        ///
        /// A flat number rather than a share of his own health, which is what it used to be. As a
        /// share it punished the three archetypes equally in proportion and unequally in effect -
        /// the closing arena is a wall, and a wall does not hit harder because the man walking into
        /// it is tougher. At 65 against health of 100 to 200 it is two or three phases from full,
        /// which is what a shrinking arena is for: somewhere you cannot stand, not somewhere
        /// expensive to stand.
        /// </summary>
        public const float HazardDamagePerPhase = 65f;
        public const int HazardSafeCycles = 6;            // arena is fully safe for the first 6 full cycles

        // --- squads ---
        public const int SquadSize = 3;
    }
}
