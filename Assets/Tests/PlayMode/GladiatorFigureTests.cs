using System.Collections;
using System.Linq;
using ColosseumDuel.Core;
using ColosseumDuel.Gameplay;
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

        private static IEnumerator RunSeconds(float seconds)
        {
            float t = 0f;
            while (t < seconds) { yield return null; t += Time.unscaledDeltaTime; }
        }
    }
}
