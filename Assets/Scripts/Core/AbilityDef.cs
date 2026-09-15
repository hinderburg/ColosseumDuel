using System.Collections.Generic;

namespace ColosseumDuel.Core
{
    /// <summary>
    /// What an ability is called, what it does and how long it lasts - the words the roster screen,
    /// the ability button and the name that goes up over a man all read from.
    ///
    /// The mechanics are where the rest of the rules are: speed and reach in GladiatorInstance, the
    /// damage sums in CombatResolver, and the ones that act on the other man or at a collision in
    /// GameManager. This is only the card.
    /// </summary>
    public sealed class AbilityDef
    {
        public readonly AbilityKey Key;
        public readonly string Name;

        /// <summary>What it does, without how long for - see <see cref="Summary"/> for both.</summary>
        public readonly string Description;

        /// <summary>
        /// Rounds it lasts, the one it fires in counted. Zero for one that is spent the moment it
        /// fires. Fired at the top of an action phase, so one round is that phase and no more.
        /// </summary>
        public readonly int Rounds;

        public AbilityDef(AbilityKey key, string name, string description, int rounds)
        {
            Key = key;
            Name = name;
            Description = description;
            Rounds = rounds;
        }

        public string DurationText => Rounds <= 0 ? "instant" : Rounds == 1 ? "1 round" : $"{Rounds} rounds";

        /// <summary>What it does and for how long, on one line: "+50% speed, -30% damage (2 rounds)".</summary>
        public string Summary => $"{Description} ({DurationText})";

        public static readonly IReadOnlyList<AbilityDef> All = new List<AbilityDef>
        {
            // Brutius: the tank. Nothing here makes him faster for free any more - that was the one
            // weakness he had, and a way round it made him win almost every duel.
            new AbilityDef(AbilityKey.Earthshaker, "Earthshaker",
                "the next blow hits +50%, throws twice as far and roots them next round", 1),
            new AbilityDef(AbilityKey.Rampage, "Rampage", "+50% speed, +20% damage", 2),
            new AbilityDef(AbilityKey.StoneSkin, "Stone Skin", "-30% damage taken", 2),

            // Barbarius: the glass cannon.
            new AbilityDef(AbilityKey.Bloodlust, "Bloodlust", "heals 80% of the damage he deals", 2),
            new AbilityDef(AbilityKey.Frenzy, "Frenzy", "his bleeds deal double and last 3 rounds", 2),
            new AbilityDef(AbilityKey.Berserk, "Berserk", "+50% damage, +25% damage taken", 2),

            // Hilius: the fast one with the light blow.
            new AbilityDef(AbilityKey.Backstab, "Backstab", "every blow lands as if from behind (+40%)", 2),
            new AbilityDef(AbilityKey.Riposte, "Riposte", "blows from the front return 50% to the striker", 2),
            new AbilityDef(AbilityKey.Mongoose, "Mongoose", "2 attacks per round", 2),

            // Scutarius: the wall.
            new AbilityDef(AbilityKey.Bulwark, "Bulwark", "no damage from the front", 2),
            new AbilityDef(AbilityKey.Testudo, "Testudo", "-60% damage taken, half speed", 2),
            new AbilityDef(AbilityKey.ShieldBash, "Shield Bash", "+50% damage; a hit throws them 3x as far and stops their run", 2),

            // Hastarius: the reach.
            new AbilityDef(AbilityKey.Lunge, "Lunge", "+50% reach and damage", 2),
            new AbilityDef(AbilityKey.Brace, "Brace", "a man charging into his front is struck first and stopped", 2),
            new AbilityDef(AbilityKey.SecondWind, "Second Wind", "heals 25% of his health", 0),

            // Retiarius: control.
            new AbilityDef(AbilityKey.Net, "Net", "the enemy cannot move or turn", 2),
            new AbilityDef(AbilityKey.Shackles, "Shackles", "the enemy cannot use his ability, and his rage burns", 2),
            new AbilityDef(AbilityKey.TridentThrow, "Trident Throw", "strikes at twice the reach", 1),
        };

        private static readonly Dictionary<AbilityKey, AbilityDef> ByKey = Index();

        private static Dictionary<AbilityKey, AbilityDef> Index()
        {
            var byKey = new Dictionary<AbilityKey, AbilityDef>();
            foreach (var def in All) byKey[def.Key] = def;
            return byKey;
        }

        public static AbilityDef Get(AbilityKey key) => ByKey[key];
    }
}
