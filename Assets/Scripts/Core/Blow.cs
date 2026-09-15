using UnityEngine;

namespace ColosseumDuel.Core
{
    /// <summary>
    /// One blow that landed, with everything the presentation needs to show it for what it was:
    /// who struck whom, with what, from which side, whether a guard met it, how much it took and
    /// whether it was the end of him. Raised beside the older Damaged event, which only says who
    /// lost how much.
    /// </summary>
    public readonly struct Blow
    {
        public readonly PlayerSide Victim;
        public readonly PlayerSide Striker;
        public readonly float Damage;

        /// <summary>The victim's own health, so the blow can be read as a share of him.</summary>
        public readonly float VictimMaxHp;

        public readonly HitSector Sector;

        /// <summary>Whether the victim's guard was set against it - up, and facing it.</summary>
        public readonly bool Blocked;

        public readonly WeaponKind Weapon;

        /// <summary>Where the striker stood, so the blow has a direction.</summary>
        public readonly Vector2 From;

        /// <summary>Whether it dropped him.</summary>
        public readonly bool Lethal;

        /// <summary>A Riposte: what came back at a striker off the man he hit.</summary>
        public readonly bool Returned;

        public Blow(PlayerSide victim, PlayerSide striker, float damage, float victimMaxHp, HitSector sector,
            bool blocked, WeaponKind weapon, Vector2 from, bool lethal, bool returned = false)
        {
            Victim = victim;
            Striker = striker;
            Damage = damage;
            VictimMaxHp = victimMaxHp;
            Sector = sector;
            Blocked = blocked;
            Weapon = weapon;
            From = from;
            Lethal = lethal;
            Returned = returned;
        }

        /// <summary>How much of the man it took, 0 to 1.</summary>
        public float Share => VictimMaxHp > 0f ? Mathf.Clamp01(Damage / VictimMaxHp) : 0f;

        /// <summary>From the side or behind - the blows worth more, and read as such.</summary>
        public bool Flanking => Sector != HitSector.Front;
    }
}
