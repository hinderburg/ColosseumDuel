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

    public enum WeaponType
    {
        None,
        OneHanded, // axe: 1.5x damage
        TwoHanded  // trident: full damage on a pass-by hit instead of the usual 50%
    }

    public enum ItemKind
    {
        Weapon,
        Shield,
        Random // a "random" pickup slot - resolve to whichever bonus items the design calls for
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
        Hilius
    }

    public enum AbilityKey
    {
        Spirit,   // Brutius - "Дух": +50% speed for 2 cycles
        Fury,     // Barbarius - "Ярость": -25% damage taken for 2 cycles
        Mongoose  // Hilius - "Мангуст": 2 attacks per cycle for 2 cycles
    }
}
