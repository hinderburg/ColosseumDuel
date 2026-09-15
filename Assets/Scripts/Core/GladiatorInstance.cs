using System.Collections.Generic;
using UnityEngine;

namespace ColosseumDuel.Core
{
    public struct ActiveBuff
    {
        public AbilityKey Key;
        public int RoundsLeft;
        public bool IsActive => RoundsLeft > 0;
    }

    /// <summary>
    /// Runtime state for one gladiator currently in a player's squad (whether on the arena floor
    /// right now or waiting on the bench). Equivalent to makeGladiatorInstance() in the JS version.
    /// </summary>
    public sealed class GladiatorInstance
    {
        public GladiatorDef Def;
        public bool Alive = true;

        public float Hp;
        public Vector2 Pos;
        public Vector2 Vel;

        /// <summary>
        /// Where he has been told to run to this round. Only meaningful while PlannedAction is Move.
        ///
        /// A point rather than a direction and a strength, which is what the order used to be: the
        /// arena has things standing in it now, and "that way, that hard" says nothing about how
        /// to get round a column. The way there is worked out from here when the phase starts -
        /// see ObstacleField.FindPath.
        /// </summary>
        public Vector2 PlannedTarget;

        /// <summary>
        /// The corners of the run he is on, starting where he set off. Empty when he is not running.
        ///
        /// Followed rather than steered: it is fixed when the phase starts, and anything that knocks
        /// him off it - a collision, a trap, a mace - ends it rather than bending it.
        /// </summary>
        public readonly List<Vector2> Path = new List<Vector2>();

        /// <summary>The corner of <see cref="Path"/> he is heading for next.</summary>
        public int PathIndex;

        /// <summary>How fast he covers the path, in virtual units a second.</summary>
        public float PathSpeed;

        public bool IsRunning => PathSpeed > 0f && PathIndex < Path.Count;

        /// <summary>
        /// Takes him off whatever path he was on and stops him dead.
        ///
        /// Everything that interrupts a run goes through here - a trap, a collision, a shove, the
        /// phase ending - so none of them can leave him with a stale path that the next step would
        /// resume, walking him from wherever he was thrown back towards a corner he has left.
        /// </summary>
        public void StopRunning()
        {
            Path.Clear();
            PathIndex = 0;
            PathSpeed = 0f;
            Vel = Vector2.zero;
        }

        /// <summary>Unit vector the model should face. Set by the simulation: towards the opponent
        /// while defending (per the design doc), along the run direction while moving.</summary>
        public Vector2 Facing = Vector2.right;

        /// <summary>
        /// What he is fighting with. Set from his training when the clash starts, and never
        /// emptied - a weapon is what a gladiator is, not
        /// a charge he spends.
        /// </summary>
        public WeaponKind Weapon = WeaponKind.None;

        /// <summary>
        /// Rounds still to run on the weapon blessing taken off the sand, the one it was taken in
        /// counted as well. Zero when his weapon is only his weapon.
        /// </summary>
        public int WeaponBuffRoundsLeft;

        /// <summary>Whether his blows carry the blessing right now.</summary>
        public bool WeaponBuffed => WeaponBuffRoundsLeft > 0;

        /// <summary>Whether this is the last round the blessing lasts - the one its glow blinks on.</summary>
        public bool WeaponBuffEnding => WeaponBuffRoundsLeft == 1;

        /// <summary>
        /// Takes up the blessing: the rest of this round and the three after it. Taken again while it
        /// is still running, it starts the count over rather than stacking.
        /// </summary>
        public void BlessWeapon(int extraRounds = 0) => WeaponBuffRoundsLeft = GameConstants.WeaponBuffRounds + 1 + extraRounds;

        /// <summary>The apple: health back, up to his own and no further. Returns what it actually gave.</summary>
        public float Heal(float amount)
        {
            float before = Hp;
            Hp = Mathf.Min(Def.MaxHp, Hp + Mathf.Max(0f, amount));
            return Hp - before;
        }

        /// <summary>
        /// The horn: rage straight onto the meter, up to full - past the lock an ability leaves behind
        /// it, which is about rage coming back from the fight, and the horn is not the fight. Returns
        /// what it actually put on.
        /// </summary>
        public float BlowHorn(float amount = GameConstants.HornRage)
        {
            float before = Rage;
            Rage = Mathf.Min(GameConstants.RageMax, Rage + amount);
            return Rage - before;
        }

        public WeaponDef WeaponDef => WeaponDef.Get(Weapon);

        /// <summary>Whether he is behind a shield. A property of the weapon now, not a slot.</summary>
        public bool HasShield => Weapon == WeaponKind.SwordAndShield || Weapon == WeaponKind.ScutumAndGladius
                                 || Weapon == WeaponKind.SpearAndShield;

        /// <summary>
        /// Rounds still to run on a net thrown over him, the one it landed in counted. He cannot run
        /// while it lasts - and since a man only turns to where he runs, he cannot turn either, so
        /// whatever he had his back to stays behind him. See EffectiveSpeed.
        /// </summary>
        public int EnsnaredRoundsLeft;

        public bool IsEnsnared => EnsnaredRoundsLeft > 0;

        /// <summary>A net lands on him. Thrown again while one is on him, it starts the count over.</summary>
        public void Ensnare() => EnsnaredRoundsLeft = GameConstants.NetRounds;

        /// <summary>
        /// Rounds still to run on the stagger an Earthshaker blow leaves - the one it landed in and
        /// the next. Rooted like a net, without the net: he stands where the blow put him.
        /// </summary>
        public int StaggeredRoundsLeft;

        public bool IsStaggered => StaggeredRoundsLeft > 0;

        public void Stagger() => StaggeredRoundsLeft = GameConstants.StaggerRounds;

        /// <summary>Held still, by a net or a stagger: his runs are cut to nothing.</summary>
        public bool IsRooted => IsEnsnared || IsStaggered;

        /// <summary>
        /// Shackles land on him: his rage burns away and his ability is out of reach for the next
        /// two whole rounds - locked the way his own use of it locks it, so rage does not build back
        /// while it lasts either.
        /// </summary>
        public void Shackle()
        {
            Rage = 0f;
            AbilityArmed = false;
            AbilityLockedRounds = Mathf.Max(AbilityLockedRounds, GameConstants.ShacklesRounds + 1);
        }

        /// <summary>Arms him with the weapon he trained on. Called when he enters the arena.</summary>
        public void EquipTrainedWeapon()
        {
            Weapon = Def.SkilledWith;
        }

        /// <summary>True when he is holding something he was never trained to hold.</summary>
        public bool IsUntrained => Weapon != WeaponKind.None && Weapon != Def.SkilledWith;

        /// <summary>Rounds of bleeding still to come. Zero means the wound has closed.</summary>
        public int BleedRoundsLeft;

        /// <summary>What one round of the current bleed costs.</summary>
        public float BleedPerRound;

        public bool IsBleeding => BleedRoundsLeft > 0;

        /// <summary>
        /// Opens a wound, or re-opens the one already there.
        ///
        /// A fresh cut restarts the count, which is the point of twin swords: keep landing and the
        /// bleed never runs out. It takes the worse of the two rates rather than the newer one - a
        /// light blow arriving after a heavy one should not talk the wound down.
        /// </summary>
        public void ApplyBleed(float rawAttackDamage, float rateMult = 1f, int rounds = GameConstants.BleedRounds)
        {
            BleedPerRound = Mathf.Max(BleedPerRound, rawAttackDamage * GameConstants.BleedFraction * rateMult);
            BleedRoundsLeft = Mathf.Max(BleedRoundsLeft, rounds);
        }

        /// <summary>
        /// Bleeds him for one round and returns what it cost. Zero when there is no wound.
        ///
        /// Called at the top of the action phase, before anyone moves: a wound that only settled up
        /// at the end of a round would let a dying fighter spend that round as though he were whole.
        /// </summary>
        public float TickBleed()
        {
            if (BleedRoundsLeft <= 0) return 0f;

            BleedRoundsLeft--;
            float amount = BleedPerRound;
            if (BleedRoundsLeft == 0) BleedPerRound = 0f;

            TakeDamage(amount);
            return amount;
        }

        public float Rage = 0f;
        public int AbilityLockedRounds = 0;
        public ActiveBuff Buff;

        // per-round planning/action bookkeeping
        public ActionType PlannedAction = ActionType.None;
        public Vector2 PlannedAimDirection;
        public float PlannedPower; // 0..1 pull strength for Move

        /// <summary>
        /// The run the player drew, corner to corner from where he stands, or empty when the order
        /// is a point (a tap, the bot, the drag controls) and the pathfinder is to find the way.
        /// Cut to his reach when the action phase starts; see GameManager.SubmitPlanningPath.
        /// </summary>
        public readonly List<Vector2> PlannedPath = new List<Vector2>();
        public bool AbilityArmed;   // toggle set during Planning; consumed at the start of Action
        public bool DealtDamageThisRound;
        public bool TookDamageThisRound;

        // Mongoose (Hilius) support: how many attacks this gladiator still gets this round.
        // Read and decremented by GameManager when a collision or a pass-by resolves.
        public int AttacksRemainingThisRound = 1;

        /// <summary>
        /// Blows he lands per exchange: what the weapon swings, doubled while Mongoose is up.
        ///
        /// Multiplied rather than overridden, so Hilius on twin swords gets four light blows and not
        /// two - the ability says "twice as many attacks", and a weapon that already attacks twice
        /// should not quietly cancel half of it.
        /// </summary>
        public int AttacksPerRound => WeaponDef.Attacks * (Has(AbilityKey.Mongoose) ? 2 : 1);

        /// <summary>
        /// The ability he took into this fight - one of his archetype's three, picked on the roster
        /// screen (the bot's at random). The first of the three until somebody says otherwise.
        /// </summary>
        public AbilityKey Ability;

        public AbilityDef AbilityInfo => AbilityDef.Get(Ability);

        /// <summary>Whether this ability of his is running right now.</summary>
        public bool Has(AbilityKey key) => Buff.IsActive && Buff.Key == key;

        /// <summary>
        /// The boons his side has taken - the side's own set, shared by every man of it, so one taken
        /// while he is on the bench is his when he steps out. Empty for a side that takes none.
        /// </summary>
        public System.Collections.Generic.HashSet<BoonKey> TeamBoons = NoBoons;

        private static readonly System.Collections.Generic.HashSet<BoonKey> NoBoons =
            new System.Collections.Generic.HashSet<BoonKey>();

        public bool HasBoon(BoonKey key) => TeamBoons.Contains(key);

        /// <summary>What his side's boons do to how far he runs: Fleet Foot.</summary>
        private float BoonSpeedMult => HasBoon(BoonKey.FleetFoot) ? GameConstants.FleetFootSpeedMult : 1f;

        /// <summary>What his side's boons do to how far his weapon reaches: Long Reach.</summary>
        private float BoonReachMult => HasBoon(BoonKey.LongReach) ? GameConstants.LongReachMult : 1f;

        /// <summary>
        /// How far his blows reach right now: his weapon's, lengthened while Lunge or Trident Throw
        /// is up. Everything that asks whether he can strike asks this rather than the weapon.
        /// </summary>
        public float Reach => WeaponDef.Reach
                              * (Has(AbilityKey.Lunge) ? GameConstants.LungeReachMult : 1f)
                              * (Has(AbilityKey.TridentThrow) ? GameConstants.TridentThrowReachMult : 1f)
                              * BoonReachMult;

        /// <summary>
        /// The planning-time twin of Reach, as PlannedSpeed is of his speed: with Lunge or Trident
        /// Throw armed and not yet working, the reach it will have once it goes off at the top of the
        /// action phase. What the wedge on the sand is drawn to - drawn to the weapon alone, it said
        /// a Lunge made no difference at all.
        /// </summary>
        public float PlannedWeaponReach()
        {
            if (!AbilityArmed || Has(Ability)) return Reach;
            return WeaponDef.Reach * ReachMultiplierOf(Ability) * BoonReachMult;
        }

        private static float ReachMultiplierOf(AbilityKey key)
            => key == AbilityKey.Lunge ? GameConstants.LungeReachMult
             : key == AbilityKey.TridentThrow ? GameConstants.TridentThrowReachMult
             : 1f;

        public GladiatorInstance(GladiatorDef def)
        {
            Def = def;
            Hp = def.MaxHp;
            Ability = def.Abilities[0];

            // Armed from the moment he exists. He is never without a weapon in a match, and an
            // instance that started empty-handed only meant every caller had to remember to arm
            // him - which is a rule that gets forgotten rather than a state that happens.
            EquipTrainedWeapon();
        }

        public bool IsDefending => PlannedAction == ActionType.Defend;

        /// <summary>
        /// Which part of him a blow coming from this point lands on.
        ///
        /// Measured from where the attacker is, not from where the blow was aimed: a man standing
        /// behind you is behind you whatever direction he swung in. The half-angles are why the
        /// front is the narrowest thing to hit despite every sector being ninety degrees wide - the
        /// front is forty-five either side of the nose, and the back is forty-five either side of
        /// the spine.
        /// </summary>
        public HitSector SectorHitFrom(Vector2 attackerPos)
        {
            var fromHim = attackerPos - Pos;
            if (fromHim.sqrMagnitude < 0.000001f) return HitSector.Front;

            var facing = Facing.sqrMagnitude > 0.000001f ? Facing.normalized : Vector2.up;

            // The cosine of the angle between where he is looking and where the blow came from.
            // Forty-five degrees is cos 45, and a hundred and thirty-five is its negative.
            float alignment = Vector2.Dot(facing, fromHim.normalized);

            const float halfSector = 0.70710678f; // cos 45 degrees
            if (alignment >= halfSector) return HitSector.Front;
            if (alignment <= -halfSector) return HitSector.Back;
            return HitSector.Side;
        }

        /// <summary>
        /// Whether a blow of his would land on somebody standing there, with him standing here.
        ///
        /// Takes both positions rather than reading Pos, because the interesting case is a blow
        /// struck in passing: the action phase asks this about the moment the two came nearest,
        /// which is somewhere inside a substep and not where either of them ended it.
        ///
        /// Two conditions, and the second is the new one. Far enough is still reach. Pointed at him
        /// is the weapon's swing arc about the way he is looking - and since he looks where he runs,
        /// this is what makes a heading a weapon: a man cannot be cut down by somebody who ran past
        /// without ever turning towards him.
        /// </summary>
        public bool CanStrikeFrom(Vector2 myPos, Vector2 targetPos)
            => WithinSwing(myPos, Facing, WeaponDef, targetPos, Reach);

        /// <summary>
        /// The same test with everything spelled out, for the strike prediction - which walks a
        /// future in which he is holding a weapon he has not picked up yet and running a heading he
        /// does not have yet, so it cannot ask a gladiator about himself.
        /// </summary>
        public static bool WithinSwing(Vector2 from, Vector2 facing, WeaponDef weapon, Vector2 target)
            => WithinSwing(from, facing, weapon, target, weapon.Reach);

        /// <summary>The same, with the reach given - lengthened by an ability, say.</summary>
        public static bool WithinSwing(Vector2 from, Vector2 facing, WeaponDef weapon, Vector2 target, float reach)
        {
            var toTarget = target - from;
            float distance = toTarget.magnitude;
            if (distance > reach) return false;

            // A body pressed against his own is inside every arc there is. Without this a collision
            // - two men who have run into each other - could resolve as a miss because one of them
            // was looking over the other's shoulder, which is not something either of them would
            // recognise as happening.
            if (distance <= GameConstants.CollideDistance) return true;

            var look = facing.sqrMagnitude > 0.000001f ? facing.normalized : Vector2.up;
            return Vector2.Angle(look, toTarget) <= weapon.SwingArcDegrees * 0.5f;
        }

        public float EffectiveSpeed()
        {
            if (IsRooted) return 0f;
            return Def.Speed * SpeedMultiplierOf(Buff.IsActive ? Buff.Key : (AbilityKey?)null) * BoonSpeedMult;
        }

        /// <summary>What an ability does to his speed while it runs: Rampage speeds him, Testudo slows him.</summary>
        private static float SpeedMultiplierOf(AbilityKey? key)
        {
            switch (key)
            {
                case AbilityKey.Rampage: return GameConstants.RampageSpeedMult;
                case AbilityKey.Testudo: return GameConstants.TestudoSpeedMult;
                default: return 1f;
            }
        }

        /// <summary>
        /// How fast he will be running once this round starts, buff included.
        ///
        /// The difference from EffectiveSpeed is one phase of timing. An armed ability has not
        /// fired yet - it fires at the top of the action phase - so during planning EffectiveSpeed
        /// still reports the unbuffed number. Anything that draws what the player is about to do
        /// has to look forward instead, or with the speed ability armed the run he is allowed to
        /// draw would be a third shorter than the one he will actually be able to make.
        /// </summary>
        public float PlannedSpeed()
        {
            // Armed and not already running, it will be by the time he moves: Rampage's run is drawn
            // half as long again, Testudo's half as long.
            if (IsRooted || !AbilityArmed || Has(Ability)) return EffectiveSpeed();
            return Def.Speed * SpeedMultiplierOf(Ability) * BoonSpeedMult;
        }

        /// <summary>
        /// How far one phase of running carries him, in virtual units: the longest run he can make,
        /// and so the longest one the player can draw for him.
        ///
        /// Here rather than in the input layer because it is the same product the action phase cuts
        /// a run to - a second copy of the formula would drift from this one the first time either
        /// factor moved. Not multiplied by the length of the phase: making the phase longer slows the
        /// run down rather than sending him further.
        /// </summary>
        public float DashReach() => EffectiveSpeed() * GameConstants.SpeedScale;

        /// <summary>
        /// How far one phase of running will carry him once this round starts, ability included.
        /// The planning-time twin of DashReach, for the same reason PlannedSpeed exists.
        /// </summary>
        public float PlannedReach() => PlannedSpeed() * GameConstants.SpeedScale;

        public void AddRage(float amount)
        {
            if (AbilityLockedRounds > 0) return; // locked out after a recent activation
            Rage = Mathf.Min(GameConstants.RageMax, Rage + amount);
        }

        public bool CanActivateAbility => AbilityLockedRounds <= 0 && Rage >= GameConstants.RageMax;

        public void ActivateAbility()
        {
            if (!CanActivateAbility) return;

            // One round at the least, even for an ability spent the moment it fires: the buff is
            // also what says, for that phase, which ability went off.
            Buff = new ActiveBuff { Key = Ability, RoundsLeft = System.Math.Max(1, AbilityInfo.Rounds) };

            // Second Wind is the one ability that is spent the moment it fires rather than lasting.
            if (Ability == AbilityKey.SecondWind)
                Hp = Mathf.Min(Def.MaxHp, Hp + Def.MaxHp * GameConstants.SecondWindHeal);

            // The ability fires at the start of Action, after BeginRound already set the attack
            // budget for this round - so Mongoose has to top it up for the round it was used in.
            AttacksRemainingThisRound = AttacksPerRound;
            Rage = 0f;
            AbilityLockedRounds = GameConstants.AbilityLockRounds + 1; // +1 so it also skips the round it was used in
        }

        /// <summary>Call once per new round, before planning is (re)opened.</summary>
        public void BeginRound()
        {
            // The plan MUST be cleared here: GameManager.AutoFillMissingPlans only fills a slot that
            // is still ActionType.None, so a stale plan would silently replay last round's move -
            // and the bot, which is only asked for a decision when its slot is empty, would repeat
            // its very first decision for the rest of the match.
            PlannedAction = ActionType.None;
            PlannedAimDirection = Vector2.zero;
            PlannedPower = 0f;
            PlannedTarget = Vector2.zero;
            PlannedPath.Clear();
            StopRunning();

            AbilityArmed = false;
            DealtDamageThisRound = false;
            TookDamageThisRound = false;
            if (AbilityLockedRounds > 0) AbilityLockedRounds--;
            if (WeaponBuffRoundsLeft > 0) WeaponBuffRoundsLeft--;
            if (EnsnaredRoundsLeft > 0) EnsnaredRoundsLeft--;
            if (StaggeredRoundsLeft > 0) StaggeredRoundsLeft--;
            if (Buff.RoundsLeft > 0)
            {
                Buff.RoundsLeft--;
            }
            // Derived AFTER the buff has been aged, so the second Mongoose round still gets 2 attacks
            // and the round right after the buff expires drops back to 1.
            AttacksRemainingThisRound = AttacksPerRound;
        }

        /// <summary>Passive + reactive rage gain, applied at the end of an action phase.</summary>
        public void ResolveRoundRage()
        {
            AddRage(GameConstants.RagePerRoundPassive);
            if (DealtDamageThisRound) AddRage(GameConstants.RageBonusOnDealDamage);
            if (TookDamageThisRound) AddRage(GameConstants.RageBonusOnTakeDamage);
        }

        public void TakeDamage(float amount)
        {
            Hp = Mathf.Max(0f, Hp - amount);
            TookDamageThisRound = true;
            if (Hp <= 0f) Alive = false;
        }

        public void ResetForNewClash()
        {
            // Winner persists with current HP (not healed) - only a freshly-picked gladiator gets this.
            StopRunning();
            PlannedAction = ActionType.None;
            PlannedPath.Clear();
            AbilityArmed = false;
            Buff = default;
            EnsnaredRoundsLeft = 0;
            StaggeredRoundsLeft = 0;
            AbilityLockedRounds = 0;

            // His own weapon, unblessed. The blessing is the reward for crossing the arena under fire
            // during a clash; carried into the next one for free, it would make the first clash the
            // only one worth taking that risk in.
            EquipTrainedWeapon();
            WeaponBuffRoundsLeft = 0;
            AttacksRemainingThisRound = AttacksPerRound;

            // Wounds close between clashes. A bleed that survived would go on draining a fighter
            // through a clash he was not in when it was opened.
            BleedRoundsLeft = 0;
            BleedPerRound = 0f;
        }
    }
}
