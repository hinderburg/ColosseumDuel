namespace ColosseumDuel.Gameplay
{
    /// <summary>
    /// How the player orders a move. Drawing is what the game offers; the others are kept in code.
    ///
    /// The default is deliberately the zero value. A C# field initialiser only applies to components
    /// created after it was written - a scene that already stored this field keeps whatever number is
    /// in the .unity file, and making a new control the default in code once left the built game on
    /// the old one. Numbering the default zero means both the fresh component and an old serialised
    /// zero mean the same thing, which is why Draw took zero from Tap rather than taking a new number.
    /// </summary>
    public enum ControlScheme
    {
        /// <summary>
        /// Draw the run with a finger. The first touch can land on him or anywhere else - he is
        /// joined to it by the way he would run there - and from then on the run follows the
        /// finger. It stops where the finger lifts or where his speed runs out, whichever is first.
        /// A tap without dragging is a straight run to the tap, so this covers tapping too.
        /// </summary>
        Draw = 0,

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

        /// <summary>
        /// Tap a spot and he runs to it, round anything in the way - as far as his speed carries him.
        /// Held and dragged, the order follows the finger. What the game opened on before drawing.
        /// </summary>
        Tap = 3,
    }
}
