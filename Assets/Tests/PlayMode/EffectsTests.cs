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
    /// The polish effects, checked by what they actually do to the scene rather than by whether the
    /// code ran - the recurring lesson on this project is that a visual can report itself working
    /// while drawing nothing.
    /// </summary>
    public class EffectsTests
    {
        private const string ScenePath = "Assets/Scenes/Arena.unity";

        private GameController _controller;

        [UnitySetUp]
        public IEnumerator LoadArenaAndReachPlanning()
        {
            yield return SceneManager.LoadSceneAsync(ScenePath, LoadSceneMode.Single);
            yield return null;

            _controller = Object.FindFirstObjectByType<GameController>();
            Assert.IsNotNull(_controller);

            _controller.SubmitPlayerPick(GladiatorId.Brutius);
            yield return RunSeconds(GameConstants.RevealTime + 0.1f);
            Assert.AreEqual(MatchPhase.Planning, _controller.Manager.State.Phase);
        }

        private MatchState State => _controller.Manager.State;

        private Transform Find(string name)
            => _controller.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == name);

        private IEnumerator ForceCollision()
        {
            State.P1.Active.Pos = new Vector2(-40f, 0f);
            State.Bot.Active.Pos = new Vector2(40f, 0f);
            _controller.Manager.SubmitPlanningAction(PlayerSide.P1, ActionType.Move, Vector2.right, 1f, false);
            _controller.Manager.SubmitPlanningAction(PlayerSide.Bot, ActionType.Move, Vector2.left, 1f, false);
            yield return RunSeconds(GameConstants.PlanningTime + 0.1f);
        }

        // ------------------------------------------------------------------

        [Test]
        public void TheCameraLooksDownAtTheArenaInPerspective()
        {
            var camera = Camera.main;
            Assert.IsFalse(camera.orthographic, "the arena is presented in 3D, not flattened");
            Assert.Greater(camera.transform.position.y, 0f, "the camera sits above the floor");
            Assert.Less(camera.transform.position.z, 0f, "and in front of it, looking back at the arena");

            float pitch = camera.transform.eulerAngles.x;
            Assert.Greater(pitch, 20f, "a shallower angle than this would not read as looking down");
            Assert.Less(pitch, 80f, "and a steeper one collapses back into a top-down view");
        }

        [UnityTest]
        public IEnumerator TheCameraNeverMoves()
        {
            // The design calls for a fixed camera: the arena always occupies the same place on
            // screen, so aiming can be muscle memory. This is the regression guard for that - the
            // planning zoom and the impact shake were both removed to honour it.
            var camera = Camera.main;
            Vector3 position = camera.transform.position;
            Quaternion rotation = camera.transform.rotation;
            float fov = camera.fieldOfView;

            yield return ForceCollision();
            yield return RunSeconds(GameConstants.ActionTime + 0.5f);

            Assert.AreEqual(position, camera.transform.position, "the camera must not move");
            Assert.AreEqual(rotation, camera.transform.rotation, "nor turn");
            Assert.AreEqual(fov, camera.fieldOfView, 0.0001f, "nor zoom");
        }

        [UnityTest]
        public IEnumerator TakingAHitPlaysAVisibleReactionThatRecovers()
        {
            var model = Find("Player").Find("Model");
            Assert.AreEqual(Vector3.one, model.localScale, "no reaction before anything lands");

            State.P1.Active.Pos = new Vector2(-40f, 0f);
            State.Bot.Active.Pos = new Vector2(40f, 0f);
            _controller.Manager.SubmitPlanningAction(PlayerSide.P1, ActionType.Move, Vector2.right, 1f, false);
            _controller.Manager.SubmitPlanningAction(PlayerSide.Bot, ActionType.Move, Vector2.left, 1f, false);

            // Sample the peak across the whole approach and reaction rather than around a predicted
            // moment: the reaction is shorter than a slow frame, so a fixed window can land entirely
            // before or after it and see nothing.
            float maxScale = 0f;
            float elapsed = 0f;
            while (elapsed < GameConstants.PlanningTime + GameConstants.ActionTime)
            {
                yield return null;
                elapsed += Time.unscaledDeltaTime;
                maxScale = Mathf.Max(maxScale, model.localScale.x);
            }
            Assert.Greater(maxScale, 1.05f, "the hit reaction should be visible on the model");

            yield return RunSeconds(0.5f);
            Assert.AreEqual(1f, model.localScale.x, 0.001f, "and must settle back to normal");
        }

        [UnityTest]
        public IEnumerator ABlowLandingSpillsBloodWhereItLanded()
        {
            var pool = _controller.Arena.GetComponentsInChildren<Transform>(true)
                .FirstOrDefault(t => t.name == "BloodBursts");
            if (pool == null)
            {
                Assert.Ignore("No blood prefab - Epic Toon FX is not imported here.");
                yield break;
            }

            var bursts = pool.Cast<Transform>().ToList();
            Assert.AreEqual(_controller.Arena.BloodPoolSize, bursts.Count,
                "the bursts are pooled, not spawned per hit");
            Assert.IsTrue(bursts.All(b => !b.gameObject.activeSelf), "nothing has been hit yet");

            var victim = State.P1.Active;
            yield return ForceCollision();
            yield return RunUntil(() => bursts.Any(b => b.gameObject.activeSelf), 5f);

            var played = bursts.First(b => b.gameObject.activeSelf);
            var expected = _controller.Arena.ToWorld(victim.Pos);

            // Horizontal position only: the burst sits at chest height, not on the floor.
            Assert.Less(Vector2.Distance(
                    new Vector2(played.position.x, played.position.z),
                    new Vector2(expected.x, expected.z)),
                2f, "the blood should appear where the blow landed");

            // Enabled is not the same as playing: a pooled system reused at the end of its
            // lifetime would sit there emitting nothing.
            var particles = played.GetComponentInChildren<ParticleSystem>(true);
            Assert.IsTrue(particles.isPlaying, "the reused burst has to be restarted, not just switched on");
        }

        [UnityTest]
        public IEnumerator AnAbilityDrawsAnExpandingBurstThatFadesOut()
        {
            var burst = Find("Player").Find("Burst").GetComponent<MeshRenderer>();
            Assert.IsFalse(burst.enabled, "nothing to draw before an ability fires");

            State.P1.Active.Rage = GameConstants.RageMax;
            _controller.Manager.SubmitPlanningAction(PlayerSide.P1, ActionType.Defend, Vector2.zero, 0f, useAbility: true);
            _controller.Manager.SubmitPlanningAction(PlayerSide.Bot, ActionType.Defend, Vector2.zero, 0f, false);
            yield return RunSeconds(GameConstants.PlanningTime + 0.1f);

            Assert.IsTrue(burst.enabled, "the burst should be drawing right after the ability fires");
            float firstRadius = burst.transform.localScale.x;
            Assert.Greater(firstRadius, 0f);

            yield return RunSeconds(0.15f);
            Assert.Greater(burst.transform.localScale.x, firstRadius, "the ring should expand");

            yield return RunSeconds(0.8f);
            Assert.IsFalse(burst.enabled, "and switch itself off once it has faded");
        }

        [UnityTest]
        public IEnumerator TheArenaFloorAndWallsUseTheGeneratedTextures()
        {
            // Regression guard for the material pipeline: a missing texture leaves a flat colour,
            // which looks deliberate rather than broken.
            var floor = GameObject.Find("ArenaFloor");
            Assert.IsNotNull(floor);
            Assert.IsNotNull(floor.GetComponent<Renderer>().sharedMaterial.mainTexture,
                "the arena floor should be drawn with the generated sand texture");

            // The wall is stone either way: blocks from the modular kit when it is imported, painted
            // cubes when it is not. Which one is running decides where the texture hangs, so the
            // assertion is on the surface being textured rather than on one property name - the kit
            // uses its own shader graph, whose colour map is not the one Unity calls "main".
            var wallSegment = GameObject.Find("Segment_00");
            Assert.IsNotNull(wallSegment, "the arena has no wall");

            var wallMaterial = wallSegment.GetComponent<Renderer>().sharedMaterial;
            Assert.IsNotNull(wallMaterial, "the wall segments have no material");
            Assert.IsTrue(HasColourMap(wallMaterial),
                $"the wall is drawn with a flat colour ({wallMaterial.shader.name}), which reads as " +
                "deliberate rather than as a missing texture");
            yield return null;
        }

        /// <summary>
        /// Deliberately does not ask for <c>mainTexture</c>: a shader with no property tagged as the
        /// main one logs an error rather than returning null, and the test runner counts a logged
        /// error as a failure. The kit's shader graph is exactly that shader.
        /// </summary>
        private static bool HasColourMap(Material material)
        {
            foreach (var property in new[] { "_BaseMap", "_MainTex", "_ColorTexture" })
                if (material.HasProperty(property) && material.GetTexture(property) != null) return true;

            return false;
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
