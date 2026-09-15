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
    /// as gladiators are swapped between clashes.
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
                // His build, grown by the one scale that also sizes his body, gear and reach.
                float scale = GameConstants.GladiatorScale;
                Assert.AreEqual(def.BuildWidth * scale, figure.localScale.x, 0.001f, $"{def.Name} width");
                Assert.AreEqual(def.BuildHeight * scale, figure.localScale.y, 0.001f, $"{def.Name} height");
                Assert.AreEqual(def.BuildWidth * scale, figure.localScale.z, 0.001f,
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
                .Any(t => t.name == GearSizes.ShellName && t.gameObject.activeInHierarchy);

        [UnityTest]
        public IEnumerator ABlessedWeaponTurnsRedAndGlows_ThenYellowForItsLastRound()
        {
            // Red and a glow are the whole of how the player is told his blows are worth more, and
            // yellow is how he is told this is the last turn they will be.
            _controller.SubmitPlayerPick(GladiatorId.Brutius);
            yield return RunSeconds(GameConstants.RevealTime + 0.2f);

            var main = FindIn("Player", "HeldWeapon");
            if (main == null) Assert.Ignore("No gear models - the weapon pack is not imported here.");

            var g = _controller.Manager.State.P1.Active;
            var view = _controller.GetComponentsInChildren<GladiatorView>(true).First(v => v.name == "Player");
            var block = new MaterialPropertyBlock();

            g.WeaponBuffRoundsLeft = 0;
            yield return null;
            Assert.AreEqual(GearSizes.CarriedTint, TintOf(main, block), "the weapon he walked in with should be steel");
            Assert.AreEqual(0f, view.WeaponGlowStrength, 0.0001f, "and not glowing");

            g.WeaponBuffRoundsLeft = GameConstants.WeaponBuffRounds;
            yield return null;
            Assert.AreEqual(GearSizes.BlessedTint, TintOf(main, block), "blessed, it should turn red");
            Assert.IsTrue(main.GetComponentsInChildren<Transform>(true)
                    .Any(t => t.name == GearSizes.GlowShellName && t.gameObject.activeInHierarchy),
                "and a glow should be showing round it");

            float least = 1f, most = 0f;
            for (float t = 0f; t < 1.4f; t += Time.unscaledDeltaTime)
            {
                yield return null;
                least = Mathf.Min(least, view.WeaponGlowStrength);
                most = Mathf.Max(most, view.WeaponGlowStrength);
            }
            Assert.AreEqual(1f, least, 0.001f, "the glow should hold steady while the blessing has rounds to run");

            g.WeaponBuffRoundsLeft = 1;
            least = 1f;
            most = 0f;
            for (float t = 0f; t < 1.4f; t += Time.unscaledDeltaTime)
            {
                yield return null;
                least = Mathf.Min(least, view.WeaponGlowStrength);
                most = Mathf.Max(most, view.WeaponGlowStrength);
            }
            Assert.AreEqual(1f, least, 0.001f, "on its last round the glow should hold steady, not blink");
            Assert.AreEqual(GearSizes.BlessingEndingTint, TintOf(main, block),
                "on its last round the weapon should turn yellow");

            var glowShell = main.GetComponentsInChildren<Transform>(true)
                .First(t => t.name == GearSizes.GlowShellName && t.gameObject.activeInHierarchy);
            glowShell.GetComponentInChildren<Renderer>(true).GetPropertyBlock(block);
            var glowColor = block.GetColor(Shader.PropertyToID("_BaseColor"));
            Assert.AreEqual(GearSizes.BlessingEndingTint.r, glowColor.r, 0.01f, "and its glow yellow with it");
            Assert.AreEqual(GearSizes.BlessingEndingTint.g, glowColor.g, 0.01f, "and its glow yellow with it");
        }

        private static Color TintOf(Transform holder, MaterialPropertyBlock block)
        {
            var renderer = holder.GetComponentsInChildren<Renderer>(true)
                .First(r => !GearSizes.IsUnderShell(r.transform));
            renderer.GetPropertyBlock(block);
            return block.GetColor(Shader.PropertyToID("_BaseColor"));
        }

        /// <summary>
        /// While an ability lasts it glows at his feet in its own colour: steady, then blinking slowly
        /// through its last round - the weapon blessing's rule. A net glows on the man it caught.
        /// </summary>
        [UnityTest]
        public IEnumerator ALastingAbilityGlowsSteadyThenBlinksThroughItsLastRound()
        {
            _controller.SubmitPlayerPick(_controller.Squad[0]);
            yield return RunSeconds(GameConstants.RevealTime + 0.2f);

            var g = _controller.Manager.State.P1.Active;
            var view = _controller.GetComponentsInChildren<GladiatorView>(true).First(v => v.name == "Player");
            var aura = FindIn("Player", "AbilityRing");
            Assert.IsNotNull(aura, "there is no aura at his feet");

            g.Buff = default;
            yield return null;
            Assert.AreEqual(0f, view.AbilityAuraStrength, 0.0001f);
            Assert.IsFalse(aura.gameObject.activeInHierarchy, "an aura with no ability running");

            g.Buff = new ActiveBuff { Key = AbilityKey.Bulwark, RoundsLeft = 2 };
            float least = 1f, most = 0f;
            for (float t = 0f; t < 1.4f; t += Time.unscaledDeltaTime)
            {
                yield return null;
                least = Mathf.Min(least, view.AbilityAuraStrength);
                most = Mathf.Max(most, view.AbilityAuraStrength);
            }
            Assert.IsTrue(aura.gameObject.activeInHierarchy);
            Assert.AreEqual(1f, least, 0.001f, "it should glow steady while it has rounds to run");

            g.Buff = new ActiveBuff { Key = AbilityKey.Bulwark, RoundsLeft = 1 };
            least = 1f;
            most = 0f;
            for (float t = 0f; t < 1.4f; t += Time.unscaledDeltaTime)
            {
                yield return null;
                least = Mathf.Min(least, view.AbilityAuraStrength);
                most = Mathf.Max(most, view.AbilityAuraStrength);
            }
            Assert.Less(least, 0.4f, "on its last round it should fade right down as it blinks");
            Assert.Greater(most, 0.9f, "and come back up again");

            // Second Wind is spent the moment it fires: nothing to glow for.
            g.Buff = new ActiveBuff { Key = AbilityKey.SecondWind, RoundsLeft = 2 };
            yield return null;
            Assert.AreEqual(0f, view.AbilityAuraStrength, 0.0001f, "an instant heal left an aura behind");

            // A net glows on the man it landed on.
            g.Buff = default;
            g.EnsnaredRoundsLeft = 2;
            yield return null;
            Assert.AreEqual(1f, view.NetAuraStrength, 0.0001f, "a netted man should show it");
            g.EnsnaredRoundsLeft = 0;
            yield return null;
            Assert.AreEqual(0f, view.NetAuraStrength, 0.0001f);
        }

        /// <summary>
        /// While an ability lasts its effect plays on the man - a shield round Bulwark, and so on - and
        /// a net plays on the man it caught. Off again when it ends.
        /// </summary>
        [UnityTest]
        public IEnumerator ALastingAbilityPlaysItsEffectOnTheMan_AndANetOnTheManItCaught()
        {
            _controller.SubmitPlayerPick(_controller.Squad[0]);
            yield return RunSeconds(GameConstants.RevealTime + 0.2f);
            if (_controller.Arena.Palette.AbilityFxFor(AbilityKey.Bulwark) == null)
                Assert.Ignore("No effect prefabs - Epic Toon FX is not imported here.");

            var g = _controller.Manager.State.P1.Active;
            var bulwark = FindIn("Player", "AbilityFx_Bulwark");
            var net = FindIn("Player", "NetFx");
            Assert.IsNotNull(bulwark, "there is no Bulwark effect on the man");
            Assert.IsNotNull(net, "there is no net effect on the man");

            g.Buff = default;
            g.EnsnaredRoundsLeft = 0;
            yield return null;
            Assert.IsFalse(bulwark.gameObject.activeSelf, "an effect is playing with no ability running");

            g.Buff = new ActiveBuff { Key = AbilityKey.Bulwark, RoundsLeft = 2 };
            yield return null;
            Assert.IsTrue(bulwark.gameObject.activeSelf, "Bulwark is up and nothing on him shows it");

            g.Buff = default;
            g.EnsnaredRoundsLeft = 2;
            yield return null;
            Assert.IsFalse(bulwark.gameObject.activeSelf, "the effect should end with the ability");
            Assert.IsTrue(net.gameObject.activeSelf, "a netted man should show the net");

            g.EnsnaredRoundsLeft = 0;
            yield return null;
            Assert.IsFalse(net.gameObject.activeSelf);
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

            // A moment into the run: the speed the legs are driven by is smoothed over a few frames.
            yield return RunWorldSeconds(0.15f);

            Assert.Greater(animator.GetFloat(AnimatorParams.Speed), AnimatorParams.RunThreshold,
                "a gladiator at full sprint should be past the run threshold");
        }

        /// <summary>Moves him by hand at this velocity, in virtual units a second of world time, for this long.</summary>
        private static IEnumerator MoveFor(GladiatorInstance g, Vector2 velocity, float worldSeconds)
        {
            float spent = 0f;
            while (spent < worldSeconds)
            {
                yield return null;
                g.Pos += velocity * Time.deltaTime;
                spent += Time.deltaTime;
            }
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

            // Squared up on each other. A blow only lands inside the swinger's own arc now, and a
            // gladiator faces the way he last ran - so set down across the short axis of an arena they
            // spawned along, these two would be looking past one another with nobody in reach.

            state.P1.Active.Facing = Vector2.right;
            state.Bot.Active.Facing = Vector2.left;

            _controller.Manager.SubmitPlanningAction(PlayerSide.P1, ActionType.Defend, Vector2.zero, 0f, false);
            _controller.Manager.SubmitPlanningAction(PlayerSide.Bot, ActionType.Defend, Vector2.zero, 0f, false);
            yield return RunUntil(() => _controller.Manager.State.Phase == MatchPhase.Action, 6f);

            // How far into the swing each of them gets, not merely whether it was entered. "Did it
            // ever reach Attack" passes either way - the swing was always triggered, it was just cut
            // off one frame later by the recoil of the blow coming back.
            //
            // Read off the clip's own progress rather than by counting frames spent in the state.
            // Counting frames measures the machine as much as the animation: a whole swing fits
            // between two frames on a loaded batch run, and the test then fails when the suite is
            // busy and passes when run on its own. One sample late in the clip proves the swing was
            // not cut, however few samples there were.
            float playerSwing = 0f, botSwing = 0f, playerClip = 0f, botClip = 0f;
            for (float t = 0f; t < 0.8f; t += Time.unscaledDeltaTime)
            {
                playerSwing = Mathf.Max(playerSwing, SwingProgress(player));
                botSwing = Mathf.Max(botSwing, SwingProgress(bot));
                playerClip = Mathf.Max(playerClip, SwingLength(player));
                botClip = Mathf.Max(botClip, SwingLength(bot));
                yield return null;
            }

            // Not the whole clip: the recoil is held off for SwingHoldsOffTheRecoil and then allowed
            // through, so the swing is meant to be cut - just not on the frame it started. The hold
            // is a fixed time, so the share of the swing it buys depends on the clip: about 0.23 of
            // the two-handed one and 0.18 of the longer one-handed one, against about half that
            // without the hold. Three quarters of what the hold buys sits between the two for
            // either clip, where one fixed number only ever fitted the clip it was measured on.
            Assert.Greater(playerSwing, SwingFloor(playerClip),
                $"the player's swing reached {playerSwing:0.##} of its {playerClip:0.##}s clip - it was cut short");
            Assert.Greater(botSwing, SwingFloor(botClip),
                $"the opponent's swing reached {botSwing:0.##} of its {botClip:0.##}s clip - it was cut short");
        }

        /// <summary>How long the swing this animator is playing lasts, in seconds, or zero if it is not swinging.</summary>
        private static float SwingLength(Animator animator)
        {
            var info = animator.GetCurrentAnimatorStateInfo(0);
            return IsSwing(info) ? info.length : 0f;
        }

        /// <summary>The least share of a clip this long that a swing held off from its recoil should reach.</summary>
        private static float SwingFloor(float clipSeconds)
            => clipSeconds > 0.01f ? 0.75f * GladiatorView.SwingHoldsOffTheRecoil / clipSeconds : 1f;

        /// <summary>How far through a swing this animator is, or zero if it is not swinging.</summary>
        private static float SwingProgress(Animator animator)
        {
            var info = animator.GetCurrentAnimatorStateInfo(0);
            if (!IsSwing(info)) return 0f;

            // Clamped to the first pass: these clips do not loop, but a state left running while
            // nothing else claims it keeps counting past one and would report a swing that was cut
            // short as having gone round twice.
            return Mathf.Clamp01(info.normalizedTime);
        }

        /// <summary>
        /// Running away from the man you are facing plays a backward cycle, not a forward one.
        ///
        /// The gladiators are turned to face each other every frame, so which way a fighter runs and
        /// which way he faces are independent - and with one forward cycle, a fighter backing off
        /// sprinted towards the man he was retreating from. Asserted on the blend parameters rather
        /// than by eye, since that is what actually chooses the clip.
        ///
        /// Damped, so the numbers are read after they have had time to arrive - in world time, which
        /// is what the damping runs on and what planning slows to a fifth. An assertion on the frame
        /// the direction changes, or after too little world time, measures the damping, not the direction.
        /// </summary>
        [UnityTest]
        public IEnumerator RunningBackwardsPlaysABackwardCycleRatherThanACharge()
        {
            _controller.SubmitPlayerPick(GladiatorId.Barbarius);
            yield return RunSeconds(GameConstants.RevealTime + 0.2f);

            var figure = FindIn("Player", $"Figure_{GladiatorId.Barbarius}");
            var animator = figure != null ? figure.GetComponentInChildren<Animator>(true) : null;
            if (animator == null || animator.runtimeAnimatorController == null)
                Assert.Ignore("No animator - the model pack is not imported here.");

            var player = _controller.Manager.State.P1.Active;
            var bot = _controller.Manager.State.Bot.Active;

            // He faces the opponent, who is straight up the arena from him, and runs the other way.
            // Actually moved, frame by frame: the legs follow where he is drawn going, not a velocity
            // that moves him nowhere - that was what made him run on the spot.
            player.Pos = new Vector2(0f, -60f);
            player.Facing = Vector2.up;
            bot.Pos = new Vector2(0f, 60f);
            yield return MoveFor(player, new Vector2(0f, -120f), 0.25f);

            Assert.Less(animator.GetFloat(AnimatorParams.MoveZ), -0.6f,
                "running away from the man he faces should read as backwards");
            Assert.That(animator.GetFloat(AnimatorParams.MoveX), Is.EqualTo(0f).Within(0.25f),
                "and straight back, not sideways");

            // Now across his own front, which is the case a single speed value cannot express at
            // all: same magnitude, same facing, a different cycle.
            yield return MoveFor(player, new Vector2(120f, 0f), 0.25f);

            Assert.Greater(animator.GetFloat(AnimatorParams.MoveX), 0.6f,
                "running to his own right should read as a right-hand strafe");
        }

        /// <summary>
        /// Both of them work the crowd while the player is deciding, and stop when he stops.
        ///
        /// The phase is four seconds of two men standing still; this is what fills it. Asserted on
        /// the animator state rather than on the flag, because setting a bool nothing listens to is
        /// exactly the failure worth catching - and on both sides, because a taunt only the player's
        /// gladiator performs reads as the opponent having lost interest.
        /// </summary>
        [UnityTest]
        public IEnumerator BothGladiatorsStandReadyWhileThePlayerDecides()
        {
            _controller.SubmitPlayerPick(GladiatorId.Barbarius);
            yield return RunSeconds(GameConstants.RevealTime + 0.4f);

            var state = _controller.Manager.State;
            Assert.AreEqual(MatchPhase.Planning, state.Phase, "this is about the planning phase");

            var player = FindIn("Player", $"Figure_{GladiatorId.Barbarius}")?.GetComponentInChildren<Animator>(true);
            var bot = FindIn("Bot", $"Figure_{state.Bot.Active.Def.Id}")?.GetComponentInChildren<Animator>(true);
            if (player == null || player.runtimeAnimatorController == null)
                Assert.Ignore("No animator - the model pack is not imported here.");

            // Waited for rather than sampled on the spot. The blend into the taunt takes an eighth
            // of a second, and planning runs the world at a fifth of its speed, so it is over half a
            // second of real time - long enough that a single sample at a fixed moment lands inside
            // it, where the current state is still the one being left.
            yield return RunUntil(() => player.GetCurrentAnimatorStateInfo(0).IsName("Ready")
                                        && bot.GetCurrentAnimatorStateInfo(0).IsName("Ready"), 3f);

            Assert.IsTrue(player.GetCurrentAnimatorStateInfo(0).IsName("Ready"),
                "the player's gladiator dropped out of his ready stance");
            Assert.IsTrue(bot.GetCurrentAnimatorStateInfo(0).IsName("Ready"),
                "the opponent dropped out of his ready stance");

            // And it stops when the thinking does: a gladiator posing while he charges is worse than
            // one who never posed at all.
            yield return RunUntil(() => _controller.Manager.State.Phase != MatchPhase.Planning, 8f);
            yield return RunSeconds(0.4f);

            Assert.IsFalse(player.GetCurrentAnimatorStateInfo(0).IsName("Ready"),
                "he was still standing ready once the fighting started");
        }

        /// <summary>
        /// Either swing counts. There are two - a sword's and the two-handed one the mace got - and
        /// Brutius, who this test picks, is the one who swings the mace.
        /// </summary>
        private static bool InAttack(Animator animator)
        {
            if (IsSwing(animator.GetCurrentAnimatorStateInfo(0))) return true;
            return animator.IsInTransition(0) && IsSwing(animator.GetNextAnimatorStateInfo(0));
        }

        /// <summary>
        /// The swing starts before the blow lands, not after it.
        ///
        /// This is the whole of what the prediction buys. Without it the weapon began moving on the
        /// frame the damage was announced, so every exchange was two men striking each other from an
        /// idle pose and then swinging at the air where the other had been.
        ///
        /// Both moments are measured off the running game - the animator state for the swing, the
        /// opponent's health for the blow - so this cannot pass by agreeing with the arithmetic that
        /// scheduled it.
        /// </summary>
        [UnityTest]
        public IEnumerator TheSwingStartsBeforeTheBlowLands()
        {
            _controller.SubmitPlayerPick(GladiatorId.Brutius);
            yield return RunSeconds(GameConstants.RevealTime + 0.2f);

            var figure = FindIn("Player", $"Figure_{GladiatorId.Brutius}");
            var animator = figure != null ? figure.GetComponentInChildren<Animator>(true) : null;
            if (animator == null || animator.runtimeAnimatorController == null)
                Assert.Ignore("No animator - the model pack is not imported here.");

            var state = _controller.Manager.State;
            var player = state.P1.Active;
            var bot = state.Bot.Active;

            player.Pos = new Vector2(-120f, 0f);
            bot.Pos = new Vector2(120f, 0f);

            // Pointed along the charge. A run leaves along the nose and bends onto its target, so two
            // men set down across an axis they did not spawn along would curve away rather than meet.

            player.Facing = Vector2.right;
            bot.Facing = Vector2.left;

            _controller.Manager.SubmitPlanningAction(PlayerSide.P1, ActionType.Move, Vector2.right, 1f, false);
            _controller.Manager.SubmitPlanningAction(PlayerSide.Bot, ActionType.Move, Vector2.left, 1f, false);

            yield return RunUntil(() => _controller.Manager.State.Phase == MatchPhase.Action, 8f);
            Assert.Greater(state.P1.StrikeEta, 0f, "the charge was not predicted to land at all");

            float startHp = bot.Hp;
            float swingAt = -1f, blowAt = -1f;
            float t = 0f;

            while (t < 2f && (swingAt < 0f || blowAt < 0f))
            {
                yield return null;
                t += Time.unscaledDeltaTime;

                if (swingAt < 0f && IsSwing(animator.GetCurrentAnimatorStateInfo(0))) swingAt = t;
                if (blowAt < 0f && bot.Hp < startHp) blowAt = t;
            }

            Assert.Greater(swingAt, -1f, "he never swung at all");
            Assert.Greater(blowAt, -1f, "the blow never landed");
            Assert.Less(swingAt, blowAt,
                $"the swing started at {swingAt:0.000}s and the blow landed at {blowAt:0.000}s - " +
                "he hit first and swung afterwards");
        }

        /// <summary>
        /// The stance covers the thinking time, less the second before the charge.
        ///
        /// Measured in real seconds off the running game rather than derived from the clip length,
        /// because the arithmetic behind it depends on the planning phase's time scale - the world
        /// runs at a fifth of its speed while the player thinks, so a clip playing at its own rate finishes
        /// in a fifth of the time it looks like it should. That coupling is invisible and easy to
        /// break; this is what catches it.
        /// </summary>
        [UnityTest]
        public IEnumerator ThePlanningStanceFillsTheThinkingTime()
        {
            _controller.SubmitPlayerPick(GladiatorId.Barbarius);
            yield return RunSeconds(GameConstants.RevealTime + 0.2f);

            var figure = FindIn("Player", $"Figure_{GladiatorId.Barbarius}");
            var animator = figure != null ? figure.GetComponentInChildren<Animator>(true) : null;
            if (animator == null || animator.runtimeAnimatorController == null)
                Assert.Ignore("No animator - the model pack is not imported here.");

            yield return RunUntil(() => animator.GetCurrentAnimatorStateInfo(0).IsName("Ready"), 3f);

            // How far through the clip one real second of planning carries it. The state reports
            // its progress as a fraction of the whole, so a full pass is 1.
            float before = animator.GetCurrentAnimatorStateInfo(0).normalizedTime;
            float started = Time.realtimeSinceStartup;
            yield return RunSeconds(1f);
            float elapsed = Time.realtimeSinceStartup - started;
            float after = animator.GetCurrentAnimatorStateInfo(0).normalizedTime;

            if (!animator.GetCurrentAnimatorStateInfo(0).IsName("Ready"))
                Assert.Ignore("The phase ended mid-measurement; nothing to conclude.");

            float wholePass = elapsed / Mathf.Max(after - before, 0.0001f);
            Assert.AreEqual(GameConstants.PlanningTime - 1f, wholePass, 0.6f,
                $"the stance takes {wholePass:0.00}s of real time, not the phase less a second");
        }

        /// <summary>Anything hanging off a fist, as opposed to the fighter himself.</summary>
        private static bool IsCarriedGear(Transform t)
        {
            for (var node = t; node != null; node = node.parent)
                if (node.name == "HeldWeapon" || node.name == "HeldOffHand") return true;
            return false;
        }

        private static bool IsSwing(AnimatorStateInfo info)
            => info.IsName("Attack") || info.IsName("AttackHeavy");

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

        /// <summary>
        /// The helmet is the top of a gladiator, on every build.
        ///
        /// The camera looks down at sixty-six degrees, so the crown is most of what a fighter shows
        /// - and the helm used to be seated a shade below the bare head, which from up there meant
        /// looking at hair. Asserted against the rest of the figure's renderers rather than by eye,
        /// and on all three archetypes, because they are scaled to different heights and a single
        /// offset that clears one can sit inside another.
        /// </summary>
        [UnityTest]
        public IEnumerator TheHelmetIsTheHighestThingOnEveryArchetype()
        {
            foreach (var def in GladiatorDef.All)
            {
                _controller.RestartMatch();
                yield return null;
                _controller.SubmitPlayerPick(def.Id);
                yield return RunSeconds(GameConstants.RevealTime + 0.2f);

                var figure = FindIn("Player", $"Figure_{def.Id}");
                var helmet = figure?.GetComponentsInChildren<Transform>(true)
                    .FirstOrDefault(t => t.name == "Helmet");
                if (helmet == null) Assert.Ignore("No helmet - the gear pack is not imported here.");

                float helmetTop = float.NegativeInfinity;
                foreach (var r in helmet.GetComponentsInChildren<Renderer>(true))
                    helmetTop = Mathf.Max(helmetTop, r.bounds.max.y);

                // The body, not what it is holding. A mace is longer than a man is tall and is
                // carried at an angle that changes with every pose, so including it measures the
                // weapon rather than the helmet - and a fighter whose hammer rides above his head is
                // not a fighter with a bare head.
                float bodyTop = float.NegativeInfinity;
                foreach (var r in figure.GetComponentsInChildren<Renderer>(true))
                {
                    if (r.transform.IsChildOf(helmet)) continue;
                    if (IsCarriedGear(r.transform)) continue;
                    bodyTop = Mathf.Max(bodyTop, r.bounds.max.y);
                }

                Assert.Greater(helmetTop, bodyTop,
                    $"{def.Name} shows {bodyTop - helmetTop:0.###} units of bare head above his helm");
            }
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
            // clash holds for a moment afterwards, and that moment is what the animation is for.
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

        /// <summary>
        /// Waits out a stretch of world time rather than of real time - what the animator and its
        /// damping run on. Planning slows the world to a fifth, so the two are far apart there. Capped
        /// in real time so a stopped world cannot hang the test.
        /// </summary>
        private static IEnumerator RunWorldSeconds(float seconds)
        {
            float world = 0f, real = 0f;
            while (world < seconds && real < seconds * 10f)
            {
                yield return null;
                world += Time.deltaTime;
                real += Time.unscaledDeltaTime;
            }
        }

        private static IEnumerator RunSeconds(float seconds)
        {
            float t = 0f;
            while (t < seconds) { yield return null; t += Time.unscaledDeltaTime; }
        }

        /// <summary>
        /// A clash opens with both men walking out from the wall behind their marks while it is being
        /// announced - running, not sliding - rather than standing on them from its first frame, and
        /// they are on their marks by the time planning opens. The simulation has them there all
        /// along: this is only where they are drawn.
        /// </summary>
        [UnityTest]
        public IEnumerator EachClashOpensWithBothWalkingOutFromTheWall()
        {
            _controller.SubmitPlayerPick(GladiatorId.Brutius);
            yield return null;
            yield return null;

            var state = _controller.Manager.State;
            Assert.AreEqual(MatchPhase.Reveal, state.Phase, "the clash should be being announced");

            foreach (var (name, g) in new[] { ("Player", state.P1.Active), ("Bot", state.Bot.Active) })
            {
                var view = View(name);
                float mark = FromCentre(_controller.Arena.ToWorld(g.Pos));
                Assert.Greater(FromCentre(view.transform.localPosition), mark + 0.5f,
                    $"the {name}'s gladiator is already on his mark - he should be walking out to it");
            }

            // A little way into the walk out: the speed the legs are driven by is smoothed over a few
            // frames, and the walk lasts a second and a half.
            yield return RunSeconds(0.3f);
            Assert.AreEqual(MatchPhase.Reveal, state.Phase, "the clash should still be being announced");
            var animator = FindIn("Player", $"Figure_{GladiatorId.Brutius}")?.GetComponentInChildren<Animator>(true);
            if (animator != null && animator.runtimeAnimatorController != null)
                Assert.Greater(animator.GetFloat(AnimatorParams.SpeedId), AnimatorParams.RunThreshold,
                    "on his way out he should be running, not sliding");

            yield return RunUntil(() => state.Phase == MatchPhase.Planning, GameConstants.RevealTime + 1f);
            yield return null;

            foreach (var (name, g) in new[] { ("Player", state.P1.Active), ("Bot", state.Bot.Active) })
                Assert.AreEqual(0f, Flat(View(name).transform.localPosition - _controller.Arena.ToWorld(g.Pos)).magnitude, 0.05f,
                    $"the {name}'s gladiator should be on his mark by the time planning opens");
        }

        /// <summary>
        /// The next clash starts clean: whatever the survivor was in the middle of - a swing, a
        /// guard - is thrown away, and he walks out from the idle like the man picked fresh.
        /// </summary>
        [UnityTest]
        public IEnumerator ANewClashThrowsAwayWhateverTheLastOneLeftPlaying()
        {
            _controller.SubmitPlayerPick(GladiatorId.Brutius);
            yield return RunUntil(() => _controller.Manager.State.Phase == MatchPhase.Planning, GameConstants.RevealTime + 1f);

            var animator = FindIn("Player", $"Figure_{GladiatorId.Brutius}")?.GetComponentInChildren<Animator>(true);
            if (animator == null || animator.runtimeAnimatorController == null)
                Assert.Ignore("No animator - the model pack is not imported here.");

            // Left mid-guard with a swing still waiting to go, the way a clash can end.
            animator.SetBool(AnimatorParams.DefendingId, true);
            animator.SetTrigger(AnimatorParams.AttackId);

            View("Player").EnterArena(_controller.Arena.ToWorld(Vector2.zero), 0.5f);
            yield return null;

            Assert.IsFalse(animator.GetBool(AnimatorParams.DefendingId) && !_controller.Manager.State.P1.Active.IsDefending,
                "the guard from the last clash is still up");
            Assert.IsFalse(animator.GetCurrentAnimatorStateInfo(0).IsName("Attack")
                           || animator.GetNextAnimatorStateInfo(0).IsName("Attack"),
                "the swing left over from the last clash went off in the new one");
        }

        /// <summary>
        /// Taking up the blessing sounds like a magic shot - the pack's etfx_shoot_magic - and not like
        /// the fireball its effect came with.
        /// </summary>
        [UnityTest]
        public IEnumerator TheBlessingSoundsLikeAMagicShot()
        {
            var palette = _controller.Arena.Palette;
            if (palette.BlessingFx == null || palette.BlessingSound == null)
                Assert.Ignore("No Epic Toon FX here - there is no effect or sound to check.");
            yield return null;

            var fx = FindIn("Player", "BlessingFx");
            Assert.IsNotNull(fx, "the player's gladiator has no blessing effect");
            var sources = fx.GetComponentsInChildren<AudioSource>(true);
            Assert.IsNotEmpty(sources, "the blessing's effect has nothing to play a sound with");
            foreach (var source in sources)
                Assert.AreEqual("etfx_shoot_magic", source.clip != null ? source.clip.name : null,
                    "the blessing should sound like a magic shot");
        }

        /// <summary>
        /// The foot on the ground stays on the ground while he runs: the run cycle is played at the
        /// rate his body is actually covering the sand, so the planted foot does not slide along
        /// under him - which is what running on the spot looks like, and what a cycle played at one
        /// fixed rate for every man and every speed did.
        ///
        /// Measured, not eyeballed: the lower of his two feet is the planted one, and its speed over
        /// the ground is taken as a share of his body's. A run matched to the ground keeps that share
        /// small; a cycle too slow or too fast for the ground makes it large. The slowest man and the
        /// fastest, because a fixed rate can only ever be right for one speed.
        /// </summary>
        [UnityTest]
        public IEnumerator ThePlantedFootStaysPutWhileTheSlowestManRuns()
        {
            yield return MeasureFootSlip(GladiatorId.Brutius);
            AssertFootStaysPut(GladiatorId.Brutius);
        }

        [UnityTest]
        public IEnumerator ThePlantedFootStaysPutWhileTheFastestManRuns()
        {
            yield return MeasureFootSlip(GladiatorId.Hilius);
            AssertFootStaysPut(GladiatorId.Hilius);
        }

        /// <summary>The planted foot's speed over the ground as a share of his body's, from the last measurement.</summary>
        private float _footSlip = -1f;

        /// <summary>
        /// How much of his body's speed the planted foot may carry. Not near nought: this measure
        /// bottoms out at about half even for a run matched as well as the calibration sweep can
        /// match it - a foot rolls from heel to toe while it is down, and the frames round touchdown
        /// count as down. Matched, the two men measure 0.5 to 0.6; at the one fixed rate the run
        /// used to play at, Brutius measured 1.07 and Hilius 0.76.
        /// </summary>
        private const float MaxFootSlip = 0.68f;

        private void AssertFootStaysPut(GladiatorId id)
        {
            if (_footSlip < 0f) Assert.Ignore("No animated figure here - there is no foot to measure.");
            Debug.Log($"[FootSlip] {id}: the planted foot moves at {_footSlip:0.00} of his speed");
            Assert.Less(_footSlip, MaxFootSlip,
                $"{id}'s planted foot slides along at {_footSlip:0.00} of his speed - he is running on the spot");
        }

        private IEnumerator MeasureFootSlip(GladiatorId id)
        {
            _footSlip = -1f;
            _controller.SubmitPlayerPick(id);
            yield return RunUntil(() => _controller.Manager.State.Phase == MatchPhase.Planning, GameConstants.RevealTime + 1f);
            yield return MeasureOneRun(id);
        }

        /// <summary>
        /// Not a check, a tuning instrument: plays the same run at a spread of multipliers on the
        /// matched rate and logs how far the planted foot slides at each, for the slowest man and the
        /// fastest. The multiplier where it slides least is what GladiatorView.StrideMatch is set to.
        /// </summary>
        [UnityTest, Explicit("a calibration sweep - run it by name when the run cycle or the figures change")]
        public IEnumerator RunRateCalibrationSweep()
        {
            var multipliers = new[] { 0.6f, 0.8f, 1.0f, 1.2f, 1.4f, 1.7f, 2.0f };
            foreach (var id in new[] { GladiatorId.Brutius, GladiatorId.Hilius })
            {
                yield return SceneManager.LoadSceneAsync(ScenePath, LoadSceneMode.Single);
                yield return null;
                _controller = Object.FindFirstObjectByType<GameController>();
                _controller.SubmitPlayerPick(id);

                var line = new System.Text.StringBuilder($"[StrideSweep] {id}:");
                foreach (float k in multipliers)
                {
                    GladiatorView.StrideTuning = k;
                    yield return RunUntil(() => _controller.Manager.State.Phase == MatchPhase.Planning, GameConstants.RevealTime + GameConstants.ActionTime + 2f);
                    yield return MeasureOneRun(id);
                    line.Append($"  x{k:0.0}={_footSlip:0.00}");
                }
                GladiatorView.StrideTuning = 1f;
                Debug.Log(line.ToString());
            }
        }

        /// <summary>
        /// One long straight run up the open middle, the opponent held at the far end, measured: how
        /// fast the planted foot moves over the ground as a share of how fast his body does.
        ///
        /// The planted foot is the lowest of his toes and ankles, and only frames where it is
        /// actually down count - near the lowest it gets over the run. An ankle rolls forward over
        /// a planted foot and both feet are off the ground in a run's flight, and counting either as
        /// sliding buried the difference between a matched cycle and a mismatched one.
        /// </summary>
        private IEnumerator MeasureOneRun(GladiatorId id)
        {
            _footSlip = -1f;
            var animator = FindIn("Player", $"Figure_{id}")?.GetComponentInChildren<Animator>(true);
            if (animator == null || animator.runtimeAnimatorController == null || !animator.isHuman) yield break;
            var contacts = new[]
                {
                    HumanBodyBones.LeftToes, HumanBodyBones.RightToes, HumanBodyBones.LeftFoot, HumanBodyBones.RightFoot
                }
                .Select(animator.GetBoneTransform).Where(t => t != null).ToArray();
            if (contacts.Length < 2) yield break;

            var state = _controller.Manager.State;
            state.P1.Active.Pos = new Vector2(0f, -300f);
            state.P1.Active.Facing = Vector2.up;
            state.Bot.Active.Pos = new Vector2(0f, 420f);
            state.Bot.Active.EnsnaredRoundsLeft = 2;
            Assert.IsTrue(_controller.Manager.SubmitPlanningAction(PlayerSide.P1, ActionType.Move, Vector2.up, 1f, false));
            yield return RunUntil(() => state.Phase == MatchPhase.Action, GameConstants.PlanningTime + 1f);

            // Into the stride before measuring: the first steps are the blend out of the stance.
            yield return RunSeconds(0.3f);

            var gladiatorView = View("Player");
            var view = gladiatorView.transform;
            var lastBody = view.position;
            var last = contacts.Select(t => t.position).ToArray();
            var samples = new List<(float height, float body, float foot)>();
            float rate = 0f;
            float until = Time.realtimeSinceStartup + 0.9f;
            while (Time.realtimeSinceStartup < until && state.Phase == MatchPhase.Action)
            {
                yield return null;
                float dt = Time.deltaTime;
                if (dt <= 0f) continue;

                int lowest = 0;
                for (int i = 1; i < contacts.Length; i++)
                    if (contacts[i].position.y < contacts[lowest].position.y) lowest = i;

                float bodySpeed = Flat(view.position - lastBody).magnitude / dt;
                float footSpeed = Flat(contacts[lowest].position - last[lowest]).magnitude / dt;
                if (bodySpeed > AnimatorParams.RunThreshold * 2f)
                {
                    samples.Add((contacts[lowest].position.y - view.position.y, bodySpeed, footSpeed));
                    rate += animator.GetFloat(AnimatorParams.RunRateId);
                }

                lastBody = view.position;
                for (int i = 0; i < contacts.Length; i++) last[i] = contacts[i].position;
            }

            Assert.Greater(samples.Count, 5, "he hardly ran - nothing to measure");
            float low = samples.Min(s => s.height);
            float high = samples.Max(s => s.height);
            var down = samples.Where(s => s.height <= low + (high - low) * 0.3f).ToList();
            _footSlip = down.Sum(s => s.foot) / down.Sum(s => s.body);

            Debug.Log($"[FootSlip] {id}: body {samples.Average(s => s.body):0.00}/s, run rate {rate / samples.Count:0.00}, " +
                      $"{down.Count} of {samples.Count} frames down, clip speeds {_controller.Arena.Palette.RunClipSpeeds}, " +
                      $"human scale {animator.humanScale:0.000}, figure scale {animator.transform.lossyScale.y:0.000}");
        }

        /// <summary>
        /// A lighter blow is a quicker swing: the mace's a quarter faster than authored and still the
        /// longest, the twin swords' three fifths faster, the rest in between in order of how hard
        /// they hit - and the lead a swing is started with shrinks with it, so the blow still lands
        /// when the weapon gets there.
        /// </summary>
        [Test]
        public void ALighterBlowIsAQuickerSwing()
        {
            Assert.AreEqual(1.25f, GladiatorView.AttackSpeed(WeaponKind.TwoHandedMace), 0.001f, "the mace");
            Assert.AreEqual(1.6f, GladiatorView.AttackSpeed(WeaponKind.DualSwords), 0.001f, "the twin swords");

            var byDamage = WeaponDef.All.OrderByDescending(w => w.DamageMultiplier).ToList();
            for (int i = 1; i < byDamage.Count; i++)
                Assert.GreaterOrEqual(GladiatorView.AttackSpeed(byDamage[i].Kind), GladiatorView.AttackSpeed(byDamage[i - 1].Kind),
                    $"{byDamage[i].Name} hits lighter than {byDamage[i - 1].Name} and should swing no slower");

            float mace = GladiatorView.SwingLead(WeaponKind.TwoHandedMace);
            foreach (var weapon in WeaponDef.All)
                Assert.LessOrEqual(GladiatorView.SwingLead(weapon.Kind), mace + 0.0001f,
                    $"the mace's swing should be the longest, and {weapon.Name}'s is longer");
        }

        /// <summary>The swing in his hands plays at that weapon's pace.</summary>
        [UnityTest]
        public IEnumerator HisSwingPlaysAtHisWeaponsPace()
        {
            _controller.SubmitPlayerPick(GladiatorId.Barbarius);
            yield return RunSeconds(GameConstants.RevealTime + 0.2f);

            var animator = FindIn("Player", $"Figure_{GladiatorId.Barbarius}")?.GetComponentInChildren<Animator>(true);
            if (animator == null || animator.runtimeAnimatorController == null)
                Assert.Ignore("No animator - the model pack is not imported here.");

            Assert.AreEqual(GladiatorView.AttackSpeed(WeaponKind.DualSwords), animator.GetFloat(AnimatorParams.AttackRateId), 0.001f,
                "the twin swords should swing at their own pace");
        }

        private GladiatorView View(string name)
            => _controller.GetComponentsInChildren<GladiatorView>(true).First(v => v.name == name);

        private float FromCentre(Vector3 local) => Flat(local - _controller.Arena.ToWorld(Vector2.zero)).magnitude;

        private static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);
    }
}
