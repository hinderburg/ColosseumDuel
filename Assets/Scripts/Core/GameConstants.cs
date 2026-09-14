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
        // while the world runs at a fifth of its speed, so this is four real seconds, not four slowed ones.
        //
        // It has been 3.0 and then 2.0. Back up to 4.0 now that the order is given by a swipe: a
        // gesture takes longer to make than a tap, and two seconds was long enough to decide but not
        // long enough to decide and then draw the decision.
        public const float PlanningTime = 4.0f;
        // How fast the world runs while the player decides: a fifth of its speed. The phase itself
        // is still four real seconds - the simulation ticks on unscaled time - so what slows is only
        // what is on screen. It ran at a third once, then at full speed for a while, and is back at
        // a fifth on request.
        //
        // A constant rather than a slider on the controller, because the ready stance divides it
        // back out of its playback speed (the animator runs on scaled time, see GladiatorAnimation)
        // and an editor script cannot read a value set on a scene that is not loaded.
        public const float PlanningTimeScale = 0.2f;
        // How long the gladiators actually move. Cut from 4.0 to 2.0 and then to 1.0 across design
        // passes - a short burst reads as a charge, a long one as a jog - and back up to 2.0 on
        // request, so a run can be followed by eye.
        //
        // It no longer decides how far anybody goes. That is speed alone (see SpeedScale): a longer
        // phase is the same run at half the pace, not twice the ground.
        public const float ActionTime = 2.0f;
        public const float RevealTime = 1.0f;   // picks stay on screen this long before the clash's first Planning
        // Pause after a death, in real seconds. Long enough that the knockout plays out at the
        // slowed rate the camera comes in on: at DeathTimeScale this is roughly half a second of
        // animation, which is what a fall takes.
        public const float ClashEndTime = 1.4f;

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
        public const float BuffRadius = 12f;   // the weapon blessing lying on the sand

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
        public const float PickupDistance = (GladiatorRadius + BuffRadius + 6f) * 1.15f;

        // Both fighters are placed at opposite ends of the arena at the start of every clash, this
        // far from the centre as a fraction of the LONG semi-axis - the one they are spread along.
        // Only HP and carried items persist for a clash winner; position does not, so no clash
        // starts from an arbitrary leftover spot.
        //
        // The cost of spreading them is measured in rounds, not in units: two fighters charging at
        // full power close the gap between them at twice their own speed, and at this distance the
        // slowest pair (two Brutius, 450 a phase each) meets inside one phase at full power; the
        // bot stops short of that on purpose (see BotAI). Lower this if the wait shows.
        public const float SpawnDistanceFraction = 0.55f;

        /// <summary>
        /// Virtual units of run in one action phase for each point of Speed, however long the phase
        /// is: Brutius at 10 draws 450, Barbarius 675, Hilius 900.
        ///
        /// Three times the 15 it was before speed was taken away. Speed came back to limit how long
        /// a drawn run can be, and at 15 the budget was a short dash - drawing a shape with it left
        /// no room for a shape.
        /// </summary>
        public const float SpeedScale = 45f;

        /// <summary>
        /// How far the tutorial puts the sword and the tap point it asks for, measured from the
        /// player. A fixed distance rather than a share of his reach: at three times the old dash a
        /// share of it put the sword past the opponent for the fastest of the three.
        /// </summary>
        public const float TutorialRunLength = 225f;

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
        public const float RagePerRoundPassive = 0.15f;
        public const float RageBonusOnDealDamage = 0.15f;
        public const float RageBonusOnTakeDamage = 0.10f;
        public const float RageMax = 1.0f;
        public const int AbilityLockRounds = 1; // rounds rage cannot charge after activating an ability

        // --- combat modifiers ---
        public const float DefendDamageMult = 0.70f;      // -30% incoming damage while defending
        public const float ShieldDamageMult = 0.50f;      // what the sword-and-shield wielder takes
        public const float ScutumDamageMult = 0.75f;      // the scutum: less than the shield, and it shoves
        public const float RoundShieldDamageMult = 0.80f; // the spearman's small round shield

        /// <summary>How far a blow from behind the scutum shoves its target - half the mace's throw.</summary>
        public const float ShieldBashKnockback = GladiatorRadius * 0.8f;

        // --- abilities: the numbers behind the words on each card (see AbilityDef) ---

        /// <summary>Share of his own health Second Wind gives back, at once.</summary>
        public const float SecondWindHeal = 0.35f;

        /// <summary>How many rounds a net holds a man still (the one it lands in counted).</summary>
        public const int NetRounds = 2;

        /// <summary>How many rounds Earthshaker roots the man it lands on (this one and the next).</summary>
        public const int StaggerRounds = 2;

        /// <summary>How many whole rounds Shackles keeps the other man from his ability.</summary>
        public const int ShacklesRounds = 2;

        public const float RampageSpeedMult = 1.5f;
        public const float RampageDamageMult = 0.7f;
        public const float StoneSkinTakenMult = 0.7f;
        public const float BloodlustHeal = 0.5f;
        public const float FrenzyBleedMult = 2f;
        public const int FrenzyBleedRounds = 3;
        public const float BerserkDamageMult = 1.5f;
        public const float BerserkTakenMult = 1.25f;
        public const float RiposteReturn = 0.5f;
        public const float TestudoTakenMult = 0.4f;
        public const float TestudoSpeedMult = 0.5f;
        public const float EarthshakerKnockbackMult = 2f;
        public const float ShieldBashKnockbackMult = 3f;
        public const float LungeReachMult = 1.5f;
        public const float TridentThrowReachMult = 2f;

        /// <summary>
        /// What a blow is worth for landing behind a gladiator, and for landing on his flank.
        ///
        /// This is what makes the way a man is facing a decision. He turns to where he is running,
        /// so ordering a run is also ordering which of him is exposed for the rest of the round -
        /// and charging past somebody to end up behind them is worth almost half a blow again.
        /// </summary>
        public const float BackAttackMult = 1.40f;

        public const float FlankAttackMult = 1.20f;

        /// <summary>
        /// How much harder a blessed weapon hits. The blessing lies on the sand every third round
        /// and lasts three rounds after it is taken - it used to be a gilded weapon lying there all
        /// round, and it is worth the same: a better version of the same weapon, not another tier.
        /// </summary>
        public const float WeaponBuffDamageMult = 1.35f;

        /// <summary>How many rounds the blessing lasts after the one it is taken in.</summary>
        public const int WeaponBuffRounds = 3;

        /// <summary>A blessing is laid on the sand on every round that is a multiple of this.</summary>
        public const int WeaponBuffEveryRounds = 3;

        /// <summary>How far one mace blow throws its target, in virtual units - most of a body.</summary>
        public const float MaceKnockback = GladiatorRadius * 1.6f;

        /// <summary>
        /// Share of the blow that opened it which a bleed deals, each round, for BleedRounds.
        ///
        /// Taken off the raw blow rather than off what landed: a wound is a wound, and the shield
        /// that softened the hit is not still in the way of it afterwards. Over its two rounds a
        /// bleed is therefore worth half of one more blow - enough that the twin swords' lower
        /// damage per hit is a trade rather than a straight loss.
        /// </summary>
        public const float BleedFraction = 0.25f;

        /// <summary>How many rounds a bleed runs for. A fresh one starts the count again.</summary>
        public const int BleedRounds = 2;

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
        /// How many full rounds the arena stays safe before the first ring bites.
        ///
        /// Raised from six. Six put the first ring down while two fighters were often still closing
        /// - the approach takes rounds now that a run bends - so the arena was deciding fights that
        /// had not started yet.
        /// </summary>
        public const int HazardSafeRounds = 8;

        /// <summary>
        /// Rounds between one ring closing and the next.
        ///
        /// It used to be one, which gave the whole shrink four rounds from first ring to dead
        /// centre: less time than two gladiators need to cross the arena, so the ending was the
        /// arena rather than either of them. At three there is a fight between each closing.
        /// </summary>
        public const int HazardRingInterval = 3;

        // --- squads ---
        public const int SquadSize = 3;
    }
}
