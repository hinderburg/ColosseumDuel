using System.Collections;
using System.Collections.Generic;
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
    /// Body colour identifies the archetype, helmet colour identifies the side. Both have to hold
    /// as gladiators are swapped between rounds.
    /// </summary>
    public class GladiatorFigureTests
    {
        private const string ScenePath = "Assets/Scenes/Arena.unity";

        private GameController _controller;

        [UnitySetUp]
        public IEnumerator LoadArena()
        {
            yield return SceneManager.LoadSceneAsync(ScenePath, LoadSceneMode.Single);
            yield return null;
            _controller = Object.FindFirstObjectByType<GameController>();
            Assert.IsNotNull(_controller);
        }

        private Transform FindIn(string viewName, string childName)
            => _controller.GetComponentsInChildren<Transform>(true)
                .FirstOrDefault(t => t.name == viewName)
                ?.GetComponentsInChildren<Transform>(true)
                .FirstOrDefault(t => t.name == childName);

        [UnityTest]
        public IEnumerator EachArchetypeHasItsOwnFigureAndOnlyTheFighterIsShown()
        {
            _controller.SubmitPlayerPick(GladiatorId.Barbarius);
            yield return RunSeconds(GameConstants.RevealTime + 0.2f);

            foreach (var def in GladiatorDef.All)
            {
                var figure = FindIn("Player", $"Figure_{def.Id}");
                Assert.IsNotNull(figure, $"no figure built for {def.Name}");

                bool shouldShow = def.Id == GladiatorId.Barbarius;
                Assert.AreEqual(shouldShow, figure.gameObject.activeSelf,
                    $"{def.Name} should {(shouldShow ? "be" : "not be")} on the arena");
            }
        }

        [UnityTest]
        public IEnumerator TheBodyCarriesTheArchetypeColourAndTheHelmetTheSide()
        {
            _controller.SubmitPlayerPick(GladiatorId.Hilius);
            yield return RunSeconds(GameConstants.RevealTime + 0.2f);

            var palette = _controller.Arena.Palette;

            foreach (var def in GladiatorDef.All)
            {
                var figure = FindIn("Player", $"Figure_{def.Id}");

                // Everything outside the helmet. Picked by where it sits rather than by its name:
                // the helmet is a model now and its renderer is a child called something else, so
                // "not the one called Helmet" quietly started matching the helmet. Searched through
                // the whole figure because the helmet is worn on the head bone, several levels down.
                var helmetRoot = figure.GetComponentsInChildren<Transform>(true)
                    .First(t => t.name == "Helmet");
                var body = figure.GetComponentsInChildren<Renderer>(true)
                    .First(r => helmetRoot == null || !r.transform.IsChildOf(helmetRoot));

                Assert.AreSame(palette.BodyMaterialFor(def.Id), body.sharedMaterial,
                    $"{def.Name}'s body should carry his own archetype colour");
            }

            // Helmets say who owns the gladiator, which is what keeps two of the same archetype
            // apart when both sides field one.
            var playerHelmet = FindIn("Player", "Helmet").GetComponentInChildren<Renderer>(true);
            var botHelmet = FindIn("Bot", "Helmet").GetComponentInChildren<Renderer>(true);
            Assert.IsNotNull(playerHelmet, "the helmet has no renderer - nothing would be drawn");

            Assert.AreSame(palette.PlayerHelmet, playerHelmet.sharedMaterial);
            Assert.AreSame(palette.BotHelmet, botHelmet.sharedMaterial);
            Assert.AreNotSame(playerHelmet.sharedMaterial, botHelmet.sharedMaterial);
        }

        /// <summary>
        /// A weapon is the same size in the fist as it was on the sand.
        ///
        /// It used to shrink: the carried copy was scaled by a factor of its own on top of whatever
        /// the hand bone inherited, so the two-hander the player crossed the arena for arrived
        /// visibly smaller than the thing they had been looking at, and the one signal that said
        /// which weapon they were carrying was gone the moment they had it.
        ///
        /// Measured off the holder's world scale rather than off a bounding box: every gear prefab
        /// is one unit along its blade, so the scale is the length, and a bounding box on a sword
        /// held at an angle measures the diagonal instead.
        /// </summary>
        [UnityTest]
        public IEnumerator ACarriedWeaponIsTheSizeItWasOnTheSand()
        {
            _controller.SubmitPlayerPick(GladiatorId.Hilius);
            yield return RunSeconds(GameConstants.RevealTime + 0.2f);

            var held = FindIn("Player", "HeldWeapon");
            if (held == null) Assert.Ignore("No gear models imported; nothing is carried.");

            var g = _controller.Manager.State.P1.Active;

            g.Weapon = WeaponKind.DualSwords;
            yield return null;
            Assert.AreEqual(GearSizes.SwordLength, held.lossyScale.y, 0.02f,
                "a carried sword should be exactly as long as the one lying on the sand");

            g.Weapon = WeaponKind.TwoHandedMace;
            yield return null;
            Assert.AreEqual(GearSizes.MaceLength, held.lossyScale.y, 0.02f,
                "a carried mace should be exactly as long as the one lying on the sand");

            Assert.Greater(GearSizes.MaceLength, GearSizes.SwordLength,
                "the mace has to be the bigger of the two or nothing distinguishes them");
        }

        [UnityTest]
        public IEnumerator TheThreeArchetypesHaveDifferentBuilds()
        {
            // Colour alone was doing this job, and colour is also what the danger rings, the hazard
            // and the two side helmets are using. A broad figure and a small thin one are legible
            // past all of that, and from further away.
            _controller.SubmitPlayerPick(GladiatorId.Barbarius);
            yield return RunSeconds(GameConstants.RevealTime + 0.2f);

            var widths = new List<float>();
            var heights = new List<float>();

            foreach (var def in GladiatorDef.All)
            {
                var figure = FindIn("Player", $"Figure_{def.Id}");
                Assert.IsNotNull(figure, $"no figure for {def.Name}");
                Assert.AreEqual(def.BuildWidth, figure.localScale.x, 0.001f, $"{def.Name} width");
                Assert.AreEqual(def.BuildHeight, figure.localScale.y, 0.001f, $"{def.Name} height");
                Assert.AreEqual(def.BuildWidth, figure.localScale.z, 0.001f,
                    $"{def.Name} is only broad from one side, which is not how a camera works");

                widths.Add(figure.localScale.x);
                heights.Add(figure.localScale.y);
            }

            // Three builds, not three copies of one. Checked here rather than in the stat table
            // because it is a claim about what shows on the arena.
            CollectionAssert.AllItemsAreUnique(widths.Zip(heights, (w, h) => $"{w}x{h}").ToList());
        }

        [UnityTest]
        public IEnumerator AFighterIsThePaintedColourOfHisOwnIcon()
        {
            // The card the player chose from and the figure that walks out have to be the same
            // colour, or the pick screen is teaching them a code the arena does not use.
            _controller.SubmitPlayerPick(GladiatorId.Hilius);
            yield return RunSeconds(GameConstants.RevealTime + 0.2f);

            var palette = _controller.Arena.Palette;
            foreach (var def in GladiatorDef.All)
            {
                var body = palette.BodyMaterialFor(def.Id);
                Assert.IsNotNull(body, $"{def.Name} has no body material");
                Assert.AreEqual(palette.ArchetypeColor(def.Id), body.color,
                    $"{def.Name}'s figure is not the colour of his own icon");
            }
        }

        /// <summary>
        /// A weapon he was never trained in is ringed in red.
        ///
        /// The simulation deliberately lets him pick up anything - the warning is the HUD's job,
        /// because a pickup that silently refused to happen reads as a bug while a red ring reads
        /// as a mistake he made. So the ring is the whole of the feedback, and if it fails to come
        /// up nothing at all tells him.
        /// </summary>
        [UnityTest]
        public IEnumerator AWeaponHeWasNeverTrainedInIsRingedInRed()
        {
            _controller.SubmitPlayerPick(GladiatorId.Brutius);
            yield return RunSeconds(GameConstants.RevealTime + 0.2f);

            var main = FindIn("Player", "HeldWeapon");
            if (main == null) Assert.Ignore("No gear models - the weapon pack is not imported here.");

            var g = _controller.Manager.State.P1.Active;
            Assert.AreEqual(WeaponKind.TwoHandedMace, g.Def.SkilledWith, "Brutius is the mace fighter");

            g.Weapon = WeaponKind.TwoHandedMace;
            yield return null;
            Assert.IsFalse(AnyShellShowing(), "his own weapon should carry no warning");

            g.Weapon = WeaponKind.DualSwords;
            yield return null;
            Assert.IsTrue(AnyShellShowing(), "twin swords are not his, and nothing else says so");

            g.Weapon = WeaponKind.TwoHandedMace;
            yield return null;
            Assert.IsFalse(AnyShellShowing(), "and the warning has to come back off again");
        }

        private bool AnyShellShowing()
            => _controller.GetComponentsInChildren<Transform>(true)
                .Any(t => t.name == ItemView.ShellName && t.gameObject.activeInHierarchy);

        [UnityTest]
        public IEnumerator GildedGearIsGoldInTheHandAndHisOwnIsSteel()
        {
            // Gold is the whole of how the player is told the copies on the sand are worth crossing
            // a mined arena for. Turning it back to steel the moment it was picked up would hide
            // the one thing that trip bought him.
            _controller.SubmitPlayerPick(GladiatorId.Brutius);
            yield return RunSeconds(GameConstants.RevealTime + 0.2f);

            var main = FindIn("Player", "HeldWeapon");
            if (main == null) Assert.Ignore("No gear models - the weapon pack is not imported here.");

            var g = _controller.Manager.State.P1.Active;
            var block = new MaterialPropertyBlock();

            g.WeaponIsGilded = false;
            yield return null;
            Assert.AreEqual(GearSizes.CarriedTint, TintOf(main, block),
                "the weapon he walked in with should be steel");

            g.WeaponIsGilded = true;
            yield return null;
            Assert.AreEqual(GearSizes.GildedTint, TintOf(main, block),
                "the one he took off the sand should stay gold in his hand");
        }

        private static Color TintOf(Transform holder, MaterialPropertyBlock block)
        {
            var renderer = holder.GetComponentsInChildren<Renderer>(true)
                .First(r => r.GetComponentsInParent<Transform>(true).All(t => t.name != ItemView.ShellName));
            renderer.GetPropertyBlock(block);
            return block.GetColor(Shader.PropertyToID("_BaseColor"));
        }

        [UnityTest]
        public IEnumerator TheFigureRunsWhenTheGladiatorDoes()
        {
            _controller.SubmitPlayerPick(GladiatorId.Hilius);
            yield return RunSeconds(GameConstants.RevealTime + 0.2f);

            var animator = FindIn("Player", $"Figure_{GladiatorId.Hilius}")
                ?.GetComponentInChildren<Animator>(true);
            if (animator == null || animator.runtimeAnimatorController == null)
            {
                Assert.Ignore("No animator - the DoubleL pack is not imported here.");
                yield break;
            }

            Assert.AreEqual(0f, animator.GetFloat(AnimatorParams.Speed), 0.01f,
                "nothing has been ordered yet, so he should be standing");

            // A full-power dash across the arena. The parameter is in world units per second, which
            // is not the unit the simulation moves in - getting that conversion wrong leaves a
            // sprinting gladiator sliding along in his idle pose, which is easy to miss on a small
            // figure and impossible to miss once seen.
            _controller.Manager.SubmitPlanningAction(PlayerSide.P1, ActionType.Move, Vector2.up, 1f, false);
            _controller.Manager.SubmitPlanningAction(PlayerSide.Bot, ActionType.Defend, Vector2.zero, 0f, false);
            yield return RunUntil(() => _controller.Manager.State.Phase == MatchPhase.Action, 5f);
            yield return null;

            Assert.Greater(animator.GetFloat(AnimatorParams.Speed), AnimatorParams.RunThreshold,
                "a gladiator at full sprint should be past the run threshold");
        }

        /// <summary>
        /// A blow is actually animated as a swing, on both sides of the exchange.
        ///
        /// It was not. An exchange is simultaneous, so both fighters are struck and both swing on
        /// the same frame; the controller enters Attack and Hit from Any State, a transition
        /// consumes only its own trigger, and the standing Hit trigger replaced the swing one frame
        /// after it started. What played was two gladiators flinching at each other and never
        /// appearing to attack at all - which is exactly what a swing that was never triggered
        /// would have looked like, so this is worth a test rather than an eye.
        /// </summary>
        [UnityTest]
        public IEnumerator BothSidesActuallyPlayASwingWhenBlowsAreExchanged()
        {
            _controller.SubmitPlayerPick(GladiatorId.Brutius);
            yield return RunSeconds(GameConstants.RevealTime + 0.2f);

            var player = FindIn("Player", $"Figure_{GladiatorId.Brutius}")?.GetComponentInChildren<Animator>(true);
            if (player == null || player.runtimeAnimatorController == null)
                Assert.Ignore("No animator - the model pack is not imported here.");

            var state = _controller.Manager.State;
            var bot = FindIn("Bot", $"Figure_{state.Bot.Active.Def.Id}").GetComponentInChildren<Animator>(true);

            // Standing inside each other's reach, so the exchange happens on the first substep of
            // the action phase rather than after a charge across the arena.
            float gap = Mathf.Min(state.P1.Active.WeaponDef.Reach, state.Bot.Active.WeaponDef.Reach) * 0.7f;
            state.P1.Active.Pos = new Vector2(-gap * 0.5f, 0f);
            state.Bot.Active.Pos = new Vector2(gap * 0.5f, 0f);

            _controller.Manager.SubmitPlanningAction(PlayerSide.P1, ActionType.Defend, Vector2.zero, 0f, false);
            _controller.Manager.SubmitPlanningAction(PlayerSide.Bot, ActionType.Defend, Vector2.zero, 0f, false);
            yield return RunUntil(() => _controller.Manager.State.Phase == MatchPhase.Action, 6f);

            // How long the swing is on screen, not merely whether it was entered. "Did it ever
            // reach Attack" passes either way - the swing was always triggered, it was just cut
            // off. Measured: a quarter of a second with the recoil held back, an eighth without,
            // so the threshold sits between the two and the test can actually fail.
            float playerSwing = 0f, botSwing = 0f;
            for (float t = 0f; t < 0.6f; t += Time.unscaledDeltaTime)
            {
                if (InAttack(player)) playerSwing += Time.unscaledDeltaTime;
                if (InAttack(bot)) botSwing += Time.unscaledDeltaTime;
                yield return null;
            }

            Assert.Greater(playerSwing, 0.2f,
                $"the player's swing was on screen for {playerSwing:0.###}s - it was cut short");
            Assert.Greater(botSwing, 0.2f,
                $"the opponent's swing was on screen for {botSwing:0.###}s - it was cut short");
        }

        private static bool InAttack(Animator animator)
        {
            if (animator.GetCurrentAnimatorStateInfo(0).IsName("Attack")) return true;
            return animator.IsInTransition(0) && animator.GetNextAnimatorStateInfo(0).IsName("Attack");
        }

        /// <summary>
        /// The helmet stays on the head through an animation, and through the death in particular.
        ///
        /// It was a child of the figure root, so it never followed the animation at all: it hung at
        /// the spot the head occupies in the bind pose while the model moved underneath it. Dying
        /// made that unmissable, the body going down and the helmet staying in mid-air above
        /// nobody - which is also the moment the camera comes in for a close look at it.
        ///
        /// Measured against the head bone rather than by eye, and across the whole fall rather than
        /// at one instant: a helmet that is right at the start and wrong by the end is exactly the
        /// failure being guarded against.
        /// </summary>
        [UnityTest]
        public IEnumerator TheHelmetRidesTheHeadThroughTheDeathAnimation()
        {
            _controller.SubmitPlayerPick(GladiatorId.Brutius);
            yield return RunSeconds(GameConstants.RevealTime + 0.2f);

            var figure = FindIn("Player", $"Figure_{GladiatorId.Brutius}");
            var animator = figure?.GetComponentInChildren<Animator>(true);
            if (animator == null || animator.runtimeAnimatorController == null)
                Assert.Ignore("No animator - the model pack is not imported here.");

            var head = animator.GetBoneTransform(HumanBodyBones.Head);
            var helmet = figure.GetComponentsInChildren<Transform>(true).First(t => t.name == "Helmet");
            Assert.IsNotNull(head, "the rig has no head bone");

            float atRest = Vector3.Distance(helmet.position, head.position);

            // Down he goes.
            _controller.Manager.State.P1.Active.Hp = 0f;
            _controller.Manager.State.P1.Active.Alive = false;
            yield return null;

            float furthest = 0f;
            for (float t = 0f; t < 1.2f; t += Time.unscaledDeltaTime)
            {
                furthest = Mathf.Max(furthest, Vector3.Distance(helmet.position, head.position));
                yield return null;
            }

            // The helmet sits a little proud of the bone, so the gap is never zero - what matters
            // is that it does not grow while the head moves away underneath it.
            Assert.Less(furthest, atRest + 0.05f,
                $"the helmet drifted {furthest - atRest:0.###} units off the head as he fell");

            // And he really did move, so the check had something to catch.
            Assert.IsTrue(animator.GetBool(AnimatorParams.Dead), "the animator was never told he fell");
        }

        [UnityTest]
        public IEnumerator AFallenGladiatorStaysOnTheSand()
        {
            _controller.SubmitPlayerPick(GladiatorId.Barbarius);
            yield return RunSeconds(GameConstants.RevealTime + 0.2f);

            var view = _controller.GetComponentsInChildren<Transform>(true)
                .First(t => t.name == "Player");

            _controller.Manager.State.P1.Active.Hp = 0f;
            _controller.Manager.State.P1.Active.Alive = false;
            yield return null;

            // He used to vanish on the frame the blow landed, which took the death with him. The
            // round holds for a moment afterwards, and that moment is what the animation is for.
            Assert.IsTrue(view.gameObject.activeSelf, "the body should still be on the arena");

            var animator = view.GetComponentsInChildren<Animator>(true)
                .FirstOrDefault(a => a.gameObject.activeInHierarchy);
            if (animator == null || animator.runtimeAnimatorController == null)
            {
                Assert.Ignore("No animator - the DoubleL pack is not imported here.");
                yield break;
            }

            Assert.IsTrue(animator.GetBool(AnimatorParams.Dead), "the animator was not told he fell");
        }

        [UnityTest]
        public IEnumerator CarriedGearGoesIntoTheRightHands()
        {
            _controller.SubmitPlayerPick(GladiatorId.Brutius);
            yield return RunSeconds(GameConstants.RevealTime + 0.2f);

            var main = FindIn("Player", "HeldWeapon");
            var off = FindIn("Player", "HeldOffHand");
            if (main == null || off == null)
            {
                Assert.Ignore("No gear models - the weapon pack is not imported here.");
                yield break;
            }

            var animator = FindIn("Player", $"Figure_{GladiatorId.Brutius}")
                .GetComponentInChildren<Animator>(true);
            Assert.AreSame(animator.GetBoneTransform(HumanBodyBones.RightHand), main.parent,
                "the weapon belongs in the right hand");
            Assert.AreSame(animator.GetBoneTransform(HumanBodyBones.LeftHand), off.parent,
                "the off hand belongs in the left");

            var g = _controller.Manager.State.P1.Active;

            // Each weapon fills the hands differently, and that is most of how the three read apart
            // on the arena: a blade in each fist, a blade and a shield, or one hammer in both.
            g.Weapon = WeaponKind.DualSwords;
            yield return null;
            Assert.IsTrue(main.gameObject.activeSelf);
            Assert.IsTrue(off.gameObject.activeSelf, "the second sword goes in the off hand");
            Assert.IsTrue(off.Find("Sword").gameObject.activeSelf);
            Assert.IsFalse(off.Find("Shield").gameObject.activeSelf);
            float bladeLength = main.lossyScale.y;

            g.Weapon = WeaponKind.SwordAndShield;
            yield return null;
            Assert.IsTrue(off.Find("Shield").gameObject.activeSelf, "the shield goes in the off hand");
            Assert.IsFalse(off.Find("Sword").gameObject.activeSelf);

            g.Weapon = WeaponKind.TwoHandedMace;
            yield return null;
            Assert.IsFalse(off.gameObject.activeSelf, "both hands are on the haft");
            Assert.IsTrue(main.Find("Mace").gameObject.activeSelf);
            Assert.IsFalse(main.Find("Sword").gameObject.activeSelf);
            Assert.Greater(main.lossyScale.y, bladeLength,
                "the mace has to read as the big weapon in the hand as well as on the sand");

            g.Weapon = WeaponKind.None;
            yield return null;
            Assert.IsFalse(main.gameObject.activeSelf, "empty hands are empty");
            Assert.IsFalse(off.gameObject.activeSelf);
        }


        private static IEnumerator RunUntil(System.Func<bool> done, float maxSeconds)
        {
            float t = 0f;
            while (!done() && t < maxSeconds) { yield return null; t += Time.unscaledDeltaTime; }
            Assert.IsTrue(done(), $"condition not reached within {maxSeconds}s");
        }

        private static IEnumerator RunSeconds(float seconds)
        {
            float t = 0f;
            while (t < seconds) { yield return null; t += Time.unscaledDeltaTime; }
        }
    }
}
