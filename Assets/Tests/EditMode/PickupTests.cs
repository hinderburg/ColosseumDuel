using System.Collections.Generic;
using System.Linq;
using ColosseumDuel.Core;
using NUnit.Framework;
using UnityEngine;

namespace ColosseumDuel.Tests
{
    /// <summary>
    /// The apple and the horn, and where all three things on the sand are laid: by a column, never on
    /// top of each other, never in the spikes, and each on its own roll.
    /// </summary>
    public class PickupTests
    {
        private const float Tol = 0.001f;

        [Test]
        public void TheAppleGivesBackAThirdOfHisHealth_AndNotPastHisOwn()
        {
            var apple = new ArenaPickup(new System.Random(1), ObstacleField.Empty, PickupKind.Apple);
            var g = new GladiatorInstance(GladiatorDef.Brutius) { Pos = Vector2.zero };
            g.Hp = 40f;

            apple.PlaceAt(Vector2.zero);
            Assert.IsTrue(apple.TryPickup(g, out float healed));
            Assert.AreEqual(GladiatorDef.Brutius.MaxHp / 3f, healed, Tol, "a third of his health");
            Assert.AreEqual(40f + GladiatorDef.Brutius.MaxHp / 3f, g.Hp, Tol);
            Assert.IsFalse(apple.Position.HasValue, "eaten, it is gone from the sand");

            g.Hp = GladiatorDef.Brutius.MaxHp - 5f;
            apple.PlaceAt(Vector2.zero);
            Assert.IsTrue(apple.TryPickup(g, out healed));
            Assert.AreEqual(5f, healed, Tol, "only what was missing");
            Assert.AreEqual(GladiatorDef.Brutius.MaxHp, g.Hp, Tol, "healed past his own health");
        }

        [Test]
        public void TheHornPutsRageOnTheMeter_EvenWhileTheAbilityHasLockedIt()
        {
            var horn = new ArenaPickup(new System.Random(1), ObstacleField.Empty, PickupKind.Horn);
            var g = new GladiatorInstance(GladiatorDef.Hilius) { Pos = Vector2.zero, Rage = 0.2f };

            horn.PlaceAt(Vector2.zero);
            Assert.IsTrue(horn.TryPickup(g, out float gained));
            Assert.AreEqual(GameConstants.HornRage, gained, Tol);
            Assert.AreEqual(0.2f + GameConstants.HornRage, g.Rage, Tol);

            // Just used: the meter will not charge from the fight, and the horn is not the fight.
            g.Rage = 0f;
            g.AbilityLockedRounds = 2;
            horn.PlaceAt(Vector2.zero);
            Assert.IsTrue(horn.TryPickup(g, out gained));
            Assert.AreEqual(GameConstants.HornRage, g.Rage, Tol, "the horn should fill the meter past the lock");

            g.Rage = GameConstants.RageMax - 0.1f;
            horn.PlaceAt(Vector2.zero);
            Assert.IsTrue(horn.TryPickup(g, out gained));
            Assert.AreEqual(0.1f, gained, Tol, "only as far as full");
            Assert.AreEqual(GameConstants.RageMax, g.Rage, Tol);
        }

        [Test]
        public void EachIsLaidBesideAColumn_ClearOfTheMen_AndNeverOnAnother()
        {
            var field = ObstacleField.Standard();
            var a = new Vector2(-40f, -300f);
            var b = new Vector2(60f, 300f);
            var columns = field.Obstacles.Where(o => o.Kind == ObstacleKind.Column).ToList();

            for (int seed = 0; seed < 30; seed++)
            {
                var rng = new System.Random(seed);
                var laid = new List<Vector2>();
                foreach (PickupKind kind in System.Enum.GetValues(typeof(PickupKind)))
                {
                    var pickup = new ArenaPickup(rng, field, kind);
                    Assert.IsTrue(pickup.SpawnByColumn(a, b, laid, 3), $"seed {seed}: no {kind} was laid");
                    var p = pickup.Position.Value;

                    var column = columns.OrderBy(c => Vector2.Distance(c.Centre, p) - c.Size).First();
                    Assert.Less(Vector2.Distance(column.Centre, p) - column.Size, GameConstants.BuffRadius * 3f,
                        $"seed {seed}: the {kind} is not by a column");
                    Assert.LessOrEqual(p.y, column.Centre.y + Tol,
                        $"seed {seed}: the {kind} is behind its column, where the camera cannot see it");
                    Assert.IsTrue(field.IsFree(p, GameConstants.BuffRadius), $"seed {seed}: the {kind} is on stone or past the wall");
                    Assert.GreaterOrEqual(Vector2.Distance(p, a), ArenaPickup.MinFromGladiator - Tol, $"seed {seed}: the {kind} is on top of a man");
                    Assert.GreaterOrEqual(Vector2.Distance(p, b), ArenaPickup.MinFromGladiator - Tol, $"seed {seed}: the {kind} is on top of a man");
                    foreach (var other in laid)
                        Assert.GreaterOrEqual(Vector2.Distance(p, other), ArenaPickup.MinApart - Tol,
                            $"seed {seed}: the {kind} is on top of another");
                    laid.Add(p);
                }
            }
        }

        [Test]
        public void NoneIsLaidInTheSpikes()
        {
            var field = ObstacleField.Standard();
            int burning = HazardSystem.Schedule[1].ActivateRound;
            for (int seed = 0; seed < 30; seed++)
            {
                var pickup = new ArenaPickup(new System.Random(seed), field, PickupKind.Apple);
                pickup.SpawnByColumn(new Vector2(0f, -120f), new Vector2(0f, 120f), null, burning);
                Assert.IsFalse(HazardSystem.IsInActiveHazard(pickup.Position.Value, burning), $"seed {seed}: laid in the spikes");
                Assert.IsFalse(HazardSystem.IsInActiveHazard(pickup.Position.Value, burning + 1), $"seed {seed}: laid where the spikes come up next");
            }
        }

        /// <summary>
        /// In a match, all three are laid on the third round - each on its own roll, and none where
        /// another already lies - and taking one up says which it was.
        /// </summary>
        [Test]
        public void OnTheThirdRoundAllThreeAreLaid_EachOnItsOwnSpot()
        {
            var squad = new[] { GladiatorDef.Brutius, GladiatorDef.Barbarius, GladiatorDef.Hilius };
            var m = new GameManager(new System.Random(3));
            m.StartMatch(squad, squad);
            m.SubmitPick(PlayerSide.P1, GladiatorId.Brutius);

            for (int frame = 0; frame < 20000 && m.State.Round < 3; frame++)
            {
                m.Tick(1f / 60f);
                if (m.State.Phase == MatchPhase.ClashEnd || m.State.Phase == MatchPhase.MatchEnd)
                    Assert.Ignore("the clash ended before the third round");
            }

            var spots = m.State.Pickups.Select(p => p.Position).ToList();
            Assert.AreEqual(3, spots.Count);
            Assert.IsTrue(spots.All(s => s.HasValue), "with a certain chance each, the third round lays all three");
            for (int i = 0; i < spots.Count; i++)
            for (int j = i + 1; j < spots.Count; j++)
                Assert.GreaterOrEqual(Vector2.Distance(spots[i].Value, spots[j].Value), ArenaPickup.MinApart - Tol);
        }

        [Test]
        public void TakingOneUpSaysWhichItWasAndWhatItGave()
        {
            var squad = new[] { GladiatorDef.Brutius };
            var m = new GameManager(new System.Random(3));
            m.StartMatch(squad, squad);
            m.SubmitPick(PlayerSide.P1, GladiatorId.Brutius);
            for (int frame = 0; frame < 600 && m.State.Phase != MatchPhase.Planning; frame++) m.Tick(1f / 60f);

            var g = m.State.P1.Active;
            g.Hp = 50f;
            m.State.Apple.PlaceAt(g.Pos);

            var heard = new List<(PlayerSide, PickupKind, float)>();
            m.PickedUp += (side, kind, amount) => heard.Add((side, kind, amount));
            m.SubmitPlanningAction(PlayerSide.P1, ActionType.Defend, Vector2.zero, 0f);
            for (int frame = 0; frame < 600 && heard.Count == 0; frame++) m.Tick(1f / 60f);

            Assert.AreEqual(1, heard.Count, "eating the apple should have been announced");
            Assert.AreEqual(PlayerSide.P1, heard[0].Item1);
            Assert.AreEqual(PickupKind.Apple, heard[0].Item2);
            Assert.AreEqual(GladiatorDef.Brutius.MaxHp / 3f, heard[0].Item3, Tol, "and how much it gave back");
        }
    }
}
