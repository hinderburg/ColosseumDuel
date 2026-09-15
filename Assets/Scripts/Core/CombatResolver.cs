namespace ColosseumDuel.Core
{
    /// <summary>
    /// Pure damage-math. Kept free of any Unity/physics dependency so it is easy to unit test.
    ///
    /// A blow is the wielder's own Damage stat put through his weapon, and then through whatever
    /// the defender is doing about it. Nothing is consumed: weapons used to break after one hit,
    /// which made sense while they were bonuses lying on the floor and stopped making sense the
    /// moment a gladiator's weapon became half of who he is.
    ///
    /// The abilities that change a blow's worth live here too, on both ends of it: what the
    /// striker's ability adds (Berserk, Rampage, Backstab, Frenzy, Bloodlust) and what the
    /// defender's takes off or sends back (Stone Skin, Testudo, Berserk, Bulwark, Riposte).
    /// </summary>
    public static class CombatResolver
    {
        /// <summary>What one blow is worth before the defender does anything about it.</summary>
        public static float ComputeAttackDamage(GladiatorInstance attacker)
        {
            float damage = attacker.Def.Damage * attacker.WeaponDef.DamageMultiplier;
            if (attacker.HasBoon(BoonKey.SharpenedSteel)) damage *= GameConstants.SharpenedSteelDamageMult;
            if (attacker.WeaponBuffed) damage *= GameConstants.WeaponBuffDamageMult;
            if (attacker.Has(AbilityKey.Berserk)) damage *= GameConstants.BerserkDamageMult;
            if (attacker.Has(AbilityKey.Rampage)) damage *= GameConstants.RampageDamageMult;
            if (attacker.Has(AbilityKey.Lunge)) damage *= GameConstants.LungeDamageMult;
            if (attacker.Has(AbilityKey.ShieldBash)) damage *= GameConstants.ShieldBashDamageMult;
            if (attacker.Has(AbilityKey.Earthshaker)) damage *= GameConstants.EarthshakerDamageMult;
            return damage;
        }

        public static float ApplyMitigation(GladiatorInstance defender, float rawDamage)
        {
            float mult = defender.WeaponDef.IncomingDamageMultiplier; // the shield, if he has one
            if (defender.IsDefending)
                mult *= defender.HasBoon(BoonKey.Stalwart) ? GameConstants.StalwartDefendMult : GameConstants.DefendDamageMult;
            if (defender.HasBoon(BoonKey.IronHide)) mult *= GameConstants.IronHideTakenMult;
            if (defender.Has(AbilityKey.StoneSkin)) mult *= GameConstants.StoneSkinTakenMult;
            if (defender.Has(AbilityKey.Testudo)) mult *= GameConstants.TestudoTakenMult;
            if (defender.Has(AbilityKey.Berserk)) mult *= GameConstants.BerserkTakenMult;
            return rawDamage * mult;
        }

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
            => DealDamage(attacker, defender, out _);

        /// <summary>
        /// Resolves one attacker-&gt;defender blow and returns the damage that landed.
        /// <paramref name="returned"/> is what came back at the attacker off a Riposte - already
        /// taken off him here, and handed back so the caller can say so.
        ///
        /// The bleeding a weapon leaves is opened here rather than by the caller: it is a property
        /// of the blow, and every place a blow can happen would otherwise have to remember to do it.
        /// </summary>
        public static float DealDamage(GladiatorInstance attacker, GladiatorInstance defender, out float returned)
        {
            returned = 0f;

            // Backstab: wherever he stands, his blow lands as though he were behind the man - and a
            // blow from behind goes round a shield wall rather than into it.
            var sector = attacker.Has(AbilityKey.Backstab) ? HitSector.Back : defender.SectorHitFrom(attacker.Pos);

            // Bulwark: a blow into the front of the shield wall lands on nothing - no damage, no
            // wound, and no credit to the man who swung it.
            if (sector == HitSector.Front && defender.Has(AbilityKey.Bulwark))
                return 0f;

            float raw = ComputeAttackDamage(attacker) * SectorMultiplier(sector);
            if (sector != HitSector.Front && attacker.HasBoon(BoonKey.Flanker)) raw *= GameConstants.FlankerDamageMult;
            float final = ApplyMitigation(defender, raw);

            defender.TakeDamage(final);
            attacker.DealtDamageThisRound = true;

            if (attacker.WeaponDef.Bleeds)
            {
                // Frenzy: the wounds he opens run twice as deep and a round longer.
                if (attacker.Has(AbilityKey.Frenzy))
                    defender.ApplyBleed(raw, GameConstants.FrenzyBleedMult, GameConstants.FrenzyBleedRounds);
                else
                    defender.ApplyBleed(raw);
            }

            // Bloodlust: he drinks half of what he deals.
            if (attacker.Has(AbilityKey.Bloodlust) && attacker.Alive)
                attacker.Hp = System.Math.Min(attacker.Def.MaxHp, attacker.Hp + final * GameConstants.BloodlustHeal);

            // Riposte: a blow from the front is met, and half of it goes back the way it came.
            if (sector == HitSector.Front && defender.Has(AbilityKey.Riposte) && final > 0f)
            {
                returned = final * GameConstants.RiposteReturn;
                attacker.TakeDamage(returned);
            }

            return final;
        }
    }
}
