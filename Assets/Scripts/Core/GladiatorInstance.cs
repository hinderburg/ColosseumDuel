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

        public WeaponType Weapon = WeaponType.None;
        public bool HasShield = false;

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

        public int AttacksPerCycle => Buff.IsActive && Buff.Key == AbilityKey.Mongoose ? 2 : 1;

        public GladiatorInstance(GladiatorDef def)
        {
            Def = def;
            Hp = def.MaxHp;
        }

        public bool IsDefending => PlannedAction == ActionType.Defend;

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
            AttacksRemainingThisCycle = 1;
        }
    }
}
