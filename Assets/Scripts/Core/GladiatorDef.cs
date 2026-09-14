using System.Collections.Generic;

namespace ColosseumDuel.Core
{
    /// <summary>
    /// Static, immutable stat block for a gladiator archetype. Plain C# so it can be unit-tested
    /// without touching UnityEngine; wrap it in a ScriptableObject later if you want designers to
    /// tweak stats from the Inspector instead of this file.
    ///
    /// Speed is back, and it is what limits a run: how far one phase of running carries him (see
    /// GladiatorInstance.DashReach). A drawn or tapped run is cut off where that runs out.
    /// </summary>
    public sealed class GladiatorDef
    {
        public readonly GladiatorId Id;
        public readonly string Name;
        public readonly float MaxHp;
        public readonly float Damage;

        /// <summary>
        /// How fast he runs. One point of it is GameConstants.SpeedScale units of run in a phase, so
        /// Hilius at 20 goes twice as far as Brutius at 10.
        /// </summary>
        public readonly float Speed;

        /// <summary>
        /// The three abilities he can take into a fight. The player picks one for him on the roster
        /// screen; until then, and for the bot's tests, he takes the first. See AbilityDef for what
        /// each does, and GladiatorInstance.Ability for the one he actually has.
        /// </summary>
        public readonly IReadOnlyList<AbilityKey> Abilities;

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

        public GladiatorDef(GladiatorId id, string name, float maxHp, float damage, float speed,
            AbilityKey[] abilities, WeaponKind skilledWith, float buildWidth = 1f, float buildHeight = 1f,
            int level = 1)
        {
            Id = id;
            Name = name;
            MaxHp = maxHp;
            Damage = damage;
            Speed = speed;
            Abilities = abilities;
            SkilledWith = skilledWith;
            BuildWidth = buildWidth;
            BuildHeight = buildHeight;
            Level = level;
        }

        // The stat line is deliberately low against the old one - a tenth of what it was. Damage is
        // now multiplied by the weapon rather than standing on its own, and the weapons range from
        // seven tenths to one and a half, so the numbers here are what a fighter is worth before
        // anyone hands him anything.

        public static readonly GladiatorDef Brutius = new GladiatorDef(
            GladiatorId.Brutius, "Brutius", maxHp: 150f, damage: 21f, speed: 13f,
            abilities: new[] { AbilityKey.Earthshaker, AbilityKey.Rampage, AbilityKey.StoneSkin },
            skilledWith: WeaponKind.TwoHandedMace, buildWidth: 1.2f);

        public static readonly GladiatorDef Barbarius = new GladiatorDef(
            GladiatorId.Barbarius, "Barbarius", maxHp: 125f, damage: 23f, speed: 15f,
            abilities: new[] { AbilityKey.Bloodlust, AbilityKey.Frenzy, AbilityKey.Berserk },
            skilledWith: WeaponKind.DualSwords);

        public static readonly GladiatorDef Hilius = new GladiatorDef(
            GladiatorId.Hilius, "Hilius", maxHp: 110f, damage: 28f, speed: 20f,
            abilities: new[] { AbilityKey.Backstab, AbilityKey.Riposte, AbilityKey.Mongoose },
            skilledWith: WeaponKind.SwordAndShield, buildWidth: 0.85f, buildHeight: 0.85f);

        // The second three. Each walks in with a weapon of his own and abilities nobody else has;
        // the numbers are set against the first three by the bot-against-bot table in
        // BalanceHarness rather than by argument.

        public static readonly GladiatorDef Scutarius = new GladiatorDef(
            GladiatorId.Scutarius, "Scutarius", maxHp: 125f, damage: 25f, speed: 12f,
            abilities: new[] { AbilityKey.Bulwark, AbilityKey.Testudo, AbilityKey.ShieldBash },
            skilledWith: WeaponKind.ScutumAndGladius, buildWidth: 1.1f);

        public static readonly GladiatorDef Hastarius = new GladiatorDef(
            GladiatorId.Hastarius, "Hastarius", maxHp: 125f, damage: 27f, speed: 16f,
            abilities: new[] { AbilityKey.Lunge, AbilityKey.Brace, AbilityKey.SecondWind },
            skilledWith: WeaponKind.SpearAndShield, buildHeight: 1.08f);

        public static readonly GladiatorDef Retiarius = new GladiatorDef(
            GladiatorId.Retiarius, "Retiarius", maxHp: 135f, damage: 22f, speed: 18f,
            abilities: new[] { AbilityKey.Net, AbilityKey.Shackles, AbilityKey.TridentThrow },
            skilledWith: WeaponKind.Trident, buildWidth: 0.9f);

        public static readonly IReadOnlyList<GladiatorDef> All = new List<GladiatorDef>
        {
            Brutius, Barbarius, Hilius, Scutarius, Hastarius, Retiarius
        };

        public static GladiatorDef Get(GladiatorId id)
        {
            switch (id)
            {
                case GladiatorId.Brutius: return Brutius;
                case GladiatorId.Barbarius: return Barbarius;
                case GladiatorId.Hilius: return Hilius;
                case GladiatorId.Scutarius: return Scutarius;
                case GladiatorId.Hastarius: return Hastarius;
                case GladiatorId.Retiarius: return Retiarius;
                default: return Brutius;
            }
        }
    }
}
