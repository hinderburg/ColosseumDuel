using System.Collections.Generic;
using ColosseumDuel.Core;
using NUnit.Framework;
using UnityEngine;

namespace ColosseumDuel.Tests
{
    /// <summary>
    /// The one-shot events the presentation layer hangs its effects on. These moments resolve and
    /// vanish inside a single frame, so nothing polling MatchState can see them - which makes it
    /// worth pinning that they fire exactly once, for the right side.
    /// </summary>
    public class EffectEventTests
    {
        private const float Dt = 1f / 60f;

        private static readonly GladiatorDef[] Squad =
        {
            GladiatorDef.Brutius, GladiatorDef.Barbarius, GladiatorDef.Hilius
        };

        private static GameManager StartedRound()
        {
            var m = new GameManager(new System.Random(4242));
            m.StartMatch(Squad, Squad);
            m.SubmitPick(PlayerSide.P1, GladiatorId.Brutius);
            Advance(m, GameConstants.RevealTime + 0.05f);
            Assert.AreEqual(MatchPhase.Planning, m.State.Phase);
            return m;
        }

        private static void Advance(GameManager m, float seconds)
        {
            for (float t = 0f; t < seconds; t += Dt) m.Tick(Dt);
        }

        private static void RunOneCycle(GameManager m)
        {
            Advance(m, GameConstants.PlanningTime + GameConstants.ActionTime + 0.1f);
        }

        [Test]
        public void AHeadOnCollisionReportsOneImpact_AndOneHitOnEachSide()
        {
            var m = StartedRound();
            var damaged = new List<PlayerSide>();
            var impacts = new List<Vector2>();
            m.Damaged += (side, _) => damaged.Add(side);
            m.Impact += p => impacts.Add(p);

            m.State.P1.Active.Pos = new Vector2(-40f, 0f);
            m.State.Bot.Active.Pos = new Vector2(40f, 0f);

            // Pointed along the charge they are about to be given. A run leaves along the nose and
            // bends onto its target, so two men set down across an axis they did not spawn along
            // would each curve away rather than meet.

            m.State.P1.Active.Facing = Vector2.right;
            m.State.Bot.Active.Facing = Vector2.left;
            m.SubmitPlanningAction(PlayerSide.P1, ActionType.Move, Vector2.right, 1f, false);
            m.SubmitPlanningAction(PlayerSide.Bot, ActionType.Move, Vector2.left, 1f, false);

            RunOneCycle(m);

            Assert.AreEqual(1, impacts.Count, "one collision, one impact");
            Assert.AreEqual(2, damaged.Count, "a collision damages both sides simultaneously");
            CollectionAssert.Contains(damaged, PlayerSide.P1);
            CollectionAssert.Contains(damaged, PlayerSide.Bot);
        }

        [Test]
        public void DamageIsReportedAgainstTheSideThatTookIt()
        {
            // The bot is the one being hit, so the event must name the bot - and the amount it
            // reports has to be the amount actually applied, which is the whole point of the test.
            var attacker = new GladiatorInstance(GladiatorDef.Brutius);
            var victim = new GladiatorInstance(GladiatorDef.Brutius);
            float dealt = CombatResolver.DealDamage(attacker, victim);

            Assert.AreEqual(GladiatorDef.Brutius.Damage * WeaponDef.TwoHandedMace.DamageMultiplier,
                dealt, 0.001f, "Brutius comes armed with the mace he trained on");
            Assert.AreEqual(GladiatorDef.Brutius.MaxHp - dealt, victim.Hp, 0.001f,
                "the amount reported must be the amount actually applied");
        }

        [Test]
        public void ABlowAtTheEndOfARunReportsDamageButNoImpact()
        {
            // A blow landed at the end of a move is not a crash, and the effects should not say it
            // was: no shockwave ring, only the two hits.
            var m = StartedRound();
            int impacts = 0, hits = 0;
            m.Impact += _ => impacts++;
            m.Damaged += (_, __) => hits++;

            var p1 = m.State.P1.Active;
            var bot = m.State.Bot.Active;
            float shortest = Mathf.Min(p1.WeaponDef.Reach, bot.WeaponDef.Reach);
            float gap = (GameConstants.CollideDistance + shortest) * 0.5f;

            p1.Pos = new Vector2(-gap * 0.5f, 0f);
            bot.Pos = new Vector2(gap * 0.5f, 0f);

            // Squared up on each other. They are standing across the arena from where they spawned,
            // and a man guards facing the way he last ran - so without this both are looking down
            // the long axis with the opponent abeam, which is outside every swing arc in the game.
            p1.Facing = Vector2.right;
            bot.Facing = Vector2.left;

            m.SubmitPlanningAction(PlayerSide.P1, ActionType.Defend, Vector2.zero, 0f, false);
            m.SubmitPlanningAction(PlayerSide.Bot, ActionType.Defend, Vector2.zero, 0f, false);

            RunOneCycle(m);

            Assert.AreEqual(0, impacts, "nobody collided head-on");
            Assert.AreEqual(2, hits, "but both were within reach when the cycle ended");
        }

        [Test]
        public void AnAbilityIsReportedOnceForTheSideThatSpentIt()
        {
            var m = StartedRound();
            var fired = new List<PlayerSide>();
            m.AbilityFired += side => fired.Add(side);

            m.State.P1.Active.Rage = GameConstants.RageMax;
            m.SubmitPlanningAction(PlayerSide.P1, ActionType.Defend, Vector2.zero, 0f, useAbility: true);
            m.SubmitPlanningAction(PlayerSide.Bot, ActionType.Defend, Vector2.zero, 0f, false);

            RunOneCycle(m);

            Assert.AreEqual(1, fired.Count);
            Assert.AreEqual(PlayerSide.P1, fired[0]);
        }

        [Test]
        public void AnAbilityThatCannotFireIsNotReported()
        {
            var m = StartedRound();
            int fired = 0;
            m.AbilityFired += _ => fired++;

            m.State.P1.Active.Rage = 0.5f; // not enough
            m.SubmitPlanningAction(PlayerSide.P1, ActionType.Defend, Vector2.zero, 0f, useAbility: true);
            m.SubmitPlanningAction(PlayerSide.Bot, ActionType.Defend, Vector2.zero, 0f, false);

            RunOneCycle(m);

            Assert.AreEqual(0, fired, "an ability that never fired must not announce itself");
        }

        [Test]
        public void MongooseReportsTwoHitsInOneCollision()
        {
            var m = StartedRound();
            m.State.P1.Active = m.State.P1.Roster.Find(g => g.Def.Id == GladiatorId.Hilius);
            m.State.P1.Active.Pos = new Vector2(-40f, 0f);
            m.State.P1.Active.Rage = GameConstants.RageMax;
            m.State.Bot.Active.Pos = new Vector2(40f, 0f);

            int hitsOnBot = 0;
            m.Damaged += (side, _) => { if (side == PlayerSide.Bot) hitsOnBot++; };

            m.SubmitPlanningAction(PlayerSide.P1, ActionType.Move, Vector2.right, 1f, useAbility: true);
            m.SubmitPlanningAction(PlayerSide.Bot, ActionType.Move, Vector2.left, 1f, false);

            RunOneCycle(m);

            Assert.AreEqual(2, hitsOnBot, "Mongoose lands twice, so the effect should play twice");
        }

        [Test]
        public void ATrapIsNotReportedAsABlow()
        {
            // The view reads "somebody was hit" as "the other one swung". A trap arriving through
            // Damaged therefore made the opponent throw an attack from wherever he happened to be
            // standing, with nobody within a hundred units of him - an attack out of nowhere, in a
            // game whose whole readability rests on attacks happening where two fighters meet.
            var m = StartedRound();

            int blows = 0, bites = 0;
            m.Damaged += (_, __) => blows++;
            m.Bitten += (_, __) => bites++;

            var p1 = m.State.P1.Active;
            var bot = m.State.Bot.Active;

            // Him alone on a trap, the opponent parked at the far end and well out of any reach.
            var trap = m.State.Traps.Traps[0];
            p1.Pos = trap.Pos;
            bot.Pos = new Vector2(0f, ArenaShape.RadiusY * 0.9f);

            m.SubmitPlanningAction(PlayerSide.P1, ActionType.Defend, Vector2.zero, 0f, false);
            m.SubmitPlanningAction(PlayerSide.Bot, ActionType.Defend, Vector2.zero, 0f, false);
            RunOneCycle(m);

            Assert.AreEqual(1, bites, "the trap should have closed on him");
            Assert.AreEqual(0, blows, "nobody was in reach of anybody, so nobody swung");
        }

        [Test]
        public void NobodySwingsAtTheEndOfAMoveThatFinishedOutOfReach()
        {
            var m = StartedRound();

            int blows = 0;
            m.Damaged += (_, __) => blows++;

            var p1 = m.State.P1.Active;
            var bot = m.State.Bot.Active;
            float gap = Mathf.Max(p1.WeaponDef.Reach, bot.WeaponDef.Reach) + 10f;
            p1.Pos = new Vector2(-gap * 0.5f, 0f);
            bot.Pos = new Vector2(gap * 0.5f, 0f);

            m.SubmitPlanningAction(PlayerSide.P1, ActionType.Defend, Vector2.zero, 0f, false);
            m.SubmitPlanningAction(PlayerSide.Bot, ActionType.Defend, Vector2.zero, 0f, false);
            RunOneCycle(m);

            Assert.AreEqual(0, blows, "the end-of-move blow must need somebody inside the reach");
        }
    }
}
