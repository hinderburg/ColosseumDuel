using System.Collections.Generic;
using UnityEngine;

namespace ColosseumDuel.Core
{
    public struct ActiveBuff
    {
        public AbilityKey Key;
        public int CyclesLeft;
        public bool IsActive => CyclesLeft > 0;
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
        /// Where he has been told to run to this cycle. Only meaningful while PlannedAction is Move.
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
        /// What he is fighting with. Set from his training when the round starts and replaced by
        /// anything he picks up off the sand - never emptied. A weapon is what a gladiator is, not
        /// a charge he spends.
        /// </summary>
        public WeaponKind Weapon = WeaponKind.None;

        /// <summary>
        /// True for a weapon taken off the arena floor rather than brought in.
        ///
        /// The gilded copies lying on the sand are the same three weapons, better made. It is the
        /// only reason to break off and cross the arena for one, and the gold is the whole of how
        /// the player is told so.
        /// </summary>
        public bool WeaponIsGilded;

        public WeaponDef WeaponDef => WeaponDef.Get(Weapon);

        /// <summary>Whether he is behind a shield. A property of the weapon now, not a slot.</summary>
        public bool HasShield => Weapon == WeaponKind.SwordAndShield;

        /// <summary>Arms him with the weapon he trained on. Called when he enters the arena.</summary>
        public void EquipTrainedWeapon()
        {
            Weapon = Def.SkilledWith;
            WeaponIsGilded = false;
        }

        /// <summary>True when he is holding something he was never trained to hold.</summary>
        public bool IsUntrained => Weapon != WeaponKind.None && Weapon != Def.SkilledWith;

        /// <summary>Cycles of bleeding still to come. Zero means the wound has closed.</summary>
        public int BleedCyclesLeft;

        /// <summary>What one cycle of the current bleed costs.</summary>
        public float BleedPerCycle;

        public bool IsBleeding => BleedCyclesLeft > 0;

        /// <summary>
        /// Opens a wound, or re-opens the one already there.
        ///
        /// A fresh cut restarts the count, which is the point of twin swords: keep landing and the
        /// bleed never runs out. It takes the worse of the two rates rather than the newer one - a
        /// light blow arriving after a heavy one should not talk the wound down.
        /// </summary>
        public void ApplyBleed(float rawAttackDamage)
        {
            BleedPerCycle = Mathf.Max(BleedPerCycle, rawAttackDamage * GameConstants.BleedFraction);
            BleedCyclesLeft = GameConstants.BleedCycles;
        }

        /// <summary>
        /// Bleeds him for one cycle and returns what it cost. Zero when there is no wound.
        ///
        /// Called at the top of the action phase, before anyone moves: a wound that only settled up
        /// at the end of a cycle would let a dying fighter spend that cycle as though he were whole.
        /// </summary>
        public float TickBleed()
        {
            if (BleedCyclesLeft <= 0) return 0f;

            BleedCyclesLeft--;
            float amount = BleedPerCycle;
            if (BleedCyclesLeft == 0) BleedPerCycle = 0f;

            TakeDamage(amount);
            return amount;
        }

        public float Rage = 0f;
        public int AbilityLockedCycles = 0;
        public ActiveBuff Buff;

        // per-cycle planning/action bookkeeping
        public ActionType PlannedAction = ActionType.None;
        public Vector2 PlannedAimDirection;
        public float PlannedPower; // 0..1 pull strength for Move
        public bool AbilityArmed;   // toggle set during Planning; consumed at the start of Action
        public bool DealtDamageThisCycle;
        public bool TookDamageThisCycle;

        // Mongoose (Hilius) support: how many attacks this gladiator still gets this cycle.
        // Read and decremented by GameManager when a collision or a pass-by resolves.
        public int AttacksRemainingThisCycle = 1;

        /// <summary>
        /// Blows he lands per exchange: what the weapon swings, doubled while Mongoose is up.
        ///
        /// Multiplied rather than overridden, so Hilius on twin swords gets four light blows and not
        /// two - the ability says "twice as many attacks", and a weapon that already attacks twice
        /// should not quietly cancel half of it.
        /// </summary>
        public int AttacksPerCycle
            => WeaponDef.Attacks * (Buff.IsActive && Buff.Key == AbilityKey.Mongoose ? 2 : 1);

        public GladiatorInstance(GladiatorDef def)
        {
            Def = def;
            Hp = def.MaxHp;

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
            => WithinSwing(myPos, Facing, WeaponDef, targetPos);

        /// <summary>
        /// The same test with everything spelled out, for the strike prediction - which walks a
        /// future in which he is holding a weapon he has not picked up yet and running a heading he
        /// does not have yet, so it cannot ask a gladiator about himself.
        /// </summary>
        public static bool WithinSwing(Vector2 from, Vector2 facing, WeaponDef weapon, Vector2 target)
        {
            var toTarget = target - from;
            float distance = toTarget.magnitude;
            if (distance > weapon.Reach) return false;

            // A body pressed against his own is inside every arc there is. Without this a collision
            // - two men who have run into each other - could resolve as a miss because one of them
            // was looking over the other's shoulder, which is not something either of them would
            // recognise as happening.
            if (distance <= GameConstants.CollideDistance) return true;

            var look = facing.sqrMagnitude > 0.000001f ? facing.normalized : Vector2.up;
            return Vector2.Angle(look, toTarget) <= weapon.SwingArcDegrees * 0.5f;
        }

        public void AddRage(float amount)
        {
            if (AbilityLockedCycles > 0) return; // locked out after a recent activation
            Rage = Mathf.Min(GameConstants.RageMax, Rage + amount);
        }

        public bool CanActivateAbility => AbilityLockedCycles <= 0 && Rage >= GameConstants.RageMax;

        public void ActivateAbility()
        {
            if (!CanActivateAbility) return;

            if (Def.Ability == AbilityKey.SecondWind)
            {
                // Instant: the health comes back the moment it fires and nothing is left running. A
                // lingering buff would light his ability marker for two cycles of doing nothing.
                Hp = Mathf.Min(Def.MaxHp, Hp + Def.MaxHp * GameConstants.SecondWindHealFraction);
            }
            else
            {
                Buff = new ActiveBuff { Key = Def.Ability, CyclesLeft = 2 };
            }

            // The ability fires at the start of Action, after BeginCycle already set the attack
            // budget for this cycle - so Mongoose has to top it up for the cycle it was used in.
            AttacksRemainingThisCycle = AttacksPerCycle;
            Rage = 0f;
            AbilityLockedCycles = GameConstants.AbilityLockCycles + 1; // +1 so it also skips the cycle it was used in
        }

        /// <summary>Call once per new cycle, before planning is (re)opened.</summary>
        public void BeginCycle()
        {
            // The plan MUST be cleared here: GameManager.AutoFillMissingPlans only fills a slot that
            // is still ActionType.None, so a stale plan would silently replay last cycle's move -
            // and the bot, which is only asked for a decision when its slot is empty, would repeat
            // its very first decision for the rest of the match.
            PlannedAction = ActionType.None;
            PlannedAimDirection = Vector2.zero;
            PlannedPower = 0f;
            PlannedTarget = Vector2.zero;
            StopRunning();

            AbilityArmed = false;
            DealtDamageThisCycle = false;
            TookDamageThisCycle = false;
            if (AbilityLockedCycles > 0) AbilityLockedCycles--;
            if (Buff.CyclesLeft > 0)
            {
                Buff.CyclesLeft--;
            }
            // Derived AFTER the buff has been aged, so the second Mongoose cycle still gets 2 attacks
            // and the cycle right after the buff expires drops back to 1.
            AttacksRemainingThisCycle = AttacksPerCycle;
        }

        /// <summary>Passive + reactive rage gain, applied at the end of an action phase.</summary>
        public void ResolveCycleRage()
        {
            AddRage(GameConstants.RagePerCyclePassive);
            if (DealtDamageThisCycle) AddRage(GameConstants.RageBonusOnDealDamage);
            if (TookDamageThisCycle) AddRage(GameConstants.RageBonusOnTakeDamage);
        }

        public void TakeDamage(float amount)
        {
            Hp = Mathf.Max(0f, Hp - amount);
            TookDamageThisCycle = true;
            if (Hp <= 0f) Alive = false;
        }

        public void ResetForNewRound()
        {
            // Winner persists with current HP (not healed) - only a freshly-picked gladiator gets this.
            StopRunning();
            PlannedAction = ActionType.None;
            AbilityArmed = false;
            Buff = default;
            AbilityLockedCycles = 0;

            // Back to his own weapon each round. A gilded one is the reward for crossing the arena
            // under fire during a round; carrying it into the next one for free would make the
            // first round the only one worth taking that risk in.
            EquipTrainedWeapon();
            AttacksRemainingThisCycle = AttacksPerCycle;

            // Wounds close between rounds. A bleed that survived would go on draining a fighter
            // through a round he was not in when it was opened.
            BleedCyclesLeft = 0;
            BleedPerCycle = 0f;
        }
    }
}
