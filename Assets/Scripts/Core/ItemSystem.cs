using System;
using System.Collections.Generic;
using UnityEngine;

namespace ColosseumDuel.Core
{
    public sealed class ArenaItem
    {
        public ItemKind Kind;
        public WeaponType WeaponType; // only meaningful when Kind == Weapon
        public Vector2 Pos;
    }

    /// <summary>
    /// Keeps exactly GameConstants.ItemCountOnArena items on the floor at all times: 1 weapon,
    /// 1 shield, 1 "random". Items are single-use - consuming one immediately respawns its slot
    /// at a new random position with a freshly rolled type where applicable.
    /// </summary>
    public sealed class ItemSystem
    {
        private readonly System.Random _rng;
        public readonly List<ArenaItem> Items = new List<ArenaItem>();

        public ItemSystem(System.Random rng)
        {
            _rng = rng;
        }

        public void SpawnInitial()
        {
            Items.Clear();
            Items.Add(new ArenaItem { Kind = ItemKind.Weapon, WeaponType = RollWeaponType(), Pos = RandomItemPos() });
            Items.Add(new ArenaItem { Kind = ItemKind.Shield, Pos = RandomItemPos() });
            Items.Add(new ArenaItem { Kind = ItemKind.Random, WeaponType = RollWeaponType(), Pos = RandomItemPos() });
        }

        /// <summary>Call after an item is consumed (picked up or used as a block) to replace it.</summary>
        public void Respawn(ArenaItem consumed)
        {
            int idx = Items.IndexOf(consumed);
            if (idx < 0) return;
            Items[idx] = new ArenaItem
            {
                Kind = consumed.Kind,
                WeaponType = consumed.Kind == ItemKind.Shield ? WeaponType.None : RollWeaponType(),
                Pos = RandomItemPos()
            };
        }

        private WeaponType RollWeaponType()
        {
            return _rng.NextDouble() < 0.5 ? WeaponType.OneHanded : WeaponType.TwoHanded;
        }

        private Vector2 RandomItemPos()
        {
            // Uniform-ish point inside the arena, kept clear of the wall. Drawn on a unit circle and
            // then stretched onto the ellipse, so the same code works whatever shape the arena is.
            float r = Mathf.Sqrt((float)_rng.NextDouble());
            float a = (float)(_rng.NextDouble() * Math.PI * 2.0);
            var onUnitCircle = new Vector2(Mathf.Cos(a) * r, Mathf.Sin(a) * r);

            float margin = GameConstants.ItemRadius * 2f;
            return new Vector2(
                onUnitCircle.x * (ArenaShape.RadiusX - margin),
                onUnitCircle.y * (ArenaShape.RadiusY - margin));
        }

        public ArenaItem TryPickup(GladiatorInstance g)
        {
            foreach (var item in Items)
            {
                if (Vector2.Distance(g.Pos, item.Pos) > GameConstants.PickupDistance) continue;
                if (!CanCarry(g, item)) continue;
                return item;
            }
            return null;
        }

        /// <summary>
        /// A two-handed weapon and a shield cannot be held at once - both hands are on the haft.
        ///
        /// Checked here rather than in ApplyPickup so that an item he cannot take is simply not
        /// picked up: it stays on the sand for him to come back to once his trident has broken,
        /// instead of being consumed and respawned somewhere else for nothing.
        ///
        /// It blocks in both directions. The rule was asked for as "a two-hander blocks the shield",
        /// but the other order produces the same impossible pair, and refusing the weapon leaves the
        /// shield the player already earned rather than quietly destroying it.
        /// </summary>
        public static bool CanCarry(GladiatorInstance g, ArenaItem item)
        {
            if (item.Kind == ItemKind.Shield) return g.Weapon != WeaponType.TwoHanded;
            if (item.WeaponType == WeaponType.TwoHanded) return !g.HasShield;
            return true;
        }

        // NOTE: the original spec leaves the "random" 3rd slot loosely defined - it is currently
        // wired up as an extra weapon roll (same effect as the Weapon slot). Swap in your own
        // bonus-item type here once it's designed (speed boost, extra rage, etc).
        public void ApplyPickup(GladiatorInstance g, ArenaItem item)
        {
            switch (item.Kind)
            {
                case ItemKind.Weapon:
                case ItemKind.Random when item.WeaponType != WeaponType.None:
                    g.Weapon = item.WeaponType;
                    break;
                case ItemKind.Shield:
                    g.HasShield = true;
                    break;
            }
            Respawn(item);
        }
    }
}
