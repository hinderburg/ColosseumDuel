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
                foreach (char c in def.Name + def.AbilityName + def.AbilityDescription)
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
        public IEnumerator PickingThroughTheHudStartsTheRoundAndHidesTheOverlay()
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
                StringAssert.Contains(def.AbilityName, ability.text);
                StringAssert.Contains(def.AbilityDescription, ability.text);
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
        /// The three buttons sit in a column down the right edge, clear of the arena.
        ///
        /// They used to ride on the gladiator, which put them on top of the thing being aimed: a
        /// press meant for the sand landed on a button often enough that they had to keep being
        /// pushed further from him. Pinned to the edge, the whole arena is pressable again - and
        /// this is the assertion that would catch them drifting back over it.
        /// </summary>
        [UnityTest]
        public IEnumerator TheActionButtonsSitInAColumnDownTheRightEdge()
        {
            _controller.SubmitPlayerPick(GladiatorId.Brutius);
            yield return RunSeconds(GameConstants.RevealTime + 0.2f);

            var ability = (RectTransform)FindButton("Ability").transform;
            var defend = (RectTransform)FindButton("Defend").transform;
            var turn = (RectTransform)FindButton("AboutFace").transform;

            foreach (var rect in new[] { ability, defend, turn })
            {
                Assert.AreEqual(new Vector2(1f, 0.5f), rect.anchorMin, $"{rect.name} is not on the edge");
                Assert.AreEqual(new Vector2(1f, 0.5f), rect.anchorMax);
                Assert.Less(rect.anchoredPosition.x, 0f, $"{rect.name} hangs off the screen");
            }

            // Stacked, in the order they are reached for, and not overlapping each other.
            Assert.Greater(ability.anchoredPosition.y, defend.anchoredPosition.y);
            Assert.Greater(defend.anchoredPosition.y, turn.anchoredPosition.y);
            Assert.Greater(defend.anchoredPosition.y - turn.anchoredPosition.y, defend.sizeDelta.y,
                "two buttons closer together than one is tall would overlap");

            // And well away from the gladiator, who is somewhere in the middle of the arena.
            var camera = _controller.Arena.ArenaCamera;
            Vector2 him = camera.WorldToScreenPoint(_controller.Arena.ToWorld(State.P1.Active.Pos));
            var canvasRect = (RectTransform)FindButton("Defend").GetComponentInParent<Canvas>().transform;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, him, null, out var himLocal);

            Assert.Greater(Vector2.Distance(himLocal, defend.anchoredPosition), defend.sizeDelta.x,
                "a button sitting on the gladiator is a press that never reaches the sand");
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

        /// <summary>
        /// The turn button spends the about-face and comes back over the cycles that follow, and
        /// the ring on it says how far along that is.
        /// </summary>
        [UnityTest]
        public IEnumerator TheTurnButtonSpendsTheAboutFaceAndShowsItComingBack()
        {
            _controller.SubmitPlayerPick(GladiatorId.Brutius);
            yield return RunSeconds(GameConstants.RevealTime + 0.2f);

            var turn = FindButton("AboutFace");
            var gauge = Find("TurnGauge").GetComponent<Image>();
            var player = State.P1.Active;
            var wasFacing = player.Facing;

            Assert.IsTrue(turn.interactable, "it starts the round charged");
            Assert.AreEqual(1f, gauge.fillAmount, 0.001f);

            turn.onClick.Invoke();
            yield return null;

            Assert.AreEqual(0f, Vector2.Angle(player.Facing, -wasFacing), 0.01f, "he did not turn");
            Assert.IsFalse(turn.interactable, "and it is spent");
            Assert.Less(gauge.fillAmount, 0.5f, "the ring should have emptied with it");

            // Round the cycle and it is visibly further along, though not yet back.
            yield return RunSeconds(GameConstants.PlanningTime + GameConstants.ActionTime + 0.5f);
            yield return RunSeconds(GameConstants.PlanningTime * 0.5f);

            Assert.Greater(gauge.fillAmount, 0f, "a cycle later it should be charging visibly");
            Assert.Less(gauge.fillAmount, 1f, "but it is not back yet");
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

            // Nothing on the sand but the two of them. Traps are laid at random, and one under
            // either man would take health off him on the first step - which reads, to a test
            // watching for a blow, exactly like a blow.
            _controller.Manager.State.Traps.Traps.Clear();

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

            // Nothing on the sand but the two of them. Traps are laid at random, and one under
            // either man would take health off him on the first step - which reads, to a test
            // watching for a blow, exactly like a blow.
            _controller.Manager.State.Traps.Traps.Clear();

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

            // Nothing on the sand but the two of them. Traps are laid at random, and one under
            // either man would take health off him on the first step - which reads, to a test
            // watching for a blow, exactly like a blow.
            _controller.Manager.State.Traps.Traps.Clear();

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
        /// The teaching labels are for the opening of the first fight and then they go.
        ///
        /// Two cycles is long enough to run at the sword and swing once with it. Left up they cover
        /// the sand the player has just learned to read - a caption over every trap and pickup is
        /// help on cycle one and an obstruction on cycle five - so the end of it is worth a test
        /// rather than an eyeball: it only shows itself several cycles into a match nobody replays.
        /// </summary>
        /// <summary>
        /// One label per kind, on the nearest one of that kind.
        ///
        /// A caption on every trap and every weapon taught the same two things six times over and
        /// filled the arena doing it. The nearest is the one the player can act on: a label on a
        /// trap across the arena is a fact, one on the trap in front of them is a warning about the
        /// run they are deciding on.
        /// </summary>
        [UnityTest]
        public IEnumerator TheTutorialLabelsOnlyOneOfEachAndTheNearest()
        {
            var tutorial = Object.FindFirstObjectByType<TutorialView>(FindObjectsInactive.Include);
            Assert.IsNotNull(tutorial);

            _controller.SubmitPlayerPick(GladiatorId.Brutius);
            yield return RunSeconds(GameConstants.RevealTime + 0.2f);

            var player = State.P1.Active;
            player.Pos = new Vector2(0f, -200f);

            // Two of each, one beside him and one across the arena, so "nearest" and "first in the
            // list" are different answers and picking the wrong one shows.
            State.Items.Items.Clear();
            State.Items.Items.Add(new ArenaItem { Kind = WeaponKind.TwoHandedMace, Pos = new Vector2(0f, 250f) });
            State.Items.Items.Add(new ArenaItem { Kind = WeaponKind.DualSwords, Pos = new Vector2(20f, -180f) });

            State.Traps.Traps.Clear();
            State.Traps.Traps.Add(new ArenaTrap { Pos = new Vector2(0f, 240f), Armed = true });
            State.Traps.Traps.Add(new ArenaTrap { Pos = new Vector2(-25f, -170f), Armed = true });
            yield return null;

            var shownItems = ActiveHints(tutorial, "ItemHint_");
            var shownTraps = ActiveHints(tutorial, "TrapHint_");

            Assert.AreEqual(1, shownItems.Count, "every weapon on the sand was labelled");
            Assert.AreEqual(1, shownTraps.Count, "every trap on the sand was labelled");

            // The near one, by where the label ended up on screen relative to the far one's world
            // position - read through the same camera the player is looking through.
            var camera = _controller.Arena.ArenaCamera;
            var near = camera.WorldToScreenPoint(_controller.Arena.ToWorld(new Vector2(20f, -180f)));
            var far = camera.WorldToScreenPoint(_controller.Arena.ToWorld(new Vector2(0f, 250f)));

            float toNear = Mathf.Abs(shownItems[0].position.y - near.y);
            float toFar = Mathf.Abs(shownItems[0].position.y - far.y);
            Assert.Less(toNear, toFar, "the weapon label went to the one across the arena");
        }

        private static List<Transform> ActiveHints(TutorialView tutorial, string prefix)
            => tutorial.GetComponentsInChildren<Transform>(true)
                .Where(t => t.name.StartsWith(prefix) && t.gameObject.activeSelf)
                .ToList();

        [UnityTest]
        public IEnumerator TheTutorialLabelsGoAwayAfterTwoCycles()
        {
            var tutorial = Object.FindFirstObjectByType<TutorialView>(FindObjectsInactive.Include);
            Assert.IsNotNull(tutorial, "the HUD should carry a tutorial layer");

            _controller.SubmitPlayerPick(GladiatorId.Brutius);
            yield return RunSeconds(GameConstants.RevealTime + 0.2f);

            Assert.IsTrue(State.Tutorial, "the first fight of a session is the taught one");
            Assert.AreEqual(1, State.Cycle);
            Assert.IsTrue(tutorial.gameObject.activeInHierarchy, "cycle one should be labelled");

            State.Cycle = TutorialView.TutorialCycles;
            yield return null;
            Assert.IsTrue(tutorial.gameObject.activeInHierarchy,
                $"cycle {TutorialView.TutorialCycles} is the last one that carries the labels");

            State.Cycle = TutorialView.TutorialCycles + 1;
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
