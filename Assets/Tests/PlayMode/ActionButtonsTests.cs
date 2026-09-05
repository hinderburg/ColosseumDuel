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
    /// The action buttons follow the player's gladiator and only exist while a decision is still
    /// open. Checked by what they do to the scene, not by whether the code ran.
    /// </summary>
    public class ActionButtonsTests
    {
        private const string ScenePath = "Assets/Scenes/Arena.unity";

        private GameController _controller;
        private ActionButtonsView _buttons;

        [UnitySetUp]
        public IEnumerator LoadArenaAndReachPlanning()
        {
            yield return SceneManager.LoadSceneAsync(ScenePath, LoadSceneMode.Single);
            yield return null;

            _controller = Object.FindFirstObjectByType<GameController>();
            _buttons = Object.FindFirstObjectByType<ActionButtonsView>(FindObjectsInactive.Include);
            Assert.IsNotNull(_buttons, "the HUD should have built the action buttons");

            _controller.SubmitPlayerPick(GladiatorId.Brutius);
            yield return RunSeconds(GameConstants.RevealTime + 0.2f);
            Assert.AreEqual(MatchPhase.Planning, _controller.Manager.State.Phase);
        }

        private MatchState State => _controller.Manager.State;

        private RectTransform RectOf(Button button) => (RectTransform)button.transform;

        private Image RageGauge => _buttons.GetComponentsInChildren<Image>(true)
            .First(i => i.name == "RageGauge");

        // ------------------------------------------------------------------

        [UnityTest]
        public IEnumerator TheButtonsOnlyExistWhileTheDecisionIsStillOpen()
        {
            Assert.IsTrue(_buttons.gameObject.activeInHierarchy,
                "during Planning the player still has a choice to make");

            yield return RunSeconds(GameConstants.PlanningTime + 0.2f);

            Assert.AreEqual(MatchPhase.Action, State.Phase);
            Assert.IsFalse(_buttons.gameObject.activeInHierarchy,
                "in the action phase the decision is already made - the buttons are just clutter");
        }

        [UnityTest]
        public IEnumerator DefendSitsAboveLeftOfTheGladiatorAndAbilityAboveRight()
        {
            yield return null;

            var defend = RectOf(_buttons.Defend).anchoredPosition;
            var ability = RectOf(_buttons.Ability).anchoredPosition;

            Assert.Less(defend.x, ability.x, "defend goes on the left, ability on the right");
            Assert.AreEqual(defend.y, ability.y, 0.01f, "both sit at the same height");
        }

        [UnityTest]
        public IEnumerator TheButtonsFollowTheGladiatorAcrossTheArena()
        {
            yield return null;
            var before = RectOf(_buttons.Ability).anchoredPosition;

            State.P1.Active.Pos = new Vector2(GameConstants.ArenaRadius * 0.5f, 0f);
            yield return null;

            Assert.Greater(Vector2.Distance(RectOf(_buttons.Ability).anchoredPosition, before), 20f,
                "moving the gladiator should move his buttons with him");
        }

        [UnityTest]
        public IEnumerator TheAbilityIsDimAndLockedUntilTheRageMeterIsFull()
        {
            var group = _buttons.Ability.GetComponent<CanvasGroup>();

            State.P1.Active.Rage = 0.4f;
            yield return null;
            Assert.IsFalse(_buttons.Ability.interactable, "a partly charged ability cannot be used");
            Assert.Less(group.alpha, 0.6f, "and should read as unavailable, not merely greyed");

            State.P1.Active.Rage = GameConstants.RageMax;
            yield return null;
            Assert.IsTrue(_buttons.Ability.interactable);
            Assert.AreEqual(1f, group.alpha, 0.01f, "a ready ability is fully opaque");
        }

        [UnityTest]
        public IEnumerator TheRingAroundTheAbilityTracksTheRageMeter()
        {
            // Regression guard of the kind this project keeps needing: Image.Type.Filled ignores
            // fillAmount outright when the Image has no sprite, and draws permanently full.
            var gauge = RageGauge;
            Assert.IsNotNull(gauge.sprite, "the radial gauge needs a sprite or fillAmount does nothing");
            Assert.AreEqual(Image.Type.Filled, gauge.type);
            Assert.AreEqual(Image.FillMethod.Radial360, gauge.fillMethod);

            State.P1.Active.Rage = 0f;
            yield return null;
            Assert.AreEqual(0f, gauge.fillAmount, 0.01f);

            State.P1.Active.Rage = GameConstants.RageMax * 0.5f;
            yield return null;
            Assert.AreEqual(0.5f, gauge.fillAmount, 0.01f);

            State.P1.Active.Rage = GameConstants.RageMax;
            yield return null;
            Assert.AreEqual(1f, gauge.fillAmount, 0.01f);
        }

        [UnityTest]
        public IEnumerator TheAbilityButtonNamesTheAbilityItWillFire()
        {
            yield return null;
            var label = _buttons.Ability.GetComponentInChildren<Text>();
            Assert.AreEqual(GladiatorDef.Brutius.AbilityName, label.text,
                "the button should say what it actually does, and that differs per gladiator");
        }

        private static IEnumerator RunSeconds(float seconds)
        {
            float t = 0f;
            while (t < seconds) { yield return null; t += Time.deltaTime; }
        }
    }
}
