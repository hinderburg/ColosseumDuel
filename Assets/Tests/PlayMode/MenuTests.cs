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

        /// <summary>
        /// The menu says which build is open.
        ///
        /// The link stays the same while what it serves changes, and a browser keeps serving the old
        /// build from its cache - so "is this the new one?" needs an answer on the screen. Tucked in
        /// the bottom corner and quiet: it is for whoever is checking, not something to read in play.
        /// </summary>
        [UnityTest]
        public IEnumerator TheMenuSaysWhichBuildItIs()
        {
            var label = Find("BuildVersion");
            Assert.IsNotNull(label, "the menu has no build label");
            Assert.IsTrue(label.activeInHierarchy, "the build label is not on the opening screen");

            var text = label.GetComponent<Text>();
            StringAssert.Contains(Application.version, text.text,
                "the label should carry the version the build was stamped with");
            Assert.AreEqual(new Vector2(1f, 0f), text.rectTransform.anchorMin,
                "it belongs in the bottom-right corner, out of the way of the squad and the buttons");

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
        public IEnumerator TheRosterScreenOffersOneOfEachAndOpensOnWhatYouAlreadyHave()
        {
            FindButton("ChooseGladiators").onClick.Invoke();
            yield return null;

            Assert.AreEqual(MenuView.Screen.Roster, _menu.Current);

            int offers = GladiatorDef.All.Count * MenuView.CopiesPerArchetype;
            Assert.AreEqual(6, offers, "six archetypes, one card each");
            for (int i = 0; i < offers; i++)
                Assert.IsNotNull(FindButton($"Offer_{i}"), $"no card for offer {i}");
            Assert.IsNull(FindButton($"Offer_{offers}"), "there should be exactly one of each");
            foreach (var def in GladiatorDef.All)
                Assert.AreEqual(1, OffersOf(def.Id).Count(), $"{def.Name} should be on offer exactly once");

            // Opened on the squad they already have, so swapping one fighter does not mean
            // re-picking the other two.
            int chosen = 0;
            for (int i = 0; i < offers; i++)
                if (Find($"OfferFrame_{i}").GetComponent<Image>().enabled) chosen++;
            Assert.AreEqual(GameConstants.SquadSize, chosen,
                "the roster screen opened empty rather than on the current squad");
        }

        [UnityTest]
        public IEnumerator ASquadOfNewArchetypesIsFought_AndTheBotFieldsTheThreeLeftOver()
        {
            // Six on offer and three taken: the three the player leaves are the bot's, so the two
            // squads between them are all six men and neither side fights its own mirror.
            FindButton("ChooseGladiators").onClick.Invoke();
            yield return null;

            // Clear whatever was carried in, then take the three that came with the new weapons.
            int offers = GladiatorDef.All.Count * MenuView.CopiesPerArchetype;
            for (int i = 0; i < offers; i++)
                if (Find($"OfferFrame_{i}").GetComponent<Image>().enabled)
                {
                    FindButton($"Offer_{i}").onClick.Invoke();
                    yield return null;
                }

            var chosen = new[] { GladiatorId.Retiarius, GladiatorId.Scutarius, GladiatorId.Hastarius };
            foreach (var id in chosen)
            {
                FindButton($"Offer_{OffersOf(id).First()}").onClick.Invoke();
                yield return null;
            }

            Assert.IsTrue(FindButton("ConfirmSquad").interactable, "three are chosen");
            FindButton("ConfirmSquad").onClick.Invoke();
            yield return null;

            CollectionAssert.AreEqual(chosen, _controller.Squad, "in the order they were chosen");

            var state = _controller.Manager.State;
            CollectionAssert.AreEqual(chosen, state.P1.Roster.Select(g => g.Def.Id));
            CollectionAssert.AreEquivalent(
                new[] { GladiatorId.Brutius, GladiatorId.Barbarius, GladiatorId.Hilius },
                state.Bot.Roster.Select(g => g.Def.Id),
                "the bot should field exactly the three the player did not take");

            // Each walks in with his own weapon.
            foreach (var g in state.P1.Roster)
                Assert.AreEqual(g.Def.SkilledWith, g.Weapon, $"{g.Def.Name} is not holding his own weapon");

            _menu.StartMatch();
            yield return null;
            for (int slot = 0; slot < GameConstants.SquadSize; slot++)
                Assert.IsTrue(FindButton($"Pick_{slot}").interactable, $"slot {slot} should be pickable");
        }

        [UnityTest]
        public IEnumerator TheSameFighterCannotBeTakenTwice()
        {
            FindButton("ChooseGladiators").onClick.Invoke();
            yield return null;

            CollectionAssert.AllItemsAreUnique(_controller.Squad);
            Assert.IsNull(FindButton($"Offer_{GladiatorDef.All.Count}"),
                "a second card for anybody would let the same man be taken twice");
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

        [UnityTest]
        public IEnumerator TheCornerButtonTakesAMatchInProgressBackToTheMenu()
        {
            FindButton("StartMatch").onClick.Invoke();
            yield return null;
            Assert.AreEqual(MenuView.Screen.Closed, _menu.Current, "the match should be running");

            var button = FindButton("MenuButton");
            Assert.IsNotNull(button, "the match HUD needs a way out of a match");

            // Top left, which is the corner neither squad strip uses. Asserted as a corner rather
            // than as coordinates: the anchor is what keeps it there on any screen, and a button
            // pinned to the right anchor with the wrong pivot still reads as top left in the editor
            // and drifts off the edge in a build.
            var rect = (RectTransform)button.transform;
            Assert.AreEqual(new Vector2(0f, 1f), rect.anchorMin);
            Assert.AreEqual(new Vector2(0f, 1f), rect.anchorMax);
            Assert.AreEqual(new Vector2(0f, 1f), rect.pivot);

            // Deep enough into the match that going back cannot be mistaken for never having left:
            // a pick is in, the clash is under way, and somebody has taken damage.
            _controller.SubmitPlayerPick(GladiatorId.Barbarius);
            yield return RunSeconds(GameConstants.RevealTime + 0.2f);
            _controller.Manager.State.P1.Active.Hp -= 40f;
            Assert.AreNotEqual(MatchPhase.Pick, _controller.Manager.State.Phase);

            button.onClick.Invoke();
            yield return null;

            Assert.AreEqual(MenuView.Screen.Main, _menu.Current);
            Assert.IsTrue(Find("MenuMain").activeInHierarchy);
            Assert.IsFalse(Find("MenuButton").activeInHierarchy,
                "the way out should not still be sitting on top of the menu it led to");

            // The match behind the curtain is a fresh one, not the wounded one he walked out of.
            Assert.AreEqual(MatchPhase.Pick, _controller.Manager.State.Phase);
            foreach (var g in _controller.Manager.State.P1.Roster)
                Assert.AreEqual(g.Def.MaxHp, g.Hp, $"{g.Def.Name} should have come back whole");
        }

        /// <summary>
        /// A finished match offers the way back to the main menu, and straight to the roster to change
        /// the squad - both onto a fresh match rather than the finished one sitting behind the menu.
        /// </summary>
        [UnityTest]
        public IEnumerator AFinishedMatchOffersTheMainMenuAndTheRoster()
        {
            _menu.StartMatch();
            yield return null;

            EndTheMatch();
            yield return null;

            var toMenu = FindButton("EndToMainMenu");
            var toRoster = FindButton("EndToRoster");
            Assert.IsNotNull(toMenu, "the end of a match has no way back to the menu");
            Assert.IsNotNull(toRoster, "the end of a match has no way to change the gladiators");
            Assert.IsTrue(toMenu.gameObject.activeInHierarchy, "the way back to the menu is not showing");
            Assert.IsTrue(toRoster.gameObject.activeInHierarchy, "the way to the roster is not showing");

            toMenu.onClick.Invoke();
            yield return null;
            Assert.AreEqual(MenuView.Screen.Main, _menu.Current);
            Assert.AreEqual(MatchPhase.Pick, _controller.Manager.State.Phase,
                "behind the menu should be a fresh match, not the one that just ended");

            _menu.StartMatch();
            yield return null;
            EndTheMatch();
            yield return null;

            FindButton("EndToRoster").onClick.Invoke();
            yield return null;
            Assert.AreEqual(MenuView.Screen.Roster, _menu.Current, "it should open straight on the roster");
            Assert.AreEqual(MatchPhase.Pick, _controller.Manager.State.Phase);
        }

        /// <summary>Puts the match straight at its end, the bot the winner, as if the last man had fallen.</summary>
        private void EndTheMatch()
        {
            var state = _controller.Manager.State;
            state.WinnerSide = PlayerSide.Bot;
            state.Phase = MatchPhase.MatchEnd;
        }

        /// <summary>
        /// Tapping a man's card opens his three abilities; choosing one there goes with him into the
        /// fight, and the card shows it from then on.
        /// </summary>
        [UnityTest]
        public IEnumerator TappingACardOpensHisAbilities_AndTheOneChosenGoesIntoTheFight()
        {
            FindButton("ChooseGladiators").onClick.Invoke();
            yield return null;

            int hilius = OffersOf(GladiatorId.Hilius).First();
            FindButton($"OfferCard_{hilius}").onClick.Invoke();
            yield return null;

            Assert.IsTrue(_menu.AbilityWindowOpen, "tapping the card should open his abilities");
            Assert.IsTrue(Find("AbilityWindow").activeInHierarchy);
            StringAssert.Contains("Hilius", Find("AbilityGladiatorName").GetComponent<Text>().text);
            for (int k = 0; k < 3; k++)
                Assert.AreEqual(AbilityDef.Get(GladiatorDef.Hilius.Abilities[k]).Name,
                    Find($"AbilityName_{k}").GetComponent<Text>().text, $"ability card {k}");

            FindButton("AbilitySelect_2").onClick.Invoke();
            yield return null;

            Assert.AreEqual(AbilityKey.Mongoose, _controller.AbilityFor(GladiatorId.Hilius));
            Assert.IsTrue(Find("AbilityOutline_2").GetComponent<Image>().enabled, "the chosen card should be outlined");
            Assert.IsFalse(Find("AbilityOutline_0").GetComponent<Image>().enabled, "and only the chosen one");
            StringAssert.Contains("Mongoose", Find("AbilityDetailName").GetComponent<Text>().text);

            FindButton("AbilityClose").onClick.Invoke();
            yield return null;
            Assert.IsFalse(_menu.AbilityWindowOpen);
            Assert.AreEqual("Mongoose", Find($"OfferAbility_{hilius}").GetComponent<Text>().text,
                "the card should show the ability he now takes in");

            // Into the fight with it.
            if (!_controller.Squad.Contains(GladiatorId.Hilius))
            {
                FindButton("TeamRemove_2").onClick.Invoke();
                yield return null;
                FindButton($"Offer_{hilius}").onClick.Invoke();
                yield return null;
            }
            FindButton("ConfirmSquad").onClick.Invoke();
            yield return null;

            var man = _controller.Manager.State.P1.Roster.First(g => g.Def.Id == GladiatorId.Hilius);
            Assert.AreEqual(AbilityKey.Mongoose, man.Ability, "he should fight with the ability chosen for him");
        }

        /// <summary>
        /// The team along the bottom is the three chosen, in order, each with the ability he takes in;
        /// its cross takes one out, and the fight cannot start a man short.
        /// </summary>
        [UnityTest]
        public IEnumerator TheTeamStripShowsTheThreeChosen_AndItsCrossTakesOneOut()
        {
            FindButton("ChooseGladiators").onClick.Invoke();
            yield return null;

            for (int slot = 0; slot < GameConstants.SquadSize; slot++)
            {
                var def = GladiatorDef.Get(_controller.Squad[slot]);
                Assert.AreEqual(def.Name, Find($"TeamName_{slot}").GetComponent<Text>().text, $"team slot {slot}");
                Assert.IsTrue(Find($"TeamRemove_{slot}").activeInHierarchy);
            }
            Assert.IsTrue(FindButton("ConfirmSquad").interactable);

            FindButton("TeamRemove_0").onClick.Invoke();
            yield return null;

            Assert.IsFalse(FindButton("ConfirmSquad").interactable, "the fight cannot start a man short");
            Assert.IsTrue(Find("TeamEmpty_2").GetComponent<Text>().enabled, "the last place should now be empty");
            Assert.AreEqual(GladiatorDef.Get(_controller.Squad[1]).Name, Find("TeamName_0").GetComponent<Text>().text,
                "the others move up rather than leaving a hole in the middle");
        }

        [UnityTest]
        public IEnumerator StartFightStartsTheFight()
        {
            FindButton("ChooseGladiators").onClick.Invoke();
            yield return null;

            FindButton("ConfirmSquad").onClick.Invoke();
            yield return null;

            Assert.AreEqual(MenuView.Screen.Closed, _menu.Current, "Start Fight should go straight into the fight");
            Assert.IsTrue(Find("Overlay").activeInHierarchy, "the pick screen should be up");
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

        private System.Collections.Generic.IEnumerable<int> OffersOf(GladiatorId id)
        {
            for (int i = 0; i < GladiatorDef.All.Count * MenuView.CopiesPerArchetype; i++)
            {
                var label = Find($"OfferName_{i}").GetComponent<Text>();
                if (label.text.StartsWith(GladiatorDef.Get(id).Name)) yield return i;
            }
        }
    }
}
