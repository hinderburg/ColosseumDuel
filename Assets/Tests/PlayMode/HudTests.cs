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
        public void TheHudFontCanActuallyDrawTheCyrillicCaptionsTheGameUses()
        {
            // Regression: the HUD was built on Unity's built-in font, which has no Cyrillic glyphs.
            // In the Editor the OS fonts quietly cover for it, so this only surfaced in the first
            // WebGL build - as gladiator names rendering into thin air.
            var font = _hud.GetComponentInChildren<Text>(true).font;
            Assert.IsNotNull(font, "HUD labels must have a font");

            foreach (var def in GladiatorDef.All)
                foreach (char c in def.Name + def.AbilityName)
                    Assert.IsTrue(font.HasCharacter(c),
                        $"the HUD font has no glyph for '{c}' - \"{def.Name}\" would render blank in a build");

            foreach (char c in "Победа Поражение Защита Способность Раунд цикл планирование выбыл")
                Assert.IsTrue(font.HasCharacter(c), $"the HUD font has no glyph for '{c}'");
        }

        [UnityTest]
        public IEnumerator TheMatchOpensOnThePickOverlayWithOneButtonPerGladiator()
        {
            var overlay = Find("Overlay");
            Assert.IsTrue(overlay.activeInHierarchy, "the pick overlay should be up before the match starts");

            foreach (var def in GladiatorDef.All)
            {
                var button = FindButton($"Pick_{def.Name}");
                Assert.IsNotNull(button, $"missing pick button for {def.Name}");
                Assert.IsTrue(button.interactable, $"{def.Name} is alive and should be pickable");
            }

            yield return null;
        }

        [UnityTest]
        public IEnumerator PickingThroughTheHudStartsTheRoundAndHidesTheOverlay()
        {
            FindButton($"Pick_{GladiatorDef.Barbarius.Name}").onClick.Invoke();
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

        [Test]
        public void ThePickCardsCarryTheIconAndSpellOutTheAbility()
        {
            foreach (var def in GladiatorDef.All)
            {
                var card = Find($"Pick_{def.Name}");
                Assert.IsNotNull(card, $"no pick card for {def.Name}");

                var icon = card.GetComponentsInChildren<Image>(true)
                    .FirstOrDefault(i => i.name == $"Icon_{def.Id}");
                Assert.IsNotNull(icon, $"{def.Name}'s card has no icon");
                Assert.AreSame(_controller.Arena.Palette.IconFor(def.Id), icon.sprite);

                // The name alone says nothing: "Мангуст" tells a first-time player neither what it
                // does nor how long it lasts, and that is the whole basis of the choice being made.
                var ability = card.GetComponentsInChildren<Text>(true)
                    .FirstOrDefault(t => t.name == $"Ability_{def.Id}");
                Assert.IsNotNull(ability, $"{def.Name}'s card has no ability line");
                StringAssert.Contains(def.AbilityName, ability.text);
                StringAssert.Contains(def.AbilityDescription, ability.text);
            }
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
        public IEnumerator TheDecisionTimerCountsDownAboveTheGladiator()
        {
            _controller.SubmitPlayerPick(GladiatorId.Brutius);
            yield return RunSeconds(GameConstants.RevealTime + 0.2f);

            var timer = Find("DecisionTimer").GetComponent<Text>();
            var defend = FindButton("Defend").GetComponent<RectTransform>();
            var ability = FindButton("Ability").GetComponent<RectTransform>();

            Assert.IsTrue(float.TryParse(timer.text.Replace(',', '.'),
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out float first),
                $"the timer should read as a number, not '{timer.text}'");

            // Above both buttons, on the gladiator's own column - the reason it moved off the status
            // line at the top of the screen, where timing a decision meant looking away from it.
            var rect = timer.rectTransform;
            Assert.Greater(rect.anchoredPosition.y, defend.anchoredPosition.y, "the timer is not above the guard");
            Assert.Greater(rect.anchoredPosition.y, ability.anchoredPosition.y, "the timer is not above the ability");

            yield return RunSeconds(0.6f);

            Assert.IsTrue(float.TryParse(timer.text.Replace(',', '.'),
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out float later));
            Assert.Less(later, first, "the timer is not counting down");
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
            _controller.Manager.SubmitPlanningAction(PlayerSide.P1, ActionType.Move, Vector2.right, 1f, false);
            _controller.Manager.SubmitPlanningAction(PlayerSide.Bot, ActionType.Move, Vector2.left, 1f, false);

            yield return RunUntil(() => State.P1.NeedsPick, 30f);

            Assert.IsTrue(Find("Overlay").activeInHierarchy);
            Assert.IsFalse(FindButton($"Pick_{GladiatorDef.Brutius.Name}").interactable,
                "the fallen gladiator must not be pickable again");
            Assert.IsTrue(FindButton($"Pick_{GladiatorDef.Hilius.Name}").interactable);
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
