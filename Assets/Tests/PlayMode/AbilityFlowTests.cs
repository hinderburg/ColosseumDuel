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
    /// The ability is a supplement to a turn, not a turn of its own. These drive it the way the
    /// buttons do, in both orders, because the order the player happens to press things in is not
    /// something they should have to know about.
    /// </summary>
    public class AbilityFlowTests
    {
        private const string ScenePath = "Assets/Scenes/Arena.unity";

        private GameController _controller;
        private PlayerInputController _input;

        [UnitySetUp]
        public IEnumerator LoadArena()
        {
            yield return SceneManager.LoadSceneAsync(ScenePath, LoadSceneMode.Single);
            yield return null;
            _controller = Object.FindFirstObjectByType<GameController>();
            _input = Object.FindFirstObjectByType<PlayerInputController>();

            _controller.SubmitPlayerPick(GladiatorId.Hilius);
            yield return RunSeconds(GameConstants.RevealTime + 0.2f);
            _controller.Manager.State.P1.Active.Rage = GameConstants.RageMax;
            yield return null;
        }

        private GladiatorInstance Player => _controller.Manager.State.P1.Active;

        [UnityTest]
        public IEnumerator ArmingBeforeTheMoveFires()
        {
            _input.ToggleAbility();
            _input.TryBeginDrag(Player.Pos);
            _input.ReleaseDrag(Player.Pos + new Vector2(0f, -GameConstants.MaxDragVirtual));

            yield return RunSeconds(GameConstants.PlanningTime + 0.2f);
            Assert.IsTrue(Player.Buff.IsActive, "armed then moved should fire");
        }

        [UnityTest]
        public IEnumerator ArmingAfterTheMoveAlsoFires()
        {
            _input.TryBeginDrag(Player.Pos);
            _input.ReleaseDrag(Player.Pos + new Vector2(0f, -GameConstants.MaxDragVirtual));
            _input.ToggleAbility();

            yield return RunSeconds(GameConstants.PlanningTime + 0.2f);
            Assert.IsTrue(Player.Buff.IsActive, "moved then armed should fire too");
        }

        [UnityTest]
        public IEnumerator ArmingWithNoMoveAtAllStillFires()
        {
            _input.ToggleAbility();

            yield return RunSeconds(GameConstants.PlanningTime + 0.2f);
            Assert.IsTrue(Player.Buff.IsActive, "the ability is not a substitute for a move");
        }

        private static IEnumerator RunSeconds(float seconds)
        {
            float t = 0f;
            while (t < seconds) { yield return null; t += Time.unscaledDeltaTime; }
        }
    }
}
