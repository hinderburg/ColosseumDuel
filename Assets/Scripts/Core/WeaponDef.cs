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
    /// and leave the enemy bleeding. A mace hits hardest and furthest and throws its target back,
    /// but lands once. Sword and shield sits between them and is the only one that answers back by
    /// halving what it takes.
    ///
    /// Sword and shield sits between the other two on every axis now, reach included. It used to
    /// reach exactly as far as the twin swords, on the argument that it carries the same sword -
    /// which is true of the blade and not of the man behind it: one fighter works two short blades
    /// in close, the other leans into a single blow from behind a shield. Set for balance rather
    /// than measured off the model, and the only one of the three that is.
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
        /// mace can end its run at a distance a sword cannot answer from. Never below
        /// CollideDistance, so running straight into somebody always counts as being in range - a
        /// weapon that could not reach a body pressed against it would be a bug, not a drawback.
        ///
        /// Set against the models rather than pulled out of the air. A blade is 56 virtual units
        /// long and a hammer 86 (GearSizes, through ArenaView's scale), and the frames drawn by
        /// WeaponReachRendersAFrame put the reach ring beside the weapon that is meant to fill it.
        /// Every one of these used to be short of its own weapon - the twin swords by half, which
        /// on screen is a blade passing through a man who takes no damage.
        ///
        /// Five units on top of what the models measure, for two of the three. A swing sweeps, so
        /// the weapon covers ground the tip is not standing on at the moment the blow resolves, and
        /// matching the reach to the still pose left a margin where a blow visibly should have
        /// landed and did not.
        ///
        /// Sword and shield is the exception and is set for balance: it reaches half again as far
        /// as the sword it carries, so that it sits between the other two on this axis as it does
        /// on every other. That is a lie about the model, and a deliberate one - the alternative
        /// was an archetype whose only distinguishing feature was the damage it takes.
        /// </summary>
        public readonly float Reach;

        /// <summary>
        /// How wide a swing sweeps, in degrees, centred on where he is looking.
        ///
        /// Reach alone made the strike zone a ring, and a ring has no front - a man could be run
        /// down from behind by somebody who never turned to face him. Paired with reach it makes the
        /// zone a wedge that has to be pointed at somebody, which is what the green arc on the
        /// control is for: choosing a heading is choosing who can be hit.
        ///
        /// The three widen in the same order they lengthen, but not by much: the mace is the widest
        /// at 120 and the twin swords the narrowest at 90, with the shield between them. Reach is
        /// what really separates the three - a mace covers four times the ground a sword does - and
        /// piling the angle on top of that made the wedge do the same job twice. Anything pressed
        /// against his body is inside every one of them - see GladiatorInstance.CanStrikeFrom.
        /// </summary>
        public readonly float SwingArcDegrees;

        public readonly string Description;

        public WeaponDef(WeaponKind kind, string name, float damageMultiplier, int attacks,
            float incomingDamageMultiplier, bool bleeds, float knockback, float reach, float swingArcDegrees,
            string description)
        {
            Kind = kind;
            Name = name;
            DamageMultiplier = damageMultiplier;
            Attacks = attacks;
            IncomingDamageMultiplier = incomingDamageMultiplier;
            Bleeds = bleeds;
            Knockback = knockback;
            Reach = reach;
            SwingArcDegrees = swingArcDegrees;
            Description = description;
        }

        /// <summary>Bare hands: what a gladiator fights with if he somehow has nothing.</summary>
        public static readonly WeaponDef Unarmed = new WeaponDef(
            WeaponKind.None, "Unarmed", damageMultiplier: 0.5f, attacks: 1,
            incomingDamageMultiplier: 1f, bleeds: false, knockback: 0f,
            reach: GameConstants.CollideDistance, swingArcDegrees: 90f,
            description: "Nothing but fists");

        public static readonly WeaponDef DualSwords = new WeaponDef(
            WeaponKind.DualSwords, "Twin swords", damageMultiplier: 0.7f, attacks: 2,
            incomingDamageMultiplier: 1f, bleeds: true, knockback: 0f,
            reach: GameConstants.GladiatorRadius * 2f + 25f, swingArcDegrees: 90f,   // 57
            description: "Two light blows, and the wound keeps bleeding");

        public static readonly WeaponDef SwordAndShield = new WeaponDef(
            WeaponKind.SwordAndShield, "Sword and shield", damageMultiplier: 1f, attacks: 1,
            incomingDamageMultiplier: GameConstants.ShieldDamageMult, bleeds: false, knockback: 0f,
            reach: GameConstants.GladiatorRadius * 2f + 52f, swingArcDegrees: 100f,   // 84 - halfway between the blades and the hammer
            description: "An even blow, and half the damage taken");

        public static readonly WeaponDef TwoHandedMace = new WeaponDef(
            WeaponKind.TwoHandedMace, "Two-handed mace", damageMultiplier: 1.5f, attacks: 1,
            incomingDamageMultiplier: 1f, bleeds: false, knockback: GameConstants.MaceKnockback,
            reach: GameConstants.GladiatorRadius * 2f + 78f, swingArcDegrees: 120f,   // 110
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
