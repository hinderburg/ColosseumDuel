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
                    Assert.Less(item.Pos.magnitude, GameConstants.ArenaRadius,
                        "an item spawned outside the wall would be unreachable");
            }
        }

        private static int CountOf(ItemSystem items, ItemKind kind)
            => items.Items.FindAll(i => i.Kind == kind).Count;
    }
}
