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
        /// Tap a spot and he runs all the way to it, round anything in the way, inside the one phase.
        /// The default, and what the tutorial teaches: one touch instead of a held
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

        /// <summary>
        /// A slingshot you can draw anywhere on the screen: pull one way, he runs the other, as far
        /// as you pulled.
        ///
        /// The pull's expressiveness without its one real cost. A slingshot has to be started on the
        /// gladiator, so the hand that aims him is also the hand covering him, and on a phone that
        /// means aiming blind. Starting anywhere lets the thumb work in the empty half of the screen
        /// while the eyes stay on the fight.
        ///
        /// It is a slingshot like the pull is, drawn back one way to run the other - the two controls
        /// answer a drag the same way, which two controls in one game had better do.
        /// </summary>
        Swipe = 2,
    }
}
