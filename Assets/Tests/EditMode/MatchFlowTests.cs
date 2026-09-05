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
        public void FightersStartAtOppositeEndsOfTheArena_FacingEachOther()
        {
            // Regression: Pos was never initialised, so both fighters spawned on top of each other
            // in the centre and every round began with an instant collision.
            var m = StartedRound();
            var p1 = m.State.P1.Active;
            var bot = m.State.Bot.Active;

            // Down the long axis, and always the same way round - the player's fighter at the near
            // end, where the player's own roster sits on screen, and the opponent's at the far end.
            float expected = ArenaShape.RadiusY * GameConstants.SpawnDistanceFraction;
            Assert.AreEqual(-expected, p1.Pos.y, Tol, "the player's fighter starts at the near end");
            Assert.AreEqual(expected, bot.Pos.y, Tol, "the opponent's starts at the far end");
            Assert.AreEqual(0f, p1.Pos.x, Tol);
            Assert.AreEqual(0f, bot.Pos.x, Tol);

            Assert.Greater(Vector2.Distance(p1.Pos, bot.Pos), GameConstants.PassByDistance,
                "they must start well out of weapon range");
            Assert.LessOrEqual(ArenaShape.NormalizedDistance(p1.Pos), 0.75f,
                "spawning past the first danger ring would start a late round already on fire");

            Assert.AreEqual(1f, p1.Facing.y, Tol, "P1 looks towards the bot");
            Assert.AreEqual(-1f, bot.Facing.y, Tol, "the bot looks back");
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
            // Measured against their bodies, not against the knockback constant. Asserting the
            // knockback matches KnockbackDistance is the constant checking itself, and it passed
            // happily for as long as that constant was 27 - less than the 28 they collide at and
            // well inside the 32 their two bodies occupy, so they were left standing in each other
            // and nothing on screen looked thrown back at all.
            float apart = Vector2.Distance(p1.Pos, bot.Pos);
            Assert.Greater(apart, GameConstants.GladiatorRadius * 2f,
                "the knockback left them overlapping, so nothing appears to have been thrown back");
            Assert.Greater(apart, GameConstants.CollideDistance,
                "they should end up outside the range they just collided at");
        }

        [Test]
        public void MongooseLandsTwoHitsThroughAWholeCycle()
        {
            // The existing Mongoose test drives CombatResolver directly, which proves the arithmetic
            // and nothing about the path a match actually takes: arming during planning, the ability
            // firing at the top of the action phase after BeginCycle has already set the attack
            // budget, and ExchangeBlows spending it. This runs that path.
            var m = NewMatch();
            m.SubmitPick(PlayerSide.P1, GladiatorId.Hilius);
            AdvanceUntilPhaseLeaves(m, MatchPhase.Reveal);

            var hilius = m.State.P1.Active;
            var victim = m.State.Bot.Active;
            hilius.Rage = GameConstants.RageMax;

            // Nose to nose, so the collision resolves on the first substep and neither has room to
            // pick anything up on the way in.
            hilius.Pos = new Vector2(0f, -GameConstants.CollideDistance * 0.4f);
            victim.Pos = new Vector2(0f, GameConstants.CollideDistance * 0.4f);
            float victimHp = victim.Hp;

            m.SubmitPlanningAction(PlayerSide.P1, ActionType.Move, Vector2.up, 1f, useAbility: true);
            m.SubmitPlanningAction(PlayerSide.Bot, ActionType.Defend, Vector2.zero, 0f, false);
            AdvanceUntilPhaseLeaves(m, MatchPhase.Planning);

            Assert.IsTrue(hilius.Buff.IsActive, "the ability did not fire");
            Assert.AreEqual(AbilityKey.Mongoose, hilius.Buff.Key);
            Assert.AreEqual(2, hilius.AttacksPerCycle);

            AdvanceUntilPhaseLeaves(m, MatchPhase.Action);

            // Two swings against a guard: base damage twice, both mitigated by the same 0.7.
            float expected = GladiatorDef.Hilius.Damage * GameConstants.DefendDamageMult * 2f;
            Assert.AreEqual(victimHp - expected, victim.Hp, Tol,
                $"Mongoose should have landed two hits, not {(victimHp - victim.Hp) / (GladiatorDef.Hilius.Damage * GameConstants.DefendDamageMult):0.0}");
        }

        [Test]
        public void MongooseKeepsBothAttacksOnTheFollowingCycleAndDropsBackAfter()
        {
            // The buff runs two cycles, and the attack budget is derived after the buff is aged - so
            // the cycle it expires on must drop back to one. Off by one either way and the ability
            // silently lasts one cycle too few or too many.
            var hilius = new GladiatorInstance(GladiatorDef.Hilius);
            hilius.BeginCycle();
            hilius.Rage = GameConstants.RageMax;
            hilius.ActivateAbility();

            Assert.AreEqual(2, hilius.AttacksRemainingThisCycle, "the cycle it was used in");

            hilius.BeginCycle();
            Assert.AreEqual(2, hilius.AttacksRemainingThisCycle, "the second cycle of the buff");

            hilius.BeginCycle();
            Assert.AreEqual(1, hilius.AttacksRemainingThisCycle, "the buff has expired by now");
        }

        [Test]
        public void DashCarriesTheSameGround()
        {
            // The reach of one dash is Speed * SpeedScale * ActionTime, and the two constants have
            // been moved in opposite directions on purpose: the action phase was halved to 1.0s so
            // the charge reads as a burst, and the speed doubled so it still crosses the same
            // ground. Changing either alone silently shortens or lengthens every move in the game,
            // which is why the expected distance is spelled out here rather than derived from them.
            const float expected = 150f; // Brutius: speed 10, at full power, over one action phase

            var m = StartedRound();
            AdvanceUntilPhaseLeaves(m, MatchPhase.Reveal);

            var p1 = m.State.P1.Active;
            var bot = m.State.Bot.Active;

            // Out of each other's way, and aimed across the short axis so the run has room and
            // meets no wall: a bounce would measure the arena rather than the dash.
            p1.Pos = new Vector2(-expected * 0.5f, 0f);
            bot.Pos = new Vector2(0f, ArenaShape.RadiusY * 0.9f);
            var start = p1.Pos;

            m.SubmitPlanningAction(PlayerSide.P1, ActionType.Move, Vector2.right, 1f, false);
            m.SubmitPlanningAction(PlayerSide.Bot, ActionType.Defend, Vector2.zero, 0f, false);
            AdvanceUntilPhaseLeaves(m, MatchPhase.Planning);
            AdvanceUntilPhaseLeaves(m, MatchPhase.Action);

            float travelled = Vector2.Distance(start, p1.Pos);
            Assert.AreEqual(expected, travelled, expected * 0.05f,
                $"a full-power dash covered {travelled:0} units where it should cover {expected:0}");
        }

        [Test]
        public void ACollisionAgainstTheWallDoesNotThrowAnyoneThroughIt()
        {
            // The knockback is applied straight to both positions, and a collision at the wall
            // pushes one of them outward. It also ends the action phase, so nothing would step him
            // again until the next one - he would stand outside the arena through all of planning.
            var m = StartedRound();
            AdvanceUntilPhaseLeaves(m, MatchPhase.Reveal);

            var p1 = m.State.P1.Active;
            var bot = m.State.Bot.Active;

            float wall = ArenaShape.RadiusY - GameConstants.GladiatorRadius;
            p1.Pos = new Vector2(0f, wall - 30f);
            bot.Pos = new Vector2(0f, wall);

            m.SubmitPlanningAction(PlayerSide.P1, ActionType.Move, Vector2.up, 1f, false);
            m.SubmitPlanningAction(PlayerSide.Bot, ActionType.Defend, Vector2.zero, 0f, false);
            AdvanceUntilPhaseLeaves(m, MatchPhase.Planning);
            AdvanceUntilPhaseLeaves(m, MatchPhase.Action);

            // Without this the test would pass on any run where they never actually met.
            Assert.IsTrue(m.State.Collided, "they never collided, so no knockback was applied");

            foreach (var g in new[] { p1, bot })
                Assert.LessOrEqual(ArenaShape.NormalizedDistance(g.Pos), 1.0001f,
                    $"{g.Def.Name} was knocked through the wall to {g.Pos}");
        }

        [Test]
        public void MongooseTurnsOneCollisionIntoTwoHits()
        {
            // Mongoose lets Hilius swing twice in one cycle, so an unarmed, undefended exchange
            // lands twice his base damage. Expressed against the stat rather than as a number, so a
            // balance pass on the damage table does not break a test about the ability.
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

            Assert.AreEqual(GladiatorDef.Brutius.MaxHp - GladiatorDef.Hilius.Damage * 2f, victim.Hp, Tol,
                "two swings should land two full hits");
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
