using System.Collections.Generic;

namespace ColosseumDuel.Core
{
    /// <summary>
    /// What a weapon does. One immutable block per kind, the same way GladiatorDef works.
    ///
    /// A weapon is no longer a consumable bonus that a gladiator might happen to be holding - it is
    /// half of what he is. Every gladiator starts with the one he is trained in and keeps it: the
    /// three weapons are three ways to fight, not three power-ups, and a fighter whose weapon
    /// vanished after one blow would spend most of the match being none of them.
    ///
    /// The three trade along the same axis in opposite directions. Twin swords hit twice for less
    /// and leave the enemy bleeding, but only at arm's length. A mace hits hardest and furthest and
    /// throws its target back, but lands once. Sword and shield sits in the middle of both and is
    /// the only one that answers back by halving what it takes.
    /// </summary>
    public sealed class WeaponDef
    {
        public readonly WeaponKind Kind;
        public readonly string Name;

        /// <summary>Share of the wielder's own Damage stat one blow carries.</summary>
        public readonly float DamageMultiplier;

        /// <summary>Blows landed per attack. Twin swords are the pair that strike twice.</summary>
        public readonly int Attacks;

        /// <summary>What the wielder takes, as a share. Below one only for the shield.</summary>
        public readonly float IncomingDamageMultiplier;

        /// <summary>Whether a blow leaves the target bleeding.</summary>
        public readonly bool Bleeds;

        /// <summary>How far a blow throws the target back, in virtual units. Zero for most.</summary>
        public readonly float Knockback;

        /// <summary>
        /// How far it strikes, centre to centre, in virtual units.
        ///
        /// The third axis the three trade along, and the one that decides who gets hit for free: a
        /// mace can end its run at a distance a pair of short blades cannot answer from. Never below
        /// CollideDistance, so running straight into somebody always counts as being in range - a
        /// weapon that could not reach a body pressed against it would be a bug, not a drawback.
        /// </summary>
        public readonly float Reach;

        public readonly string Description;

        public WeaponDef(WeaponKind kind, string name, float damageMultiplier, int attacks,
            float incomingDamageMultiplier, bool bleeds, float knockback, float reach, string description)
        {
            Kind = kind;
            Name = name;
            DamageMultiplier = damageMultiplier;
            Attacks = attacks;
            IncomingDamageMultiplier = incomingDamageMultiplier;
            Bleeds = bleeds;
            Knockback = knockback;
            Reach = reach;
            Description = description;
        }

        /// <summary>Bare hands: what a gladiator fights with if he somehow has nothing.</summary>
        public static readonly WeaponDef Unarmed = new WeaponDef(
            WeaponKind.None, "Unarmed", damageMultiplier: 0.5f, attacks: 1,
            incomingDamageMultiplier: 1f, bleeds: false, knockback: 0f,
            reach: GameConstants.CollideDistance,
            description: "Nothing but fists");

        public static readonly WeaponDef DualSwords = new WeaponDef(
            WeaponKind.DualSwords, "Twin swords", damageMultiplier: 0.7f, attacks: 2,
            incomingDamageMultiplier: 1f, bleeds: true, knockback: 0f,
            reach: GameConstants.GladiatorRadius * 2f + 8f,
            description: "Two light blows, and the wound keeps bleeding");

        public static readonly WeaponDef SwordAndShield = new WeaponDef(
            WeaponKind.SwordAndShield, "Sword and shield", damageMultiplier: 1f, attacks: 1,
            incomingDamageMultiplier: GameConstants.ShieldDamageMult, bleeds: false, knockback: 0f,
            reach: GameConstants.GladiatorRadius * 2f + 26f,
            description: "An even blow, and half the damage taken");

        public static readonly WeaponDef TwoHandedMace = new WeaponDef(
            WeaponKind.TwoHandedMace, "Two-handed mace", damageMultiplier: 1.5f, attacks: 1,
            incomingDamageMultiplier: 1f, bleeds: false, knockback: GameConstants.MaceKnockback,
            reach: GameConstants.GladiatorRadius * 2f + 46f,
            description: "One heavy blow that throws them back");

        /// <summary>The three a gladiator can be trained in, in the order the UI lists them.</summary>
        public static readonly IReadOnlyList<WeaponDef> All = new List<WeaponDef>
        {
            DualSwords, SwordAndShield, TwoHandedMace
        };

        public static WeaponDef Get(WeaponKind kind)
        {
            switch (kind)
            {
                case WeaponKind.DualSwords: return DualSwords;
                case WeaponKind.SwordAndShield: return SwordAndShield;
                case WeaponKind.TwoHandedMace: return TwoHandedMace;
                default: return Unarmed;
            }
        }
    }
}
