using ColosseumDuel.Gameplay.View;
using System.IO;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace ColosseumDuel.EditorTools
{
    /// <summary>
    /// Builds the gladiator's animator controller from the imported clip library.
    ///
    /// Generated rather than authored by hand, like the rest of the project's assets: the state
    /// machine is small enough to read as code, and a controller built in the Editor window is a
    /// binary blob that nothing explains and a merge cannot resolve.
    ///
    /// Only the in-place clips are used. The pack ships each movement twice, with and without root
    /// motion, and root motion here would be the animation dragging the model around while the
    /// simulation independently decides where the gladiator actually is - two authorities on one
    /// position, disagreeing every frame.
    /// </summary>
    public static class GladiatorAnimation
    {
        public const string ControllerPath = "Assets/Animation/Gladiator.controller";
        private const string ClipDir = "Assets/DoubleL/Demo/Anim";

        /// <summary>
        /// The second clip library, which fills the gaps the first one has no answer for: a real
        /// knockdown instead of a hit played as a death, a throw backwards for the mace, and a swing
        /// made with both hands on the haft.
        ///
        /// Optional in the same way the first one is. Every clip taken from here has a fallback from
        /// the pack that is already there, so a project without it looks like the project did before
        /// rather than standing in a bind pose.
        /// </summary>
        private const string PackDir =
            "Assets/ExplosiveLLC/RPG Character Mecanim Animation Pack FREE/Animations";

        /// <summary>Builds or rebuilds the controller. Returns null if the clip pack is absent.</summary>
        public static AnimatorController EnsureController()
        {
            var idle = Clip("OneHand_Up_Idle");
            if (idle == null)
            {
                Debug.LogWarning($"[Colosseum] Animation clips not found in {ClipDir} - gladiators " +
                                 "will stand in their bind pose. Import the DoubleL pack to get them.");
                return null;
            }

            if (!AssetDatabase.IsValidFolder("Assets/Animation"))
                AssetDatabase.CreateFolder("Assets", "Animation");

            // Rebuilt from scratch rather than patched: an incrementally edited controller
            // accumulates the states and transitions of every earlier version of this method.
            AssetDatabase.DeleteAsset(ControllerPath);
            var controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);

            controller.AddParameter(AnimatorParams.Speed, AnimatorControllerParameterType.Float);
            controller.AddParameter(AnimatorParams.Defending, AnimatorControllerParameterType.Bool);
            controller.AddParameter(AnimatorParams.Attack, AnimatorControllerParameterType.Trigger);
            controller.AddParameter(AnimatorParams.Hit, AnimatorControllerParameterType.Trigger);
            controller.AddParameter(AnimatorParams.Dead, AnimatorControllerParameterType.Bool);
            controller.AddParameter(AnimatorParams.MoveX, AnimatorControllerParameterType.Float);
            controller.AddParameter(AnimatorParams.MoveZ, AnimatorControllerParameterType.Float);
            controller.AddParameter(AnimatorParams.TwoHanded, AnimatorControllerParameterType.Bool);
            controller.AddParameter(AnimatorParams.Knockback, AnimatorControllerParameterType.Trigger);

            var machine = controller.layers[0].stateMachine;

            var idleState = machine.AddState("Idle");
            idleState.motion = idle;
            machine.defaultState = idleState;

            var runState = BuildRun(controller, idle);

            var blockState = machine.AddState("Block");
            blockState.motion = Clip("OneHand_Up_Shield_Block_Idle");

            var attackState = machine.AddState("Attack");
            attackState.motion = Clip("OneHand_Up_Attack_1_InPlace");

            // The mace is swung with both hands on the haft. The one-handed clip put a two-handed
            // hammer through a sword's arc, with the off hand nowhere near it.
            var heavyAttackState = machine.AddState("AttackHeavy");
            heavyAttackState.motion = PackClip("2Hand-Sword", "2Hand-Sword-Attack1")
                                      ?? attackState.motion;

            var hitState = machine.AddState("Hit");
            hitState.motion = Clip("Hit_F_1_InPlace");

            // Thrown off his feet rather than flinching: the mace's blow moves him, and a flinch
            // played over a body sliding backwards reads as the ground moving instead.
            var knockbackState = machine.AddState("Knockback");
            knockbackState.motion = PackClip("Unarmed", "Unarmed-Knockback-Back1") ?? hitState.motion;

            // A real fall. This was a hit clip - the second of the two flinches - so a dead
            // gladiator finished the match standing up, having winced.
            var deadState = machine.AddState("Dead");
            deadState.motion = PackClip("Unarmed", "Unarmed-Knockdown1") ?? Clip("Hit_F_2_InPlace");

            // --- standing, running, guarding ---
            // Guarding wins over running: Defend and Move are exclusive plans, so a gladiator who is
            // blocking is not going anywhere, and checking the flag first keeps a stray velocity
            // from putting him in a run cycle while he holds his shield up.
            Move(idleState, blockState, Bool(AnimatorParams.Defending, true));
            Move(runState, blockState, Bool(AnimatorParams.Defending, true));
            Move(blockState, idleState, Bool(AnimatorParams.Defending, false));

            Move(idleState, runState, Float(AnimatorParams.Speed, AnimatorConditionMode.Greater, AnimatorParams.RunThreshold),
                 Bool(AnimatorParams.Defending, false));
            Move(runState, idleState, Float(AnimatorParams.Speed, AnimatorConditionMode.Less, AnimatorParams.RunThreshold));

            // --- one-shots ---
            // From Any State, because a blow can land in any of them, and each returns on its own
            // exit time rather than on a flag the simulation would have to remember to clear.
            //
            // The two swings are told apart on the weapon flag rather than by the view picking a
            // state: only one of the pair can match, so the trigger is still consumed exactly once.
            AnyState(machine, attackState, Bool(AnimatorParams.Dead, false),
                     Bool(AnimatorParams.TwoHanded, false), Trigger(AnimatorParams.Attack));
            AnyState(machine, heavyAttackState, Bool(AnimatorParams.Dead, false),
                     Bool(AnimatorParams.TwoHanded, true), Trigger(AnimatorParams.Attack));
            AnyState(machine, hitState, Bool(AnimatorParams.Dead, false), Trigger(AnimatorParams.Hit));
            AnyState(machine, knockbackState, Bool(AnimatorParams.Dead, false),
                     Trigger(AnimatorParams.Knockback));
            Return(attackState, idleState);
            Return(heavyAttackState, idleState);
            Return(hitState, idleState);
            Return(knockbackState, idleState);

            // --- death ---
            // No exit: a dead gladiator stays down. The view hides him a moment later, and the pose
            // he is hidden in should be the one he fell in.
            AnyState(machine, deadState, Bool(AnimatorParams.Dead, true));

            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();
            return controller;
        }

        /// <summary>
        /// Running, as a direction rather than as a speed.
        ///
        /// A two-dimensional blend over the four in-place run cycles, read in the gladiator's own
        /// frame. He is turned to face his opponent every frame, so the direction he runs and the
        /// direction he faces are independent - and with a single forward cycle, a fighter backing
        /// off or circling ran that way while his legs said he was charging.
        ///
        /// The idle sits at the centre. The state is only entered above the run threshold so the
        /// centre is never really sampled, but leaving a hole there makes the blend at low speed
        /// depend on which of the four happens to be nearest.
        /// </summary>
        private static AnimatorState BuildRun(AnimatorController controller, AnimationClip idle)
        {
            var state = controller.CreateBlendTreeInController("Run", out var tree, 0);
            tree.blendType = BlendTreeType.SimpleDirectional2D;
            tree.blendParameter = AnimatorParams.MoveX;
            tree.blendParameterY = AnimatorParams.MoveZ;
            tree.useAutomaticThresholds = false;

            tree.AddChild(idle, Vector2.zero);
            AddDirection(tree, "OneHand_Up_Run_F_InPlace", new Vector2(0f, 1f), idle);
            AddDirection(tree, "OneHand_Up_Run_B_InPlace", new Vector2(0f, -1f), idle);
            AddDirection(tree, "OneHand_Up_Run_L_InPlace", new Vector2(-1f, 0f), idle);
            AddDirection(tree, "OneHand_Up_Run_R_InPlace", new Vector2(1f, 0f), idle);
            return state;
        }

        private static void AddDirection(BlendTree tree, string clipName, Vector2 at, AnimationClip fallback)
            => tree.AddChild(Clip(clipName) ?? fallback, at);

        private static AnimationClip Clip(string name)
            => AssetDatabase.LoadAssetAtPath<AnimationClip>(Path.Combine(ClipDir, name + ".anim").Replace('\\', '/'));

        /// <summary>
        /// One clip out of the second pack, which ships them inside FBX files rather than as loose
        /// assets - so it has to be dug out of the file's sub-assets by name, and LoadAssetAtPath
        /// on the FBX would hand back the model instead.
        /// </summary>
        private static AnimationClip PackClip(string folder, string clipName)
        {
            string path = $"{PackDir}/{folder}/RPG-Character@{clipName}.FBX";
            StripEvents(path);

            foreach (var asset in AssetDatabase.LoadAllAssetRepresentationsAtPath(path))
            {
                var clip = asset as AnimationClip;
                if (clip != null && clip.name == clipName) return clip;
            }
            return null;
        }

        /// <summary>
        /// Takes the animation events off a pack clip.
        ///
        /// The clips are authored for the character controller the pack ships with, and they call
        /// back into it on every footfall and every impact frame - FootL, FootR, Hit. That
        /// controller is not in this project, and Unity does not shrug at a call with no receiver:
        /// it logs an error, once per event, for as long as the clip plays. A knockdown alone was
        /// enough to turn the console red.
        ///
        /// Cleared through the importer rather than on the clip. An imported clip is read-only, so
        /// AnimationUtility would appear to work and be undone by the next reimport.
        /// </summary>
        private static void StripEvents(string path)
        {
            var importer = AssetImporter.GetAtPath(path) as ModelImporter;
            if (importer == null) return;

            var clips = importer.clipAnimations;
            if (clips == null || clips.Length == 0) clips = importer.defaultClipAnimations;
            if (clips == null || clips.Length == 0) return;

            bool changed = false;
            foreach (var clip in clips)
            {
                if (clip.events == null || clip.events.Length == 0) continue;
                clip.events = new AnimationEvent[0];
                changed = true;
            }

            if (!changed) return;

            importer.clipAnimations = clips;
            importer.SaveAndReimport();
        }

        private static AnimatorCondition Bool(string parameter, bool value) => new AnimatorCondition
        {
            parameter = parameter,
            mode = value ? AnimatorConditionMode.If : AnimatorConditionMode.IfNot,
        };

        private static AnimatorCondition Float(string parameter, AnimatorConditionMode mode, float threshold)
            => new AnimatorCondition { parameter = parameter, mode = mode, threshold = threshold };

        private static AnimatorCondition Trigger(string parameter)
            => new AnimatorCondition { parameter = parameter, mode = AnimatorConditionMode.If };

        /// <summary>A blended transition that waits on its conditions rather than on the clip ending.</summary>
        private static void Move(AnimatorState from, AnimatorState to, params AnimatorCondition[] conditions)
        {
            var transition = from.AddTransition(to);
            transition.hasExitTime = false;
            transition.duration = 0.12f;
            transition.conditions = conditions;
        }

        private static void AnyState(AnimatorStateMachine machine, AnimatorState to,
                                     params AnimatorCondition[] conditions)
        {
            var transition = machine.AddAnyStateTransition(to);
            transition.hasExitTime = false;
            transition.duration = 0.06f;
            transition.canTransitionToSelf = false;
            transition.conditions = conditions;
        }

        /// <summary>Plays out and goes back, for the one-shots.</summary>
        private static void Return(AnimatorState from, AnimatorState to)
        {
            var transition = from.AddTransition(to);
            transition.hasExitTime = true;
            transition.exitTime = 0.8f;
            transition.duration = 0.15f;
        }
    }
}
