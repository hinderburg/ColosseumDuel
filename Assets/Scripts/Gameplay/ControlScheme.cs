namespace ColosseumDuel.Gameplay
{
    /// <summary>
    /// How the player orders a move. Chosen once on the opening pick screen and kept for the match.
    /// </summary>
    public enum ControlScheme
    {
        /// <summary>
        /// Pull back from the gladiator and release, slingshot style. The default: the pull length
        /// sets the power, so aim and commitment are one gesture and the preview can promise exactly
        /// where the run ends.
        /// </summary>
        Drag = 0,

        /// <summary>
        /// Tap a spot and he runs at it - as far as the tap if he can reach it, flat out towards it
        /// if he cannot. One touch instead of a held gesture, which is easier on a phone and easier
        /// to explain, at the cost of never deliberately running short.
        /// </summary>
        Tap = 1,
    }
}
