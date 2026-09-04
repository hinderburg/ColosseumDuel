namespace ColosseumDuel.Core
{
    /// <summary>
    /// Pure damage-math, ported from computeAttackDamage / applyMitigation / dealDamage in the
    /// JS prototype. Kept free of any Unity/physics dependency so it's easy to unit test.
    /// </summary>
    public static class CombatResolver
    {
        public static float WeaponMultiplier(GladiatorInstance attacker, bool isPassBy)
        {
            if (attacker.Weapon == WeaponType.OneHanded) return GameConstants.OneHandedWeaponMult;
            if (attacker.Weapon == WeaponType.TwoHanded && isPassBy) return GameConstants.TwoHandedPassByDamageMult / GameConstants.PassByDamageMult;
            return 1f;
        }

        public static float ComputeAttackDamage(GladiatorInstance attacker, bool isCollision)
        {
            float baseDamage = attacker.Def.Damage;
            float mult = WeaponMultiplier(attacker, isPassBy: !isCollision);
            float shareMult = isCollision ? 1f : GameConstants.PassByDamageMult;
            return baseDamage * mult * shareMult;
        }

        public static float ApplyMitigation(GladiatorInstance defender, float rawDamage)
        {
            float mult = 1f;
            if (defender.IsDefending) mult *= GameConstants.DefendDamageMult;
            if (defender.HasShield) mult *= GameConstants.ShieldDamageMult;
            if (defender.Buff.IsActive && defender.Buff.Key == AbilityKey.Fury) mult *= 0.75f; // -25% damage taken
            return rawDamage * mult;
        }

        /// <summary>
        /// Resolves one attacker->defender hit. Consumes the attacker's weapon and the defender's
        /// shield (single-use, per design) and returns the final damage applied.
        /// </summary>
        /// <remarks>
        /// Breaking an item does NOT respawn anything here: ItemSystem.ApplyPickup already respawns
        /// a slot the moment it is picked up, so the arena keeps exactly ItemCountOnArena items on
        /// the floor at all times. Carried items simply leave the world when they break.
        /// </remarks>
        public static float DealDamage(GladiatorInstance attacker, GladiatorInstance defender, bool isCollision)
        {
            float raw = ComputeAttackDamage(attacker, isCollision);
            float final = ApplyMitigation(defender, raw);

            defender.TakeDamage(final);
            attacker.DealtDamageThisCycle = true;

            // single-use consumption
            attacker.Weapon = WeaponType.None;
            defender.HasShield = false;

            return final;
        }
    }
}
