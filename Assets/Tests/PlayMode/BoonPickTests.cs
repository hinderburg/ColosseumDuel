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
                Assert.IsNotNull(Find($"BoonArt_{i}").GetComponent<Image>().sprite,
                    $"card {i} has no painting - the card sheet is cut by the bootstrap into the palette");
            }
            Assert.AreEqual(GameConstants.BoonPickTime, float.Parse(Find("BoonTimer").GetComponent<Text>().text), 1f,
                "the clock should start from ten");

            FindButton("BoonCard_1").onClick.Invoke();
            yield return null;

            CollectionAssert.Contains(State.P1.Boons, offer[1], "the boon tapped should be his");
            Assert.AreEqual(MatchPhase.Reveal, State.Phase, "and the clash announced");
            Assert.IsFalse(Find("BoonPick").activeInHierarchy, "and the cards gone");
        }

        /// <summary>
        /// The boon taken is announced over the man once the clash is - its picture beside its name -
        /// and the picture then goes onto the shelf in the side's strip, where it stays. The bot's
        /// goes onto its own shelf the same way.
        /// </summary>
        [UnityTest]
        public IEnumerator TheBoonTakenIsAnnouncedOverTheMan_AndItsPictureGoesOntoTheShelf()
        {
            _controller.SubmitPlayerPick(_controller.Squad[0]);
            yield return null;
            var key = State.P1.BoonOffer[0];
            _controller.SubmitPlayerBoon(key);
            yield return null;
            Assert.AreEqual(MatchPhase.Reveal, State.Phase);

            var callouts = Object.FindFirstObjectByType<AbilityCalloutView>(FindObjectsInactive.Include);
            var callout = _hud.GetComponentsInChildren<Transform>(true)
                .FirstOrDefault(t => t.name.StartsWith("Callout_") && t.gameObject.activeSelf
                                     && t.Find("Name").GetComponent<Text>().text == BoonDef.Get(key).Name);
            Assert.IsNotNull(callout, "the boon's name should have gone up when the clash was announced");
            Assert.IsTrue(callout.Find("Art").GetComponent<Image>().enabled, "and its picture beside it");
            Assert.IsFalse(callouts.IsShelved(PlayerSide.P1, key), "the picture is still in the air");

            yield return RunUntil(() => callouts.IsShelved(PlayerSide.P1, key),
                AbilityCalloutView.RiseSeconds + AbilityCalloutView.FlySeconds + 1f);
            Assert.IsTrue(callouts.IsShelved(PlayerSide.P1, key), "the picture never reached the shelf");
            var shelf = callouts.ShelfFor(PlayerSide.P1);
            var first = shelf.GetComponentsInChildren<Image>(true).First(i => i.name == "Boon_0");
            Assert.IsTrue(first.enabled && first.sprite != null, "the shelf should show the painting");
            Assert.Less(shelf.position.y, Screen.height * 0.15f, "the player's shelf belongs in his squad's row");
            Assert.Greater(shelf.position.x, Screen.width * 0.6f, "to the right of the auto toggle");

            Assert.AreEqual(1, State.Bot.Boons.Count, "the bot took one too");
            yield return RunUntil(() => callouts.IsShelved(PlayerSide.Bot, State.Bot.Boons.First()), 1f);
            Assert.IsTrue(callouts.IsShelved(PlayerSide.Bot, State.Bot.Boons.First()), "and it should be on the bot's shelf");
            var botShelf = callouts.ShelfFor(PlayerSide.Bot);
            Assert.Greater(botShelf.position.y, Screen.height * 0.85f, "the bot's shelf belongs in its squad's row");
            Assert.Less(botShelf.position.x, Screen.width * 0.4f, "to the right of the menu button, left of its squad");
        }

        private static IEnumerator RunUntil(System.Func<bool> done, float maxSeconds)
        {
            float end = Time.realtimeSinceStartup + maxSeconds;
            while (!done() && Time.realtimeSinceStartup < end) yield return null;
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
            foreach (var d in BoonDef.All)
                Assert.IsNotNull(FindButton($"BoonChoice_{d.Key}").transform.Find("Art").GetComponent<Image>().sprite,
                    $"{d.Key} has no painting on its card");

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
