using System;
using ColosseumDuel.Core;
using NUnit.Framework;
using UnityEngine;

namespace ColosseumDuel.Tests
{
    /// <summary>GDD section 8: the shrinking arena.</summary>
    public class HazardSystemTests
    {
        private static Vector2 AtRadiusFraction(float f) => new Vector2(GameConstants.ArenaRadius * f, 0f);

        [Test]
        public void ArenaIsCompletelySafeForTheFirstSixCycles()
        {
            for (int cycle = 1; cycle <= GameConstants.HazardSafeCycles; cycle++)
                for (float f = 0f; f <= 1f; f += 0.1f)
                    Assert.IsFalse(HazardSystem.IsInActiveHazard(AtRadiusFraction(f), cycle),
                        $"cycle {cycle}, radius fraction {f:0.0} should still be safe");
        }

        [Test]
        public void RingsCloseInFromTheEdgeStartingOnCycle7()
        {
            Assert.IsTrue(HazardSystem.IsInActiveHazard(AtRadiusFraction(0.9f), 7));
            Assert.IsFalse(HazardSystem.IsInActiveHazard(AtRadiusFraction(0.6f), 7));

            Assert.IsTrue(HazardSystem.IsInActiveHazard(AtRadiusFraction(0.6f), 8));
            Assert.IsFalse(HazardSystem.IsInActiveHazard(AtRadiusFraction(0.35f), 8));

            Assert.IsTrue(HazardSystem.IsInActiveHazard(AtRadiusFraction(0.35f), 9));
        }

        [Test]
        public void TheCoreStaysSafeTwoCyclesLongerThanTheLastRing()
        {
            Assert.IsFalse(HazardSystem.IsInActiveHazard(Vector2.zero, 10));
            Assert.IsTrue(HazardSystem.IsInActiveHazard(Vector2.zero, 11),
                "by now there is nowhere left to stand");
        }

        [Test]
        public void NextStageIsTelegraphedOneCycleAhead()
        {
            var upcoming = HazardSystem.UpcomingStage(6);
            Assert.IsTrue(upcoming.HasValue, "during cycle 6 the UI must be able to warn about cycle 7");
            Assert.AreEqual(1.00f, upcoming.Value.OuterFraction, 0.0001f);

            Assert.IsFalse(HazardSystem.UpcomingStage(3).HasValue, "nothing to warn about that early");
        }
    }

    /// <summary>GDD section 6: exactly three items on the floor, all single-use.</summary>
    public class ItemSystemTests
    {
        [Test]
        public void SpawnInitial_PlacesOneWeaponOneShieldAndOneRandom()
        {
            var items = new ItemSystem(new System.Random(1));
            items.SpawnInitial();

            Assert.AreEqual(GameConstants.ItemCountOnArena, items.Items.Count);
            Assert.AreEqual(1, CountOf(items, ItemKind.Weapon));
            Assert.AreEqual(1, CountOf(items, ItemKind.Shield));
            Assert.AreEqual(1, CountOf(items, ItemKind.Random));
        }

        [Test]
        public void PickingUpAnItem_ImmediatelyRefillsItsSlot()
        {
            var items = new ItemSystem(new System.Random(7));
            items.SpawnInitial();
            var g = new GladiatorInstance(GladiatorDef.Brutius);

            for (int i = 0; i < 30; i++)
            {
                // Empty-handed each time round. This test is about the slot invariant, and a
                // gladiator who happened to be holding a two-hander would rightly decline the
                // shield - a rule of its own, covered separately.
                g.Weapon = WeaponType.None;
                g.HasShield = false;

                var target = items.Items[i % items.Items.Count];
                g.Pos = target.Pos;
                var picked = items.TryPickup(g);
                Assert.IsNotNull(picked, "standing on an item must pick it up");
                items.ApplyPickup(g, picked);

                Assert.AreEqual(GameConstants.ItemCountOnArena, items.Items.Count,
                    "the arena always holds exactly three items");
                Assert.AreEqual(1, CountOf(items, ItemKind.Weapon));
                Assert.AreEqual(1, CountOf(items, ItemKind.Shield));
                Assert.AreEqual(1, CountOf(items, ItemKind.Random));
            }
        }

        [Test]
        public void PickedUpShieldAndWeapon_LandOnTheGladiator()
        {
            var items = new ItemSystem(new System.Random(3));
            items.SpawnInitial();
            var g = new GladiatorInstance(GladiatorDef.Brutius);

            var shield = items.Items.Find(i => i.Kind == ItemKind.Shield);
            items.ApplyPickup(g, shield);
            Assert.IsTrue(g.HasShield);

            var weapon = items.Items.Find(i => i.Kind == ItemKind.Weapon);
            items.ApplyPickup(g, weapon);
            Assert.AreNotEqual(WeaponType.None, g.Weapon);
        }

        [Test]
        public void ItemsAlwaysSpawnInsideTheArena()
        {
            var items = new ItemSystem(new System.Random(11));
            for (int i = 0; i < 50; i++)
            {
                items.SpawnInitial();
                foreach (var item in items.Items)
                    Assert.Less(ArenaShape.NormalizedDistance(item.Pos), 1f,
                        "an item spawned outside the wall would be unreachable");
            }
        }

        [Test]
        public void ATwoHandedWeaponAndAShieldCannotBeCarriedTogether()
        {
            var g = new GladiatorInstance(GladiatorDef.Brutius);
            var shield = new ArenaItem { Kind = ItemKind.Shield };
            var oneHanded = new ArenaItem { Kind = ItemKind.Weapon, WeaponType = WeaponType.OneHanded };
            var twoHanded = new ArenaItem { Kind = ItemKind.Weapon, WeaponType = WeaponType.TwoHanded };

            Assert.IsTrue(ItemSystem.CanCarry(g, shield), "empty-handed, he can take anything");
            Assert.IsTrue(ItemSystem.CanCarry(g, twoHanded));

            g.Weapon = WeaponType.TwoHanded;
            Assert.IsFalse(ItemSystem.CanCarry(g, shield), "both hands are on the haft");
            Assert.IsTrue(ItemSystem.CanCarry(g, oneHanded), "swapping weapons is still fine");

            g.Weapon = WeaponType.None;
            g.HasShield = true;
            Assert.IsFalse(ItemSystem.CanCarry(g, twoHanded), "the same impossible pair, arrived at backwards");
            Assert.IsTrue(ItemSystem.CanCarry(g, oneHanded), "sword and board is the whole point");
        }

        [Test]
        public void AnItemHeCannotCarryIsLeftOnTheSand()
        {
            // Not merely "not equipped": TryPickup has to decline it, or ApplyPickup would consume
            // the shield and respawn it elsewhere for nothing, and the player would watch a pickup
            // vanish with no effect and no explanation.
            var items = new ItemSystem(new System.Random(7));
            items.SpawnInitial();

            var shield = items.Items.Find(i => i.Kind == ItemKind.Shield);
            var g = new GladiatorInstance(GladiatorDef.Brutius)
            {
                Weapon = WeaponType.TwoHanded,
                Pos = shield.Pos,
            };

            Assert.AreNotSame(shield, items.TryPickup(g), "a two-hander should walk straight over a shield");
            CollectionAssert.Contains(items.Items, shield, "and it should still be lying there");
        }

        private static int CountOf(ItemSystem items, ItemKind kind)
            => items.Items.FindAll(i => i.Kind == kind).Count;
    }
}
