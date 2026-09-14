using System.Linq;
using ColosseumDuel.Core;
using NUnit.Framework;
using UnityEngine;

namespace ColosseumDuel.Tests
{
    /// <summary>
    /// The six archetypes: each with a weapon of his own, and the three new abilities doing what
    /// their cards say.
    /// </summary>
    public class RosterTests
    {
        private const float Tol = 0.001f;
        private const float Dt = 1f / 60f;

        [Test]
        public void SixArchetypes_EachTrainedInAWeaponNobodyElseCarries()
        {
            Assert.AreEqual(6, GladiatorDef.All.Count);
            CollectionAssert.AllItemsAreUnique(GladiatorDef.All.Select(d => d.Id));
            CollectionAssert.AllItemsAreUnique(GladiatorDef.All.Select(d => d.SkilledWith),
                "two archetypes share a weapon - each is meant to walk in with his own");
            CollectionAssert.AllItemsAreUnique(GladiatorDef.All.Select(d => d.Ability));

            foreach (var def in GladiatorDef.All)
            {
                Assert.AreSame(def, GladiatorDef.Get(def.Id), $"{def.Name} is not what Get hands back");
                var weapon = WeaponDef.Get(def.SkilledWith);
                Assert.AreEqual(def.SkilledWith, weapon.Kind, $"{def.Name}'s weapon comes back as {weapon.Name}");
                CollectionAssert.Contains(WeaponDef.All, weapon, $"{weapon.Name} is missing from the list the UI reads");
                Assert.GreaterOrEqual(weapon.Reach, GameConstants.CollideDistance,
                    $"{weapon.Name} cannot reach a man pressed against him");

                Assert.AreEqual(def.SkilledWith, new GladiatorInstance(def).Weapon,
                    $"{def.Name} should walk in holding his own weapon");
            }
        }

        [Test]
        public void Bulwark_TurnsAsideEveryBlowFromTheFront_AndNoneFromBehind()
        {
            var scutarius = new GladiatorInstance(GladiatorDef.Scutarius)
            {
                Pos = Vector2.zero, Facing = Vector2.up,
                Buff = new ActiveBuff { Key = AbilityKey.Bulwark, CyclesLeft = 2 },
            };
            var front = new GladiatorInstance(GladiatorDef.Brutius) { Pos = new Vector2(0f, 30f) };
            var behind = new GladiatorInstance(GladiatorDef.Brutius) { Pos = new Vector2(0f, -30f) };

            float hp = scutarius.Hp;
            Assert.AreEqual(0f, CombatResolver.DealDamage(front, scutarius), Tol, "a blow into the shield wall landed");
            Assert.AreEqual(hp, scutarius.Hp, Tol);
            Assert.IsFalse(scutarius.IsBleeding, "and opened no wound either");

            Assert.Greater(CombatResolver.DealDamage(behind, scutarius), 0f, "Bulwark covers the front, not his back");
        }

        [Test]
        public void SecondWind_HealsAQuarterOfHisHealth_AndNotPastFull()
        {
            var h = new GladiatorInstance(GladiatorDef.Hastarius) { Hp = 50f, Rage = GameConstants.RageMax };
            h.ActivateAbility();
            Assert.AreEqual(50f + GladiatorDef.Hastarius.MaxHp * GameConstants.SecondWindHeal, h.Hp, Tol);

            var nearlyWhole = new GladiatorInstance(GladiatorDef.Hastarius)
            {
                Hp = GladiatorDef.Hastarius.MaxHp - 5f, Rage = GameConstants.RageMax,
            };
            nearlyWhole.ActivateAbility();
            Assert.AreEqual(GladiatorDef.Hastarius.MaxHp, nearlyWhole.Hp, Tol, "healed past his own health");
        }

        [Test]
        public void ANet_HalvesHowFarHeRuns_ForTheCycleItLandsAndTheNext()
        {
            var g = new GladiatorInstance(GladiatorDef.Brutius);
            float full = g.DashReach();

            g.Ensnare();
            Assert.AreEqual(full * GameConstants.NetSpeedMult, g.DashReach(), Tol, "the cycle it lands");

            g.BeginCycle();
            Assert.AreEqual(full * GameConstants.NetSpeedMult, g.DashReach(), Tol, "the cycle after");

            g.BeginCycle();
            Assert.AreEqual(full, g.DashReach(), Tol, "and free again after that");

            g.Ensnare();
            g.ResetForNewRound();
            Assert.AreEqual(full, g.DashReach(), Tol, "a net does not outlast the round");
        }

        [Test]
        public void ANetThrownThisPhaseShortensTheRunTheOtherManOrderedThisPhase()
        {
            // The abilities go off before either run is laid out. Were the runs laid out first, a net
            // thrown by the player would miss the bot's run on the one phase it was thrown for,
            // because the player's side is always laid out first.
            var m = new GameManager(new System.Random(5));
            m.StartMatch(new[] { GladiatorDef.Retiarius }, new[] { GladiatorDef.Brutius });
            m.SubmitPick(PlayerSide.P1, GladiatorId.Retiarius);
            AdvanceUntil(m, MatchPhase.Planning);

            var retiarius = m.State.P1.Active;
            var bot = m.State.Bot.Active;
            float full = bot.DashReach();

            retiarius.Rage = GameConstants.RageMax;
            Assert.IsTrue(m.SubmitPlanningAction(PlayerSide.P1, ActionType.Defend, Vector2.zero, 0f, true));
            Assert.IsTrue(m.SubmitPlanningAction(PlayerSide.Bot, ActionType.Move,
                (retiarius.Pos - bot.Pos).normalized, 1f, false));

            AdvanceUntil(m, MatchPhase.Action);

            Assert.IsTrue(bot.IsEnsnared, "the net did not land");
            Assert.LessOrEqual(ObstacleField.Length(bot.Path), full * GameConstants.NetSpeedMult + 0.5f,
                "his run was laid out before the net landed on him");
        }

        private static void AdvanceUntil(GameManager m, MatchPhase phase)
        {
            for (float t = 0f; t < 30f && m.State.Phase != phase; t += Dt) m.Tick(Dt);
            Assert.AreEqual(phase, m.State.Phase, $"never reached {phase}");
        }
    }
}
