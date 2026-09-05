using UnityEngine;

namespace ColosseumDuel.Gameplay.View
{
    /// <summary>
    /// The names the gladiator's animator controller is driven by.
    ///
    /// In the runtime assembly rather than beside the Editor script that builds the controller, so
    /// that the builder and the view read the same constants. Two lists of hand-typed parameter
    /// names is a mismatch waiting to happen, and a mismatch here is silent: Unity logs nothing for
    /// a SetFloat on a parameter that does not exist, it simply does nothing forever.
    /// </summary>
    public static class AnimatorParams
    {
        public const string Speed = "Speed";
        public const string Defending = "Defending";
        public const string Attack = "Attack";
        public const string Hit = "Hit";
        public const string Dead = "Dead";

        /// <summary>Speed, in world units per second, above which the run cycle plays.</summary>
        public const float RunThreshold = 0.35f;

        public static readonly int SpeedId = Animator.StringToHash(Speed);
        public static readonly int DefendingId = Animator.StringToHash(Defending);
        public static readonly int AttackId = Animator.StringToHash(Attack);
        public static readonly int HitId = Animator.StringToHash(Hit);
        public static readonly int DeadId = Animator.StringToHash(Dead);
    }
}
