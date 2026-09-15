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

        private static GameManager StartedClash(int seed = 1234)
        {
            var m = NewMatch(seed);
            Assert.AreEqual(MatchPhase.Pick, m.State.Phase);
            m.SubmitPick(PlayerSide.P1, GladiatorId.Brutius);
            return m;
        }

        // ------------------------------------------------------------------

        [Test]
        public void AMatchOpensInThePickPhase_AndTheBotChoosesOnlyWhenThePlayerDoes()
        {
            var m = NewMatch();
            Assert.AreEqual(MatchPhase.Pick, m.State.Phase);
            Assert.IsNull(m.State.P1.Active, "the player still has to choose");
            Assert.IsNull(m.State.Bot.Active, "the bot's choice must not be made - or seen - before the player's");

            m.SubmitPick(PlayerSide.P1, GladiatorId.Brutius);
            Assert.IsNotNull(m.State.Bot.Active, "the bot chooses the moment the player does");
            Assert.AreEqual(MatchPhase.Reveal, m.State.Phase);
        }

        /// <summary>Against another player the other side may well choose first, and its choice has to hold.</summary>
        [Test]
        public void APickTheOtherSideSubmitsFirst_Stands()
        {
            var m = NewMatch();
            var chosen = m.State.Bot.Roster[2];
            Assert.IsTrue(m.SubmitPick(PlayerSide.Bot, 2));
            Assert.AreEqual(MatchPhase.Pick, m.State.Phase, "still waiting on the player");

            m.SubmitPick(PlayerSide.P1, GladiatorId.Brutius);
            Assert.AreSame(chosen, m.State.Bot.Active);
            Assert.AreEqual(MatchPhase.Reveal, m.State.Phase);
        }

        [Test]
        public void BothPicks_MoveTheMatchIntoReveal_WhichLastsARealAmountOfTime()
        {
            // Regression: Reveal used to be entered and left within the same frame, so no UI could
            // ever draw it.
            var m = StartedClash();
            Assert.AreEqual(MatchPhase.Reveal, m.State.Phase);

            m.Tick(Dt);
            Assert.AreEqual(MatchPhase.Reveal, m.State.Phase, "Reveal must survive at least one tick");

            AdvanceUntilPhaseLeaves(m, MatchPhase.Reveal);
            Assert.AreEqual(MatchPhase.Planning, m.State.Phase);
            Assert.AreEqual(1, m.State.Round);
        }

        [Test]
        public void FightersStartAtOppositeEndsOfTheArena_FacingEachOther()
        {
            // Regression: Pos was never initialised, so both fighters spawned on top of each other
            // in the centre and every clash began with an instant collision.
            var m = StartedClash();
            var p1 = m.State.P1.Active;
            var bot = m.State.Bot.Active;

            // Down the long axis, and always the same way round - the player's fighter at the near
            // end, where the player's own roster sits on screen, and the opponent's at the far end.
            float expected = ArenaShape.RadiusY * GameConstants.SpawnDistanceFraction;
            Assert.AreEqual(-expected, p1.Pos.y, Tol, "the player's fighter starts at the near end");
            Assert.AreEqual(expected, bot.Pos.y, Tol, "the opponent's starts at the far end");
            Assert.AreEqual(0f, p1.Pos.x, Tol);
            Assert.AreEqual(0f, bot.Pos.x, Tol);

            Assert.Greater(Vector2.Distance(p1.Pos, bot.Pos), WeaponDef.TwoHandedMace.Reach,
                "they must start out of even the longest weapon's range");
            Assert.LessOrEqual(ArenaShape.NormalizedDistance(p1.Pos), 0.75f,
                "spawning past the first danger ring would start a late clash already on fire");

            Assert.AreEqual(1f, p1.Facing.y, Tol, "P1 looks towards the bot");
            Assert.AreEqual(-1f, bot.Facing.y, Tol, "the bot looks back");
        }

        [Test]
        public void TheyLookAlongTheirRun_NotAtEachOther()
        {
            // They used to look at each other whatever they were doing, which read well and made
            // the direction a man faces mean nothing. It means something now: it decides which of
            // him a blow lands on. And with nothing limiting where he can be sent, it is entirely the
            // player's to choose - he faces along his run from the first step to the last.
            var m = StartedClash();
            AdvanceUntilPhaseLeaves(m, MatchPhase.Reveal);

            var p1 = m.State.P1.Active;
            var bot = m.State.Bot.Active;

            // On open sand in the middle, clear of the wall and of every obstacle, so the run is a
            // straight line and his heading has only one right answer.
            p1.Pos = Vector2.zero;
            bot.Pos = new Vector2(0f, 200f);

            // Straight backwards, away from the opponent, at full power.
            m.SubmitPlanningAction(PlayerSide.P1, ActionType.Move, Vector2.down, 1f, false);
            m.SubmitPlanningAction(PlayerSide.Bot, ActionType.Defend, Vector2.zero, 0f, false);
            AdvanceUntilPhaseLeaves(m, MatchPhase.Planning);

            for (int i = 0; i < 400 && m.State.Phase == MatchPhase.Action; i++)
            {
                Assert.AreEqual(0f, Vector2.Angle(p1.Facing, Vector2.down), 1f,
                    "he was sent backwards and should be looking backwards, from the first step on");
                m.Tick(Dt);
                if (!p1.Alive || !bot.Alive) break;
            }

            // And that is a back turned, not just a number. There is no limit on turning any more,
            // so running away from somebody shows him your spine whoever you are.
            Assert.AreEqual(HitSector.Back, p1.SectorHitFrom(bot.Pos),
                "running away from somebody should present your back to them");

            // She never moved, and a gladiator who stands still keeps the heading he last ran on.
            Assert.AreEqual(0f, Vector2.Angle(bot.Facing, Vector2.down), 0.01f,
                "standing still is not a reason to swivel");

            Assert.Less(p1.Pos.y, bot.Pos.y, "he really did move away, so this was not vacuous");
        }

        /// <summary>
        /// Two men who run into each other are thrown apart - and are still looking at each other
        /// when they land.
        ///
        /// The recoil moves them, and facing used to follow any movement at all, so the instant they
        /// struck both turned round and looked the way they had been thrown: away from the man they
        /// had just hit, back offered to him for the whole of the next planning phase. Facing follows
        /// the run he was ordered on, and being knocked back is not one.
        /// </summary>
        [Test]
        public void ThrownBackByACollision_TheyStillFaceEachOther()
        {
            var m = StartedClash();
            AdvanceUntilPhaseLeaves(m, MatchPhase.Reveal);

            var p1 = m.State.P1.Active;
            var bot = m.State.Bot.Active;

            // The same open stretch as above, and each sent to where the other is standing: head on.
            p1.Pos = Vector2.zero;
            bot.Pos = new Vector2(0f, 200f);

            bool collided = false;
            m.Impact += _ => collided = true;

            m.SubmitPlanningMoveTo(PlayerSide.P1, bot.Pos);
            m.SubmitPlanningMoveTo(PlayerSide.Bot, p1.Pos);
            AdvanceUntilPhaseLeaves(m, MatchPhase.Planning);
            AdvanceUntilPhaseLeaves(m, MatchPhase.Action);

            Assert.IsTrue(collided, "they never met, so nothing here threw anybody back");

            Assert.AreEqual(0f, Vector2.Angle(p1.Facing, Vector2.up), 1f,
                "he was thrown back from the collision and turned round to look where he was going");
            Assert.AreEqual(0f, Vector2.Angle(bot.Facing, Vector2.down), 1f,
                "so was she");
            Assert.AreEqual(HitSector.Front, p1.SectorHitFrom(bot.Pos),
                "after an exchange the two should still be face to face");
        }

        /// <summary>
        /// Sent to the far side of a column, he runs round it - never through it - and turns at the
        /// corner, facing each leg of the run as he takes it. And he still gets there: a run round an
        /// obstacle is paced to arrive, exactly like one across open sand.
        ///
        /// Without the pathfinder this fails three ways at once: he would stop against the stone,
        /// never arrive, and look one way the whole time.
        /// </summary>
        [Test]
        public void ARunRoundAColumnGoesRoundItAndTurnsAtTheCorner()
        {
            var m = StartedClash();
            AdvanceUntilPhaseLeaves(m, MatchPhase.Reveal);

            var p1 = m.State.P1.Active;
            var bot = m.State.Bot.Active;

            Obstacle column = null;
            foreach (var o in m.State.Obstacles.Obstacles)
                if (o.Kind == ObstacleKind.Column) { column = o; break; }
            Assert.IsNotNull(column, "the arena has no columns to run round");

            // Just below the column and just above it: the straight line between goes through it.
            // Close enough that the way round fits inside one of Brutius's dashes.
            p1.Pos = column.Centre + new Vector2(0f, -55f);
            var target = column.Centre + new Vector2(0f, 55f);
            Assert.IsFalse(m.State.Obstacles.IsClear(p1.Pos, target, GameConstants.GladiatorRadius),
                "the test needs the straight line to be blocked");

            bot.Pos = new Vector2(0f, 420f);
            m.SubmitPlanningMoveTo(PlayerSide.P1, target);
            m.SubmitPlanningAction(PlayerSide.Bot, ActionType.Defend, Vector2.zero, 0f, false);
            AdvanceUntilPhaseLeaves(m, MatchPhase.Planning);

            var firstHeading = p1.Facing;
            float widestTurn = 0f;
            float closest = float.MaxValue;

            for (int i = 0; i < 400 && m.State.Phase == MatchPhase.Action; i++)
            {
                m.Tick(Dt);
                widestTurn = Mathf.Max(widestTurn, Vector2.Angle(firstHeading, p1.Facing));
                closest = Mathf.Min(closest, Vector2.Distance(p1.Pos, column.Centre));
            }

            Assert.GreaterOrEqual(closest, column.Size + GameConstants.GladiatorRadius - 0.5f,
                "he went through the stone, or into it");
            Assert.Greater(widestTurn, 20f,
                "he looked one way the whole run - he was not facing along the way round");
            // Within a twentieth of the run rather than exactly - the same allowance
            // DashCarriesTheSameGround makes, for the same reason: the phase is ticked in frames, and
            // at this pace a frame is a couple of units of ground.
            float runLength = ObstacleField.Length(m.State.Obstacles.FindPath(
                column.Centre + new Vector2(0f, -55f), target, GameConstants.GladiatorRadius));
            Assert.Less(Vector2.Distance(p1.Pos, target), runLength * 0.05f,
                $"he should have got round and arrived, and is at {p1.Pos} instead of {target}");
        }

        [Test]
        public void TheBotMakesAFreshDecisionEveryRound()
        {
            // Regression: BeginRound did not clear PlannedAction, and AutoFillMissingPlans only asks
            // the AI when the slot is empty - so the bot replayed its first decision forever.
            var m = StartedClash();
            AdvanceUntilPhaseLeaves(m, MatchPhase.Reveal);

            var powers = new List<float>();
            for (int round = 0; round < 5 && m.State.Phase != MatchPhase.MatchEnd; round++)
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
            // value would be byte-identical across every round. (Aim direction is deliberately not
            // checked here: the bot charges straight down the x axis at a defending, stationary
            // player, so the same direction several rounds in a row is the correct answer.)
            Assert.Greater(powers.Count, 2);
            Assert.IsTrue(powers.Exists(p => !Mathf.Approximately(p, powers[0])),
                "the bot re-rolled its pull strength every round, so these should not all be identical");
        }

        [Test]
        public void AMoveThatEndsInsideWeaponRange_FinishesWithABlow()
        {
            // Ordinary movement can end in an attack now, which is what makes closing to exactly
            // the edge of your reach a decision rather than a formality.
            var m = StartedClash();
            AdvanceUntilPhaseLeaves(m, MatchPhase.Reveal);

            var p1 = m.State.P1.Active;
            var bot = m.State.Bot.Active;

            // Well inside both weapons and well outside a collision, so nobody runs into anybody.
            float gap = GameConstants.CollideDistance
                        + (Mathf.Min(p1.WeaponDef.Reach, bot.WeaponDef.Reach) - GameConstants.CollideDistance) * 0.5f;
            p1.Pos = new Vector2(-gap * 0.5f, 0f);
            bot.Pos = new Vector2(gap * 0.5f, 0f);

            // Squared up. A gladiator guards facing the way he last ran, and these two have been
            // set down across the short axis of an arena they spawned along - so left as they are,
            // each has the other abeam, which is outside every swing arc there is.
            p1.Facing = Vector2.right;
            bot.Facing = Vector2.left;

            float p1Hp = p1.Hp, botHp = bot.Hp;

            m.SubmitPlanningAction(PlayerSide.P1, ActionType.Defend, Vector2.zero, 0f, false);
            m.SubmitPlanningAction(PlayerSide.Bot, ActionType.Defend, Vector2.zero, 0f, false);
            AdvanceUntilPhaseLeaves(m, MatchPhase.Planning);
            AdvanceUntilPhaseLeaves(m, MatchPhase.Action);

            Assert.IsFalse(m.State.Collided, "they never touched - this is the reach blow, not a crash");
            Assert.Less(p1.Hp, p1Hp, "the player should have been struck at the end of the round");
            Assert.Less(bot.Hp, botHp, "and so should the bot");
        }

        [Test]
        public void ARunStraightPastSomebodyStillCosts()
        {
            // Passing through reach and stopping inside it are the same event to a man with a
            // weapon in his hand. Resolved only at the end of the round, a charge clean through the
            // opponent cost nothing at all - the one approach in the game that most obviously
            // should - and at full speed a fighter crosses most of a body length per substep, so
            // the crossing has to be measured over the step rather than sampled at its ends.
            var m = StartedClash();
            AdvanceUntilPhaseLeaves(m, MatchPhase.Reveal);

            var p1 = m.State.P1.Active;
            var bot = m.State.Bot.Active;
            p1.Weapon = WeaponKind.DualSwords;   // the shortest reach in the game, level with the shield
            bot.Weapon = WeaponKind.DualSwords;

            // Side by side across his path, far enough apart that they never collide, and starting
            // half a dash short of her so a full dash carries him the same distance out the far
            // side. Both distances are derived rather than written down, so the test keeps failing
            // on the rule instead of on its own geometry when the numbers are retuned.
            //
            // The widest a pass can miss by and still land is reach times the sine of the half arc:
            // at exactly that gap, the moment she comes inside his reach is the moment she reaches
            // the edge of his swing, and any wider and she is abeam before he can answer. Four
            // fifths of it, so the test sits inside the rule rather than on its boundary.
            float halfArc = WeaponDef.DualSwords.SwingArcDegrees * 0.5f * Mathf.Deg2Rad;
            float miss = WeaponDef.DualSwords.Reach * Mathf.Sin(halfArc) * 0.8f;
            Assert.Greater(miss, GameConstants.CollideDistance,
                "and still wide enough that running past is a pass and not a crash");
            p1.Pos = new Vector2(-miss, -p1.DashReach() * 0.5f);
            bot.Pos = new Vector2(0f, 0f);
            float botHp = bot.Hp;

            m.SubmitPlanningAction(PlayerSide.P1, ActionType.Move, Vector2.up, 1f, false);
            m.SubmitPlanningAction(PlayerSide.Bot, ActionType.Defend, Vector2.zero, 0f, false);
            AdvanceUntilPhaseLeaves(m, MatchPhase.Planning);
            AdvanceUntilPhaseLeaves(m, MatchPhase.Action);

            Assert.IsFalse(m.State.Collided, "they passed, they did not crash");
            Assert.Greater(Vector2.Distance(p1.Pos, bot.Pos), WeaponDef.DualSwords.Reach,
                "he should have run clean past and be out of reach again by the end");
            Assert.Less(bot.Hp, botHp, "and he should have struck her on the way through");
        }

        [Test]
        public void AMoveThatEndsOutOfRange_LandsNothing()
        {
            // The other half of the same rule, and the half that makes reach worth reading: stop
            // one unit short and the round costs nothing at all.
            var m = StartedClash();
            AdvanceUntilPhaseLeaves(m, MatchPhase.Reveal);

            var p1 = m.State.P1.Active;
            var bot = m.State.Bot.Active;

            float gap = Mathf.Max(p1.WeaponDef.Reach, bot.WeaponDef.Reach) + 4f;
            p1.Pos = new Vector2(-gap * 0.5f, 0f);
            bot.Pos = new Vector2(gap * 0.5f, 0f);

            // Squared up. A gladiator guards facing the way he last ran, and these two have been
            // set down across the short axis of an arena they spawned along - so left as they are,
            // each has the other abeam, which is outside every swing arc there is.
            p1.Facing = Vector2.right;
            bot.Facing = Vector2.left;

            float p1Hp = p1.Hp, botHp = bot.Hp;

            m.SubmitPlanningAction(PlayerSide.P1, ActionType.Defend, Vector2.zero, 0f, false);
            m.SubmitPlanningAction(PlayerSide.Bot, ActionType.Defend, Vector2.zero, 0f, false);
            AdvanceUntilPhaseLeaves(m, MatchPhase.Planning);
            AdvanceUntilPhaseLeaves(m, MatchPhase.Action);

            Assert.AreEqual(p1Hp, p1.Hp, Tol, "nothing was in range of him");
            Assert.AreEqual(botHp, bot.Hp, Tol);
        }

        [Test]
        public void TheLongerWeaponStrikesFromWhereTheShorterOneCannotAnswer()
        {
            // The reason reach is a stat rather than a constant. A mace ending its run at its own
            // limit hits a twin-sword fighter who has no way to reach back from there.
            var m = StartedClash();
            AdvanceUntilPhaseLeaves(m, MatchPhase.Reveal);

            var p1 = m.State.P1.Active;
            var bot = m.State.Bot.Active;
            p1.Weapon = WeaponKind.DualSwords;
            bot.Weapon = WeaponKind.TwoHandedMace;

            float gap = (WeaponDef.DualSwords.Reach + WeaponDef.TwoHandedMace.Reach) * 0.5f;
            Assert.Greater(gap, WeaponDef.DualSwords.Reach, "the gap has to be past the short weapon");
            p1.Pos = new Vector2(-gap * 0.5f, 0f);
            bot.Pos = new Vector2(gap * 0.5f, 0f);

            // Squared up. A gladiator guards facing the way he last ran, and these two have been
            // set down across the short axis of an arena they spawned along - so left as they are,
            // each has the other abeam, which is outside every swing arc there is.
            p1.Facing = Vector2.right;
            bot.Facing = Vector2.left;

            float p1Hp = p1.Hp, botHp = bot.Hp;

            m.SubmitPlanningAction(PlayerSide.P1, ActionType.Defend, Vector2.zero, 0f, false);
            m.SubmitPlanningAction(PlayerSide.Bot, ActionType.Defend, Vector2.zero, 0f, false);
            AdvanceUntilPhaseLeaves(m, MatchPhase.Planning);
            AdvanceUntilPhaseLeaves(m, MatchPhase.Action);

            Assert.Less(p1.Hp, p1Hp, "the mace should have reached him");
            Assert.AreEqual(botHp, bot.Hp, Tol, "and the short blades should not have reached back");
        }

        [Test]
        public void ADirectCollision_DamagesBothAndKnocksThemApart()
        {
            var m = StartedClash();
            AdvanceUntilPhaseLeaves(m, MatchPhase.Reveal);

            var p1 = m.State.P1.Active;
            var bot = m.State.Bot.Active;
            p1.Pos = new Vector2(-40f, 0f);
            bot.Pos = new Vector2(40f, 0f);

            // Pointed along the charge they are about to be given. A run leaves along the nose and
            // bends onto its target, so two men set down across an axis they did not spawn along
            // would each curve away rather than meet.

            p1.Facing = Vector2.right;
            bot.Facing = Vector2.left;
            float p1Hp = p1.Hp, botHp = bot.Hp;

            m.SubmitPlanningAction(PlayerSide.P1, ActionType.Move, Vector2.right, 1f, false);
            m.SubmitPlanningAction(PlayerSide.Bot, ActionType.Move, Vector2.left, 1f, false);
            AdvanceUntilPhaseLeaves(m, MatchPhase.Planning);

            // The recoil is a fact about the action phase, so it is read inside it. Everyone is
            // stopped when the phase ends - nothing between there and the next one moves anybody,
            // and a velocity left standing had the view running them on the spot through four
            // seconds of planning - so reading it afterwards finds a zero and proves nothing.
            var recoil = Vector2.zero;
            float elapsed = 0f;
            while (m.State.Phase == MatchPhase.Action && elapsed < 30f)
            {
                if (m.State.Collided) recoil = p1.Vel;
                m.Tick(Dt);
                elapsed += Dt;
            }
            Assert.AreNotEqual(MatchPhase.Action, m.State.Phase, "stuck in Action");

            Assert.Less(p1.Hp, p1Hp);
            Assert.Less(bot.Hp, botHp);
            // Measured against their bodies, not against whatever constant does the bouncing.
            // Asserting the recoil matches the recoil constant is the constant checking itself, and
            // that passed happily for as long as the old knockback was 27 - less than the 28 they
            // collide at and well inside the 32 their two bodies occupy, so they were left standing
            // in each other and nothing on screen looked thrown back at all.
            float apart = Vector2.Distance(p1.Pos, bot.Pos);
            Assert.Greater(apart, GameConstants.GladiatorRadius * 2f,
                "the recoil left them overlapping, so nothing appears to have been thrown back");
            Assert.Greater(apart, GameConstants.CollideDistance,
                "they should end up outside the range they just collided at");

            // And they got there by moving, not by being teleported: a cut on the frame the swings
            // were meant to play is what this replaced.
            Assert.Greater(Vector2.Dot(recoil, (p1.Pos - bot.Pos).normalized), 0f,
                "the player was never travelling away from the impact - he was moved, not thrown");
        }

        [Test]
        public void MongooseLandsTwoHitsThroughAWholeRound()
        {
            // The existing Mongoose test drives CombatResolver directly, which proves the arithmetic
            // and nothing about the path a match actually takes: arming during planning, the ability
            // firing at the top of the action phase after BeginRound has already set the attack
            // budget, and ExchangeBlows spending it. This runs that path.
            var m = NewMatch();
            m.SubmitPick(PlayerSide.P1, GladiatorId.Hilius);
            AdvanceUntilPhaseLeaves(m, MatchPhase.Reveal);

            var hilius = m.State.P1.Active;
            var victim = m.State.Bot.Active;
            hilius.Ability = AbilityKey.Mongoose;
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
            Assert.AreEqual(2, hilius.AttacksPerRound);

            AdvanceUntilPhaseLeaves(m, MatchPhase.Action);

            // Two swings against a guard: base damage twice, both mitigated by the same 0.7.
            float expected = GladiatorDef.Hilius.Damage * GameConstants.DefendDamageMult * 2f;
            Assert.AreEqual(victimHp - expected, victim.Hp, Tol,
                $"Mongoose should have landed two hits, not {(victimHp - victim.Hp) / (GladiatorDef.Hilius.Damage * GameConstants.DefendDamageMult):0.0}");
        }

        [Test]
        public void MongooseKeepsBothAttacksOnTheFollowingRoundAndDropsBackAfter()
        {
            // The buff runs as many rounds as its card says, and the attack budget is derived after the
            // buff is aged - so the round it expires on must drop back to one. Off by one either way
            // and the ability silently lasts one round too few or too many.
            var hilius = new GladiatorInstance(GladiatorDef.Hilius) { Ability = AbilityKey.Mongoose };
            hilius.BeginRound();
            hilius.Rage = GameConstants.RageMax;
            hilius.ActivateAbility();

            Assert.AreEqual(2, hilius.AttacksRemainingThisRound, "the round it was used in");

            int rounds = AbilityDef.Get(AbilityKey.Mongoose).Rounds;
            Assert.GreaterOrEqual(rounds, 2, "this test is about the round after the one it was used in");
            for (int round = 2; round <= rounds; round++)
            {
                hilius.BeginRound();
                Assert.AreEqual(2, hilius.AttacksRemainingThisRound, $"round {round} of the buff");
            }

            hilius.BeginRound();
            Assert.AreEqual(1, hilius.AttacksRemainingThisRound, "the buff has expired by now");
        }

        [Test]
        public void ARestartGoesBackToThePickScreenWithEverythingCleared()
        {
            var m = StartedClash();
            AdvanceUntilPhaseLeaves(m, MatchPhase.Reveal);

            // Leave the match thoroughly dirtied: a fighter chosen, rage banked, a buff running, the
            // ability on cooldown, gear carried and several rounds on the clock.
            var p1 = m.State.P1.Active;
            p1.Rage = GameConstants.RageMax;
            p1.ActivateAbility();
            p1.Weapon = WeaponKind.TwoHandedMace;
            p1.BlessWeapon();
            p1.Hp = 5f;
            m.State.Round = 9;

            m.StartMatch(Squad, Squad);

            // Back at the pick screen, which is what NeedsPick answers - and it reads Active, so an
            // Active left over from the previous match is exactly what used to skip this screen.
            Assert.AreEqual(MatchPhase.Pick, m.State.Phase);
            Assert.IsNull(m.State.P1.Active, "the player should have nobody chosen yet");
            Assert.IsTrue(m.State.P1.NeedsPick, "the pick screen is keyed off this");
            Assert.AreEqual(0, m.State.Clash);
            Assert.AreEqual(0, m.State.Round);

            foreach (var g in m.State.P1.Roster)
            {
                Assert.IsTrue(g.Alive);
                Assert.AreEqual(g.Def.MaxHp, g.Hp, Tol);
                Assert.AreEqual(0f, g.Rage, Tol, $"{g.Def.Name} carried rage into a fresh match");
                Assert.IsFalse(g.Buff.IsActive, $"{g.Def.Name} carried a running ability into a fresh match");
                Assert.IsFalse(g.WeaponBuffed,
                    $"{g.Def.Name} carried a blessed weapon into a fresh match");
            }
        }

        [Test]
        public void TheTutorialClashPutsTheBlessingOnTheWayToTheTapPoint()
        {
            // Whoever was picked. The layout is a fixed distance, so it has to sit inside the reach
            // of the slowest of them - and the blessing has to be between him and the point the
            // tutorial tells him to tap, or the one sentence the tutorial says is not true.
            foreach (var def in GladiatorDef.All)
            {
                var m = new GameManager(new System.Random(def.Id.GetHashCode()));
                // A squad of him alone: the six are not all in any one squad of three any more.
                m.StartMatch(new[] { def }, Squad, tutorial: true);
                m.SubmitPick(PlayerSide.P1, def.Id);

                var player = m.State.P1.Active;
                Assert.IsTrue(m.State.Buffs.Position.HasValue, $"{def.Name}'s tutorial has no blessing on the sand");
                var blessing = m.State.Buffs.Position.Value;

                float toBlessing = Vector2.Distance(player.Pos, blessing);
                float toTap = Vector2.Distance(player.Pos, m.State.TutorialTapPoint);
                Assert.Less(toBlessing, player.DashReach(),
                    $"{def.Name} cannot reach the blessing the tutorial tells him to run through");
                Assert.Greater(toTap, toBlessing, "the tap has to be beyond the blessing, not on it");
                Assert.Less(toTap, player.DashReach(), "and still inside one run");
            }
        }

        [Test]
        public void TheArenaStandsTenColumns_EachWithRoomToRunRound()
        {
            var field = ObstacleField.Standard();
            int columns = 0;
            foreach (var o in field.Obstacles)
            {
                if (o.Kind != ObstacleKind.Column) continue;
                columns++;

                // A man can stand on every side of it, between it and the wall included, so no
                // column seals off a stretch of the arena.
                foreach (var side in new[] { Vector2.up, Vector2.down, Vector2.left, Vector2.right })
                {
                    var spot = o.Centre + side * (o.Size + GameConstants.GladiatorRadius + 2f);
                    Assert.IsTrue(field.IsFree(spot, GameConstants.GladiatorRadius),
                        $"no room to stand beside the column at {o.Centre}, towards {side}");
                }
            }

            Assert.AreEqual(10, columns, "the arena should stand ten columns, as sketched");
        }

        [Test]
        public void AnOrdinaryMatchIsNotRearranged()
        {
            // The teaching layout is a first-fight thing. A later match that quietly kept the free
            // blessing on the path would be an easier game wearing the same clothes.
            var m = NewMatch();
            m.SubmitPick(PlayerSide.P1, GladiatorId.Brutius);

            Assert.IsFalse(m.State.Tutorial);
            Assert.AreEqual(Vector2.zero, m.State.TutorialTapPoint);
            Assert.IsFalse(m.State.Buffs.Position.HasValue,
                "no blessing should be laid on the path of a man nobody is pointing anywhere");
        }

        /// <summary>
        /// A run inside his reach arrives; one beyond it stops where his reach runs out.
        ///
        /// Checked from a step to more than Brutius can run, so neither half can hide behind a test
        /// that only ever asked for something short.
        /// </summary>
        [Test]
        public void ARunArrivesInsideHisReach_AndStopsWhereItRunsOut()
        {
            foreach (float length in new[] { 40f, 225f, 520f })
            {
                var m = StartedClash();
                AdvanceUntilPhaseLeaves(m, MatchPhase.Reveal);

                var p1 = m.State.P1.Active;
                var bot = m.State.Bot.Active;

                // Straight up the middle from the near end - open sand, clear of every obstacle -
                // with the bot out of the way to one side, well beyond any weapon's reach.
                p1.Pos = new Vector2(0f, -260f);
                bot.Pos = new Vector2(240f, 0f);
                var target = p1.Pos + new Vector2(0f, length);
                float expected = Mathf.Min(length, p1.DashReach());
                var end = p1.Pos + new Vector2(0f, expected);

                m.SubmitPlanningMoveTo(PlayerSide.P1, target);
                m.SubmitPlanningAction(PlayerSide.Bot, ActionType.Defend, Vector2.zero, 0f, false);
                AdvanceUntilPhaseLeaves(m, MatchPhase.Planning);
                AdvanceUntilPhaseLeaves(m, MatchPhase.Action);

                // Within a twentieth of the run: the phase is ticked in frames, and a frame of a run
                // paced to fill one second is a sixtieth of it.
                Assert.Less(Vector2.Distance(p1.Pos, end), expected * 0.05f + 1f,
                    $"a run of {length:0} with a reach of {p1.DashReach():0} finished at {p1.Pos}, not {end}");
            }
        }

        /// <summary>
        /// One point of speed is three times the run it used to be: 45 units a phase rather than 15.
        /// Spelled out rather than derived, because the constants that make it up have each been
        /// moved on purpose, and moving any of them silently changes every run in the game.
        /// </summary>
        [Test]
        public void EachArchetypeRunsHisSpeedTimesTheScale()
        {
            Assert.AreEqual(13f * GameConstants.SpeedScale, new GladiatorInstance(GladiatorDef.Brutius).DashReach(), 0.01f, "Brutius, speed 13");
            Assert.AreEqual(15f * GameConstants.SpeedScale, new GladiatorInstance(GladiatorDef.Barbarius).DashReach(), 0.01f, "Barbarius, speed 15");
            Assert.AreEqual(20f * GameConstants.SpeedScale, new GladiatorInstance(GladiatorDef.Hilius).DashReach(), 0.01f, "Hilius, speed 20");
        }

        /// <summary>
        /// The action phase lasts two seconds - and a run is no longer for it. The reach above is
        /// per phase, so doubling the phase halved the pace instead of doubling the ground.
        /// </summary>
        [Test]
        public void TheActionPhaseLastsTwoSeconds()
        {
            Assert.AreEqual(2f, GameConstants.ActionTime, 0.0001f, "the action phase is meant to be two seconds");

            var m = StartedClash();
            AdvanceUntilPhaseLeaves(m, MatchPhase.Reveal);

            // Far apart and standing still, so nothing cuts the phase short.
            m.State.P1.Active.Pos = new Vector2(0f, -200f);
            m.State.Bot.Active.Pos = new Vector2(0f, 200f);
            m.SubmitPlanningAction(PlayerSide.P1, ActionType.Defend, Vector2.zero, 0f, false);
            m.SubmitPlanningAction(PlayerSide.Bot, ActionType.Defend, Vector2.zero, 0f, false);
            AdvanceUntilPhaseLeaves(m, MatchPhase.Planning);

            float spent = 0f;
            while (m.State.Phase == MatchPhase.Action && spent < 10f)
            {
                m.Tick(Dt);
                spent += Dt;
            }

            Assert.AreEqual(2f, spent, Dt * 1.5f, "the action phase ran for the wrong length of time");
        }

        /// <summary>
        /// A drawn run is run as it was drawn - round its corner, not straight at its end. The shape
        /// is the order: two runs to the same place by different ways are different decisions.
        /// </summary>
        [Test]
        public void ADrawnRunIsRunAsDrawn_CornerAndAll()
        {
            var m = StartedClash();
            AdvanceUntilPhaseLeaves(m, MatchPhase.Reveal);

            var p1 = m.State.P1.Active;
            var bot = m.State.Bot.Active;

            // An L on the open sand in the middle: up, then across. Going straight for its end would
            // pass seventy units wide of the corner, so reaching the corner is only the drawn run.
            p1.Pos = new Vector2(-40f, -120f);
            bot.Pos = new Vector2(0f, ArenaShape.RadiusY * 0.85f);
            var corner = new Vector2(-40f, 100f);
            var end = new Vector2(40f, 100f);

            Assert.IsTrue(m.SubmitPlanningPath(PlayerSide.P1, new[] { corner, end }));
            m.SubmitPlanningAction(PlayerSide.Bot, ActionType.Defend, Vector2.zero, 0f, false);
            AdvanceUntilPhaseLeaves(m, MatchPhase.Planning);

            float nearestToCorner = float.MaxValue;
            for (int i = 0; i < 400 && m.State.Phase == MatchPhase.Action; i++)
            {
                m.Tick(Dt);
                nearestToCorner = Mathf.Min(nearestToCorner, Vector2.Distance(p1.Pos, corner));
            }

            Assert.Less(nearestToCorner, 6f, "he cut the corner the player drew");
            Assert.Less(Vector2.Distance(p1.Pos, end), 16f, "and should have finished where the drawing did");
        }

        /// <summary>
        /// A drawn run longer than he can run is cut exactly where his reach runs out - when it is
        /// filed, so the lane can show it, and again in the running.
        /// </summary>
        [Test]
        public void ADrawnRunLongerThanHisReachIsCutWhereItRunsOut()
        {
            var m = StartedClash();
            AdvanceUntilPhaseLeaves(m, MatchPhase.Reveal);

            var p1 = m.State.P1.Active;
            p1.Pos = new Vector2(0f, -100f);
            m.State.Bot.Active.Pos = new Vector2(0f, ArenaShape.RadiusY * 0.85f);

            // Up and down the open middle, two hundred a stroke: eight hundred drawn against what
            // Brutius can run, which runs out somewhere up the third stroke.
            var strokes = new[]
            {
                new Vector2(0f, 100f), new Vector2(0f, -100f), new Vector2(0f, 100f), new Vector2(0f, -100f)
            };
            Assert.IsTrue(m.SubmitPlanningPath(PlayerSide.P1, strokes));

            float reach = p1.DashReach();
            Assert.That(reach, Is.InRange(400f, 600f), "this test's strokes are laid out for a reach inside the third");
            var stop = new Vector2(0f, -100f + (reach - 400f));
            Assert.AreEqual(reach, ObstacleField.Length(p1.PlannedPath), 0.5f, "filed longer than he can run");
            Assert.Less(Vector2.Distance(p1.PlannedTarget, stop), 0.5f, "cut somewhere other than where it ran out");

            m.SubmitPlanningAction(PlayerSide.Bot, ActionType.Defend, Vector2.zero, 0f, false);
            AdvanceUntilPhaseLeaves(m, MatchPhase.Planning);
            AdvanceUntilPhaseLeaves(m, MatchPhase.Action);

            Assert.Less(Vector2.Distance(p1.Pos, stop), reach * 0.05f + 1f,
                "he should have stopped where his reach ran out");
        }

        /// <summary>
        /// Armed, Rampage lengthens the run that can be filed by half again - before it has fired,
        /// because the run is drawn during planning and the ability fires as the action phase opens.
        /// </summary>
        [Test]
        public void ArmedRampageLengthensTheRunThatCanBeDrawn()
        {
            var m = StartedClash();
            AdvanceUntilPhaseLeaves(m, MatchPhase.Reveal);

            var p1 = m.State.P1.Active;
            p1.Ability = AbilityKey.Rampage;
            p1.Pos = new Vector2(0f, -100f);

            // Longer than the run Rampage gives him, so the length filed is the reach and not the drawing.
            var strokes = new[]
            {
                new Vector2(0f, 100f), new Vector2(0f, -100f), new Vector2(0f, 100f), new Vector2(0f, -100f),
                new Vector2(0f, 100f), new Vector2(0f, -100f)
            };

            p1.Rage = GameConstants.RageMax;
            Assert.IsTrue(m.SubmitAbility(PlayerSide.P1, true), "the ability did not arm, so this proves nothing");
            Assert.IsTrue(m.SubmitPlanningPath(PlayerSide.P1, strokes));

            Assert.AreEqual(p1.DashReach() * 1.5f, ObstacleField.Length(p1.PlannedPath), 0.5f,
                "an armed Rampage should let him be sent half as far again");
        }

        /// <summary>
        /// The bot runs at what it wants and through it, not three times past it. A run is three
        /// times the dash it was, and at the bot's usual three quarters and more of it, it charged
        /// straight past the player and on into the far wall.
        /// </summary>
        [Test]
        public void TheBotDoesNotRunFarPastWhatItIsGoingFor()
        {
            var me = new GladiatorInstance(GladiatorDef.Hilius) { Pos = Vector2.zero };
            var opp = new GladiatorInstance(GladiatorDef.Brutius) { Pos = new Vector2(0f, 120f) };
            var buffs = new ArenaPickup(new System.Random(1), ObstacleField.Empty);

            int moves = 0;
            for (int seed = 0; seed < 20; seed++)
            {
                var decision = BotAI.Decide(me, opp, buffs, new System.Random(seed));
                if (decision.Action != ActionType.Move) continue;
                moves++;

                float run = decision.Power * me.DashReach();
                Assert.LessOrEqual(run, 120f + GameConstants.GladiatorRadius * 2f + 0.01f,
                    $"the bot ordered a run of {run:0} at a man 120 away");
            }

            Assert.Greater(moves, 10, "the bot hardly moved, so this proves nothing");
        }

        /// <summary>
        /// With the player's side on auto the bot plays both: it picks for him, plans every one of
        /// his rounds, and the match goes the whole way with nothing from the player at all.
        /// </summary>
        [Test]
        public void OnAutoTheBotPlaysThePlayersSideToTheEndOfTheMatch()
        {
            var m = NewMatch(77);
            m.P1Auto = true;

            bool movedOnHisOwn = false;
            for (float t = 0f; t < 900f && m.State.Phase != MatchPhase.MatchEnd; t += Dt)
            {
                var p1 = m.State.P1.Active;
                if (m.State.Phase == MatchPhase.Action && p1 != null && p1.PlannedAction == ActionType.Move)
                    movedOnHisOwn = true;
                m.Tick(Dt);
            }

            Assert.AreEqual(MatchPhase.MatchEnd, m.State.Phase, "an auto match never finished");
            Assert.IsTrue(movedOnHisOwn,
                "the player's side only ever stood - the timeout's Defend, not the bot playing him");
        }

        [Test]
        public void TurningAutoOnMidPlanningPlansThePlayersRoundThere_AndOffLeavesItToHim()
        {
            var m = StartedClash();
            AdvanceUntilPhaseLeaves(m, MatchPhase.Reveal);
            Assert.AreEqual(MatchPhase.Planning, m.State.Phase);
            Assert.AreEqual(ActionType.None, m.State.P1.Active.PlannedAction);

            m.P1Auto = true;
            Assert.AreNotEqual(ActionType.None, m.State.P1.Active.PlannedAction,
                "switched on during planning, it should decide this round rather than wait for the next");

            m.P1Auto = false;
            AdvanceUntilPhaseLeaves(m, MatchPhase.Planning);
            AdvanceUntilPhaseLeaves(m, MatchPhase.Action);
            if (m.State.Phase != MatchPhase.Planning) Assert.Ignore("the clash ended in that exchange");
            m.Tick(Dt);
            Assert.AreEqual(ActionType.None, m.State.P1.Active.PlannedAction,
                "off again, the next round is the player's to plan");
        }

        [Test]
        public void ACollisionAgainstTheWallDoesNotThrowAnyoneThroughIt()
        {
            // The knockback is applied straight to both positions, and a collision at the wall
            // pushes one of them outward. It also ends the action phase, so nothing would step him
            // again until the next one - he would stand outside the arena through all of planning.
            var m = StartedClash();
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
            // Mongoose lets Hilius swing twice in one round, so an unarmed, undefended exchange
            // lands twice his base damage. Expressed against the stat rather than as a number, so a
            // balance pass on the damage table does not break a test about the ability.
            var attacker = new GladiatorInstance(GladiatorDef.Hilius) { Ability = AbilityKey.Mongoose };
            attacker.BeginRound();
            attacker.Rage = 1f;
            attacker.ActivateAbility();

            var victim = new GladiatorInstance(GladiatorDef.Brutius);
            victim.BeginRound();

            while (attacker.AttacksRemainingThisRound > 0)
            {
                attacker.AttacksRemainingThisRound--;
                CombatResolver.DealDamage(attacker, victim);
            }

            // Hilius fights with sword and shield, so his own blow is his flat stat - and Mongoose
            // doubles the one attack his weapon swings. Against a defender who is also behind a
            // shield, each blow lands at half.
            float perHit = GladiatorDef.Hilius.Damage
                           * WeaponDef.SwordAndShield.DamageMultiplier
                           * WeaponDef.Get(GladiatorDef.Brutius.SkilledWith).IncomingDamageMultiplier;
            Assert.AreEqual(GladiatorDef.Brutius.MaxHp - perHit * 2f, victim.Hp, Tol,
                "two swings should land two full hits");
        }

        [Test]
        public void ABleedIsPaidAtTheTopOfTheRound_BeforeAnybodyMoves()
        {
            // Where it lands in the round is the whole shape of it. Settled at the end instead, a
            // fighter it was about to kill would get one more full round to fight in first - and
            // the player would watch him die from nothing after the exchange was over.
            var m = StartedClash();
            AdvanceUntilPhaseLeaves(m, MatchPhase.Reveal);

            var p1 = m.State.P1.Active;
            p1.ApplyBleed(40f);
            float before = p1.Hp;

            float bled = 0f;
            m.Bled += (side, amount) => { if (side == PlayerSide.P1) bled += amount; };

            m.SubmitPlanningAction(PlayerSide.P1, ActionType.Defend, Vector2.zero, 0f, false);
            m.SubmitPlanningAction(PlayerSide.Bot, ActionType.Defend, Vector2.zero, 0f, false);
            AdvanceUntilPhaseLeaves(m, MatchPhase.Planning);

            // One tick into the action phase - long before anything could have reached him.
            Assert.AreEqual(MatchPhase.Action, m.State.Phase);
            Assert.AreEqual(40f * GameConstants.BleedFraction, bled, Tol,
                "the wound should have been paid the moment the action phase opened");
            Assert.AreEqual(before - bled, p1.Hp, Tol);
            Assert.AreEqual(GameConstants.BleedRounds - 1, p1.BleedRoundsLeft);
        }

        [Test]
        public void ARoundWinner_StaysOnTheArenaWithTheHpTheyEndedOn()
        {
            var m = StartedClash();
            AdvanceUntilPhaseLeaves(m, MatchPhase.Reveal);

            var p1 = m.State.P1.Active;
            var bot = m.State.Bot.Active;
            p1.Hp = 120f;
            bot.Hp = 1f; // dies on the first exchange
            p1.Pos = new Vector2(-40f, 0f);
            bot.Pos = new Vector2(40f, 0f);

            // Pointed along the charge they are about to be given. A run leaves along the nose and
            // bends onto its target, so two men set down across an axis they did not spawn along
            // would each curve away rather than meet.

            p1.Facing = Vector2.right;
            bot.Facing = Vector2.left;

            m.SubmitPlanningAction(PlayerSide.P1, ActionType.Move, Vector2.right, 1f, false);
            m.SubmitPlanningAction(PlayerSide.Bot, ActionType.Move, Vector2.left, 1f, false);
            AdvanceUntilPhaseLeaves(m, MatchPhase.Planning);
            AdvanceUntilPhaseLeaves(m, MatchPhase.Action);

            Assert.AreEqual(MatchPhase.ClashEnd, m.State.Phase);
            Assert.IsFalse(bot.Alive);
            Assert.IsTrue(p1.Alive);
            float hpAtClashEnd = p1.Hp;
            Assert.Less(hpAtClashEnd, 120f, "the dying bot still landed its simultaneous return blow");

            // Note: the losing side is the only one that picks, so the Pick phase is entered and left
            // within the same frame here - waiting for Phase == Pick would hang. Wait for the next
            // clash's Reveal instead.
            RunUntil(m, s => s.Phase == MatchPhase.Reveal || s.Phase == MatchPhase.MatchEnd, 30f);
            Assert.AreEqual(MatchPhase.Reveal, m.State.Phase);

            Assert.AreSame(p1, m.State.P1.Active, "the winner stays on the arena");
            Assert.AreEqual(hpAtClashEnd, p1.Hp, Tol, "and is not healed for the new clash");
            Assert.AreEqual(2, m.State.Clash);
        }

        /// <summary>
        /// A clash winner keeps what is his - his health, his rage, and the rounds left on his
        /// blessing and his running ability - and loses what the other man did to him: the net, the
        /// stagger, the bleed. Nothing of the plan survives either.
        /// </summary>
        [Test]
        public void AClashWinnerKeepsHisHealthRageAndRunningCharges_AndLosesWhatWasDoneToHim()
        {
            var m = StartedClash();
            AdvanceUntilPhaseLeaves(m, MatchPhase.Reveal);

            var p1 = m.State.P1.Active;
            var bot = m.State.Bot.Active;
            p1.Hp = 120f;
            bot.Hp = 1f; // dies on the first exchange
            p1.Pos = new Vector2(-40f, 0f);
            bot.Pos = new Vector2(40f, 0f);
            p1.Facing = Vector2.right;
            bot.Facing = Vector2.left;

            m.SubmitPlanningAction(PlayerSide.P1, ActionType.Move, Vector2.right, 1f, false);
            m.SubmitPlanningAction(PlayerSide.Bot, ActionType.Move, Vector2.left, 1f, false);
            AdvanceUntilPhaseLeaves(m, MatchPhase.Planning);
            AdvanceUntilPhaseLeaves(m, MatchPhase.Action);
            Assert.AreEqual(MatchPhase.ClashEnd, m.State.Phase);

            // Everything that can be running on a man, running on the winner as the clash ends.
            p1.Rage = 0.6f;
            p1.Buff = new ActiveBuff { Key = AbilityKey.Earthshaker, RoundsLeft = 2 };
            p1.AbilityLockedRounds = 2;
            p1.AbilityArmed = true;
            p1.BlessWeapon();
            p1.BleedRoundsLeft = 2;
            p1.BleedPerRound = 3f;
            p1.Ensnare();
            p1.Stagger();
            float hpAtClashEnd = p1.Hp;

            RunUntil(m, s => s.Phase == MatchPhase.Reveal || s.Phase == MatchPhase.MatchEnd, 30f);
            Assert.AreEqual(MatchPhase.Reveal, m.State.Phase);
            Assert.AreSame(p1, m.State.P1.Active);

            Assert.AreEqual(hpAtClashEnd, p1.Hp, Tol, "his health goes with him");
            Assert.AreEqual(0.6f, p1.Rage, Tol, "and so does his rage");
            Assert.IsTrue(p1.Buff.IsActive && p1.Buff.Key == AbilityKey.Earthshaker, "his running ability goes with him");
            Assert.AreEqual(2, p1.Buff.RoundsLeft, "with the rounds it had left");
            Assert.AreEqual(2, p1.AbilityLockedRounds, "and the lock that runs beside it");
            Assert.IsTrue(p1.WeaponBuffed, "the blessing goes with him");
            Assert.AreEqual(GameConstants.WeaponBuffRounds + 1, p1.WeaponBuffRoundsLeft, "with the rounds it had left");
            Assert.IsFalse(p1.AbilityArmed, "nothing of the plan survives");
            Assert.IsFalse(p1.IsBleeding, "his wounds closed");
            Assert.IsFalse(p1.IsEnsnared, "the net is off him");
            Assert.IsFalse(p1.IsStaggered);
        }

        /// <summary>
        /// A mace throws the man it hits - unless his guard was set against it from the front, in
        /// which case he stands where he is. Both men are Brutius with a mace here.
        /// </summary>
        [TestCase(true)]
        [TestCase(false)]
        public void AGuardSetAgainstAMaceFromTheFront_IsNotThrown(bool facingTheBlow)
        {
            var m = StartedClash();
            AdvanceUntilPhaseLeaves(m, MatchPhase.Reveal);

            var p1 = m.State.P1.Active;
            var bot = m.State.Bot.Active;
            Assert.AreEqual(WeaponKind.TwoHandedMace, bot.Weapon, "this test wants a mace on the bot");

            // Within the mace's reach, well clear of a collision, nobody running.
            p1.Pos = new Vector2(-50f, 0f);
            bot.Pos = new Vector2(50f, 0f);
            p1.Facing = facingTheBlow ? Vector2.right : Vector2.left;
            bot.Facing = Vector2.left;
            p1.Hp = 1000f;

            m.SubmitPlanningAction(PlayerSide.P1, ActionType.Defend, Vector2.zero, 0f, false);
            m.SubmitPlanningAction(PlayerSide.Bot, ActionType.Defend, Vector2.zero, 0f, false);
            AdvanceUntilPhaseLeaves(m, MatchPhase.Planning);
            // Read partway through the phase: the flags are the round's, and the next round clears them.
            for (int i = 0; i < 60 && m.State.Phase == MatchPhase.Action; i++) m.Tick(Dt);

            Assert.IsTrue(p1.TookDamageThisRound, "the mace should have landed");
            if (facingTheBlow)
            {
                Assert.AreEqual(-50f, p1.Pos.x, 0.5f, "his guard met the blow and held him where he stood");
                Assert.IsTrue(p1.BlockedFrontThisRound);
            }
            else
            {
                Assert.Less(p1.Pos.x, -50f - GameConstants.MaceKnockback * 0.9f, "struck in the back, he was thrown");
                Assert.IsFalse(p1.BlockedFrontThisRound);
            }
        }

        /// <summary>A lock the other man put on him - Shackles - is over with the clash; only a lock beside his own running ability stays.</summary>
        [Test]
        public void AShacklesLockOnAClashWinner_EndsWithTheClash()
        {
            var m = StartedClash();
            AdvanceUntilPhaseLeaves(m, MatchPhase.Reveal);

            var p1 = m.State.P1.Active;
            var bot = m.State.Bot.Active;
            bot.Hp = 1f;
            p1.Pos = new Vector2(-40f, 0f);
            bot.Pos = new Vector2(40f, 0f);
            p1.Facing = Vector2.right;
            bot.Facing = Vector2.left;
            m.SubmitPlanningAction(PlayerSide.P1, ActionType.Move, Vector2.right, 1f, false);
            m.SubmitPlanningAction(PlayerSide.Bot, ActionType.Move, Vector2.left, 1f, false);
            AdvanceUntilPhaseLeaves(m, MatchPhase.Planning);
            AdvanceUntilPhaseLeaves(m, MatchPhase.Action);
            Assert.AreEqual(MatchPhase.ClashEnd, m.State.Phase);

            p1.Buff = default;
            p1.Shackle();
            Assert.Greater(p1.AbilityLockedRounds, 0);

            RunUntil(m, s => s.Phase == MatchPhase.Reveal || s.Phase == MatchPhase.MatchEnd, 30f);
            Assert.AreEqual(MatchPhase.Reveal, m.State.Phase);
            Assert.AreEqual(0, p1.AbilityLockedRounds, "the shackles came off with the man who put them on");
        }

        [Test]
        public void OnlyTheLosingSidePicksAfterTheFirstClash()
        {
            var m = StartedClash();
            AdvanceUntilPhaseLeaves(m, MatchPhase.Reveal);

            var p1 = m.State.P1.Active;
            var bot = m.State.Bot.Active;
            var firstBotFighter = bot.Def.Id;
            bot.Hp = 1f;
            p1.Pos = new Vector2(-40f, 0f);
            bot.Pos = new Vector2(40f, 0f);

            // Pointed along the charge they are about to be given. A run leaves along the nose and
            // bends onto its target, so two men set down across an axis they did not spawn along
            // would each curve away rather than meet.

            p1.Facing = Vector2.right;
            bot.Facing = Vector2.left;

            m.SubmitPlanningAction(PlayerSide.P1, ActionType.Move, Vector2.right, 1f, false);
            m.SubmitPlanningAction(PlayerSide.Bot, ActionType.Move, Vector2.left, 1f, false);
            AdvanceUntilPhaseLeaves(m, MatchPhase.Planning);
            AdvanceUntilPhaseLeaves(m, MatchPhase.Action);
            RunUntil(m, s => s.Phase == MatchPhase.Reveal, 30f);

            Assert.AreSame(p1, m.State.P1.Active, "the player never had to pick again");
            Assert.AreNotEqual(firstBotFighter, m.State.Bot.Active.Def.Id,
                "the bot sent in a different gladiator after losing the clash");
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

                // A draw is a real ending now - the last man on both sides falling in the one exchange -
                // and only that: with nobody named the winner, nobody may be left standing anywhere.
                if (!m.State.WinnerSide.HasValue)
                {
                    Assert.IsFalse(m.State.P1.HasAnyAlive || m.State.Bot.HasAnyAlive,
                        $"seed {seed} finished without a winner while somebody was still standing");
                    continue;
                }

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
