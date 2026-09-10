using ColosseumDuel.Core;
using NUnit.Framework;
using UnityEngine;

namespace ColosseumDuel.Tests
{
    /// <summary>
    /// The phase knows when its blows land before anybody has moved.
    ///
    /// It can, because the phase is deterministic: both plans are in before it starts and nothing
    /// inside it depends on anything outside it. That is what lets a swing begin early enough for
    /// the weapon to arrive on the frame the blow does, rather than the blow landing and the swing
    /// starting afterwards.
    ///
    /// Every test here checks the prediction against what the phase then actually does, rather than
    /// against a number written down beside it - a prediction agreeing with its own arithmetic is
    /// not a prediction.
    /// </summary>
    public class StrikePredictionTests
    {
        private const float Dt = 1f / 60f;

        private static GameManager StartedRound()
        {
            var m = new GameManager(new System.Random(20260909));
            m.StartMatch(
                new[] { GladiatorDef.Brutius, GladiatorDef.Barbarius, GladiatorDef.Hilius },
                new[] { GladiatorDef.Brutius, GladiatorDef.Barbarius, GladiatorDef.Hilius },
                tutorial: false);
            m.SubmitPick(PlayerSide.P1, 0);
            m.SubmitPick(PlayerSide.Bot, 0);
            return m;
        }

        private static void AdvanceUntilPhaseLeaves(GameManager m, MatchPhase phase, float maxSeconds = 30f)
        {
            float t = 0f;
            while (m.State.Phase == phase && t < maxSeconds)
            {
                m.Tick(Dt);
                t += Dt;
            }
        }

        /// <summary>
        /// Two fighters charging each other are told when they will meet, and they meet then.
        ///
        /// The tolerance is a couple of frames: the prediction walks the phase at its own step and
        /// the phase walks it at another, so agreeing to the frame would be agreeing by luck.
        /// </summary>
        [Test]
        public void ACharge_IsPredictedWithinAFrameOrTwoOfWhenItLands()
        {
            var m = StartedRound();
            AdvanceUntilPhaseLeaves(m, MatchPhase.Reveal);

            var p1 = m.State.P1.Active;
            var bot = m.State.Bot.Active;
            p1.Pos = new Vector2(-120f, 0f);
            bot.Pos = new Vector2(120f, 0f);

            // Pointed along the charge they are about to be given. A run leaves along the nose and
            // bends onto its target, so two men set down across an axis they did not spawn along
            // would each curve away rather than meet.

            p1.Facing = Vector2.right;
            bot.Facing = Vector2.left;

            m.SubmitPlanningAction(PlayerSide.P1, ActionType.Move, Vector2.right, 1f, false);
            m.SubmitPlanningAction(PlayerSide.Bot, ActionType.Move, Vector2.left, 1f, false);
            AdvanceUntilPhaseLeaves(m, MatchPhase.Planning);

            float predicted = m.State.P1.StrikeEta;
            Assert.Greater(predicted, 0f, "a head-on charge should be predicted to land");

            // When it actually lands, timed off the health that goes with it.
            float startHp = bot.Hp;
            float actual = -1f;
            float t = 0f;
            while (m.State.Phase == MatchPhase.Action && t < 5f)
            {
                m.Tick(Dt);
                t += Dt;
                if (actual < 0f && bot.Hp < startHp) actual = t;
            }

            Assert.Greater(actual, 0f, "the blow the prediction promised never landed");
            Assert.AreEqual(actual, predicted, Dt * 3f,
                $"predicted {predicted:0.000}s, landed {actual:0.000}s");
        }

        /// <summary>
        /// Standing still out of reach of each other is predicted as no blow at all.
        ///
        /// The negative case matters as much as the positive one: a prediction that fires whatever
        /// happens would have every gladiator swinging at open air every cycle.
        /// </summary>
        [Test]
        public void TwoFightersWhoNeverMeet_ArePredictedNoBlow()
        {
            var m = StartedRound();
            AdvanceUntilPhaseLeaves(m, MatchPhase.Reveal);

            m.State.P1.Active.Pos = new Vector2(-250f, 0f);
            m.State.Bot.Active.Pos = new Vector2(250f, 0f);

            m.SubmitPlanningAction(PlayerSide.P1, ActionType.Defend, Vector2.zero, 0f, false);
            m.SubmitPlanningAction(PlayerSide.Bot, ActionType.Defend, Vector2.zero, 0f, false);
            AdvanceUntilPhaseLeaves(m, MatchPhase.Planning);

            Assert.Less(m.State.P1.StrikeEta, 0f, "nobody moved, so nobody can be about to strike");
            Assert.Less(m.State.Bot.StrikeEta, 0f);
        }

        /// <summary>
        /// The longer weapon is predicted to land first, from the same starting positions.
        ///
        /// Which is the whole point of reach, and the reason the prediction has to be per side: the
        /// two fighters in one exchange do not strike at the same moment unless their weapons agree
        /// about how far they reach.
        /// </summary>
        [Test]
        public void TheLongerWeaponIsPredictedToLandFirst()
        {
            var m = StartedRound();
            AdvanceUntilPhaseLeaves(m, MatchPhase.Reveal);

            var p1 = m.State.P1.Active;
            var bot = m.State.Bot.Active;
            p1.Weapon = WeaponKind.TwoHandedMace;
            bot.Weapon = WeaponKind.DualSwords;
            p1.Pos = new Vector2(-120f, 0f);
            bot.Pos = new Vector2(120f, 0f);

            // Pointed along the charge they are about to be given. A run leaves along the nose and
            // bends onto its target, so two men set down across an axis they did not spawn along
            // would each curve away rather than meet.

            p1.Facing = Vector2.right;
            bot.Facing = Vector2.left;

            m.SubmitPlanningAction(PlayerSide.P1, ActionType.Move, Vector2.right, 1f, false);
            m.SubmitPlanningAction(PlayerSide.Bot, ActionType.Move, Vector2.left, 1f, false);
            AdvanceUntilPhaseLeaves(m, MatchPhase.Planning);

            Assert.Greater(m.State.P1.StrikeEta, 0f);
            Assert.Greater(m.State.Bot.StrikeEta, 0f);
            Assert.Less(m.State.P1.StrikeEta, m.State.Bot.StrikeEta,
                "the mace reaches twice as far as the twin swords and should get there first");
        }

        /// <summary>
        /// A weapon picked up on the way counts from where it is picked up.
        ///
        /// Running at a longer weapon and then at the opponent is a real plan, and a prediction that
        /// used only what he set off with would start his swing for the wrong reach - late by the
        /// difference between the two, which for swords against a mace is most of a body length.
        /// </summary>
        [Test]
        public void AWeaponSweptUpOnTheWayIsPredictedWith()
        {
            var m = StartedRound();
            AdvanceUntilPhaseLeaves(m, MatchPhase.Reveal);

            var p1 = m.State.P1.Active;
            var bot = m.State.Bot.Active;
            p1.Weapon = WeaponKind.DualSwords;
            bot.Weapon = WeaponKind.DualSwords;

            p1.Pos = new Vector2(-120f, 0f);
            bot.Pos = new Vector2(120f, 0f);

            // Pointed along the charge they are about to be given. A run leaves along the nose and
            // bends onto its target, so two men set down across an axis they did not spawn along
            // would each curve away rather than meet.

            p1.Facing = Vector2.right;
            bot.Facing = Vector2.left;

            // Everything off the sand except a mace, laid squarely on his path.
            m.State.Items.Items.Clear();
            m.State.Items.Items.Add(new ArenaItem
            {
                Kind = WeaponKind.TwoHandedMace,
                Pos = new Vector2(-40f, 0f),
            });

            m.SubmitPlanningAction(PlayerSide.P1, ActionType.Move, Vector2.right, 1f, false);
            m.SubmitPlanningAction(PlayerSide.Bot, ActionType.Move, Vector2.left, 1f, false);
            AdvanceUntilPhaseLeaves(m, MatchPhase.Planning);

            // Both start with the same weapon and run at each other, so without the mace on the
            // ground between them the two predictions would be the same moment.
            Assert.Greater(m.State.P1.StrikeEta, 0f);
            Assert.Greater(m.State.Bot.StrikeEta, 0f);
            Assert.Less(m.State.P1.StrikeEta, m.State.Bot.StrikeEta,
                "he was predicted with the swords he set off with rather than the mace he picked up");
        }
    }
}
