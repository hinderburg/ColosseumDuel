using System.Collections.Generic;

namespace ColosseumDuel.Core
{
    /// <summary>
    /// Static, immutable stat block for a gladiator archetype. Plain C# so it can be unit-tested
    /// without touching UnityEngine; wrap it in a ScriptableObject later if you want designers to
    /// tweak stats from the Inspector instead of this file.
    ///
    /// There is no speed stat. There was one, and it decided how far a man could get in a phase;
    /// a tap now always takes him all the way to where it landed, inside the one phase, so how fast
    /// he is stopped being a thing that could differ between archetypes. They are told apart by
    /// what they can take, what they can deal, what they carry and what their ability does.
    /// </summary>
    public sealed class GladiatorDef
    {
        public readonly GladiatorId Id;
        public readonly string Name;
        public readonly float MaxHp;
        public readonly float Damage;
        public readonly AbilityKey Ability;
        public readonly string AbilityName;
        public readonly string AbilityDescription;

        /// <summary>
        /// The weapon he is trained in. He starts the match holding it, and anything else he picks
        /// up off the sand is marked in the HUD as outside his training.
        /// </summary>
        public readonly WeaponKind SkilledWith;

        /// <summary>
        /// How wide and how tall he is against an ordinary figure.
        ///
        /// Three archetypes that fought differently and looked identical was the standing complaint:
        /// on an arena this size the only thing separating them was the colour of a body, and colour
        /// is also what the danger rings and the hazard use. Silhouette reads first and reads from
        /// further away - a broad one is the one who takes a beating, a small thin one the one who
        /// fights by landing blows rather than by standing them.
        /// </summary>
        public readonly float BuildWidth;
        public readonly float BuildHeight;

        /// <summary>
        /// Shown on the roster cards. Nothing consumes it yet - progression between matches is a
        /// design question the doc leaves open - so every archetype starts at 1 and the HUD simply
        /// reports whatever is here.
        /// </summary>
        public readonly int Level;

        public GladiatorDef(GladiatorId id, string name, float maxHp, float damage,
            AbilityKey ability, string abilityName, string abilityDescription,
            WeaponKind skilledWith, float buildWidth = 1f, float buildHeight = 1f, int level = 1)
        {
            Id = id;
            Name = name;
            MaxHp = maxHp;
            Damage = damage;
            Ability = ability;
            AbilityName = abilityName;
            AbilityDescription = abilityDescription;
            SkilledWith = skilledWith;
            BuildWidth = buildWidth;
            BuildHeight = buildHeight;
            Level = level;
        }

        // The stat line is deliberately low against the old one - a tenth of what it was. Damage is
        // now multiplied by the weapon rather than standing on its own, and the weapons range from
        // seven tenths to one and a half, so the numbers here are what a fighter is worth before
        // anyone hands him anything.

        // Brutius used to have Spirit, half as fast again for two cycles. With no speed left to
        // add to, it became Second Wind: he is the one who does not die - twice anybody else's
        // health - and an ability that gives some of it back is that, sharpened. Instant rather
        // than a buff, so it cannot be wasted by the round ending under it, and it is plainly not
        // Fury (taking less) or Mongoose (hitting more often).
        public static readonly GladiatorDef Brutius = new GladiatorDef(
            GladiatorId.Brutius, "Brutius", maxHp: 200f, damage: 10f,
            ability: AbilityKey.SecondWind, abilityName: "Second Wind",
            abilityDescription: "Heals 20% of max health",
            skilledWith: WeaponKind.TwoHandedMace, buildWidth: 1.2f);

        public static readonly GladiatorDef Barbarius = new GladiatorDef(
            GladiatorId.Barbarius, "Barbarius", maxHp: 100f, damage: 13f,
            ability: AbilityKey.Fury, abilityName: "Fury",
            abilityDescription: "-25% damage taken for 2 cycles",
            skilledWith: WeaponKind.DualSwords);

        public static readonly GladiatorDef Hilius = new GladiatorDef(
            GladiatorId.Hilius, "Hilius", maxHp: 150f, damage: 7f,
            ability: AbilityKey.Mongoose, abilityName: "Mongoose",
            abilityDescription: "2 attacks per cycle, for 2 cycles",
            skilledWith: WeaponKind.SwordAndShield, buildWidth: 0.85f, buildHeight: 0.85f);

        public static readonly IReadOnlyList<GladiatorDef> All = new List<GladiatorDef>
        {
            Brutius, Barbarius, Hilius
        };

        public static GladiatorDef Get(GladiatorId id)
        {
            switch (id)
            {
                case GladiatorId.Brutius: return Brutius;
                case GladiatorId.Barbarius: return Barbarius;
                case GladiatorId.Hilius: return Hilius;
                default: return Brutius;
            }
        }
    }
}
