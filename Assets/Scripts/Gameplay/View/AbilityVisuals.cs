using ColosseumDuel.Core;
using UnityEngine;

namespace ColosseumDuel.Gameplay.View
{
    /// <summary>
    /// How an ability looks: its colour, and how brightly anything lasting from it glows.
    ///
    /// One place for both, because three things draw an ability - the ring that goes out when it
    /// fires, the effect on the man while it lasts, and the aura at his feet - and all three have to
    /// be recognisably the same thing.
    ///
    /// An ability's glow is steady for as long as it lasts and blinks slowly through the last round,
    /// so "this is the last turn you have it" reads at his feet. The weapon blessing used to share the
    /// rule; its last round now turns the weapon yellow instead - see GladiatorView.SyncWeaponGlow.
    /// </summary>
    public static class AbilityVisuals
    {
        /// <summary>One slow blink of a last round's glow, in real seconds.</summary>
        public const float BlinkPeriod = 1.2f;

        /// <summary>How far the blink fades: to a tenth, never quite out.</summary>
        public const float BlinkFloor = 0.1f;

        /// <summary>
        /// How strongly something lasting glows this frame, 0 to 1: nothing when it is not there,
        /// steady while it lasts, blinking through its last round. On unscaled time - planning slows
        /// the world to a fifth, and a blink at a fifth of its speed is not a blink.
        /// </summary>
        public static float GlowStrength(bool on, bool lastRound)
        {
            if (!on) return 0f;
            if (!lastRound) return 1f;
            float wave = 0.5f + 0.5f * Mathf.Cos(Time.unscaledTime * Mathf.PI * 2f / BlinkPeriod);
            return BlinkFloor + (1f - BlinkFloor) * wave;
        }

        /// <summary>The colour of an ability, wherever it is drawn.</summary>
        public static Color ColorFor(AbilityKey key)
        {
            switch (key)
            {
                case AbilityKey.Earthshaker: return new Color(1.00f, 0.62f, 0.18f);  // the ground giving
                case AbilityKey.Rampage: return new Color(0.35f, 0.85f, 1.00f);      // quick, cold
                case AbilityKey.StoneSkin: return new Color(0.72f, 0.66f, 0.55f);    // stone
                case AbilityKey.Bloodlust: return new Color(0.90f, 0.10f, 0.16f);    // blood drunk
                case AbilityKey.Frenzy: return new Color(0.75f, 0.12f, 0.30f);       // the wound
                case AbilityKey.Berserk: return new Color(1.00f, 0.32f, 0.20f);      // blood up
                case AbilityKey.Backstab: return new Color(0.55f, 0.45f, 0.85f);     // unseen
                case AbilityKey.Riposte: return new Color(0.60f, 0.85f, 1.00f);      // steel flash
                case AbilityKey.Mongoose: return new Color(1.00f, 0.86f, 0.25f);     // the second strike
                case AbilityKey.Bulwark: return new Color(0.55f, 0.72f, 1.00f);      // the wall
                case AbilityKey.Testudo: return new Color(0.35f, 0.80f, 0.45f);      // the shell
                case AbilityKey.ShieldBash: return new Color(1.00f, 0.45f, 0.22f);   // impact
                case AbilityKey.Lunge: return new Color(0.40f, 0.95f, 0.60f);        // the long point
                case AbilityKey.Brace: return new Color(1.00f, 0.38f, 0.30f);        // set against it
                case AbilityKey.SecondWind: return new Color(0.40f, 0.95f, 0.45f);   // healing
                case AbilityKey.Net: return new Color(0.80f, 0.80f, 0.70f);          // rope
                case AbilityKey.Shackles: return new Color(0.70f, 0.40f, 0.95f);     // the chain
                case AbilityKey.TridentThrow: return new Color(0.40f, 0.70f, 1.00f); // thrown steel
                default: return new Color(0.95f, 0.65f, 0.15f);
            }
        }

        /// <summary>
        /// Whether the ability leaves something on the man who used it while it lasts. Not Second
        /// Wind: it heals once and is spent, and an aura after would say it was still doing something.
        /// Not Net or Shackles either - they act on the other man, not on the one who threw them.
        /// </summary>
        public static bool AurasOnUser(AbilityKey key)
            => key != AbilityKey.SecondWind && key != AbilityKey.Net && key != AbilityKey.Shackles;
    }
}
