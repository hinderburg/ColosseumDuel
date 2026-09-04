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
            // uniform-ish random point within the arena, kept away from the exact center/wall
            float r = Mathf.Sqrt((float)_rng.NextDouble()) * (GameConstants.ArenaRadius - GameConstants.ItemRadius * 2f);
            float a = (float)(_rng.NextDouble() * Math.PI * 2.0);
            return new Vector2(Mathf.Cos(a) * r, Mathf.Sin(a) * r);
        }

        public ArenaItem TryPickup(GladiatorInstance g)
        {
            foreach (var item in Items)
            {
                if (Vector2.Distance(g.Pos, item.Pos) <= GameConstants.PickupDistance)
                    return item;
            }
            return null;
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
