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
    /// The screens in front of the match: the squad you are taking in, and the one for changing it.
    /// </summary>
    public class MenuTests
    {
        private const string ScenePath = "Assets/Scenes/Arena.unity";

        private GameController _controller;
        private MenuView _menu;
        private MatchHud _hud;

        [UnitySetUp]
        public IEnumerator LoadArena()
        {
            yield return SceneManager.LoadSceneAsync(ScenePath, LoadSceneMode.Single);
            yield return null;

            _controller = Object.FindFirstObjectByType<GameController>();
            _menu = Object.FindFirstObjectByType<MenuView>();
            _hud = Object.FindFirstObjectByType<MatchHud>();
            Assert.IsNotNull(_menu, "the Arena scene must carry a MenuView");
            yield return null;
        }

        private GameObject Find(string name)
            => _hud.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == name)?.gameObject;

        private Button FindButton(string name)
            => _hud.GetComponentsInChildren<Button>(true).FirstOrDefault(b => b.name == name);

        [UnityTest]
        public IEnumerator TheGameOpensOnTheMenuAndTheMatchHudStandsDown()
        {
            Assert.AreEqual(MenuView.Screen.Main, _menu.Current);
            Assert.IsTrue(Find("MenuMain").activeInHierarchy);
            Assert.IsFalse(Find("MenuRoster").activeInHierarchy);

            // The match underneath is built and waiting rather than not yet started - that is what
            // makes "start" a curtain going up instead of a load, and what keeps the first fight on
            // exactly the same code path as every later one.
            Assert.AreEqual(MatchPhase.Pick, _controller.Manager.State.Phase);
            Assert.IsFalse(Find("Overlay").activeInHierarchy,
                "the pick overlay must not be showing through the menu");

            yield return null;
        }

        [UnityTest]
        public IEnumerator StartMatchHandsTheScreenToThePickOverlay()
        {
            FindButton("StartMatch").onClick.Invoke();
            yield return null;

            Assert.IsFalse(_menu.IsOpen);
            Assert.IsFalse(Find("MenuMain").activeInHierarchy);
            Assert.IsTrue(Find("Overlay").activeInHierarchy, "the pick screen should be up now");
            Assert.IsTrue(FindButton("Pick_0").interactable);
        }

        [UnityTest]
        public IEnumerator TheMainScreenShowsTheSquadAndWhatEachOneFightsWith()
        {
            for (int slot = 0; slot < GameConstants.SquadSize; slot++)
            {
                var def = GladiatorDef.Get(_controller.Squad[slot]);

                var name = Find($"SquadName_{slot}").GetComponent<Text>();
                StringAssert.Contains(def.Name, name.text);
                StringAssert.Contains(WeaponDef.Get(def.SkilledWith).Name, name.text,
                    "the squad line has to say what he fights with - it is half of what he is");

                var badge = Find($"SquadSkill_{slot}").GetComponent<Image>();
                Assert.AreSame(_controller.Arena.Palette.IconFor(def.SkilledWith), badge.sprite);
                Assert.IsTrue(badge.enabled, "the weapon badge is not being drawn");
            }

            yield return null;
        }

        [UnityTest]
        public IEnumerator EveryWeaponKindHasItsOwnBadge()
        {
            // Three badges that are the same picture tell the player nothing at all, which is the
            // failure mode a "the sprite exists" check waves straight through.
            var palette = _controller.Arena.Palette;
            var seen = new System.Collections.Generic.List<Sprite>();

            foreach (var weapon in WeaponDef.All)
            {
                var icon = palette.IconFor(weapon.Kind);
                Assert.IsNotNull(icon, $"{weapon.Name} has no badge - run the bootstrap");
                CollectionAssert.DoesNotContain(seen, icon, $"{weapon.Name} shares a badge");
                seen.Add(icon);
            }

            yield return null;
        }

        [UnityTest]
        public IEnumerator TheRosterScreenOffersTwoOfEachAndOpensOnWhatYouAlreadyHave()
        {
            FindButton("ChooseGladiators").onClick.Invoke();
            yield return null;

            Assert.AreEqual(MenuView.Screen.Roster, _menu.Current);

            int offers = GladiatorDef.All.Count * MenuView.CopiesPerArchetype;
            for (int i = 0; i < offers; i++)
                Assert.IsNotNull(FindButton($"Offer_{i}"), $"no card for offer {i}");
            Assert.IsNull(FindButton($"Offer_{offers}"), "there should be exactly two of each");

            // Opened on the squad they already have, so swapping one fighter does not mean
            // re-picking the other two.
            int chosen = 0;
            for (int i = 0; i < offers; i++)
                if (Find($"OfferFrame_{i}").GetComponent<Image>().enabled) chosen++;
            Assert.AreEqual(GameConstants.SquadSize, chosen,
                "the roster screen opened empty rather than on the current squad");
        }

        [UnityTest]
        public IEnumerator ASquadCanBeTwoOfTheSameFighter()
        {
            // The whole reason two of each are offered. Anything that quietly collapsed the pair
            // would leave the player looking at a composition they cannot actually field.
            FindButton("ChooseGladiators").onClick.Invoke();
            yield return null;

            // Clear whatever was carried in, then take both Brutius and one Hilius.
            int offers = GladiatorDef.All.Count * MenuView.CopiesPerArchetype;
            for (int i = 0; i < offers; i++)
                if (Find($"OfferFrame_{i}").GetComponent<Image>().enabled)
                {
                    FindButton($"Offer_{i}").onClick.Invoke();
                    yield return null;
                }

            foreach (int offer in OffersOf(GladiatorId.Brutius))
            {
                FindButton($"Offer_{offer}").onClick.Invoke();
                yield return null;
            }
            FindButton($"Offer_{OffersOf(GladiatorId.Hilius).First()}").onClick.Invoke();
            yield return null;

            Assert.IsTrue(FindButton("ConfirmSquad").interactable, "three are chosen");
            FindButton("ConfirmSquad").onClick.Invoke();
            yield return null;

            CollectionAssert.AreEqual(
                new[] { GladiatorId.Brutius, GladiatorId.Brutius, GladiatorId.Hilius },
                _controller.Squad);

            // And the match under the menu is fought with it - two separate men, two pick cards.
            var roster = _controller.Manager.State.P1.Roster;
            Assert.AreEqual(2, roster.Count(g => g.Def.Id == GladiatorId.Brutius));
            Assert.AreNotSame(roster[0], roster[1], "two Brutius must be two instances, not one twice");

            _menu.StartMatch();
            yield return null;
            for (int slot = 0; slot < GameConstants.SquadSize; slot++)
                Assert.IsTrue(FindButton($"Pick_{slot}").interactable, $"slot {slot} should be pickable");
        }

        [UnityTest]
        public IEnumerator AFourthPickIsRefusedRatherThanPushingOutTheFirst()
        {
            // Order is the order the pick screen will offer them in, so silently dropping the
            // earliest choice would change a decision the player had already made.
            FindButton("ChooseGladiators").onClick.Invoke();
            yield return null;

            Assert.IsFalse(FindButton("ConfirmSquad").interactable == false,
                "the squad carried in is already three, so confirm should be live");

            int spare = Enumerable.Range(0, GladiatorDef.All.Count * MenuView.CopiesPerArchetype)
                .First(i => !Find($"OfferFrame_{i}").GetComponent<Image>().enabled);

            Assert.IsFalse(FindButton($"Offer_{spare}").interactable,
                "a fourth fighter should be refused while three are already chosen");
        }

        private System.Collections.Generic.IEnumerable<int> OffersOf(GladiatorId id)
        {
            for (int i = 0; i < GladiatorDef.All.Count * MenuView.CopiesPerArchetype; i++)
            {
                var label = Find($"Offer_{i}").GetComponentInChildren<Text>();
                if (label.text.StartsWith(GladiatorDef.Get(id).Name)) yield return i;
            }
        }
    }
}
