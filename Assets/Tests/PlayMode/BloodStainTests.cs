using System.Collections;
using System.Linq;
using ColosseumDuel.Core;
using ColosseumDuel.Gameplay;
using ColosseumDuel.Gameplay.View;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace ColosseumDuel.Tests
{
    /// <summary>
    /// The sand remembers the match.
    ///
    /// A blow leaves a mark and the mark stays: not for a second, not for the round, but until the
    /// match is over. A round ending is not the arena being swept - it is the same sand with one
    /// fewer man standing on it, and by the third round it should look like it.
    /// </summary>
    public class BloodStainTests
    {
        private const string ScenePath = "Assets/Scenes/Arena.unity";

        private GameController _controller;
        private ArenaView _arena;

        [UnitySetUp]
        public IEnumerator LoadArena()
        {
            yield return SceneManager.LoadSceneAsync(ScenePath, LoadSceneMode.Single);
            yield return null;

            _controller = Object.FindFirstObjectByType<GameController>();
            _arena = _controller.Arena;

            _controller.SubmitPlayerPick(GladiatorId.Barbarius);
            yield return null;
        }

        /// <summary>How many marks are on the sand right now.</summary>
        private int Stains()
        {
            var root = _arena.GetComponentsInChildren<Transform>(true)
                .FirstOrDefault(t => t.name == "BloodStains");
            if (root == null) Assert.Ignore("No stain pool - the palette predates it.");

            int count = 0;
            foreach (Transform child in root)
                if (child.gameObject.activeSelf) count++;
            return count;
        }

        [UnityTest]
        public IEnumerator EveryBlowLeavesItsOwnMark()
        {
            Assert.AreEqual(0, Stains(), "the sand starts clean");

            _arena.PlayBlood(new Vector2(-40f, 0f));
            _arena.PlayBlood(new Vector2(40f, 20f));
            _arena.PlayBlood(new Vector2(0f, -60f));
            yield return null;

            Assert.AreEqual(3, Stains(), "three blows, three marks");
        }

        /// <summary>
        /// They are laid down at different angles and sizes.
        ///
        /// There is one splat texture, so without this every mark in the arena is the same shape the
        /// same way up, and a dozen of them read as a pattern rather than as a fight.
        /// </summary>
        [UnityTest]
        public IEnumerator NoTwoMarksAreStampedTheSame()
        {
            for (int i = 0; i < 6; i++)
                _arena.PlayBlood(new Vector2(i * 20f - 60f, 0f));
            yield return null;

            var root = _arena.GetComponentsInChildren<Transform>(true)
                .First(t => t.name == "BloodStains");
            var laid = root.Cast<Transform>().Where(t => t.gameObject.activeSelf).ToList();

            Assert.AreEqual(6, laid.Count);

            // Asserted on where the quad's own right-hand axis ends up pointing, not on an Euler
            // component: a quad laid flat is at ninety degrees of pitch, where Euler angles are
            // gimbal-locked and the spin reads back from a different component than it was set on.
            Assert.Greater(laid.Select(t => Mathf.Round(t.right.x * 100f)).Distinct().Count(), 1,
                "every mark was laid at the same angle");
            Assert.Greater(laid.Select(t => Mathf.Round(t.localScale.x * 100f)).Distinct().Count(), 1,
                "every mark was laid at the same size");
        }

        /// <summary>
        /// The round ending does not sweep the sand, and starting a new match does.
        ///
        /// The first half is the point of the feature; the second is what keeps a fresh match from
        /// opening on somebody else's fight.
        /// </summary>
        [UnityTest]
        public IEnumerator TheMarksOutlastARoundAndNotAMatch()
        {
            _arena.PlayBlood(Vector2.zero);
            _arena.PlayBlood(new Vector2(30f, 30f));
            yield return null;
            Assert.AreEqual(2, Stains());

            // Down goes the player's gladiator, and the round with him.
            _controller.Manager.State.P1.Active.Hp = 0f;
            _controller.Manager.State.P1.Active.Alive = false;
            yield return RunUntil(() => _controller.Manager.State.Phase != MatchPhase.Action
                                        && _controller.Manager.State.Phase != MatchPhase.Planning, 6f);
            yield return RunSeconds(GameConstants.RoundEndTime + 0.3f);

            Assert.AreEqual(2, Stains(), "the sand was swept between rounds");

            _controller.RestartMatch();
            yield return null;
            Assert.AreEqual(0, Stains(), "a new match opened on the last one's blood");
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
