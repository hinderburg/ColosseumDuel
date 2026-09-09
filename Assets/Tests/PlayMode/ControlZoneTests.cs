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
    /// The control drawn on the sand: the green arc of ground he can be sent onto, and the red
    /// wedge his weapon covers.
    ///
    /// Both are pictures of rules the simulation enforces, so what these check is that the drawing
    /// and the rule are the same shape. A zone that flattered the rule would be worse than none:
    /// the player would aim at ground he cannot reach, or expect a blow that never lands.
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
            // Brutius throughout: he is the one with the speed ability, which the last of these
            // needs, and the zone he gets is the widest and shortest in the game - the easiest
            // shape to be wrong about.
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
        public IEnumerator TheZonesAreUpWhileHeIsBeingGivenOrdersAndDownWhileHeRuns()
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

        /// <summary>
        /// The green arc reaches exactly as far as one dash, and no further. Drawn short it hides
        /// ground he could have taken; drawn long it promises ground he cannot.
        /// </summary>
        [UnityTest]
        public IEnumerator TheGreenArcEndsWhereOneDashDoes()
        {
            yield return ReachPlanning();

            var player = _controller.Manager.State.P1.Active;
            float expected = _controller.Arena.ScaleLength(MoveEnvelope.For(player).OuterRadius);

            Assert.AreEqual(expected, Extent(Layer("MoveZone")), 0.01f);
        }

        /// <summary>
        /// And the red wedge ends where the weapon does - inside the green, which is what makes the
        /// two readable as one picture: this is where you may go, and this is what you cover.
        /// </summary>
        [UnityTest]
        public IEnumerator TheRedWedgeEndsWhereTheWeaponDoes()
        {
            yield return ReachPlanning();

            var player = _controller.Manager.State.P1.Active;
            float expected = _controller.Arena.ScaleLength(player.WeaponDef.Reach);

            float drawn = Extent(Layer("StrikeZone"));
            Assert.AreEqual(expected, drawn, 0.01f);
            Assert.Less(drawn, Extent(Layer("MoveZone")), "the strike zone sits inside the run");
        }

        /// <summary>
        /// Both arcs are struck about the way he is looking. Pinned by turning him and reading the
        /// drawing back - a zone bolted to the arena would be a different game entirely, since the
        /// whole rule is that a heading is something he carries.
        /// </summary>
        [UnityTest]
        public IEnumerator TheArcsPointWhereHeIsLooking()
        {
            yield return ReachPlanning();

            var player = _controller.Manager.State.P1.Active;
            player.Facing = new Vector2(1f, 0f);
            yield return null;

            var root = Layer("ControlZone");
            var forward = root.forward;

            Assert.AreEqual(0f, Vector3.Angle(forward, Vector3.right), 0.5f,
                "he is looking down positive X and the arc should be too");

            player.Facing = new Vector2(0f, -1f);
            yield return null;

            Assert.AreEqual(0f, Vector3.Angle(root.forward, Vector3.back), 0.5f,
                "and it follows him round rather than staying where it was");
        }

        /// <summary>
        /// Arming the speed ability lengthens the arc and narrows it, while the player is still
        /// deciding. The ability does not fire until the action phase, so a zone drawn off what he
        /// is rather than off what he is about to be would be a picture of the wrong cycle.
        /// </summary>
        [UnityTest]
        public IEnumerator ArmingTheSpeedAbilityChangesTheZoneBeforeItFires()
        {
            yield return ReachPlanning();

            var picked = _controller.Manager.State.P1.Active;
            Assert.AreEqual(AbilityKey.Spirit, picked.Def.Ability, "this test is about the speed one");

            float before = Extent(Layer("MoveZone"));

            picked.Rage = GameConstants.RageMax;
            _controller.SubmitPlayerAbility(true);
            yield return null;

            Assert.Greater(Extent(Layer("MoveZone")), before * 1.4f,
                "the run he is about to make is half again as long, and the zone should say so");
        }
    }
}
