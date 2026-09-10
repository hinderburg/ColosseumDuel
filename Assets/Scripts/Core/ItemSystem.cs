using System;
using System.Collections.Generic;
using UnityEngine;

namespace ColosseumDuel.Core
{
    public sealed class ArenaItem
    {
        public WeaponKind Kind;
        public Vector2 Pos;
    }

    /// <summary>
    /// The weapons lying on the sand: one of each kind, always, gilded and better than the one a
    /// gladiator walked in with.
    ///
    /// One of each rather than a random roll, because the arena is now a menu of three answers and
    /// which of them is available should not be luck. A player being beaten by a mace can look for
    /// the shield; a player who wants to bleed the opponent can look for the swords.
    ///
    /// Picking one up respawns its slot somewhere else, so the three choices stay on the floor for
    /// the whole round.
    /// </summary>
    public sealed class ItemSystem
    {
        private readonly System.Random _rng;
        private readonly ObstacleField _obstacles;
        public readonly List<ArenaItem> Items = new List<ArenaItem>();

        public ItemSystem(System.Random rng, ObstacleField obstacles = null)
        {
            _rng = rng;
            _obstacles = obstacles ?? ObstacleField.Empty;
        }

        public void SpawnInitial()
        {
            Items.Clear();
            foreach (var weapon in WeaponDef.All)
                Items.Add(new ArenaItem { Kind = weapon.Kind, Pos = RandomItemPos() });
        }

        /// <summary>Call after an item is taken to put the same kind back somewhere else.</summary>
        public void Respawn(ArenaItem consumed)
        {
            int idx = Items.IndexOf(consumed);
            if (idx < 0) return;
            Items[idx] = new ArenaItem { Kind = consumed.Kind, Pos = RandomItemPos() };
        }

        private Vector2 RandomItemPos()
        {
            // Not under a column or in a crate, where it could be seen and never reached. Drawn
            // again rather than pushed off, so the spread stays uniform instead of piling weapons up
            // along the obstacles' edges; the last draw stands if every try somehow fails.
            Vector2 pos = AnyItemPos();
            for (int attempt = 0; attempt < 16 && !_obstacles.IsFree(pos, GameConstants.ItemRadius * 2f); attempt++)
                pos = AnyItemPos();
            return pos;
        }

        private Vector2 AnyItemPos()
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

        /// <summary>
        /// The weapon he is standing on, if any.
        ///
        /// Anything is takeable now, including a weapon he was never trained in - the old rule about
        /// a two-hander refusing a shield is gone with the slots it policed. Picking up the wrong
        /// weapon is a mistake the player is allowed to make; the HUD rings it in red rather than
        /// the simulation refusing it, because a pickup that silently does not happen reads as a
        /// bug and a red ring reads as a warning.
        ///
        /// One he is already carrying is skipped: walking over the mace you are holding should not
        /// teleport an identical mace to the other end of the arena.
        /// </summary>
        public ArenaItem TryPickup(GladiatorInstance g)
        {
            foreach (var item in Items)
            {
                if (Vector2.Distance(g.Pos, item.Pos) > GameConstants.PickupDistance) continue;
                if (g.Weapon == item.Kind && g.WeaponIsGilded) continue;
                return item;
            }
            return null;
        }

        public void ApplyPickup(GladiatorInstance g, ArenaItem item)
        {
            g.Weapon = item.Kind;
            g.WeaponIsGilded = true;

            // A weapon that swings a different number of times changes what is left this cycle.
            // Without this, swapping twin swords for a mace mid-run would still land two blows.
            g.AttacksRemainingThisCycle = Mathf.Min(g.AttacksRemainingThisCycle, g.AttacksPerCycle);

            Respawn(item);
        }
    }
}
