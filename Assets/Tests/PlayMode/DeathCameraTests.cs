using System.Collections;
using System.Linq;
using ColosseumDuel.Core;
using ColosseumDuel.Gameplay;
using ColosseumDuel.Gameplay.View;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace ColosseumDuel.Tests
{
    /// <summary>
    /// The camera never moves except for a knockout. That exception is worth a test in both
    /// directions: one that only checks it comes in would pass just as well if it never went back,
    /// and a camera stuck in close is a broken game rather than a missing effect.
    /// </summary>
    public class DeathCameraTests
    {
        private const string ScenePath = "Assets/Scenes/Arena.unity";

        private GameController _controller;
        private DeathCameraView _deathCamera;
        private Transform _camera;
        private Vector3 _home;

        [UnitySetUp]
        public IEnumerator LoadArena()
        {
            yield return SceneManager.LoadSceneAsync(ScenePath, LoadSceneMode.Single);
            yield return null;

            _controller = Object.FindFirstObjectByType<GameController>();
            _deathCamera = Object.FindFirstObjectByType<DeathCameraView>();
            Assert.IsNotNull(_deathCamera, "the Arena scene must carry a DeathCameraView");

            _camera = _deathCamera.transform;
            _home = _camera.position;
        }

        [UnityTest]
        public IEnumerator TheCameraHoldsStillThroughAnOrdinaryCycle()
        {
            // The fixed frame is most of what makes this arena readable - the player learns one
            // view and every distance in it. Anything that moved it during a fight would be a bug.
            _controller.SubmitPlayerPick(GladiatorId.Brutius);
            yield return RunSeconds(GameConstants.RevealTime + GameConstants.PlanningTime + 0.5f);

            Assert.AreEqual(0f, Vector3.Distance(_home, _camera.position), 0.001f,
                "the camera moved during ordinary play");
        }

        [UnityTest]
        public IEnumerator ALandedBlowKnocksTheCameraAndItSettlesBack()
        {
            // A hit should be felt as well as seen. Both halves are asserted: a shake that never
            // stopped would leave the arena permanently off its centre, and the fixed frame is what
            // every distance in this game is read against.
            _controller.SubmitPlayerPick(GladiatorId.Brutius);
            yield return RunSeconds(GameConstants.RevealTime + 0.2f);

            var state = _controller.Manager.State;
            float gap = Mathf.Min(state.P1.Active.WeaponDef.Reach, state.Bot.Active.WeaponDef.Reach) * 0.7f;
            state.P1.Active.Pos = new Vector2(-gap * 0.5f, 0f);
            state.Bot.Active.Pos = new Vector2(gap * 0.5f, 0f);


            // Squared up on each other. A blow only lands inside the swinger's own arc now, and a

            // gladiator faces the way he last ran - so set down across the short axis of an arena they

            // spawned along, these two would be looking past one another with nobody in reach.

            state.P1.Active.Facing = Vector2.right;
            state.Bot.Active.Facing = Vector2.left;

            _controller.Manager.SubmitPlanningAction(PlayerSide.P1, ActionType.Defend, Vector2.zero, 0f, false);
            _controller.Manager.SubmitPlanningAction(PlayerSide.Bot, ActionType.Defend, Vector2.zero, 0f, false);
            yield return RunUntil(() => _controller.Manager.State.Phase == MatchPhase.Action, 6f);

            // Long enough to cover the wait the effects now sit behind. Two fighters already inside
            // each other's reach exchange on the first substep, and there is no room in front of
            // that to wind a swing up - so the shake is held back to meet the weapon instead of
            // going off while it is still on its way.
            float furthest = 0f;
            for (float t = 0f; t < 1f; t += Time.unscaledDeltaTime)
            {
                furthest = Mathf.Max(furthest, Vector3.Distance(_home, _camera.position));
                yield return null;
            }

            Assert.Greater(furthest, 0.02f, "the camera never moved when the blows landed");
            Assert.Less(furthest, 1f, "a shake this big would cost the player the fight they are watching");

            yield return RunSeconds(0.6f);
            Assert.AreEqual(0f, Vector3.Distance(_home, _camera.position), 0.005f,
                "the shake left the camera off centre");
        }

        [UnityTest]
        public IEnumerator ItClosesInOnAKnockoutAndComesBackForTheNextRound()
        {
            _controller.SubmitPlayerPick(GladiatorId.Brutius);
            yield return RunSeconds(GameConstants.RevealTime + 0.2f);

            var loser = KillNextCycle();
            yield return RunUntil(() => _controller.Manager.State.Phase == MatchPhase.RoundEnd, 8f);
            Assert.IsFalse(loser.Alive, "this test needs the round to have ended in a death");

            // Given the length of the round-end pause, not a fixed number of frames: the camera
            // moves on unscaled time precisely so that it arrives inside it.
            yield return RunSeconds(GameConstants.RoundEndTime * 0.6f);

            float closed = Vector3.Distance(_home, _camera.position);
            Assert.Greater(closed, 0.5f, "the camera should have come in on the body");

            var focus = _controller.Arena.ToWorld(loser.Pos);
            Assert.Less(Vector3.Distance(_camera.position, focus),
                Vector3.Distance(_home, focus),
                "and it should have come in towards the fallen gladiator, not away from him");

            // And home again by the time anyone is fighting in it. Snapped rather than drifted, so
            // a round never opens on a frame that is still moving under the player.
            yield return RunUntil(() => _controller.Manager.State.Phase == MatchPhase.Pick
                                        || _controller.Manager.State.Phase == MatchPhase.Reveal
                                        || _controller.Manager.State.Phase == MatchPhase.MatchEnd, 8f);
            _controller.SubmitPlayerPick(GladiatorId.Brutius);
            yield return null;
            yield return null;

            Assert.AreEqual(0f, Vector3.Distance(_home, _camera.position), 0.001f,
                "the camera has to be exactly home when a round starts");
        }

        [UnityTest]
        public IEnumerator AKnockoutSlowsTheWorldDownAndLetsItGoAgain()
        {
            _controller.SubmitPlayerPick(GladiatorId.Brutius);
            yield return RunSeconds(GameConstants.RevealTime + 0.2f);

            KillNextCycle();
            yield return RunUntil(() => _controller.Manager.State.Phase == MatchPhase.RoundEnd, 8f);
            yield return null;

            Assert.Less(Time.timeScale, 1f, "a knockout should slow the world down");
            Assert.AreEqual(_controller.DeathTimeScale, Time.timeScale, 0.001f);

            yield return RunUntil(() => _controller.Manager.State.Phase != MatchPhase.RoundEnd, 8f);
            yield return null;

            Assert.AreNotEqual(_controller.DeathTimeScale, Time.timeScale,
                "and let it go again once the round is over");
        }

        /// <summary>
        /// Sets the round up to end in the very next exchange, and returns whoever is about to fall.
        ///
        /// Placed within reach by hand rather than run at each other across the arena: at the
        /// spawn distance a charge takes three or four cycles, which is most of a minute of test
        /// time for a fact about the camera.
        /// </summary>
        private GladiatorInstance KillNextCycle()
        {
            var state = _controller.Manager.State;
            var winner = state.P1.Active;
            var loser = state.Bot.Active;

            float gap = Mathf.Min(winner.WeaponDef.Reach, loser.WeaponDef.Reach) * 0.7f;
            Assert.Greater(gap, GameConstants.CollideDistance, "they must not be close enough to crash");

            winner.Pos = new Vector2(-gap * 0.5f, 0f);
            loser.Pos = new Vector2(gap * 0.5f, 0f);



            // Squared up on each other. A blow only lands inside the swinger's own arc now, and a


            // gladiator faces the way he last ran - so set down across the short axis of an arena they


            // spawned along, these two would be looking past one another with nobody in reach.


            winner.Facing = Vector2.right;
            loser.Facing = Vector2.left;
            loser.Hp = 0.1f;

            _controller.Manager.SubmitPlanningAction(PlayerSide.P1, ActionType.Defend, Vector2.zero, 0f, false);
            _controller.Manager.SubmitPlanningAction(PlayerSide.Bot, ActionType.Defend, Vector2.zero, 0f, false);
            return loser;
        }

        private static IEnumerator RunSeconds(float seconds)
        {
            float t = 0f;
            while (t < seconds) { yield return null; t += Time.unscaledDeltaTime; }
        }

        private static IEnumerator RunUntil(System.Func<bool> done, float maxSeconds)
        {
            float t = 0f;
            while (!done() && t < maxSeconds) { yield return null; t += Time.unscaledDeltaTime; }
            Assert.IsTrue(done(), $"condition not reached within {maxSeconds}s");
        }
    }
}
