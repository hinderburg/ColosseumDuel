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

        /// <summary>
        /// Resolves one attacker-&gt;defender blow and returns the damage that landed.
        ///
        /// The bleeding a weapon leaves is opened here rather than by the caller: it is a property
        /// of the blow, and every place a blow can happen would otherwise have to remember to do it.
        /// </summary>
        /// <summary>
        /// What a blow is worth for the part of the man it landed on.
        ///
        /// A property of the blow rather than of the defence, so it scales the raw damage and every
        /// mitigation still applies on top - a shield held by a man who has been got behind is a
        /// shield facing the wrong way, but it is still a shield.
        ///
        /// It scales the bleeding with it, which follows: a wound opened from behind is a deeper
        /// wound, not a shallower one that keeps bleeding at the same rate.
        /// </summary>
        public static float SectorMultiplier(HitSector sector)
        {
            switch (sector)
            {
                case HitSector.Back: return GameConstants.BackAttackMult;
                case HitSector.Side: return GameConstants.FlankAttackMult;
                default: return 1f;
            }
        }

        public static float DealDamage(GladiatorInstance attacker, GladiatorInstance defender)
        {
            float raw = ComputeAttackDamage(attacker)
                        * SectorMultiplier(defender.SectorHitFrom(attacker.Pos));
            float final = ApplyMitigation(defender, raw);

            defender.TakeDamage(final);
            attacker.DealtDamageThisCycle = true;

            if (attacker.WeaponDef.Bleeds) defender.ApplyBleed(raw);
            return final;
        }
    }
}
