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
        /// A dashed white circle round him at the length of his reach, up while he is being given his
        /// orders and while he carries them out - where his blow lands is worth reading in both - and
        /// kept on him as he runs. The wedge still goes down when the run starts.
        /// </summary>
        [UnityTest]
        public IEnumerator TheReachCircleIsUpThroughPlanningAndActionAtTheLengthOfHisReach()
        {
            yield return ReachPlanning();

            var zone = _controller.ControlZone;
            var player = _controller.Manager.State.P1.Active;
            Assert.IsTrue(zone.IsRingShowing, "the circle should be up while he is given his orders");

            var ring = Layer("ReachRing");
            var vertices = ring.GetComponent<MeshFilter>().sharedMesh.vertices;
            float reach = _controller.Arena.ScaleLength(player.Reach);
            Assert.AreEqual(reach, vertices.Average(v => new Vector2(v.x, v.z).magnitude), 0.01f,
                "the circle should be drawn at the length of his reach");
            Assert.GreaterOrEqual(zone.RingDashes, 12, "the circle should be a dashed line");

            _controller.SubmitPlayerMove(Vector2.up, 1f);
            yield return RunSeconds(GameConstants.PlanningTime + 0.3f);

            Assert.AreEqual(MatchPhase.Action, _controller.Manager.State.Phase);
            Assert.IsTrue(zone.IsRingShowing, "and it should stay up while he runs");
            Assert.IsFalse(zone.IsShowing, "the wedge is still for the planning phase only");

            var centre = Layer("ReachRingRoot").position;
            var him = _controller.Arena.ToWorld(player.Pos);
            Assert.Less(Vector2.Distance(new Vector2(centre.x, centre.z), new Vector2(him.x, him.z)), 0.05f,
                "the circle should go with him");
        }

        /// <summary>The wedge is reddish and fades in towards the edge of his reach, not a flat fill.</summary>
        [Test]
        public void TheWedgeIsReddishAndFades()
        {
            var material = _controller.Arena.Palette.StrikeZone;
            Assert.Greater(material.color.r, 0.9f, "the wedge should be reddish");
            Assert.Greater(material.color.r - material.color.g, 0.3f, "the wedge should be reddish, not white");
            Assert.Greater(material.color.r - material.color.b, 0.3f, "the wedge should be reddish, not white");
            Assert.Greater(material.color.g, 0.2f, "a warm red, lighter than the flat wedge it replaced");
            Assert.IsNotNull(material.GetTexture("_BaseMap"), "the wedge should carry its fade");
        }

        /// <summary>
        /// Lunge reaches half as far again, and the wedge says so: while it is armed for the round
        /// about to be played - which is when it is being decided on - and while it is working. It
        /// was drawn the weapon's length through both.
        /// </summary>
        [UnityTest]
        public IEnumerator TheWedgeReachesAsFarAsALunge()
        {
            yield return ReachPlanning();

            var player = _controller.Manager.State.P1.Active;
            player.Ability = AbilityKey.Lunge;
            float plain = _controller.Arena.ScaleLength(player.WeaponDef.Reach);
            float lunge = _controller.Arena.ScaleLength(player.WeaponDef.Reach * GameConstants.LungeReachMult);

            player.Rage = GameConstants.RageMax;
            Assert.IsTrue(_controller.Manager.SubmitPlanningAction(PlayerSide.P1, ActionType.Defend, Vector2.zero, 0f, true));
            Assert.IsTrue(player.AbilityArmed, "the rage is full, and the ability should have armed");
            yield return null;
            Assert.AreEqual(lunge, Extent(Layer("StrikeZone")), 0.01f, "armed, the wedge should reach as far as the lunge will");

            player.AbilityArmed = false;
            player.Buff = new ActiveBuff { Key = AbilityKey.Lunge, RoundsLeft = 2 };
            yield return null;
            Assert.AreEqual(lunge, Extent(Layer("StrikeZone")), 0.01f, "working, the wedge should reach as far as it does");

            player.Buff = default;
            yield return null;
            Assert.AreEqual(plain, Extent(Layer("StrikeZone")), 0.01f, "and back to the weapon's length once it is over");
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
