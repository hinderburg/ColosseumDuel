namespace ColosseumDuel.Core
{
    /// <summary>
    /// Pure damage-math. Kept free of any Unity/physics dependency so it is easy to unit test.
    ///
    /// A blow is the wielder's own Damage stat put through his weapon, and then through whatever
    /// the defender is doing about it. Nothing is consumed: weapons used to break after one hit,
    /// which made sense while they were bonuses lying on the floor and stopped making sense the
    /// moment a gladiator's weapon became half of who he is.
    /// </summary>
    public static class CombatResolver
    {
        /// <summary>What one blow is worth before the defender does anything about it.</summary>
        public static float ComputeAttackDamage(GladiatorInstance attacker)
        {
            float damage = attacker.Def.Damage * attacker.WeaponDef.DamageMultiplier;
            if (attacker.WeaponIsGilded) damage *= GameConstants.GildedWeaponMult;
            return damage;
        }

        public static float ApplyMitigation(GladiatorInstance defender, float rawDamage)
        {
            float mult = defender.WeaponDef.IncomingDamageMultiplier; // the shield, if he has one
            if (defender.IsDefending) mult *= GameConstants.DefendDamageMult;
            if (defender.Buff.IsActive && defender.Buff.Key == AbilityKey.Fury) mult *= 0.75f;
            return rawDamage * mult;
        }

        /// <summary>Resolves one attacker-&gt;defender blow and returns the damage that landed.</summary>
        public static float DealDamage(GladiatorInstance attacker, GladiatorInstance defender)
        {
            float raw = ComputeAttackDamage(attacker);
            float final = ApplyMitigation(defender, raw);

            defender.TakeDamage(final);
            attacker.DealtDamageThisCycle = true;
            return final;
        }
    }
}
