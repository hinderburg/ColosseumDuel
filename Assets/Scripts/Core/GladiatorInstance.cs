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
        /// How far a full-power dash carries this gladiator, in virtual units.
        ///
        /// Here rather than in the input layer because it is the same product the action phase
        /// actually integrates - aiming at a point needs it to work out how hard to pull, and a
        /// second copy of the formula would drift from this one the first time either factor moved.
        /// Ignores wall bounces, which change where he ends up but not how far he runs.
        /// </summary>
        public float DashReach() => EffectiveSpeed() * GameConstants.SpeedScale * GameConstants.ActionTime;

        public float EffectiveSpeed()
        {
            float speed = Def.Speed;
            if (Buff.IsActive && Buff.Key == AbilityKey.Spirit)
                speed *= 1.5f;
            return speed;
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
            Buff = new ActiveBuff { Key = Def.Ability, CyclesLeft = 2 };
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
            Vel = Vector2.zero;
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
