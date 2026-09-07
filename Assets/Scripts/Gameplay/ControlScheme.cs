namespace ColosseumDuel.Gameplay
{
    /// <summary>
    /// How the player orders a move. Tapping is what the game offers; the other is kept in code.
    ///
    /// Tap is deliberately the zero value. A C# field initialiser only applies to components created
    /// after it was written - a scene that already stored this field keeps whatever number is in the
    /// .unity file, so making Tap the default in code left the built game on Drag and every tap on
    /// the sand did nothing. Numbering the default zero means both the fresh component and the old
    /// serialised zero mean the same thing.
    /// </summary>
    public enum ControlScheme
    {
        /// <summary>
        /// Tap a spot and he runs at it - as far as the tap if he can reach it, flat out towards it
        /// if he cannot. The default, and what the tutorial teaches: one touch instead of a held
        /// gesture, which is easier on a phone and takes one sentence to explain.
        /// </summary>
        Tap = 0,

        /// <summary>
        /// Pull back from the gladiator and release, slingshot style. The pull length sets the power,
        /// so aim and commitment are one gesture and the preview can promise exactly where the run
        /// ends. No longer offered in the UI - it asks the player to understand power before they
        /// have understood movement - but kept because it is the more expressive of the two and
        /// still the one a returning player may want.
        /// </summary>
        Drag = 1,
    }
}
