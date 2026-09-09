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
    /// The numbers that fly off a gladiator when something costs him health.
    ///
    /// Every source gets one, which is the whole point of them: a player who is suddenly down forty
    /// wants to know what took it, and a blow, a trap, a wound and the spikes are four different
    /// answers.
    /// </summary>
    public class DamageNumberTests
    {
        private const string ScenePath = "Assets/Scenes/Arena.unity";

        private GameController _controller;
        private DamageNumbersView _numbers;

        [UnitySetUp]
        public IEnumerator LoadArena()
        {
            yield return SceneManager.LoadSceneAsync(ScenePath, LoadSceneMode.Single);
            yield return null;

            _controller = Object.FindFirstObjectByType<GameController>();
            _numbers = Object.FindFirstObjectByType<DamageNumbersView>();
            Assert.IsNotNull(_numbers, "the HUD must build the damage numbers");

            Object.FindFirstObjectByType<MenuView>().StartMatch();
            _controller.SubmitPlayerPick(GladiatorId.Barbarius);
            yield return RunSeconds(GameConstants.RevealTime + 0.2f);
        }

        /// <summary>Every label that is currently in the air, with what it says.</summary>
        private Text[] Showing()
            => _numbers.GetComponentsInChildren<Text>(true)
                .Where(t => t.gameObject.activeInHierarchy && !string.IsNullOrEmpty(t.text))
                .ToArray();

        [UnityTest]
        public IEnumerator ABlowSendsUpItsOwnNumber()
        {
            Assert.IsEmpty(Showing(), "nothing should be in the air before anyone is hit");

            _numbers.Show(Vector2.zero, 37f, DamageNumbersView.Source.Blow);
            yield return null;

            var up = Showing();
            Assert.AreEqual(1, up.Length);
            Assert.AreEqual("37", up[0].text, "the number is what he lost, rounded");
        }

        /// <summary>
        /// It rises and goes out on its own.
        ///
        /// Both halves matter: one that never moved would be a label stuck to the arena, and one
        /// that never went out would leave the screen covered in old numbers by the third round.
        /// </summary>
        [UnityTest]
        public IEnumerator ANumberClimbsAndThenGoesOut()
        {
            _numbers.Show(Vector2.zero, 20f, DamageNumbersView.Source.Blow);
            yield return null;

            var label = Showing().Single();
            float startedAt = label.rectTransform.anchoredPosition.y;

            yield return RunSeconds(0.35f);
            Assert.Greater(label.rectTransform.anchoredPosition.y, startedAt + 5f,
                "the number never left the spot it was drawn at");

            yield return RunSeconds(1.2f);
            Assert.IsEmpty(Showing(), "the number never went out");
        }

        /// <summary>
        /// The four sources are told apart by colour, so the reason reads without a legend.
        /// </summary>
        [UnityTest]
        public IEnumerator EachSourceHasItsOwnColour()
        {
            var seen = new System.Collections.Generic.List<Color>();

            foreach (DamageNumbersView.Source source in
                     System.Enum.GetValues(typeof(DamageNumbersView.Source)))
            {
                _numbers.Show(Vector2.zero, 12f, source);
                yield return null;

                var label = Showing().Last();
                foreach (var already in seen)
                    Assert.AreNotEqual(already, label.color, $"{source} shares a colour with another source");
                seen.Add(label.color);
            }

            Assert.AreEqual(4, seen.Count);
        }

        /// <summary>
        /// A real exchange puts a number up, through the game's own event.
        ///
        /// The half that actually breaks is the wiring, not the view: Show works whether or not
        /// anything ever calls it, and an event left unhooked is silent.
        /// </summary>
        [UnityTest]
        public IEnumerator ABlowStruckInTheMatchIsReported()
        {
            var state = _controller.Manager.State;

            // Standing inside each other's reach, so the exchange lands on the first substep rather
            // than after a charge across the arena.
            float gap = Mathf.Min(state.P1.Active.WeaponDef.Reach, state.Bot.Active.WeaponDef.Reach) * 0.7f;
            state.P1.Active.Pos = new Vector2(-gap * 0.5f, 0f);
            state.Bot.Active.Pos = new Vector2(gap * 0.5f, 0f);


            // Squared up on each other. A blow only lands inside the swinger's own arc now, and a

            // gladiator faces the way he last ran - so set down across the short axis of an arena they

            // spawned along, these two would be looking past one another with nobody in reach.

            state.P1.Active.Facing = Vector2.right;
            state.Bot.Active.Facing = Vector2.left;

            _controller.Manager.SubmitPlanningAction(PlayerSide.P1, ActionType.Defend, Vector2.zero, 0f, false);
            _controller.Manager.SubmitPlanningAction(PlayerSide.Bot, ActionType.Defend, Vector2.zero, 0f, false);

            float hpBefore = state.Bot.Active.Hp;
            yield return RunUntil(() => state.Bot.Active.Hp < hpBefore, 8f);

            // Waited for rather than read on the frame the health drops. Two fighters already inside
            // each other's reach exchange on the first substep of the phase, which leaves no room in
            // front of it to wind a swing up - so the number is held back to arrive with the weapon
            // rather than ahead of it.
            yield return RunUntil(() => Showing().Any(), 1f);

            Assert.IsNotEmpty(Showing(), "a blow landed in the match and no number came off him");
        }

        /// <summary>
        /// The spikes put a number up too.
        ///
        /// That it is announced once per phase rather than once per substep is a rule of the
        /// simulation and is asserted there, in HazardReportTests - counting labels on screen cannot
        /// tell one side's number from the other's, and both sides burn once the arena is small
        /// enough.
        /// </summary>
        [UnityTest]
        public IEnumerator APhaseSpentInTheSpikesIsReported()
        {
            var state = _controller.Manager.State;

            // Far enough into the match that the rings are live, and standing in one.
            state.Cycle = 7;
            state.P1.Active.Pos = new Vector2(0f, GameConstants.ArenaRadius * GameConstants.ArenaElongation * 0.92f);

            // The opponent goes dead centre, which the outer ring has not reached.
            state.Bot.Active.Pos = Vector2.zero;

            _controller.Manager.SubmitPlanningAction(PlayerSide.P1, ActionType.Defend, Vector2.zero, 0f, false);
            _controller.Manager.SubmitPlanningAction(PlayerSide.Bot, ActionType.Defend, Vector2.zero, 0f, false);

            yield return RunUntil(() => Showing().Length > 0, 8f);

            var up = Showing();
            Assert.IsNotEmpty(up, "a phase spent in the spikes put no number up");
            Assert.Greater(int.Parse(up[0].text), 1,
                "a whole phase in the fire should cost more than a point - the damage was not totalled");
        }

        private static IEnumerator RunUntil(System.Func<bool> done, float timeout)
        {
            float t = 0f;
            while (t < timeout && !done())
            {
                yield return null;
                t += Time.unscaledDeltaTime;
            }
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
    }
}
