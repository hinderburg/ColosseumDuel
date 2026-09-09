using System;
using System.Linq;
using ColosseumDuel.Core;
using NUnit.Framework;
using UnityEngine;

namespace ColosseumDuel.Tests
{
    /// <summary>GDD section 8: the shrinking arena.</summary>
    public class HazardSystemTests
    {
        private static Vector2 AtRadiusFraction(float f) => new Vector2(GameConstants.ArenaRadius * f, 0f);

        /// <summary>The cycle the nth ring bites on, counting from zero.</summary>
        private static int RingCycle(int index)
            => GameConstants.HazardSafeCycles + 1 + index * GameConstants.HazardRingInterval;

        [Test]
        public void ArenaIsCompletelySafeForTheSafeCycles()
        {
            for (int cycle = 1; cycle <= GameConstants.HazardSafeCycles; cycle++)
                for (float f = 0f; f <= 1f; f += 0.1f)
                    Assert.IsFalse(HazardSystem.IsInActiveHazard(AtRadiusFraction(f), cycle),
                        $"cycle {cycle}, radius fraction {f:0.0} should still be safe");
        }

        /// <summary>
        /// The rings come in from the wall, one every HazardRingInterval cycles, and each one leaves
        /// the ground inside it alone until its own turn comes round.
        /// </summary>
        [Test]
        public void RingsCloseInFromTheEdgeOneEveryInterval()
        {
            Assert.IsTrue(HazardSystem.IsInActiveHazard(AtRadiusFraction(0.9f), RingCycle(0)));
            Assert.IsFalse(HazardSystem.IsInActiveHazard(AtRadiusFraction(0.6f), RingCycle(0)));

            Assert.IsTrue(HazardSystem.IsInActiveHazard(AtRadiusFraction(0.6f), RingCycle(1)));
            Assert.IsFalse(HazardSystem.IsInActiveHazard(AtRadiusFraction(0.35f), RingCycle(1)));

            Assert.IsTrue(HazardSystem.IsInActiveHazard(AtRadiusFraction(0.35f), RingCycle(2)));
        }

        /// <summary>
        /// And the middle of the arena stays standable until the last of them.
        ///
        /// The gap between one ring and the next is the whole of the pacing: it used to be one
        /// cycle, which took the arena from first ring to nowhere-to-stand in less time than two
        /// gladiators need to cross it. Asserted as a gap rather than as a cycle number so it keeps
        /// meaning that when the numbers move again.
        /// </summary>
        [Test]
        public void TheMiddleIsTheLastGroundToGo()
        {
            int last = RingCycle(3);

            Assert.IsFalse(HazardSystem.IsInActiveHazard(Vector2.zero, last - 1),
                "the middle should still be standable the cycle before the last ring");
            Assert.IsTrue(HazardSystem.IsInActiveHazard(Vector2.zero, last),
                "by now there is nowhere left to stand");

            Assert.AreEqual(GameConstants.HazardRingInterval, RingCycle(1) - RingCycle(0),
                "the rings are meant to arrive a fixed number of cycles apart");
        }

        [Test]
        public void NextStageIsTelegraphedOneCycleAhead()
        {
            int first = RingCycle(0);

            var upcoming = HazardSystem.UpcomingStage(first - 1);
            Assert.IsTrue(upcoming.HasValue,
                $"during cycle {first - 1} the UI must be able to warn about cycle {first}");
            Assert.AreEqual(1.00f, upcoming.Value.OuterFraction, 0.0001f);

            Assert.IsFalse(HazardSystem.UpcomingStage(first - 2).HasValue,
                "the warning is one cycle out, not a standing notice");
            Assert.IsFalse(HazardSystem.UpcomingStage(1).HasValue, "nothing to warn about that early");
        }
    }

    /// <summary>Exactly three weapons on the floor, one of each kind, all gilded.</summary>
    public class ItemSystemTests
    {
        [Test]
        public void SpawnInitial_PutsOneOfEveryWeaponOnTheSand()
        {
            // One of each rather than a random roll. The arena is a menu of three answers now, and
            // which of them is on offer should not be luck: a player being beaten by a mace has to
            // be able to go looking for the shield.
            var items = new ItemSystem(new System.Random(1));
            items.SpawnInitial();

            Assert.AreEqual(GameConstants.ItemCountOnArena, items.Items.Count);
            foreach (var weapon in WeaponDef.All)
                Assert.AreEqual(1, CountOf(items, weapon.Kind), $"exactly one {weapon.Name}");
        }

        [Test]
        public void PickingUpAWeapon_ImmediatelyRefillsItsSlot()
        {
            var items = new ItemSystem(new System.Random(7));
            items.SpawnInitial();
            var g = new GladiatorInstance(GladiatorDef.Brutius);

            for (int i = 0; i < 30; i++)
            {
                var target = items.Items[i % items.Items.Count];
                g.Pos = target.Pos;
                var picked = items.TryPickup(g);
                Assert.IsNotNull(picked, "standing on a weapon must pick it up");
                items.ApplyPickup(g, picked);

                Assert.AreEqual(GameConstants.ItemCountOnArena, items.Items.Count,
                    "the arena always holds exactly three weapons");
                foreach (var weapon in WeaponDef.All)
                    Assert.AreEqual(1, CountOf(items, weapon.Kind),
                        $"all three choices stay on the floor - {weapon.Name} went missing");
            }
        }

        [Test]
        public void APickedUpWeaponIsTheGildedOne()
        {
            var items = new ItemSystem(new System.Random(3));
            items.SpawnInitial();

            var g = new GladiatorInstance(GladiatorDef.Brutius);
            g.EquipTrainedWeapon();
            Assert.IsFalse(g.WeaponIsGilded, "he walks in with his own");

            var shieldPair = items.Items.Find(i => i.Kind == WeaponKind.SwordAndShield);
            items.ApplyPickup(g, shieldPair);

            Assert.AreEqual(WeaponKind.SwordAndShield, g.Weapon);
            Assert.IsTrue(g.HasShield);
            Assert.IsTrue(g.WeaponIsGilded, "everything on the sand is the better copy");
        }

        [Test]
        public void AnythingCanBePickedUp_TrainedOrNot()
        {
            // The old rule about a two-hander refusing a shield went with the slots it policed.
            // Taking the wrong weapon is a mistake the player is allowed to make - the HUD rings it
            // in red rather than the simulation silently declining it, because a pickup that does
            // not happen reads as a bug and a red ring reads as a warning.
            var items = new ItemSystem(new System.Random(5));
            items.SpawnInitial();

            var g = new GladiatorInstance(GladiatorDef.Hilius);
            g.EquipTrainedWeapon();
            Assert.AreEqual(WeaponKind.SwordAndShield, g.Weapon);

            var mace = items.Items.Find(i => i.Kind == WeaponKind.TwoHandedMace);
            g.Pos = mace.Pos;
            Assert.AreSame(mace, items.TryPickup(g), "he is allowed to pick up the wrong weapon");

            items.ApplyPickup(g, mace);
            Assert.AreEqual(WeaponKind.TwoHandedMace, g.Weapon);
            Assert.IsTrue(g.IsUntrained, "and the HUD has to be able to say so");
        }

        [Test]
        public void StandingOnTheGildedWeaponHeIsAlreadyHolding_DoesNothing()
        {
            // Otherwise walking over your own mace teleports an identical mace to the far end of
            // the arena, and the three choices quietly shuffle every time anyone stands still.
            var items = new ItemSystem(new System.Random(9));
            items.SpawnInitial();

            var mace = items.Items.Find(i => i.Kind == WeaponKind.TwoHandedMace);
            var g = new GladiatorInstance(GladiatorDef.Brutius)
            {
                Weapon = WeaponKind.TwoHandedMace,
                WeaponIsGilded = true,
                Pos = mace.Pos,
            };

            Assert.IsNull(items.TryPickup(g));
            CollectionAssert.Contains(items.Items, mace, "and it stays exactly where it was");
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
        public void TrapsAreSplitEvenlyBetweenTheTwoEndsOfTheArena()
        {
            // The two ends are not interchangeable - the player starts in one and the opponent in
            // the other - so a free scatter that happened to drop four of six into one end would
            // hand that round to whoever was standing in the other. Checked over many draws,
            // because getting it right once by luck is exactly the failure being guarded against.
            for (int seed = 0; seed < 40; seed++)
            {
                var traps = new TrapSystem(new System.Random(seed));
                traps.SpawnForRound();

                Assert.AreEqual(GameConstants.TrapCount, traps.Traps.Count);

                int near = traps.Traps.Count(t => t.Pos.y < 0f);
                int far = traps.Traps.Count(t => t.Pos.y > 0f);
                Assert.AreEqual(GameConstants.TrapCount / 2, near, $"seed {seed}: near end");
                Assert.AreEqual(GameConstants.TrapCount / 2, far, $"seed {seed}: far end");

                foreach (var trap in traps.Traps)
                    Assert.LessOrEqual(ArenaShape.NormalizedDistance(trap.Pos), 1f,
                        $"seed {seed}: a trap was laid outside the wall");
            }
        }

        private static int CountOf(ItemSystem items, WeaponKind kind)
            => items.Items.FindAll(i => i.Kind == kind).Count;
    }
}
