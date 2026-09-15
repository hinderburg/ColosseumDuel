using System.Collections;
using System.Collections.Generic;
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
    /// The HUD reports match state and routes clicks; these check both halves against a real scene.
    /// </summary>
    public class HudTests
    {
        private const string ScenePath = "Assets/Scenes/Arena.unity";

        private GameController _controller;
        private MatchHud _hud;

        [UnitySetUp]
        public IEnumerator LoadArena()
        {
            yield return SceneManager.LoadSceneAsync(ScenePath, LoadSceneMode.Single);
            yield return null;

            _controller = Object.FindFirstObjectByType<GameController>();
            _hud = Object.FindFirstObjectByType<MatchHud>();
            Assert.IsNotNull(_hud, "the Arena scene must contain a MatchHud");

            // Past the menu. Every one of these tests is about the match HUD, and the menu standing
            // in front of it is a separate screen with its own tests.
            Object.FindFirstObjectByType<MenuView>().StartMatch();
            yield return null; // let LateUpdate populate it once
        }

        private MatchState State => _controller.Manager.State;

        private GameObject Find(string name)
            => _hud.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == name)?.gameObject;

        private Button FindButton(string name)
            => _hud.GetComponentsInChildren<Button>(true).FirstOrDefault(b => b.name == name);

        // ------------------------------------------------------------------

        [Test]
        public void TheSceneHasAnEventSystem()
        {
            // Without one the buttons render and do nothing, which is easy to miss by eye.
            Assert.IsNotNull(Object.FindFirstObjectByType<UnityEngine.EventSystems.EventSystem>());
        }

        [Test]
        public void TheHudFontCanActuallyDrawEveryCaptionTheGameUses()
        {
            // Regression: the HUD was built on Unity's built-in font, which has no Cyrillic glyphs.
            // In the Editor the OS fonts quietly cover for it, so this only surfaced in the first
            // WebGL build - as gladiator names rendering into thin air. The captions are English
            // now and that particular hole is closed, but the check is kept and pointed at whatever
            // the HUD is actually holding rather than at a list of words copied out of it: a list
            // goes stale the moment the wording changes, and then it is checking nothing.
            var font = _hud.GetComponentInChildren<Text>(true).font;
            Assert.IsNotNull(font, "HUD labels must have a font");

            int checkedCharacters = 0;
            foreach (var label in _hud.GetComponentsInChildren<Text>(true))
                foreach (char c in label.text ?? "")
                {
                    if (char.IsWhiteSpace(c)) continue;
                    Assert.IsTrue(font.HasCharacter(c),
                        $"the HUD font has no glyph for '{c}' - \"{label.text}\" on {label.name} " +
                        "would render blank in a build");
                    checkedCharacters++;
                }

            foreach (var def in GladiatorDef.All)
                foreach (char c in def.Name + string.Concat(def.Abilities.Select(k => AbilityDef.Get(k).Name + AbilityDef.Get(k).Summary)))
                {
                    if (char.IsWhiteSpace(c)) continue;
                    Assert.IsTrue(font.HasCharacter(c),
                        $"the HUD font has no glyph for '{c}' - \"{def.Name}\" would render blank in a build");
                    checkedCharacters++;
                }

            Assert.Greater(checkedCharacters, 50,
                "nothing was actually checked - the HUD came up with no text in it");
        }

        [UnityTest]
        public IEnumerator TheMatchOpensOnThePickOverlayWithOneButtonPerGladiator()
        {
            var overlay = Find("Overlay");
            Assert.IsTrue(overlay.activeInHierarchy, "the pick overlay should be up before the match starts");

            for (int slot = 0; slot < GameConstants.SquadSize; slot++)
            {
                var button = FindButton($"Pick_{slot}");
                Assert.IsNotNull(button, $"missing pick button for squad slot {slot}");
                Assert.IsTrue(button.interactable, $"slot {slot} is alive and should be pickable");
            }

            yield return null;
        }

        [UnityTest]
        public IEnumerator PickingThroughTheHudStartsTheClashAndHidesTheOverlay()
        {
            int barbarius = _controller.Squad.IndexOf(GladiatorId.Barbarius);
            FindButton($"Pick_{barbarius}").onClick.Invoke();
            yield return null;

            Assert.AreEqual(GladiatorId.Barbarius, State.P1.Active.Def.Id,
                "the pick button should submit that gladiator");
            Assert.AreEqual(MatchPhase.Reveal, State.Phase);
            Assert.IsTrue(Find("Overlay").activeInHierarchy, "reveal is still an overlay banner");

            yield return RunSeconds(GameConstants.RevealTime + 0.2f);

            Assert.AreEqual(MatchPhase.Planning, State.Phase);
            Assert.IsFalse(Find("Overlay").activeInHierarchy, "planning must not be behind an overlay");
        }

        [UnityTest]
        public IEnumerator TheDefendButtonOnlyWorksDuringPlanning_AndSubmitsDefend()
        {
            var defend = FindButton("Defend");
            Assert.IsFalse(defend.interactable, "nothing to defend with before a gladiator is picked");

            _controller.SubmitPlayerPick(GladiatorId.Brutius);
            yield return RunSeconds(GameConstants.RevealTime + 0.2f);
            Assert.IsTrue(defend.interactable);

            defend.onClick.Invoke();
            yield return null;
            Assert.AreEqual(ActionType.Defend, State.P1.Active.PlannedAction);

            yield return RunSeconds(GameConstants.PlanningTime + 0.1f);
            Assert.AreEqual(MatchPhase.Action, State.Phase);
            Assert.IsFalse(defend.interactable, "the action phase is not the time to change your mind");
        }

        /// <summary>
        /// The Auto switch sits just right of the player's own squad icons and hands his side to the
        /// bot: it picks and plans for him with nothing pressed, and a second press gives it back.
        /// </summary>
        [UnityTest]
        public IEnumerator TheAutoSwitchBesideTheSquadHandsThePlayersSideToTheBot()
        {
            yield return null;
            var toggle = FindButton("AutoToggle");
            Assert.IsNotNull(toggle, "there is no Auto switch");
            Assert.IsFalse(_controller.AutoPlay, "a match should start with the player playing");

            // Right of the rightmost of his own icons, and level with them.
            var icons = Enumerable.Range(0, GameConstants.SquadSize)
                .Select(i => Find($"P1_{i}").GetComponent<RectTransform>()).ToList();
            var toggleRect = toggle.GetComponent<RectTransform>();
            float iconsRight = icons.Max(r => WorldRect(r).xMax);
            Assert.Greater(WorldRect(toggleRect).xMin, iconsRight, "the switch is not to the right of the icons");
            Assert.Less(WorldRect(toggleRect).xMin - iconsRight, 60f, "the switch is nowhere near the icons");
            var iconRect = WorldRect(icons[0]);
            Assert.IsTrue(WorldRect(toggleRect).yMax > iconRect.yMin && WorldRect(toggleRect).yMin < iconRect.yMax,
                "the switch is not level with the icons");

            toggle.onClick.Invoke();
            yield return null;
            Assert.IsTrue(_controller.AutoPlay);
            StringAssert.Contains("Auto", toggle.GetComponentInChildren<Text>().text);

            // Nobody touches anything: the pick goes in and the first plan with it.
            yield return RunUntil(() => State.Phase == MatchPhase.Planning, 5f);
            Assert.IsNotNull(State.P1.Active, "on auto the player's fighter should have been sent in for him");
            yield return null;
            Assert.AreNotEqual(ActionType.None, State.P1.Active.PlannedAction, "on auto his round should be planned for him");
            Assert.IsFalse(FindButton("Defend").gameObject.activeInHierarchy,
                "the action buttons should stand down while the bot is playing him");

            toggle.onClick.Invoke();
            yield return null;
            Assert.IsFalse(_controller.AutoPlay, "a second press should give the side back");
        }

        private static Rect WorldRect(RectTransform rect)
        {
            var corners = new Vector3[4];
            rect.GetWorldCorners(corners);
            return Rect.MinMaxRect(corners[0].x, corners[0].y, corners[2].x, corners[2].y);
        }

        /// <summary>
        /// An ability firing puts its name up over the man who used it, large, with what it does in
        /// smaller type underneath - and takes it down again once it has been read.
        /// </summary>
        [UnityTest]
        public IEnumerator AnAbilityFiringPutsItsNameUpLargeWithWhatItDoesUnderneath()
        {
            _controller.SubmitPlayerPick(_controller.Squad[0]);
            yield return RunUntil(() => State.Phase == MatchPhase.Planning, 5f);

            var g = State.P1.Active;
            g.Rage = GameConstants.RageMax;
            Assert.IsTrue(_controller.Manager.SubmitPlanningAction(PlayerSide.P1, ActionType.Defend, Vector2.zero, 0f, true));
            yield return RunUntil(() => State.Phase == MatchPhase.Action, 6f);
            yield return null;

            var callouts = Object.FindFirstObjectByType<AbilityCalloutView>(FindObjectsInactive.Include);
            Assert.IsNotNull(callouts, "the HUD has no ability callouts");
            var shown = callouts.GetComponentsInChildren<Transform>(true)
                .Where(t => t.name.StartsWith("Callout_") && t.gameObject.activeSelf)
                .Select(t => t.GetComponentsInChildren<Text>(true))
                .FirstOrDefault(labels => labels.Any(l => l.name == "Name" && l.text == g.AbilityInfo.Name));
            Assert.IsNotNull(shown, $"{g.AbilityInfo.Name} went off and nothing said so");

            var name = shown.First(l => l.name == "Name");
            var description = shown.First(l => l.name == "Description");
            StringAssert.AreEqualIgnoringCase(g.AbilityInfo.Summary, description.text);
            Assert.Greater(name.fontSize, description.fontSize, "the name should be the big line");

            // The player's own man: the name goes up in the player's blue, not the ability's colour.
            Assert.AreEqual(HudFactory.PlayerColor.r, name.color.r, 0.01f, "the player's ability should be named in blue");
            Assert.AreEqual(HudFactory.PlayerColor.g, name.color.g, 0.01f, "the player's ability should be named in blue");
            Assert.AreEqual(HudFactory.PlayerColor.b, name.color.b, 0.01f, "the player's ability should be named in blue");

            var onScreen = _controller.Arena.ArenaCamera.WorldToScreenPoint(_controller.Arena.ToWorld(g.Pos));
            Assert.Less(Mathf.Abs(name.transform.position.x - onScreen.x), 60f, "the name is not over the man who used it");
            Assert.Greater(name.transform.position.y, onScreen.y, "the name should go up over him, not under him");

            yield return RunSeconds(AbilityCalloutView.Seconds + 0.3f);

            // By what it says rather than by the object: the pool hands a finished one straight to
            // the next thing announced - the opponent taking up the tutorial's blessing on his way
            // over, say - and then the same object is up again, saying something else.
            Assert.IsFalse(callouts.GetComponentsInChildren<Transform>(true)
                    .Where(t => t.name.StartsWith("Callout_") && t.gameObject.activeSelf)
                    .Any(t => t.GetComponentsInChildren<Text>(true).Any(l => l.name == "Name" && l.text == g.AbilityInfo.Name)),
                "the callout never went away");
        }

        /// <summary>
        /// One that lasts does not just fade: once it has been read over the man it goes down into
        /// the player's strip - low on the screen by his squad, on the right, the ability's side -
        /// and stays there with the rounds it has left, the last of them in yellow. Over, it goes.
        /// </summary>
        [UnityTest]
        public IEnumerator ALastingAbilityGoesDownIntoTheStripAndCountsDownItsRounds()
        {
            _controller.SubmitPlayerPick(_controller.Squad[0]);
            yield return RunUntil(() => State.Phase == MatchPhase.Planning, 5f);

            var g = State.P1.Active;
            g.Ability = AbilityKey.Bulwark;
            g.Rage = GameConstants.RageMax;
            Assert.IsTrue(_controller.Manager.SubmitPlanningAction(PlayerSide.P1, ActionType.Defend, Vector2.zero, 0f, true));
            yield return RunUntil(() => State.Phase == MatchPhase.Action, 6f);

            var callouts = Object.FindFirstObjectByType<AbilityCalloutView>(FindObjectsInactive.Include);
            yield return RunUntil(() => callouts.IsDocked(PlayerSide.P1, AbilityCalloutView.Slot.Ability),
                AbilityCalloutView.RiseSeconds + AbilityCalloutView.FlySeconds + 0.5f);
            Assert.IsTrue(callouts.IsDocked(PlayerSide.P1, AbilityCalloutView.Slot.Ability),
                "Bulwark is still working and never went into the strip");
            Assert.IsFalse(callouts.IsDocked(PlayerSide.P1, AbilityCalloutView.Slot.Blessing), "nothing was blessed");

            var dock = callouts.DockFor(PlayerSide.P1, AbilityCalloutView.Slot.Ability);
            var labels = dock.GetComponentsInChildren<Text>(true);
            Assert.AreEqual(g.AbilityInfo.Name, labels.First(l => l.name == "Name").text);
            StringAssert.AreEqualIgnoringCase(g.AbilityInfo.Description, labels.First(l => l.name == "Description").text);

            var centre = Centre(dock);
            Assert.Less(centre.y, Screen.height * 0.3f, "the player's strip belongs low, by his squad");
            Assert.Greater(centre.x, Screen.width * 0.5f, "the ability goes on the right");

            // Into the next round, which is its last: a steady moment to read the count at.
            yield return RunUntil(() => State.Phase == MatchPhase.Planning, 4f);
            yield return null;
            var timer = labels.First(l => l.name == "Timer");
            Assert.AreEqual(1, g.Buff.RoundsLeft, "this test expects Bulwark's last round here");
            Assert.AreEqual("1", timer.text, "the count should be the rounds it has left");
            var badge = dock.GetComponentsInChildren<Image>(true).First(i => i.name == "TimerBadge");
            Assert.AreEqual(HudFactory.RageColor.r, badge.color.r, 0.01f, "the last round should be marked in yellow");
            Assert.AreEqual(HudFactory.RageColor.g, badge.color.g, 0.01f, "the last round should be marked in yellow");

            g.Buff = default;
            yield return RunSeconds(0.5f);
            Assert.IsFalse(callouts.IsDocked(PlayerSide.P1, AbilityCalloutView.Slot.Ability), "it is over and is still in the strip");
            Assert.IsFalse(dock.gameObject.activeSelf, "it is over and its place is still drawn");
        }

        /// <summary>
        /// Taking up the blessing is announced the way an ability is - its name over the man, and an
        /// effect on him - and it goes into the left of his strip with its three rounds to run.
        /// </summary>
        [UnityTest]
        public IEnumerator TakingUpTheBlessingIsAnnouncedLikeAnAbility_AndGoesIntoTheLeftOfTheStrip()
        {
            _controller.SubmitPlayerPick(_controller.Squad[0]);
            yield return RunUntil(() => State.Phase == MatchPhase.Planning, 5f);

            var g = State.P1.Active;
            State.Buffs.PlaceAt(g.Pos);
            Assert.IsTrue(_controller.Manager.SubmitPlanningAction(PlayerSide.P1, ActionType.Defend, Vector2.zero, 0f, false));
            yield return RunUntil(() => g.WeaponBuffed, 6f);
            Assert.IsTrue(g.WeaponBuffed, "standing on the blessing, he should have taken it up");
            yield return null;

            var callouts = Object.FindFirstObjectByType<AbilityCalloutView>(FindObjectsInactive.Include);
            var shown = callouts.GetComponentsInChildren<Transform>(true)
                .Where(t => t.name.StartsWith("Callout_") && t.gameObject.activeSelf)
                .Select(t => t.GetComponentsInChildren<Text>(true))
                .FirstOrDefault(labels => labels.Any(l => l.name == "Name" && l.text == AbilityCalloutView.BlessingName));
            Assert.IsNotNull(shown, "the blessing was taken up and nothing said so");

            var palette = _controller.Arena.Palette;
            if (palette.BlessingFx != null)
            {
                var fx = Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                    .FirstOrDefault(t => t.name == "BlessingFx" && t.parent != null && t.parent.name == "Player");
                Assert.IsNotNull(fx, "the player's gladiator has no blessing effect");
                Assert.IsTrue(fx.gameObject.activeInHierarchy, "the blessing effect should be playing on him");
            }

            yield return RunUntil(() => callouts.IsDocked(PlayerSide.P1, AbilityCalloutView.Slot.Blessing),
                AbilityCalloutView.RiseSeconds + AbilityCalloutView.FlySeconds + 0.5f);
            Assert.IsTrue(callouts.IsDocked(PlayerSide.P1, AbilityCalloutView.Slot.Blessing),
                "the blessing is working and never went into the strip");

            var dock = callouts.DockFor(PlayerSide.P1, AbilityCalloutView.Slot.Blessing);
            Assert.AreEqual(GameConstants.WeaponBuffRounds.ToString(),
                dock.GetComponentsInChildren<Text>(true).First(l => l.name == "Timer").text,
                "freshly taken, it has all its rounds to run");
            Assert.Less(Centre(dock).x, Screen.width * 0.5f, "the blessing goes on the left");

            g.WeaponBuffRoundsLeft = 0;
            yield return RunSeconds(0.5f);
            Assert.IsFalse(callouts.IsDocked(PlayerSide.P1, AbilityCalloutView.Slot.Blessing), "it is over and is still in the strip");
        }

        private static Vector3 Centre(RectTransform rect)
        {
            var corners = new Vector3[4];
            rect.GetWorldCorners(corners);
            return (corners[0] + corners[2]) * 0.5f;
        }

        /// <summary>
        /// Taking up the horn brings the ability button up over the player's man for a moment, out of
        /// turn and not to be pressed, its ring filling to the rage he has now - and the guard and
        /// the clock stay down, because nothing is being decided.
        /// </summary>
        [UnityTest]
        public IEnumerator TheHornShowsTheRageItGaveOnTheAbilityButton_OutOfTurn()
        {
            _controller.SubmitPlayerPick(_controller.Squad[0]);
            yield return RunUntil(() => State.Phase == MatchPhase.Planning, 5f);

            var g = State.P1.Active;
            g.Rage = 0.1f;
            State.Horn.PlaceAt(g.Pos);

            // The opponent held where he is, so the phase is not cut short by the two of them meeting.
            State.Bot.Active.EnsnaredRoundsLeft = 2;
            Assert.IsTrue(_controller.Manager.SubmitPlanningAction(PlayerSide.P1, ActionType.Defend, Vector2.zero, 0f, false));
            yield return RunUntil(() => g.Rage > 0.3f, 6f);
            Assert.AreEqual(MatchPhase.Action, State.Phase, "standing on it, he should take the horn up as the phase starts");

            yield return RunSeconds(0.7f);
            var buttons = Object.FindFirstObjectByType<ActionButtonsView>(FindObjectsInactive.Include);
            Assert.AreEqual(MatchPhase.Action, State.Phase);
            Assert.IsTrue(buttons.ShowingRageGain, "the horn's gain is not being shown");
            Assert.IsTrue(buttons.gameObject.activeInHierarchy, "the ability button should be up out of turn");
            Assert.IsFalse(buttons.Ability.interactable, "and not be something to press");
            Assert.AreEqual((0.1f + GameConstants.HornRage) / GameConstants.RageMax, buttons.RageGaugeFill, 0.02f,
                "its ring should have filled to the rage he has now");
            Assert.IsFalse(Find("Defend").activeInHierarchy, "the guard should stay down - nothing is being decided");

            yield return RunSeconds(1.2f);
            if (State.Phase == MatchPhase.Action)
                Assert.IsFalse(buttons.gameObject.activeInHierarchy, "and it should go again");
        }

        /// <summary>Every one of the eighteen abilities has an icon, and no two share one.</summary>
        [Test]
        public void EveryAbilityHasItsOwnIcon()
        {
            var palette = _controller.Arena.Palette;
            var seen = new List<Sprite>();
            foreach (var ability in AbilityDef.All)
            {
                var icon = palette.AbilityIconFor(ability.Key);
                Assert.IsNotNull(icon, $"{ability.Name} has no icon - run the bootstrap");
                CollectionAssert.DoesNotContain(seen, icon, $"{ability.Name} shares an icon with another ability");
                seen.Add(icon);
            }
        }

        [Test]
        public void EveryArchetypeHasItsOwnIcon()
        {
            var palette = _controller.Arena.Palette;

            var seen = new List<Sprite>();
            foreach (var def in GladiatorDef.All)
            {
                var icon = palette.IconFor(def.Id);
                Assert.IsNotNull(icon, $"{def.Name} has no icon - run the bootstrap");

                // Unique, not just present: the whole job of these is to tell three cards apart, and
                // one sprite reused for all three does that no better than no sprite at all.
                CollectionAssert.DoesNotContain(seen, icon, $"{def.Name} shares an icon with another archetype");
                seen.Add(icon);
            }
        }

        [UnityTest]
        public IEnumerator ThePickCardsCarryTheIconAndSpellOutTheAbility()
        {
            yield return null;

            for (int slot = 0; slot < GameConstants.SquadSize; slot++)
            {
                var def = GladiatorDef.Get(_controller.Squad[slot]);
                var card = Find($"Pick_{slot}");
                Assert.IsNotNull(card, $"no pick card in slot {slot}");

                var icon = card.GetComponentsInChildren<Image>(true)
                    .FirstOrDefault(i => i.name == $"Icon_{slot}");
                Assert.IsNotNull(icon, $"{def.Name}'s card has no icon");
                Assert.AreSame(_controller.Arena.Palette.IconFor(def.Id), icon.sprite);

                // The name alone says nothing: "Mongoose" tells a first-time player neither what it
                // does nor how long it lasts, and that is the whole basis of the choice being made.
                var ability = card.GetComponentsInChildren<Text>(true)
                    .FirstOrDefault(t => t.name == $"Ability_{slot}");
                Assert.IsNotNull(ability, $"{def.Name}'s card has no ability line");
                var chosen = AbilityDef.Get(_controller.AbilityFor(def.Id));
                StringAssert.Contains(chosen.Name, ability.text);
                StringAssert.Contains(chosen.Summary, ability.text);
            }
        }

        [UnityTest]
        public IEnumerator RestartingBringsBackThePickOverlayWithNothingArmed()
        {
            var input = Object.FindFirstObjectByType<PlayerInputController>();

            _controller.SubmitPlayerPick(GladiatorId.Brutius);
            yield return RunSeconds(GameConstants.RevealTime + 0.2f);

            State.P1.Active.Rage = GameConstants.RageMax;
            yield return null;
            input.ToggleAbility();
            FindButton("Defend").onClick.Invoke();
            yield return null;
            Assert.IsTrue(input.AbilityArmed && input.DefendArmed, "the test needs both armed first");

            FindButton("Restart").onClick.Invoke();
            yield return null;
            yield return null;

            Assert.IsTrue(Find("Overlay").activeInHierarchy, "a restart should land on the pick screen");
            for (int slot = 0; slot < GameConstants.SquadSize; slot++)
                Assert.IsTrue(FindButton($"Pick_{slot}").interactable,
                    $"slot {slot} should be pickable again");

            Assert.IsFalse(input.AbilityArmed, "an armed ability survived into the new match");
            Assert.IsFalse(input.DefendArmed, "an armed guard survived into the new match");
        }

        [UnityTest]
        public IEnumerator TheScreenEdgesTintWhilePlanningAndClearWhileActing()
        {
            _controller.SubmitPlayerPick(GladiatorId.Brutius);
            yield return RunSeconds(GameConstants.RevealTime + 0.4f);

            var vignette = Find("PlanningVignette").GetComponent<Image>();
            Assert.IsNotNull(vignette.sprite, "no vignette sprite - run the bootstrap");
            Assert.AreEqual(MatchPhase.Planning, State.Phase);
            Assert.IsTrue(vignette.enabled && vignette.color.a > 0.05f,
                "planning should be visible at the edges of the screen");

            // It must not be raycastable: it covers the whole screen, and the phase it marks is the
            // one the player spends dragging on exactly that surface.
            Assert.IsFalse(vignette.raycastTarget, "the vignette would swallow every drag");

            yield return RunSeconds(GameConstants.PlanningTime + 0.5f);

            Assert.AreEqual(MatchPhase.Action, State.Phase);
            Assert.IsTrue(!vignette.enabled || vignette.color.a < 0.05f,
                "the tint should be gone once the gladiators are moving");
        }

        [UnityTest]
        public IEnumerator TheDefendButtonLatchesAndCanBeTakenBack()
        {
            _controller.SubmitPlayerPick(GladiatorId.Brutius);
            yield return RunSeconds(GameConstants.RevealTime + 0.2f);

            var input = Object.FindFirstObjectByType<PlayerInputController>();
            var defend = FindButton("Defend");
            var background = (Image)defend.targetGraphic;

            Color idle = background.color;
            Assert.IsFalse(input.DefendArmed);

            defend.onClick.Invoke();
            yield return null;

            // The whole point of the change: pressing it has to leave a mark. It used to file the
            // plan and look exactly as it had a moment earlier, so there was no telling a chosen
            // guard from a phase nobody had touched.
            Assert.IsTrue(input.DefendArmed);
            Assert.AreEqual(ActionType.Defend, State.P1.Active.PlannedAction);
            Assert.AreNotEqual(idle, background.color, "a pressed guard looks the same as an unpressed one");
            Assert.IsTrue(Find("DefendGlow").GetComponent<Image>().enabled);

            defend.onClick.Invoke();
            yield return null;

            Assert.IsFalse(input.DefendArmed);
            Assert.AreEqual(ActionType.None, State.P1.Active.PlannedAction,
                "taking the guard back should leave the player undecided again");
            Assert.AreEqual(idle, background.color);
        }

        /// <summary>
        /// The two buttons ride on the gladiator - the guard up and to his left, the ability up and
        /// to his right, level with each other and above him - and there are only two of them: the
        /// about-face and its button are gone.
        /// </summary>
        [UnityTest]
        public IEnumerator TheActionButtonsSitEitherSideAboveTheGladiator()
        {
            _controller.SubmitPlayerPick(GladiatorId.Brutius);
            yield return RunSeconds(GameConstants.RevealTime + 0.2f);

            var ability = (RectTransform)FindButton("Ability").transform;
            var defend = (RectTransform)FindButton("Defend").transform;

            var camera = _controller.Arena.ArenaCamera;
            Vector2 him = camera.WorldToScreenPoint(_controller.Arena.ToWorld(State.P1.Active.Pos));
            var canvasRect = (RectTransform)defend.GetComponentInParent<Canvas>().transform;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, him, null, out var himLocal);

            Assert.Less(defend.anchoredPosition.x, himLocal.x, "the guard goes to his left");
            Assert.Greater(ability.anchoredPosition.x, himLocal.x, "the ability to his right");
            Assert.AreEqual(defend.anchoredPosition.y, ability.anchoredPosition.y, 0.01f,
                "both sit at the same height");
            Assert.Greater(defend.anchoredPosition.y, himLocal.y + defend.sizeDelta.y,
                "and above him, clear of his feet where a press is meant for the sand");

            bool turnButtonLeft = Object.FindObjectsByType<Button>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .Any(b => b.name == "AboutFace");
            Assert.IsFalse(turnButtonLeft, "the about-face was removed, and its button with it");
        }

        /// <summary>
        /// The countdown is drawn around the gladiator himself, faintly.
        ///
        /// It was above his head as a small bright ring. On him it is read without the eye leaving
        /// the fight at all, which is what the phase is for - and it has to be faint, or the thing
        /// it is drawn over stops being visible through it.
        /// </summary>
        [UnityTest]
        public IEnumerator TheCountdownIsDrawnFaintlyAroundTheGladiator()
        {
            _controller.SubmitPlayerPick(GladiatorId.Brutius);
            yield return RunSeconds(GameConstants.RevealTime + 0.2f);

            var ring = Find("DecisionTimer").GetComponent<Image>();
            var rect = ring.rectTransform;

            Assert.Less(ring.color.a, 0.8f, "a solid ring would hide the man it is drawn on");
            Assert.Greater(ring.color.a, 0.2f, "and one this faint could not be read at all");

            var camera = _controller.Arena.ArenaCamera;
            Vector2 him = camera.WorldToScreenPoint(_controller.Arena.ToWorld(State.P1.Active.Pos));
            var canvasRect = (RectTransform)ring.GetComponentInParent<Canvas>().transform;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, him, null, out var himLocal);

            Assert.AreEqual(0f, Vector2.Distance(himLocal, rect.anchoredPosition), 2f,
                "the ring should be centred on him, not floating above his head");

            // And it still counts. Drawn in a new place but doing the same job.
            float before = ring.fillAmount;
            yield return RunSeconds(0.6f);
            Assert.Less(ring.fillAmount, before, "the ring is not emptying");
        }

        [UnityTest]
        public IEnumerator PullingBackReleasesTheGuard()
        {
            _controller.SubmitPlayerPick(GladiatorId.Brutius);
            yield return RunSeconds(GameConstants.RevealTime + 0.2f);

            var input = Object.FindFirstObjectByType<PlayerInputController>();
            FindButton("Defend").onClick.Invoke();
            yield return null;
            Assert.IsTrue(input.DefendArmed);

            // Guard and move are the two halves of one either-or, so starting a pull has to let the
            // button go - otherwise it sits lit while the gladiator charges.
            input.TryBeginDrag(State.P1.Active.Pos);
            yield return null;

            Assert.IsFalse(input.DefendArmed, "the guard stayed lit through a pull-back");
            input.CancelDrag();
        }

        [UnityTest]
        public IEnumerator TheDecisionTimerIsSeenRatherThanRead()
        {
            _controller.SubmitPlayerPick(GladiatorId.Brutius);
            yield return RunSeconds(GameConstants.RevealTime + 0.2f);

            var timer = Find("DecisionTimer").GetComponent<Image>();

            // A ring that empties, with no number in it: how much is left is its shape. Where it is
            // drawn is TheCountdownIsDrawnFaintlyAroundTheGladiator's business, not this one's.
            Assert.AreEqual(Image.Type.Filled, timer.type, "the countdown is not a fill at all");
            Assert.IsEmpty(Find("DecisionTimer").GetComponentsInChildren<Text>(true),
                "the countdown went back to being read rather than seen");

            float first = timer.fillAmount;
            Assert.Greater(first, 0.5f, "the ring should start nearly full");

            yield return RunSeconds(0.6f);

            Assert.Less(timer.fillAmount, first, "the ring is not emptying");
        }

        [UnityTest]
        public IEnumerator TheAbilityButtonUnlocksOnlyWhenTheRageMeterIsFull()
        {
            _controller.SubmitPlayerPick(GladiatorId.Brutius);
            yield return RunSeconds(GameConstants.RevealTime + 0.2f);

            var ability = FindButton("Ability");
            State.P1.Active.Rage = 0.5f;
            yield return null;
            Assert.IsFalse(ability.interactable, "half a meter is not enough");

            State.P1.Active.Rage = GameConstants.RageMax;
            yield return null;
            Assert.IsTrue(ability.interactable);

            // Arming used to be a tick in the caption; the caption now names the ability instead,
            // and the armed state is carried by the button filling with the rage colour.
            var background = (Image)ability.targetGraphic;
            var idle = background.color;

            ability.onClick.Invoke();
            yield return null;
            Assert.AreNotEqual(idle, background.color, "an armed ability should read as armed");
        }

        [UnityTest]
        public IEnumerator TheOverlayReturnsWhenThePlayersGladiatorDies_AndOffersTheSurvivors()
        {
            _controller.SubmitPlayerPick(GladiatorId.Brutius);
            yield return RunSeconds(GameConstants.RevealTime + 0.2f);

            State.P1.Active.Hp = 0.01f;
            State.P1.Active.Pos = new Vector2(-40f, 0f);
            State.Bot.Active.Pos = new Vector2(40f, 0f);

            // Pointed along the charge. A run leaves along the nose and bends onto its target, so two
            // men set down across an axis they did not spawn along would curve away rather than meet.

            State.P1.Active.Facing = Vector2.right;
            State.Bot.Active.Facing = Vector2.left;

            _controller.Manager.SubmitPlanningAction(PlayerSide.P1, ActionType.Move, Vector2.right, 1f, false);
            _controller.Manager.SubmitPlanningAction(PlayerSide.Bot, ActionType.Move, Vector2.left, 1f, false);

            yield return RunUntil(() => State.P1.NeedsPick, 30f);

            Assert.IsTrue(Find("Overlay").activeInHierarchy);
            Assert.IsFalse(FindButton($"Pick_{_controller.Squad.IndexOf(GladiatorId.Brutius)}").interactable,
                "the fallen gladiator must not be pickable again");
            Assert.IsTrue(FindButton($"Pick_{_controller.Squad.IndexOf(GladiatorId.Hilius)}").interactable);
        }

        [UnityTest]
        public IEnumerator AFallenGladiatorIsMarkedWithASkull_AndTheActiveOneIsFramed()
        {
            var tile = Find("P1_0");
            var skull = tile.GetComponentsInChildren<Transform>(true).First(t => t.name == "Skull");
            var frame = tile.GetComponentsInChildren<Transform>(true).First(t => t.name == "ActiveFrame");

            Assert.IsFalse(skull.gameObject.activeSelf, "nobody has fallen yet");
            Assert.IsNotNull(skull.GetComponent<Image>().sprite,
                "the skull needs a sprite - without one it would draw as a blank square");

            _controller.SubmitPlayerPick(GladiatorId.Brutius); // roster index 0
            yield return RunSeconds(GameConstants.RevealTime + 0.2f);
            Assert.IsTrue(frame.gameObject.activeSelf, "the gladiator on the arena is framed");

            State.P1.Active.Hp = 0.01f;
            State.P1.Active.Pos = new Vector2(-40f, 0f);
            State.Bot.Active.Pos = new Vector2(40f, 0f);

            // Pointed along the charge. A run leaves along the nose and bends onto its target, so two
            // men set down across an axis they did not spawn along would curve away rather than meet.

            State.P1.Active.Facing = Vector2.right;
            State.Bot.Active.Facing = Vector2.left;

            _controller.Manager.SubmitPlanningAction(PlayerSide.P1, ActionType.Move, Vector2.right, 1f, false);
            _controller.Manager.SubmitPlanningAction(PlayerSide.Bot, ActionType.Move, Vector2.left, 1f, false);

            yield return RunUntil(() => !State.P1.Roster[0].Alive, 30f);

            Assert.IsTrue(skull.gameObject.activeSelf, "a fallen gladiator carries a skull");
            Assert.IsFalse(frame.gameObject.activeSelf, "and is no longer framed as the active one");
        }

        [UnityTest]
        public IEnumerator TheHpBarActuallyShrinksAsAGladiatorTakesDamage()
        {
            // Regression: the bars were built as Image.Type.Filled, whose fillAmount is ignored on an
            // Image with no sprite - every bar sat permanently full while reporting the right value.
            _controller.SubmitPlayerPick(GladiatorId.Brutius);
            yield return RunSeconds(GameConstants.RevealTime + 0.2f);

            var card = Find("P1_0");
            var fill = card.GetComponentsInChildren<RectTransform>(true)
                .First(t => t.name == "Fill" && t.parent.name == "Hp");

            Assert.AreEqual(1f, fill.anchorMax.x, 0.01f, "a full gladiator should have a full bar");

            State.P1.Active.Hp = State.P1.Active.Def.MaxHp * 0.25f;
            yield return null;

            Assert.AreEqual(0.25f, fill.anchorMax.x, 0.01f,
                "the bar must follow the HP it is reporting");
        }

        [UnityTest]
        public IEnumerator TheRestartButtonStartsAWholeNewMatch()
        {
            _controller.SubmitPlayerPick(GladiatorId.Brutius);
            yield return RunSeconds(GameConstants.RevealTime + 0.2f);

            // The bench has to be genuinely empty, not merely on low HP: with anyone left alive the
            // match correctly waits in Pick for a replacement that this test never sends.
            foreach (var g in State.P1.Roster)
            {
                if (ReferenceEquals(g, State.P1.Active)) continue;
                g.Hp = 0f;
                g.Alive = false;
            }
            State.P1.Active.Hp = 0.01f;
            State.P1.Active.Pos = new Vector2(-40f, 0f);
            State.Bot.Active.Pos = new Vector2(40f, 0f);

            // Pointed along the charge. A run leaves along the nose and bends onto its target, so two
            // men set down across an axis they did not spawn along would curve away rather than meet.

            State.P1.Active.Facing = Vector2.right;
            State.Bot.Active.Facing = Vector2.left;

            _controller.Manager.SubmitPlanningAction(PlayerSide.P1, ActionType.Move, Vector2.right, 1f, false);
            _controller.Manager.SubmitPlanningAction(PlayerSide.Bot, ActionType.Move, Vector2.left, 1f, false);

            yield return RunUntil(() => State.Phase == MatchPhase.MatchEnd, 60f);

            var restart = FindButton("Restart");
            Assert.IsTrue(restart.gameObject.activeInHierarchy, "the match-end overlay should offer a restart");

            restart.onClick.Invoke();
            yield return null;

            Assert.AreEqual(MatchPhase.Pick, State.Phase);
            Assert.IsTrue(State.P1.Roster.All(g => g.Alive), "a new match starts with a full squad");
            Assert.AreEqual(GladiatorDef.Brutius.MaxHp, State.P1.Roster[0].Hp, 0.01f);
        }

        /// <summary>
        /// A last exchange that takes both men ends the match on one screen. With the opponent's bench
        /// empty and the player's not, the player has won - and is left with nobody fighting and men
        /// to send, which used to put the pick screen up over the one saying so.
        /// </summary>
        [UnityTest]
        public IEnumerator ADoubleKnockoutWithMenLeftOnOneSideEndsOnOneScreen()
        {
            yield return KillBothInOneExchange(emptyPlayerBench: false);

            Assert.AreEqual(PlayerSide.P1, State.WinnerSide, "the opponent has nobody left, so the player has won");
            Assert.IsTrue(State.P1.Roster.Any(g => g.Alive), "this test needs the player to have men left");
            Assert.IsFalse(Find("PickRow").activeInHierarchy, "the pick screen is up over the end of the match");
            Assert.IsTrue(FindButton("Restart").gameObject.activeInHierarchy, "the end of the match should be showing");
            Assert.AreEqual("Victory", Find("Title").GetComponent<Text>().text);
        }

        /// <summary>
        /// And one that takes the last man on both sides is a draw - not a win handed to whichever side
        /// the code happened to ask about first.
        /// </summary>
        [UnityTest]
        public IEnumerator ADoubleKnockoutOfBothLastMenIsADraw()
        {
            yield return KillBothInOneExchange(emptyPlayerBench: true);

            Assert.IsNull(State.WinnerSide, "nobody is left on either side - nobody has won");
            Assert.IsFalse(Find("PickRow").activeInHierarchy, "there is nobody to pick");
            Assert.IsTrue(FindButton("Restart").gameObject.activeInHierarchy, "the end of the match should be showing");
            Assert.AreEqual("Draw", Find("Title").GetComponent<Text>().text);
        }

        /// <summary>
        /// Plays out an exchange that kills both men at once, the opponent's bench empty and the
        /// player's too if asked, and waits for the match to end.
        /// </summary>
        private IEnumerator KillBothInOneExchange(bool emptyPlayerBench)
        {
            _controller.SubmitPlayerPick(GladiatorId.Brutius);
            yield return RunUntil(() => State.Phase == MatchPhase.Planning, GameConstants.RevealTime + 1f);

            foreach (var side in emptyPlayerBench ? new[] { State.P1, State.Bot } : new[] { State.Bot })
                foreach (var g in side.Roster)
                {
                    if (ReferenceEquals(g, side.Active)) continue;
                    g.Hp = 0f;
                    g.Alive = false;
                }

            // Inside each other's reach, squared up and standing their ground, so the exchange is
            // the first thing that happens in the action phase and both blows land in it together.
            var p1 = State.P1.Active;
            var bot = State.Bot.Active;
            float gap = Mathf.Min(p1.WeaponDef.Reach, bot.WeaponDef.Reach) * 0.7f;
            p1.Pos = new Vector2(-gap * 0.5f, 0f);
            bot.Pos = new Vector2(gap * 0.5f, 0f);
            p1.Facing = Vector2.right;
            bot.Facing = Vector2.left;
            p1.Hp = 0.01f;
            bot.Hp = 0.01f;

            _controller.Manager.SubmitPlanningAction(PlayerSide.P1, ActionType.Defend, Vector2.zero, 0f, false);
            _controller.Manager.SubmitPlanningAction(PlayerSide.Bot, ActionType.Defend, Vector2.zero, 0f, false);

            yield return RunUntil(() => State.Phase == MatchPhase.MatchEnd, 30f);
            Assert.AreEqual(MatchPhase.MatchEnd, State.Phase, "the match should have ended");
            Assert.IsFalse(p1.Alive, "this test needs both men to fall");
            Assert.IsFalse(bot.Alive, "this test needs both men to fall");
            yield return null;
        }

        /// <summary>
        /// The tutorial labels the blessing on the sand, over the blessing itself, with what it does -
        /// and takes the label away when there is nothing there to label.
        /// </summary>
        [UnityTest]
        public IEnumerator TheTutorialLabelsTheBlessingWhereItLies()
        {
            var tutorial = Object.FindFirstObjectByType<TutorialView>(FindObjectsInactive.Include);
            Assert.IsNotNull(tutorial);

            _controller.SubmitPlayerPick(GladiatorId.Brutius);
            yield return RunSeconds(GameConstants.RevealTime + 0.2f);

            var spot = new Vector2(20f, -120f);
            State.Buffs.PlaceAt(spot);
            yield return null;

            var hint = tutorial.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "BlessingHint");
            Assert.IsNotNull(hint, "the tutorial has no label for the blessing");
            Assert.IsTrue(hint.gameObject.activeSelf, "the blessing on the sand is not labelled");
            StringAssert.Contains("Blessing", hint.GetComponentInChildren<Text>().text);

            var onScreen = _controller.Arena.ArenaCamera.WorldToScreenPoint(_controller.Arena.ToWorld(spot));
            Assert.Less(Mathf.Abs(hint.position.x - onScreen.x), 80f, "the label is not over the blessing");

            State.Buffs.Clear();
            yield return null;
            Assert.IsFalse(hint.gameObject.activeSelf, "a label left over sand with nothing on it");
        }

        /// <summary>
        /// The teaching labels are for the opening of the first fight and then they go.
        ///
        /// Two rounds is long enough to run at the blessing and swing once with it. Left up they
        /// cover the sand the player has just learned to read - a caption is help on round one and
        /// an obstruction on round five - so the end of it is worth a test rather than an eyeball:
        /// it only shows itself several rounds into a match nobody replays.
        /// </summary>

        [UnityTest]
        public IEnumerator TheTutorialLabelsGoAwayAfterTwoRounds()
        {
            var tutorial = Object.FindFirstObjectByType<TutorialView>(FindObjectsInactive.Include);
            Assert.IsNotNull(tutorial, "the HUD should carry a tutorial layer");

            _controller.SubmitPlayerPick(GladiatorId.Brutius);
            yield return RunSeconds(GameConstants.RevealTime + 0.2f);

            Assert.IsTrue(State.Tutorial, "the first fight of a session is the taught one");
            Assert.AreEqual(1, State.Round);
            Assert.IsTrue(tutorial.gameObject.activeInHierarchy, "round one should be labelled");

            State.Round = TutorialView.TutorialRounds;
            yield return null;
            Assert.IsTrue(tutorial.gameObject.activeInHierarchy,
                $"round {TutorialView.TutorialRounds} is the last one that carries the labels");

            State.Round = TutorialView.TutorialRounds + 1;
            yield return null;
            Assert.IsFalse(tutorial.gameObject.activeInHierarchy,
                "the labels should be gone by now - they cover the arena the player is reading");

            // The ordinary control hint takes over where the tutorial line left off, rather than
            // both being off and the player left with nothing.
            var hint = _hud.GetComponentsInChildren<Text>(true).First(t => t.name == "Hint");
            Assert.IsTrue(hint.enabled, "the standing hint should come back once the tutorial ends");
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

            // The HUD redraws in LateUpdate, so on the frame a condition first becomes true it is
            // still showing the previous state. One more frame, and assertions can trust the HUD.
            yield return null;
        }
    }
}
