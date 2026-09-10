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
    /// The red wedge drawn on the sand in front of the player's gladiator: the ground his weapon
    /// covers.
    ///
    /// A picture of a rule the simulation enforces, so what these check is that the drawing and the
    /// rule are the same shape. There used to be a green arc under it as well - the ground he could
    /// be sent onto - and it went with the limit it drew; NoGreenArcIsLeftBehind makes sure it did.
    /// </summary>
    public class ControlZoneTests
    {
        private const string ScenePath = "Assets/Scenes/Arena.unity";

        private GameController _controller;

        [UnitySetUp]
        public IEnumerator LoadArena()
        {
            yield return SceneManager.LoadSceneAsync(ScenePath, LoadSceneMode.Single);
            yield return null;

            _controller = Object.FindFirstObjectByType<GameController>();
            _controller.SubmitPlayerPick(GladiatorId.Brutius);
            yield return null;
        }

        private IEnumerator RunSeconds(float seconds)
        {
            float spent = 0f;
            while (spent < seconds)
            {
                spent += Time.unscaledDeltaTime;
                yield return null;
            }
        }

        private IEnumerator ReachPlanning()
        {
            yield return RunSeconds(GameConstants.RevealTime + 0.2f);
            Assert.AreEqual(MatchPhase.Planning, _controller.Manager.State.Phase,
                "the rest of this is about what is drawn during planning");
        }

        private Transform Layer(string name)
        {
            var found = Object.FindFirstObjectByType<ArenaView>()
                .GetComponentsInChildren<Transform>(true)
                .FirstOrDefault(t => t.name == name);
            Assert.IsNotNull(found, $"no {name} in the scene");
            return found;
        }

        /// <summary>The furthest any of a layer's vertices sits from the gladiator, in world units.</summary>
        private static float Extent(Transform layer)
        {
            var mesh = layer.GetComponent<MeshFilter>().sharedMesh;
            Assert.IsNotNull(mesh, $"{layer.name} has no mesh");
            return mesh.vertices.Max(v => v.magnitude);
        }

        [UnityTest]
        public IEnumerator TheWedgeIsUpWhileHeIsBeingGivenOrdersAndDownWhileHeRuns()
        {
            yield return ReachPlanning();

            var zone = _controller.ControlZone;
            Assert.IsTrue(zone.IsShowing, "the player has a decision to make and should be able to see it");

            _controller.SubmitPlayerMove(Vector2.up, 1f);
            yield return RunSeconds(GameConstants.PlanningTime + 0.2f);

            Assert.AreEqual(MatchPhase.Action, _controller.Manager.State.Phase);
            Assert.IsFalse(zone.IsShowing,
                "left up while they run it would describe a decision already carried out");
        }

        /// <summary>The red wedge ends where the weapon does.</summary>
        [UnityTest]
        public IEnumerator TheRedWedgeEndsWhereTheWeaponDoes()
        {
            yield return ReachPlanning();

            var player = _controller.Manager.State.P1.Active;
            float expected = _controller.Arena.ScaleLength(player.WeaponDef.Reach);

            Assert.AreEqual(expected, Extent(Layer("StrikeZone")), 0.01f);
        }

        /// <summary>
        /// The wedge is struck about the way he is looking. Pinned by turning him and reading the
        /// drawing back - a wedge bolted to the arena would promise blows in a direction he is not
        /// facing.
        /// </summary>
        [UnityTest]
        public IEnumerator TheWedgePointsWhereHeIsLooking()
        {
            yield return ReachPlanning();

            var player = _controller.Manager.State.P1.Active;
            player.Facing = new Vector2(1f, 0f);
            yield return null;

            var root = Layer("ControlZone");
            Assert.AreEqual(0f, Vector3.Angle(root.forward, Vector3.right), 0.5f,
                "he is looking down positive X and the wedge should be too");

            player.Facing = new Vector2(0f, -1f);
            yield return null;

            Assert.AreEqual(0f, Vector3.Angle(root.forward, Vector3.back), 0.5f,
                "and it follows him round rather than staying where it was");
        }

        /// <summary>
        /// The green arc is gone, and gone from the scene rather than merely hidden - a zone left
        /// in the hierarchy is a zone the next change can switch back on by accident, drawing a
        /// limit the simulation no longer has.
        /// </summary>
        [UnityTest]
        public IEnumerator NoGreenArcIsLeftBehind()
        {
            yield return ReachPlanning();

            var names = Object.FindFirstObjectByType<ArenaView>()
                .GetComponentsInChildren<Transform>(true)
                .Select(t => t.name)
                .ToList();

            CollectionAssert.DoesNotContain(names, "MoveZone");
            CollectionAssert.DoesNotContain(names, "MoveZoneEdge");
        }
    }
}
