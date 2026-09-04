using System;
using System.Collections.Generic;
using ColosseumDuel.Core;
using NUnit.Framework;
using UnityEngine;

namespace ColosseumDuel.Tests
{
    /// <summary>
    /// End-to-end behaviour of the match state machine, driven exactly the way GameController
    /// drives it (Tick with a fixed delta), with no scene involved.
    /// </summary>
    public class MatchFlowTests
    {
        private const float Dt = 1f / 60f;
        private const float Tol = 0.01f;

        private static readonly GladiatorDef[] Squad =
        {
            GladiatorDef.Brutius, GladiatorDef.Barbarius, GladiatorDef.Hilius
        };

        private static GameManager NewMatch(int seed = 1234)
        {
            var m = new GameManager(new System.Random(seed));
            m.StartMatch(Squad, Squad);
            return m;
        }

        /// <summary>Ticks until the phase changes or the budget runs out.</summary>
        private static void AdvanceUntilPhaseLeaves(GameManager m, MatchPhase phase, float maxSeconds = 30f)
        {
            float t = 0f;
            while (m.State.Phase == phase && t < maxSeconds)
            {
                m.Tick(Dt);
                t += Dt;
            }
            Assert.AreNotEqual(phase, m.State.Phase, $"stuck in {phase} for {maxSeconds}s");
        }

        private static GameManager StartedRound(int seed = 1234)
        {
            var m = NewMatch(seed);
            Assert.AreEqual(MatchPhase.Pick, m.State.Phase);
            m.SubmitPick(PlayerSide.P1, GladiatorId.Brutius);
            return m;
        }

        // ------------------------------------------------------------------

        [Test]
        public void AMatchOpensInThePickPhase_AndTheBotPicksOnItsOwn()
        {
            var m = NewMatch();
            Assert.AreEqual(MatchPhase.Pick, m.State.Phase);
            Assert.IsNotNull(m.State.Bot.Active, "the bot should have picked immediately");
            Assert.IsNull(m.State.P1.Active, "the player still has to choose");
        }

        [Test]
        public void BothPicks_MoveTheMatchIntoReveal_WhichLastsARealAmountOfTime()
        {
            // Regression: Reveal used to be entered and left within the same frame, so no UI could
            // ever draw it.
            var m = StartedRound();
            Assert.AreEqual(MatchPhase.Reveal, m.State.Phase);

            m.Tick(Dt);
            Assert.AreEqual(MatchPhase.Reveal, m.State.Phase, "Reveal must survive at least one tick");

            AdvanceUntilPhaseLeaves(m, MatchPhase.Reveal);
            Assert.AreEqual(MatchPhase.Planning, m.State.Phase);
            Assert.AreEqual(1, m.State.Cycle);
        }

        [Test]
        public void FightersStartOnOppositeSidesOfTheArena_FacingEachOther()
        {
            // Regression: Pos was never initialised, so both fighters spawned on top of each other
            // in the centre and every round began with an instant collision.
            var m = StartedRound();
            var p1 = m.State.P1.Active;
            var bot = m.State.Bot.Active;

            float expected = GameConstants.ArenaRadius * GameConstants.SpawnDistanceFraction;
            Assert.AreEqual(-expected, p1.Pos.x, Tol);
            Assert.AreEqual(expected, bot.Pos.x, Tol);
            Assert.Greater(Vector2.Distance(p1.Pos, bot.Pos), GameConstants.PassByDistance,
                "they must start well out of weapon range");

            Assert.AreEqual(1f, p1.Facing.x, Tol, "P1 looks towards the bot");
            Assert.AreEqual(-1f, bot.Facing.x, Tol, "the bot looks back");
        }

        [Test]
        public void TheBotMakesAFreshDecisionEveryCycle()
        {
            // Regression: BeginCycle did not clear PlannedAction, and AutoFillMissingPlans only asks
            // the AI when the slot is empty - so the bot replayed its first decision forever.
            var m = StartedRound();
            AdvanceUntilPhaseLeaves(m, MatchPhase.Reveal);

            var powers = new List<float>();
            for (int cycle = 0; cycle < 5 && m.State.Phase != MatchPhase.MatchEnd; cycle++)
            {
                Assert.AreEqual(MatchPhase.Planning, m.State.Phase);
                Assert.AreEqual(ActionType.None, m.State.Bot.Active.PlannedAction,
                    "the bot's plan must be empty when a new planning phase opens");

                AdvanceUntilPhaseLeaves(m, MatchPhase.Planning);
                Assert.AreNotEqual(ActionType.None, m.State.Bot.Active.PlannedAction,
                    "and it must be filled again by the time the action phase starts");
                powers.Add(m.State.Bot.Active.PlannedPower);
                AdvanceUntilPhaseLeaves(m, MatchPhase.Action);
            }

            // BotAI rolls a fresh pull strength every time it is asked. If the plan were stale, this
            // value would be byte-identical across every cycle. (Aim direction is deliberately not
            // checked here: the bot charges straight down the x axis at a defending, stationary
            // player, so the same direction several cycles in a row is the correct answer.)
            Assert.Greater(powers.Count, 2);
            Assert.IsTrue(powers.Exists(p => !Mathf.Approximately(p, powers[0])),
                "the bot re-rolled its pull strength every cycle, so these should not all be identical");
        }

        [Test]
        public void APassByThatNeverSeparates_StillDealsDamageWhenTheCycleEnds()
        {
            // Regression: a pass-by only resolved on leaving the near band, so a cycle that ended
            // with both fighters still standing next to each other dealt no damage at all.
            var m = StartedRound();
            AdvanceUntilPhaseLeaves(m, MatchPhase.Reveal);

            var p1 = m.State.P1.Active;
            var bot = m.State.Bot.Active;
            float gap = (GameConstants.CollideDistance + GameConstants.PassByDistance) * 0.5f;
            p1.Pos = new Vector2(-gap * 0.5f, 0f);
            bot.Pos = new Vector2(gap * 0.5f, 0f);

            float p1Hp = p1.Hp, botHp = bot.Hp;

            m.SubmitPlanningAction(PlayerSide.P1, ActionType.Defend, Vector2.zero, 0f, false);
            m.SubmitPlanningAction(PlayerSide.Bot, ActionType.Defend, Vector2.zero, 0f, false);
            AdvanceUntilPhaseLeaves(m, MatchPhase.Planning);
            AdvanceUntilPhaseLeaves(m, MatchPhase.Action);

            Assert.Less(p1.Hp, p1Hp, "the player should have taken pass-by damage");
            Assert.Less(bot.Hp, botHp, "and so should the bot");
        }

        [Test]
        public void ADirectCollision_DamagesBothAndKnocksThemApart()
        {
            var m = StartedRound();
            AdvanceUntilPhaseLeaves(m, MatchPhase.Reveal);

            var p1 = m.State.P1.Active;
            var bot = m.State.Bot.Active;
            p1.Pos = new Vector2(-40f, 0f);
            bot.Pos = new Vector2(40f, 0f);
            float p1Hp = p1.Hp, botHp = bot.Hp;

            m.SubmitPlanningAction(PlayerSide.P1, ActionType.Move, Vector2.right, 1f, false);
            m.SubmitPlanningAction(PlayerSide.Bot, ActionType.Move, Vector2.left, 1f, false);
            AdvanceUntilPhaseLeaves(m, MatchPhase.Planning);
            AdvanceUntilPhaseLeaves(m, MatchPhase.Action);

            Assert.Less(p1.Hp, p1Hp);
            Assert.Less(bot.Hp, botHp);
            Assert.GreaterOrEqual(Vector2.Distance(p1.Pos, bot.Pos), GameConstants.KnockbackDistance - Tol,
                "the knockback should leave room to disengage next cycle");
        }

        [Test]
        public void MongooseTurnsOneCollisionIntoTwoHits()
        {
            // Hilius: 7 damage, ability Mongoose. Unarmed, undefended, two swings = 14.
            var attacker = new GladiatorInstance(GladiatorDef.Hilius);
            attacker.BeginCycle();
            attacker.Rage = 1f;
            attacker.ActivateAbility();

            var victim = new GladiatorInstance(GladiatorDef.Brutius);
            victim.BeginCycle();

            while (attacker.AttacksRemainingThisCycle > 0)
            {
                attacker.AttacksRemainingThisCycle--;
                CombatResolver.DealDamage(attacker, victim, isCollision: true);
            }

            Assert.AreEqual(GladiatorDef.Brutius.MaxHp - 14f, victim.Hp, Tol);
        }

        [Test]
        public void ARoundWinner_StaysOnTheArenaWithTheHpTheyEndedOn()
        {
            var m = StartedRound();
            AdvanceUntilPhaseLeaves(m, MatchPhase.Reveal);

            var p1 = m.State.P1.Active;
            var bot = m.State.Bot.Active;
            p1.Hp = 120f;
            bot.Hp = 1f; // dies on the first exchange
            p1.Pos = new Vector2(-40f, 0f);
            bot.Pos = new Vector2(40f, 0f);

            m.SubmitPlanningAction(PlayerSide.P1, ActionType.Move, Vector2.right, 1f, false);
            m.SubmitPlanningAction(PlayerSide.Bot, ActionType.Move, Vector2.left, 1f, false);
            AdvanceUntilPhaseLeaves(m, MatchPhase.Planning);
            AdvanceUntilPhaseLeaves(m, MatchPhase.Action);

            Assert.AreEqual(MatchPhase.RoundEnd, m.State.Phase);
            Assert.IsFalse(bot.Alive);
            Assert.IsTrue(p1.Alive);
            float hpAtRoundEnd = p1.Hp;
            Assert.Less(hpAtRoundEnd, 120f, "the dying bot still landed its simultaneous return blow");

            // Note: the losing side is the only one that picks, so the Pick phase is entered and left
            // within the same frame here - waiting for Phase == Pick would hang. Wait for the next
            // round's Reveal instead.
            RunUntil(m, s => s.Phase == MatchPhase.Reveal || s.Phase == MatchPhase.MatchEnd, 30f);
            Assert.AreEqual(MatchPhase.Reveal, m.State.Phase);

            Assert.AreSame(p1, m.State.P1.Active, "the winner stays on the arena");
            Assert.AreEqual(hpAtRoundEnd, p1.Hp, Tol, "and is not healed for the new round");
            Assert.AreEqual(2, m.State.Round);
        }

        [Test]
        public void OnlyTheLosingSidePicksAfterTheFirstRound()
        {
            var m = StartedRound();
            AdvanceUntilPhaseLeaves(m, MatchPhase.Reveal);

            var p1 = m.State.P1.Active;
            var bot = m.State.Bot.Active;
            var firstBotFighter = bot.Def.Id;
            bot.Hp = 1f;
            p1.Pos = new Vector2(-40f, 0f);
            bot.Pos = new Vector2(40f, 0f);

            m.SubmitPlanningAction(PlayerSide.P1, ActionType.Move, Vector2.right, 1f, false);
            m.SubmitPlanningAction(PlayerSide.Bot, ActionType.Move, Vector2.left, 1f, false);
            AdvanceUntilPhaseLeaves(m, MatchPhase.Planning);
            AdvanceUntilPhaseLeaves(m, MatchPhase.Action);
            RunUntil(m, s => s.Phase == MatchPhase.Reveal, 30f);

            Assert.AreSame(p1, m.State.P1.Active, "the player never had to pick again");
            Assert.AreNotEqual(firstBotFighter, m.State.Bot.Active.Def.Id,
                "the bot sent in a different gladiator after losing the round");
        }

        [Test]
        public void AWholeMatchRunsToAWinner_WithoutGettingStuck()
        {
            for (int seed = 0; seed < 8; seed++)
            {
                var m = NewMatch(seed);
                float t = 0f;
                while (m.State.Phase != MatchPhase.MatchEnd && t < 900f)
                {
                    if (m.State.P1.NeedsPick)
                        m.SubmitPick(PlayerSide.P1, FirstAliveOf(m.State.P1));
                    m.Tick(Dt);
                    t += Dt;
                }

                Assert.AreEqual(MatchPhase.MatchEnd, m.State.Phase, $"seed {seed} never finished");
                Assert.IsTrue(m.State.WinnerSide.HasValue, $"seed {seed} finished without a winner");

                var loser = m.State.Get(m.State.WinnerSide.Value == PlayerSide.P1 ? PlayerSide.Bot : PlayerSide.P1);
                Assert.IsFalse(loser.HasAnyAlive, $"seed {seed}: the loser should have no gladiators left");
            }
        }

        private static GladiatorId FirstAliveOf(PlayerState p)
            => p.Roster.Find(g => g.Alive).Def.Id;

        private static void RunUntil(GameManager m, Predicate<MatchState> done, float maxSeconds)
        {
            float t = 0f;
            while (!done(m.State) && t < maxSeconds)
            {
                m.Tick(Dt);
                t += Dt;
            }
            Assert.IsTrue(done(m.State), $"condition not reached within {maxSeconds}s");
        }
    }
}
