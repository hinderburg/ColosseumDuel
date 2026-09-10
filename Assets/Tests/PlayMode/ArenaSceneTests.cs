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
    /// Runs the real Arena scene for a few seconds of game time and checks that the presentation
    /// layer actually reflects the simulation. These pass headlessly (-batchmode -nographics), so
    /// "the scene still works" is something CI can answer, not something you have to eyeball.
    ///
    /// Any Debug.LogError or unhandled exception during a frame fails the test by default, which is
    /// most of the value here: it catches null wiring after a scene or bootstrap change.
    /// </summary>
    public class ArenaSceneTests
    {
        private const string ScenePath = "Assets/Scenes/Arena.unity";

        private GameController _controller;

        [UnitySetUp]
        public IEnumerator LoadArena()
        {
            yield return SceneManager.LoadSceneAsync(ScenePath, LoadSceneMode.Single);
            yield return null; // let Start() run

            _controller = Object.FindFirstObjectByType<GameController>();
            Assert.IsNotNull(_controller, "the Arena scene must contain a GameController");
        }

        [UnityTest]
        public IEnumerator SceneStartsAMatchAndTheViewsFollowTheSimulation()
        {
            Assert.IsNotNull(_controller.Manager, "the controller should have started a match in Start()");
            Assert.IsNotNull(_controller.Arena, "GameController.Arena must be wired by the bootstrap");
            Assert.IsNotNull(_controller.Arena.Palette, "ArenaView.Palette must be wired by the bootstrap");

            // The player has to pick before anything moves.
            _controller.SubmitPlayerPick(GladiatorId.Brutius);
            yield return null;

            var state = _controller.Manager.State;
            Assert.IsNotNull(state.P1.Active);
            Assert.IsNotNull(state.Bot.Active);

            var playerView = FindView("Player");
            var botView = FindView("Bot");
            Assert.IsNotNull(playerView, "a view should exist for the player's gladiator");
            Assert.IsNotNull(botView, "a view should exist for the bot's gladiator");

            // Give the match long enough to leave Reveal, plan, and run a couple of action phases.
            yield return RunSeconds(GameConstants.RevealTime + (GameConstants.PlanningTime + GameConstants.ActionTime) * 2f);

            Assert.IsTrue(playerView.gameObject.activeInHierarchy, "the player's gladiator should be on screen");
            Assert.IsTrue(botView.gameObject.activeInHierarchy, "the bot's gladiator should be on screen");

            AssertViewMatchesSimulation(playerView, state.P1.Active);
            AssertViewMatchesSimulation(botView, state.Bot.Active);
        }

        [UnityTest]
        public IEnumerator ItemViewsTrackTheThreePickupsOnTheFloor()
        {
            _controller.SubmitPlayerPick(GladiatorId.Brutius);
            yield return null;

            var items = _controller.Manager.State.Items.Items;
            Assert.AreEqual(GameConstants.ItemCountOnArena, items.Count);

            for (int i = 0; i < items.Count; i++)
            {
                var view = FindView($"Item_{i}");
                Assert.IsNotNull(view, $"Item_{i} view is missing");
                Assert.IsTrue(view.gameObject.activeInHierarchy, $"Item_{i} should be visible");

                var expected = _controller.Arena.ToWorld(items[i].Pos);
                Assert.Less(Vector3.Distance(view.position, expected), 0.001f,
                    $"Item_{i} is not drawn where the simulation says it is");
            }
        }

        /// <summary>
        /// The scene has to ship on the tap control, not merely default to it in code.
        ///
        /// This is the bug it exists to catch, and it shipped: the field initialiser was changed to
        /// Tap, but PlayerInputController was already serialised into Arena.unity, and a serialised
        /// component keeps its stored number no matter what the initialiser later says. The built
        /// game stayed on Drag and tapping the sand did nothing at all. Every existing tap test set
        /// the scheme itself before driving it, so all of them passed while the game was unplayable.
        /// Asserted against the scene, because the scene is what the player gets.
        /// </summary>
        [UnityTest]
        public IEnumerator TheSceneShipsWithTappingAsTheControl()
        {
            var input = Object.FindFirstObjectByType<PlayerInputController>();
            Assert.IsNotNull(input, "the Arena scene must contain a PlayerInputController");
            Assert.AreEqual(ControlScheme.Tap, input.Scheme,
                "the scene did not ship on the control the game is meant to open on");

            // A tap has to survive the trip through the scene's own camera and arena scaling, not
            // just through a test rig: aim at where the simulation says the gladiator's own dash
            // ends and check the plan comes back pointing that way.
            _controller.SubmitPlayerPick(GladiatorId.Brutius);
            yield return RunSeconds(GameConstants.RevealTime + 0.05f);
            Assert.AreEqual(MatchPhase.Planning, _controller.Manager.State.Phase);

            var g = _controller.Manager.State.P1.Active;
            var target = g.Pos + new Vector2(0f, GameConstants.AimedRunLength * 0.5f);
            var screen = input.ArenaCamera.WorldToScreenPoint(_controller.Arena.ToWorld(target));

            Assert.IsTrue(input.TapTo(screen), "a tap on open sand should file a move");

            Assert.AreEqual(ActionType.Move, g.PlannedAction);
            Assert.Greater(Vector2.Dot(g.PlannedAimDirection, Vector2.up), 0.95f,
                "the ordered run should point at the tap");
        }

        [UnityTest]
        public IEnumerator ATrapStopsTheGladiatorAndBites()
        {
            _controller.SubmitPlayerPick(GladiatorId.Brutius);
            yield return RunSeconds(GameConstants.RevealTime + 0.2f);

            var traps = _controller.Manager.State.Traps;
            Assert.IsNotNull(traps);

            // Not the full count: the scene opens on a tutorial match, which sweeps traps off the
            // path it points the player down. What matters here is that the ones left still bite.
            Assert.Greater(traps.Traps.Count, 0);
            Assert.LessOrEqual(traps.Traps.Count, GameConstants.TrapCount);

            var g = _controller.Manager.State.P1.Active;
            var trap = traps.Traps[0];
            g.Pos = trap.Pos;
            g.Vel = new Vector2(0f, 200f);
            float hp = g.Hp;

            Assert.AreSame(trap, traps.TryTrigger(g));

            // Stopping him is the point; the bite is the smallest attack in the game on purpose, so
            // the scenery never out-hits the fighters it is there to inconvenience.
            Assert.AreEqual(Vector2.zero, g.Vel, "a sprung trap has to stop the charge");
            Assert.AreEqual(hp - TrapSystem.Damage, g.Hp, 0.001f);
            Assert.AreEqual(GladiatorDef.All.Min(d => d.Damage), TrapSystem.Damage, 0.001f);

            Assert.IsFalse(trap.Armed);
            Assert.IsNull(traps.TryTrigger(g), "a sprung trap must not bite twice");
            yield return null;
        }

        [UnityTest]
        public IEnumerator AGladiatorLaunchedByTheSlingshotActuallyMovesOnScreen()
        {
            _controller.SubmitPlayerPick(GladiatorId.Brutius);
            yield return RunSeconds(GameConstants.RevealTime + 0.1f);
            Assert.AreEqual(MatchPhase.Planning, _controller.Manager.State.Phase);

            var playerView = FindView("Player");
            Vector3 before = playerView.position;

            _controller.SubmitPlayerMove(Vector2.up, 1f);
            yield return RunSeconds(GameConstants.PlanningTime + GameConstants.ActionTime * 0.5f);

            Assert.Greater(Vector3.Distance(playerView.position, before), 0.1f,
                "a full-power move should visibly displace the gladiator");
        }

        // ------------------------------------------------------------------

        private void AssertViewMatchesSimulation(Transform view, GladiatorInstance g)
        {
            var expected = _controller.Arena.ToWorld(g.Pos);
            Assert.Less(Vector3.Distance(view.position, expected), 0.001f,
                $"{view.name} is drawn at {view.position} but the simulation says {expected}");
        }

        private Transform FindView(string name)
            => _controller.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == name);

        private static IEnumerator RunSeconds(float seconds)
        {
            float t = 0f;
            while (t < seconds)
            {
                yield return null;
                t += Time.unscaledDeltaTime;
            }
        }
    }
}
