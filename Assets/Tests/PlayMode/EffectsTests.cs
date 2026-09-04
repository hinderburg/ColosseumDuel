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
        public void TheSceneHasACameraShake()
        {
            Assert.IsNotNull(_controller.Shake, "GameController.Shake must be wired by the bootstrap");
        }

        [UnityTest]
        public IEnumerator ACollisionShakesTheCameraAndPutsItBack()
        {
            var camera = Camera.main;
            Vector3 resting = camera.transform.position;

            yield return ForceCollision();
            yield return RunUntil(() => _controller.Shake.IsShaking, 3f);

            // Sample across a few frames: a shake passes through zero twice per cycle, so a single
            // frame could legitimately catch it at the origin.
            float maxOffset = 0f;
            for (int i = 0; i < 12; i++)
            {
                yield return null;
                maxOffset = Mathf.Max(maxOffset, Vector3.Distance(camera.transform.position, resting));
            }
            Assert.Greater(maxOffset, 0.01f, "the camera should visibly move on a head-on collision");

            yield return RunUntil(() => !_controller.Shake.IsShaking, 3f);
            yield return null;
            Assert.Less(Vector3.Distance(camera.transform.position, resting), 0.001f,
                "and must come back to rest, not drift");
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
                elapsed += Time.deltaTime;
                maxScale = Mathf.Max(maxScale, model.localScale.x);
            }
            Assert.Greater(maxScale, 1.05f, "the hit reaction should be visible on the model");

            yield return RunSeconds(0.5f);
            Assert.AreEqual(1f, model.localScale.x, 0.001f, "and must settle back to normal");
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

            var wallSegment = GameObject.Find("Segment_00");
            Assert.IsNotNull(wallSegment);
            Assert.IsNotNull(wallSegment.GetComponent<Renderer>().sharedMaterial.mainTexture,
                "the wall should be drawn with the generated stone texture");
            yield return null;
        }

        private static IEnumerator RunSeconds(float seconds)
        {
            float t = 0f;
            while (t < seconds) { yield return null; t += Time.deltaTime; }
        }

        private static IEnumerator RunUntil(System.Func<bool> done, float maxSeconds)
        {
            float t = 0f;
            while (!done() && t < maxSeconds) { yield return null; t += Time.deltaTime; }
            Assert.IsTrue(done(), $"condition not reached within {maxSeconds}s");
        }
    }
}
