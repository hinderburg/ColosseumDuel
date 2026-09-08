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

        [UnityTest]
        public IEnumerator HazardRingsStayHiddenWhileTheArenaIsSafe()
        {
            _controller.SubmitPlayerPick(GladiatorId.Brutius);
            yield return RunSeconds(GameConstants.RevealTime + 0.2f);

            Assert.AreEqual(1, _controller.Manager.State.Cycle, "should still be on the first cycle");

            var rings = RingRenderers();
            Assert.AreEqual(HazardSystem.Schedule.Count, rings.Length, "one ring per hazard stage");
            Assert.IsTrue(rings.All(r => !r.enabled),
                "no danger ring should be drawn during the arena's safe cycles");
        }

        [UnityTest]
        public IEnumerator HazardRingsLightUpOnceTheArenaStartsClosingIn()
        {
            _controller.SubmitPlayerPick(GladiatorId.Brutius);
            yield return RunSeconds(GameConstants.RevealTime + 0.1f);

            // Fast-forward the cycle counter instead of playing out seven real cycles.
            _controller.Manager.State.Cycle = 9;
            yield return null;

            var rings = RingRenderers();
            int expected = HazardSystem.ActiveStagesAt(9).Count;
            Assert.AreEqual(expected, rings.Count(r => r.enabled),
                "every stage active on cycle 9 should be drawn");
            Assert.Greater(expected, 0, "cycle 9 must have active hazard stages for this test to mean anything");

            foreach (var ring in rings.Where(r => r.enabled))
            {
                Assert.AreSame(_controller.Arena.Palette.HazardActive, ring.sharedMaterial);

                // A ring wound the wrong way round is invisible from the top-down camera while
                // still reporting enabled == true, so check the geometry actually faces upwards.
                var mesh = ring.GetComponent<MeshFilter>().sharedMesh;
                Assert.Greater(FrontFaceNormal(mesh).y, 0.5f,
                    $"{ring.name} is wound face-down and would be culled away");
            }
        }

        [UnityTest]
        public IEnumerator SpikesRiseOutOfTheGroundWhereItTurnsDangerous()
        {
            var spikeRoot = _controller.Arena.GetComponentsInChildren<Transform>(true)
                .FirstOrDefault(t => t.name == "Spikes");
            Assert.IsNotNull(spikeRoot, "the arena has no spikes");

            var spikes = spikeRoot.Cast<Transform>().ToList();
            Assert.AreEqual(_controller.Arena.SpikeCount, spikes.Count);

            _controller.SubmitPlayerPick(GladiatorId.Brutius);
            yield return RunSeconds(GameConstants.RevealTime + 0.2f);

            Assert.IsTrue(spikes.All(s => s.localPosition.y < 0f),
                "nothing should be up while the whole arena is still safe");

            // Cycle 8: the stages at 0.75-1.00 and 0.50-0.75 are live, so everything past half way
            // out is dangerous and everything inside it is not.
            _controller.Manager.State.Cycle = 8;
            yield return RunSeconds(1.5f); // they rise rather than snap up

            foreach (var spike in spikes)
            {
                float d = NormalisedRadius(spike.localPosition);
                bool shouldBeUp = d > 0.5f;

                // Skip the ones sitting right on the boundary: which side of it they land on is a
                // matter of floating-point luck, and it is not what this test is about.
                if (Mathf.Abs(d - 0.5f) < 0.03f) continue;

                Assert.AreEqual(shouldBeUp, spike.localPosition.y > -0.01f,
                    $"a spike at {d:0.00} of the way out is {(shouldBeUp ? "down" : "up")} " +
                    "when the danger boundary is at 0.50");
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
        public IEnumerator TheSceneShipsWithSwipingAsTheControl()
        {
            var input = Object.FindFirstObjectByType<PlayerInputController>();
            Assert.IsNotNull(input, "the Arena scene must contain a PlayerInputController");
            Assert.AreEqual(ControlScheme.Swipe, input.Scheme,
                "the scene did not ship on the control the menu opens on");

            // A tap has to survive the trip through the scene's own camera and arena scaling, not
            // just through a test rig: aim at where the simulation says the gladiator's own dash
            // ends and check the plan comes back pointing that way.
            _controller.SubmitPlayerPick(GladiatorId.Brutius);
            yield return RunSeconds(GameConstants.RevealTime + 0.05f);
            Assert.AreEqual(MatchPhase.Planning, _controller.Manager.State.Phase);

            var g = _controller.Manager.State.P1.Active;
            var target = g.Pos + new Vector2(0f, g.DashReach() * 0.5f);
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


        /// <summary>Distance from the centre in wall units, measured on the arena's ellipse.</summary>
        private float NormalisedRadius(Vector3 localPosition)
            => new Vector2(localPosition.x / _controller.Arena.WorldRadiusX,
                           localPosition.z / _controller.Arena.WorldRadiusZ).magnitude;

        /// <summary>Unity's front-face normal for a triangle (v0, v1, v2) is cross(v1-v0, v2-v0).</summary>
        private static Vector3 FrontFaceNormal(Mesh mesh)
        {
            var v = mesh.vertices;
            var t = mesh.triangles;
            return Vector3.Cross(v[t[1]] - v[t[0]], v[t[2]] - v[t[0]]).normalized;
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

        private Renderer[] RingRenderers()
        {
            var root = _controller.Arena.GetComponentsInChildren<Transform>(true)
                .FirstOrDefault(t => t.name == "HazardRings");
            Assert.IsNotNull(root, "ArenaView should have built a HazardRings root");
            return root.GetComponentsInChildren<Renderer>(true);
        }

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
