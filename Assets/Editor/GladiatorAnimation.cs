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

            var machine = controller.layers[0].stateMachine;

            var idleState = machine.AddState("Idle");
            idleState.motion = idle;
            machine.defaultState = idleState;

            var runState = machine.AddState("Run");
            runState.motion = Clip("OneHand_Up_Run_F_InPlace");

            var blockState = machine.AddState("Block");
            blockState.motion = Clip("OneHand_Up_Shield_Block_Idle");

            var attackState = machine.AddState("Attack");
            attackState.motion = Clip("OneHand_Up_Attack_1_InPlace");

            var hitState = machine.AddState("Hit");
            hitState.motion = Clip("Hit_F_1_InPlace");

            var deadState = machine.AddState("Dead");
            deadState.motion = Clip("Hit_F_2_InPlace");

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
            AnyState(machine, attackState, Bool(AnimatorParams.Dead, false), Trigger(AnimatorParams.Attack));
            AnyState(machine, hitState, Bool(AnimatorParams.Dead, false), Trigger(AnimatorParams.Hit));
            Return(attackState, idleState);
            Return(hitState, idleState);

            // --- death ---
            // No exit: a dead gladiator stays down. The view hides him a moment later, and the pose
            // he is hidden in should be the one he fell in.
            AnyState(machine, deadState, Bool(AnimatorParams.Dead, true));

            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();
            return controller;
        }

        private static AnimationClip Clip(string name)
            => AssetDatabase.LoadAssetAtPath<AnimationClip>(Path.Combine(ClipDir, name + ".anim").Replace('\\', '/'));

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
