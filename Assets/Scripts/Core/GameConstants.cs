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

        /// <summary>
        /// How big a gladiator is against the size everything was first tuned at.
        ///
        /// One knob for the whole man, because the man is several things that have to agree: the
        /// radius collisions and paths are resolved against, the figure drawn on screen, the gear
        /// in his hands and the reach that gear strikes at. Scaled separately they drift - a bigger
        /// figure over the same collision circle has bodies passing through each other, and a
        /// bigger figure holding the same sword has a blade that no longer reaches as far as it
        /// looks. GladiatorView and GearSizes both read this, and so do the weapon reaches.
        ///
        /// 1.2 since the obstacles went in: at the old size a man was small against a column, and
        /// running round things reads better when the things and the men are of a kind.
        /// </summary>
        public const float GladiatorScale = 1.2f;

        /// <summary>The body radius everything was tuned at, before <see cref="GladiatorScale"/>.</summary>
        public const float BaseGladiatorRadius = 16f;

        public const float GladiatorRadius = BaseGladiatorRadius * GladiatorScale;   // 19.2
        public const float ItemRadius = 12f;

        // --- obstacles ---

        /// <summary>
        /// A column's radius, in virtual units. A little wider than a man, so it is obviously
        /// something to go round rather than someone to fight.
        /// </summary>
        public const float ColumnRadius = 26f;

        /// <summary>Half a crate's side. About a man's width, and so about his cover.</summary>
        public const float CrateHalfSize = 20f;

        public const float CollideDistance = GladiatorRadius * 2f - 4f;   // 34.4
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

        /// <summary>
        /// How far an order given as an aim and a strength sends a man, at full strength.
        ///
        /// Gladiators have no speed any more: a run always takes the whole action phase and always
        /// arrives, however far it goes, so a tap is simply a point. The callers that still think in
        /// aims - the bot, the old drag controls, most of the tests - need a distance for "all the
        /// way", and this is it: the dash the middle archetype used to have, so the bot's pace
        /// across the arena is about what it always was.
        /// </summary>
        public const float AimedRunLength = 225f;

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
        /// Modest against a charge - a run covers hundreds of units in a phase - so this is a recoil
        /// rather than a second dash in the other direction.
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

        /// <summary>
        /// What Brutius's Second Wind gives back, as a share of his full health: a fifth, forty
        /// points of his two hundred.
        ///
        /// Enough to be worth the rage it costs - several cycles of fighting to fill - and not so
        /// much that the man with twice everybody else's health can outlast every fight by topping
        /// himself back up. Taken off his full health rather than what he has left, so it is worth
        /// the same whenever it is spent.
        /// </summary>
        public const float SecondWindHealFraction = 0.2f;

        // --- combat modifiers ---
        public const float DefendDamageMult = 0.70f;      // -30% incoming damage while defending
        public const float ShieldDamageMult = 0.50f;      // what the sword-and-shield wielder takes

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

        /// <summary>
        /// How many full cycles the arena stays safe before the first ring bites.
        ///
        /// Raised from six. Six put the first ring down while two fighters were often still closing
        /// - the approach takes cycles now that a run bends - so the arena was deciding fights that
        /// had not started yet.
        /// </summary>
        public const int HazardSafeCycles = 8;

        /// <summary>
        /// Cycles between one ring closing and the next.
        ///
        /// It used to be one, which gave the whole shrink four cycles from first ring to dead
        /// centre: less time than two gladiators need to cross the arena, so the ending was the
        /// arena rather than either of them. At three there is a fight between each closing.
        /// </summary>
        public const int HazardRingInterval = 3;

        // --- squads ---
        public const int SquadSize = 3;
    }
}
