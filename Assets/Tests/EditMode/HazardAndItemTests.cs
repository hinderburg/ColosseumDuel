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

    /// <summary>
    /// The weapon blessing: laid on the sand every third cycle, between the two, and worth a third
    /// again on every blow for the three cycles after the one it is taken in.
    /// </summary>
    public class WeaponBuffTests
    {
        private const float Tol = 0.0001f;

        [Test]
        public void ItIsLaidOnEveryThirdCycleAndNoOther()
        {
            for (int cycle = 0; cycle <= 12; cycle++)
                Assert.AreEqual(cycle > 0 && cycle % 3 == 0, WeaponBuffPickups.IsDueOn(cycle), $"cycle {cycle}");
        }

        [Test]
        public void ItLandsAsFarFromOneAsFromTheOther_OnOpenSand()
        {
            var field = ObstacleField.Standard();
            var a = new Vector2(-40f, -330f);
            var b = new Vector2(60f, 330f);

            for (int seed = 0; seed < 30; seed++)
            {
                var buffs = new WeaponBuffPickups(new System.Random(seed), field);
                Assert.IsTrue(buffs.SpawnBetween(a, b));

                var p = buffs.Position.Value;
                Assert.AreEqual(Vector2.Distance(p, a), Vector2.Distance(p, b), 1f,
                    $"seed {seed}: it landed nearer one of them");
                Assert.IsTrue(field.IsFree(p, GameConstants.BuffRadius), $"seed {seed}: on stone or past the wall");
            }
        }

        [Test]
        public void ASecondIsNotLaidWhileOneIsStillLyingThere()
        {
            var buffs = new WeaponBuffPickups(new System.Random(1), ObstacleField.Empty);
            buffs.SpawnBetween(new Vector2(0f, -200f), new Vector2(0f, 200f));
            var first = buffs.Position;

            Assert.IsFalse(buffs.SpawnBetween(new Vector2(-100f, 0f), new Vector2(100f, 0f)));
            Assert.AreEqual(first, buffs.Position, "and the one already there stays where it was");
        }

        [Test]
        public void RunningOverItBlessesTheWeaponAndTakesItOffTheSand()
        {
            var buffs = new WeaponBuffPickups(new System.Random(1), ObstacleField.Empty);
            buffs.PlaceAt(new Vector2(50f, 50f));
            var g = new GladiatorInstance(GladiatorDef.Brutius) { Pos = Vector2.zero };

            Assert.IsFalse(buffs.TryPickup(g), "too far away to take it");
            Assert.IsFalse(g.WeaponBuffed);

            g.Pos = new Vector2(50f, 45f);
            Assert.IsTrue(buffs.TryPickup(g));
            Assert.IsTrue(g.WeaponBuffed);
            Assert.IsFalse(buffs.Position.HasValue, "it should be gone from the sand");
        }

        [Test]
        public void ItLastsThreeCyclesAfterTheOneItIsTakenIn_AndOnlyTheLastIsMarkedAsTheLast()
        {
            var g = new GladiatorInstance(GladiatorDef.Barbarius);
            g.BlessWeapon();
            Assert.IsTrue(g.WeaponBuffed, "the cycle it is taken in");
            Assert.IsFalse(g.WeaponBuffEnding);

            for (int cycle = 1; cycle <= GameConstants.WeaponBuffCycles; cycle++)
            {
                g.BeginCycle();
                Assert.IsTrue(g.WeaponBuffed, $"{cycle} cycle(s) after it was taken");
                Assert.AreEqual(cycle == GameConstants.WeaponBuffCycles, g.WeaponBuffEnding,
                    $"{cycle} cycle(s) after: only the last is the last");
            }

            g.BeginCycle();
            Assert.IsFalse(g.WeaponBuffed, "and gone after that");
        }

        [Test]
        public void TheBlessingEndsWithTheRound()
        {
            var g = new GladiatorInstance(GladiatorDef.Brutius);
            g.BlessWeapon();
            g.ResetForNewRound();
            Assert.IsFalse(g.WeaponBuffed);
        }

        [Test]
        public void InAMatchTheFirstOneIsLaidOnTheThirdCycle()
        {
            var squad = new[] { GladiatorDef.Brutius, GladiatorDef.Barbarius, GladiatorDef.Hilius };
            var m = new GameManager(new System.Random(3));
            m.StartMatch(squad, squad);
            m.SubmitPick(PlayerSide.P1, GladiatorId.Brutius);

            for (int frame = 0; frame < 20000 && m.State.Cycle < 3; frame++)
            {
                Assert.IsFalse(m.State.Buffs.Position.HasValue, $"a blessing lay on the sand on cycle {m.State.Cycle}");
                m.Tick(1f / 60f);
                if (m.State.Phase == MatchPhase.RoundEnd || m.State.Phase == MatchPhase.MatchEnd)
                    Assert.Ignore("the round ended before the third cycle");
            }

            Assert.AreEqual(3, m.State.Cycle);
            Assert.IsTrue(m.State.Buffs.Position.HasValue, "the third cycle should have laid one");
        }
    }
}
