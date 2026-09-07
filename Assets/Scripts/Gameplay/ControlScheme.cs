namespace ColosseumDuel.Gameplay
{
    /// <summary>
    /// How the player orders a move. Tapping is what the game offers; the other is kept in code.
    /// </summary>
    public enum ControlScheme
    {
        /// <summary>
        /// Pull back from the gladiator and release, slingshot style. The pull length sets the power,
        /// so aim and commitment are one gesture and the preview can promise exactly where the run
        /// ends. No longer offered in the UI - it asks the player to understand power before they
        /// have understood movement - but kept because it is the more expressive of the two and
        /// still the one a returning player may want.
        /// </summary>
        Drag = 0,

        /// <summary>
        /// Tap a spot and he runs at it - as far as the tap if he can reach it, flat out towards it
        /// if he cannot. The default, and what the tutorial teaches: one touch instead of a held
        /// gesture, which is easier on a phone and takes one sentence to explain.
        /// </summary>
        Tap = 1,
    }
}
