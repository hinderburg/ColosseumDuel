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
    /// Body colour identifies the archetype, helmet colour identifies the side. Both have to hold
    /// as gladiators are swapped between rounds.
    /// </summary>
    public class GladiatorFigureTests
    {
        private const string ScenePath = "Assets/Scenes/Arena.unity";

        private GameController _controller;

        [UnitySetUp]
        public IEnumerator LoadArena()
        {
            yield return SceneManager.LoadSceneAsync(ScenePath, LoadSceneMode.Single);
            yield return null;
            _controller = Object.FindFirstObjectByType<GameController>();
            Assert.IsNotNull(_controller);
        }

        private Transform FindIn(string viewName, string childName)
            => _controller.GetComponentsInChildren<Transform>(true)
                .FirstOrDefault(t => t.name == viewName)
                ?.GetComponentsInChildren<Transform>(true)
                .FirstOrDefault(t => t.name == childName);

        [UnityTest]
        public IEnumerator EachArchetypeHasItsOwnFigureAndOnlyTheFighterIsShown()
        {
            _controller.SubmitPlayerPick(GladiatorId.Barbarius);
            yield return RunSeconds(GameConstants.RevealTime + 0.2f);

            foreach (var def in GladiatorDef.All)
            {
                var figure = FindIn("Player", $"Figure_{def.Id}");
                Assert.IsNotNull(figure, $"no figure built for {def.Name}");

                bool shouldShow = def.Id == GladiatorId.Barbarius;
                Assert.AreEqual(shouldShow, figure.gameObject.activeSelf,
                    $"{def.Name} should {(shouldShow ? "be" : "not be")} on the arena");
            }
        }

        [UnityTest]
        public IEnumerator TheBodyCarriesTheArchetypeColourAndTheHelmetTheSide()
        {
            _controller.SubmitPlayerPick(GladiatorId.Hilius);
            yield return RunSeconds(GameConstants.RevealTime + 0.2f);

            var palette = _controller.Arena.Palette;

            foreach (var def in GladiatorDef.All)
            {
                var body = FindIn("Player", $"Figure_{def.Id}")
                    .GetComponentsInChildren<Renderer>(true)
                    .First(r => r.name != "Helmet");

                Assert.AreSame(palette.BodyMaterialFor(def.Id), body.sharedMaterial,
                    $"{def.Name}'s body should carry his own archetype colour");
            }

            // Helmets say who owns the gladiator, which is what keeps two of the same archetype
            // apart when both sides field one.
            var playerHelmet = FindIn("Player", "Helmet").GetComponent<Renderer>();
            var botHelmet = FindIn("Bot", "Helmet").GetComponent<Renderer>();

            Assert.AreSame(palette.PlayerHelmet, playerHelmet.sharedMaterial);
            Assert.AreSame(palette.BotHelmet, botHelmet.sharedMaterial);
            Assert.AreNotSame(playerHelmet.sharedMaterial, botHelmet.sharedMaterial);
        }

        [UnityTest]
        public IEnumerator TheFigureRunsWhenTheGladiatorDoes()
        {
            _controller.SubmitPlayerPick(GladiatorId.Hilius);
            yield return RunSeconds(GameConstants.RevealTime + 0.2f);

            var animator = FindIn("Player", $"Figure_{GladiatorId.Hilius}")
                ?.GetComponentInChildren<Animator>(true);
            if (animator == null || animator.runtimeAnimatorController == null)
            {
                Assert.Ignore("No animator - the DoubleL pack is not imported here.");
                yield break;
            }

            Assert.AreEqual(0f, animator.GetFloat(AnimatorParams.Speed), 0.01f,
                "nothing has been ordered yet, so he should be standing");

            // A full-power dash across the arena. The parameter is in world units per second, which
            // is not the unit the simulation moves in - getting that conversion wrong leaves a
            // sprinting gladiator sliding along in his idle pose, which is easy to miss on a small
            // figure and impossible to miss once seen.
            _controller.Manager.SubmitPlanningAction(PlayerSide.P1, ActionType.Move, Vector2.up, 1f, false);
            _controller.Manager.SubmitPlanningAction(PlayerSide.Bot, ActionType.Defend, Vector2.zero, 0f, false);
            yield return RunUntil(() => _controller.Manager.State.Phase == MatchPhase.Action, 5f);
            yield return null;

            Assert.Greater(animator.GetFloat(AnimatorParams.Speed), AnimatorParams.RunThreshold,
                "a gladiator at full sprint should be past the run threshold");
        }

        [UnityTest]
        public IEnumerator AFallenGladiatorStaysOnTheSand()
        {
            _controller.SubmitPlayerPick(GladiatorId.Barbarius);
            yield return RunSeconds(GameConstants.RevealTime + 0.2f);

            var view = _controller.GetComponentsInChildren<Transform>(true)
                .First(t => t.name == "Player");

            _controller.Manager.State.P1.Active.Hp = 0f;
            _controller.Manager.State.P1.Active.Alive = false;
            yield return null;

            // He used to vanish on the frame the blow landed, which took the death with him. The
            // round holds for a moment afterwards, and that moment is what the animation is for.
            Assert.IsTrue(view.gameObject.activeSelf, "the body should still be on the arena");

            var animator = view.GetComponentsInChildren<Animator>(true)
                .FirstOrDefault(a => a.gameObject.activeInHierarchy);
            if (animator == null || animator.runtimeAnimatorController == null)
            {
                Assert.Ignore("No animator - the DoubleL pack is not imported here.");
                yield break;
            }

            Assert.IsTrue(animator.GetBool(AnimatorParams.Dead), "the animator was not told he fell");
        }

        private static IEnumerator RunUntil(System.Func<bool> done, float maxSeconds)
        {
            float t = 0f;
            while (!done() && t < maxSeconds) { yield return null; t += Time.unscaledDeltaTime; }
            Assert.IsTrue(done(), $"condition not reached within {maxSeconds}s");
        }

        private static IEnumerator RunSeconds(float seconds)
        {
            float t = 0f;
            while (t < seconds) { yield return null; t += Time.unscaledDeltaTime; }
        }
    }
}
