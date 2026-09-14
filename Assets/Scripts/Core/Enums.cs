namespace ColosseumDuel.Core
{
    public enum MatchPhase
    {
        Start,      // pre-match overlay
        Pick,       // players choosing who enters the arena for this round
        Reveal,     // picks revealed simultaneously
        Planning,   // players choose Move / Defend (+ optional Ability) for the coming cycle
        Action,     // chosen actions execute
        RoundEnd,   // one of the two active gladiators just died
        MatchEnd    // one player has zero gladiators left
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

    public enum AbilityKey
    {
        Spirit,   // Brutius - +50% speed for 2 cycles
        Fury,     // Barbarius - -25% damage taken for 2 cycles
        Mongoose,   // Hilius - 2 attacks per cycle for 2 cycles
        Bulwark,    // Scutarius - blows from the front do nothing for 2 cycles
        SecondWind, // Hastarius - heals a quarter of his health at once
        Net         // Retiarius - the enemy runs at half speed for 2 cycles
    }
}
