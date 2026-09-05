using System.Collections.Generic;

namespace ColosseumDuel.Core
{
    /// <summary>
    /// Static, immutable stat block for a gladiator archetype. Plain C# so it can be unit-tested
    /// without touching UnityEngine; wrap it in a ScriptableObject later if you want designers to
    /// tweak stats from the Inspector instead of this file.
    /// </summary>
    public sealed class GladiatorDef
    {
        public readonly GladiatorId Id;
        public readonly string Name;
        public readonly float MaxHp;
        public readonly float Damage;
        public readonly float Speed;
        public readonly AbilityKey Ability;
        public readonly string AbilityName;
        public readonly string AbilityDescription;

        /// <summary>
        /// Shown on the roster cards. Nothing consumes it yet - progression between matches is a
        /// design question the doc leaves open - so every archetype starts at 1 and the HUD simply
        /// reports whatever is here.
        /// </summary>
        public readonly int Level;

        public GladiatorDef(GladiatorId id, string name, float maxHp, float damage, float speed,
            AbilityKey ability, string abilityName, string abilityDescription, int level = 1)
        {
            Id = id;
            Name = name;
            MaxHp = maxHp;
            Damage = damage;
            Speed = speed;
            Ability = ability;
            AbilityName = abilityName;
            AbilityDescription = abilityDescription;
            Level = level;
        }

        public static readonly GladiatorDef Brutius = new GladiatorDef(
            GladiatorId.Brutius, "Брутиус", maxHp: 200f, damage: 20f, speed: 10f,
            ability: AbilityKey.Spirit, abilityName: "Дух",
            abilityDescription: "+50% скорости на 2 цикла");

        public static readonly GladiatorDef Barbarius = new GladiatorDef(
            GladiatorId.Barbarius, "Барбариус", maxHp: 100f, damage: 26f, speed: 15f,
            ability: AbilityKey.Fury, abilityName: "Ярость",
            abilityDescription: "-25% получаемого урона на 2 цикла");

        public static readonly GladiatorDef Hilius = new GladiatorDef(
            GladiatorId.Hilius, "Хилиус", maxHp: 150f, damage: 14f, speed: 20f,
            ability: AbilityKey.Mongoose, abilityName: "Мангуст",
            abilityDescription: "2 атаки за цикл на 2 цикла");

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
