using System.Collections;
using System.Linq;
using ColosseumDuel.Core;
using ColosseumDuel.Gameplay;
using ColosseumDuel.Gameplay.Hud;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace ColosseumDuel.Tests
{
    /// <summary>
    /// The two screens boons are chosen on: three cards in the match when a new man steps out, and
    /// the menu's screen for the five taken into every match. Boons are off in a batch run - see
    /// GameController.OfferBoons - so these turn them on for themselves.
    /// </summary>
    public class BoonPickTests
    {
        private const string ScenePath = "Assets/Scenes/Arena.unity";

        private GameController _controller;
        private MatchHud _hud;

        [UnitySetUp]
        public IEnumerator LoadArena()
        {
            GameController.OfferBoonsOverride = true;
            yield return SceneManager.LoadSceneAsync(ScenePath, LoadSceneMode.Single);
            yield return null;
            _controller = Object.FindFirstObjectByType<GameController>();
            _hud = Object.FindFirstObjectByType<MatchHud>();
            Object.FindFirstObjectByType<MenuView>().StartMatch();
            yield return null;
        }

        [TearDown]
        public void BoonsBackToTheirDefault() => GameController.OfferBoonsOverride = null;

        private MatchState State => _controller.Manager.State;

        private GameObject Find(string name)
            => _hud.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == name)?.gameObject;

        private Button FindButton(string name)
            => _hud.GetComponentsInChildren<Button>(true).FirstOrDefault(b => b.name == name);

        [UnityTest]
        public IEnumerator ANewManSteppingOutIsOfferedThreeBoons_AndTheOneTakenGoesWithTheSquad()
        {
            _controller.SubmitPlayerPick(_controller.Squad[0]);
            yield return null;

            Assert.AreEqual(MatchPhase.BoonPick, State.Phase, "the clash should wait on the player's boon");
            Assert.IsTrue(Find("BoonPick").activeInHierarchy, "the boon cards should be up");
            var offer = State.P1.BoonOffer.ToList();
            for (int i = 0; i < offer.Count; i++)
            {
                Assert.IsTrue(Find($"BoonCard_{i}").activeInHierarchy, $"card {i} is not showing");
                Assert.AreEqual(BoonDef.Get(offer[i]).Name, Find($"BoonName_{i}").GetComponent<Text>().text);
            }
            Assert.AreEqual(GameConstants.BoonPickTime, float.Parse(Find("BoonTimer").GetComponent<Text>().text), 1f,
                "the clock should start from ten");

            FindButton("BoonCard_1").onClick.Invoke();
            yield return null;

            CollectionAssert.Contains(State.P1.Boons, offer[1], "the boon tapped should be his");
            Assert.AreEqual(MatchPhase.Reveal, State.Phase, "and the clash announced");
            Assert.IsFalse(Find("BoonPick").activeInHierarchy, "and the cards gone");
        }

        [UnityTest]
        public IEnumerator TheMenuChoosesTheFiveBoonsTakenIntoEveryMatch()
        {
            var menu = Object.FindFirstObjectByType<MenuView>();
            menu.ReturnToMainMenu();
            yield return null;

            FindButton("ChooseBoons").onClick.Invoke();
            yield return null;
            Assert.IsTrue(Find("BoonLoadout").activeInHierarchy, "the boon screen should be open");
            Assert.AreEqual(BoonDef.All.Count, BoonDef.All.Count(d => FindButton($"BoonChoice_{d.Key}") != null),
                "every boon should be on it");

            var done = FindButton("BoonsDone");
            Assert.IsTrue(done.interactable, "the five he brings now are a whole loadout");

            var dropped = _controller.BoonLoadout[0];
            var added = BoonDef.All.Select(d => d.Key).First(k => !_controller.BoonLoadout.Contains(k));
            FindButton($"BoonChoice_{dropped}").onClick.Invoke();
            yield return null;
            Assert.IsFalse(done.interactable, "four is not a loadout");
            Assert.AreEqual("4 / 5", Find("BoonCounter").GetComponent<Text>().text);

            FindButton($"BoonChoice_{added}").onClick.Invoke();
            yield return null;
            Assert.IsTrue(done.interactable);

            done.onClick.Invoke();
            yield return null;
            Assert.IsFalse(Find("BoonLoadout").activeInHierarchy, "done closes the screen");
            CollectionAssert.Contains(_controller.BoonLoadout, added);
            CollectionAssert.DoesNotContain(_controller.BoonLoadout, dropped);
            Assert.AreEqual(BoonDef.LoadoutSize, _controller.BoonLoadout.Count);
        }
    }
}
