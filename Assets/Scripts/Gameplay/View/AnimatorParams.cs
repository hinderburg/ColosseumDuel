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

        /// <summary>
        /// Which way he is running, as a unit vector in his own frame: MoveZ ahead, MoveX to his
        /// right. Two numbers rather than one, because the gladiators are turned to face each other
        /// at all times and so most of their running is not forwards - a fighter circling or backing
        /// off is moving sideways relative to himself, and one speed value cannot say that.
        /// </summary>
        public const string MoveX = "MoveX";
        public const string MoveZ = "MoveZ";

        /// <summary>Whether the weapon in his hands takes both of them. Picks the swing.</summary>
        public const string TwoHanded = "TwoHanded";

        /// <summary>A blow that throws him back rather than only hurting him.</summary>
        public const string Knockback = "Knockback";

        /// <summary>
        /// Whether he is working the crowd. Held for the whole planning phase rather than fired
        /// once, because the phase is four seconds of two men standing still and the alternative is
        /// four seconds of two men standing still.
        /// </summary>
        public const string Taunting = "Taunting";

        /// <summary>Speed, in world units per second, above which the run cycle plays.</summary>
        public const float RunThreshold = 0.35f;

        public static readonly int SpeedId = Animator.StringToHash(Speed);
        public static readonly int DefendingId = Animator.StringToHash(Defending);
        public static readonly int AttackId = Animator.StringToHash(Attack);
        public static readonly int HitId = Animator.StringToHash(Hit);
        public static readonly int DeadId = Animator.StringToHash(Dead);
        public static readonly int MoveXId = Animator.StringToHash(MoveX);
        public static readonly int MoveZId = Animator.StringToHash(MoveZ);
        public static readonly int TwoHandedId = Animator.StringToHash(TwoHanded);
        public static readonly int KnockbackId = Animator.StringToHash(Knockback);
        public static readonly int TauntingId = Animator.StringToHash(Taunting);
    }
}
