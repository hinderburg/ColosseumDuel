namespace ColosseumDuel.Core
{
    public enum MatchPhase
    {
        Start,      // pre-match overlay
        Pick,       // players choosing who enters the arena for this clash
        BoonPick,   // a side that has sent a new man in chooses one boon of three for the rest of the match
        Reveal,     // picks revealed simultaneously
        Planning,   // players choose Move / Defend (+ optional Ability) for the coming round
        Action,     // chosen actions execute
        ClashEnd,   // one of the two active gladiators just died
        MatchEnd    // one player has zero gladiators left
    }

    /// <summary>A boon a side takes for the rest of the match, on every man of it. See BoonDef.</summary>
    public enum BoonKey
    {
        SharpenedSteel,
        IronHide,
        FleetFoot,
        LongReach,
        BattleSpirit,
        Flanker,
        Stalwart,
        Quartermaster
    }

    /// <summary>What lies on the sand to be run over. See ArenaPickup.</summary>
    public enum PickupKind
    {
        Blessing,   // the weapon blessing: harder blows for three rounds
        Apple,      // a third of his health back
        Horn        // rage onto the meter
    }

    public enum ActionType
    {
        None,
        Move,
        Defend
    }

    /// <summary>
    /// The six ways to fight, one per archetype. See WeaponDef for what each one does; every
    /// gladiator is trained in exactly one of them and starts the match holding it.
    /// </summary>
    public enum WeaponKind
    {
        None,
        DualSwords,
        SwordAndShield,
        TwoHandedMace,
        ScutumAndGladius,
        SpearAndShield,
        Trident
    }

    public enum PlayerSide
    {
        P1,
        Bot
    }

    public enum GladiatorId
    {
        Brutius,
        Barbarius,
        Hilius,
        Scutarius,
        Hastarius,
        Retiarius
    }

    /// <summary>
    /// Which part of a gladiator a blow landed on, measured against the way he is facing.
    ///
    /// Three sectors of ninety degrees each: forty-five either side of his own nose is the front,
    /// the ninety behind him is his back, and what is left is a flank on each side. The split is
    /// what makes which way a man is looking a decision rather than decoration.
    /// </summary>
    public enum HitSector
    {
        Front,
        Side,
        Back,
    }

    /// <summary>
    /// Every ability in the game, three to an archetype and in his order. See AbilityDef for what
    /// each one does and GladiatorDef.Abilities for whose they are.
    /// </summary>
    public enum AbilityKey
    {
        Earthshaker,  // Brutius
        Rampage,
        StoneSkin,
        Bloodlust,    // Barbarius
        Frenzy,
        Berserk,
        Backstab,     // Hilius
        Riposte,
        Mongoose,
        Bulwark,      // Scutarius
        Testudo,
        ShieldBash,
        Lunge,        // Hastarius
        Brace,
        SecondWind,
        Net,          // Retiarius
        Shackles,
        TridentThrow
    }
}
