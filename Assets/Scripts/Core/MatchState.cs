using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace ColosseumDuel.Core
{
    public sealed class PlayerState
    {
        public PlayerSide Side;
        public List<GladiatorInstance> Roster = new List<GladiatorInstance>();
        public GladiatorInstance Active; // null while waiting to pick

        public bool NeedsPick => Active == null && Roster.Any(g => g.Alive);
        public bool HasAnyAlive => Roster.Any(g => g.Alive);

        /// <summary>
        /// Seconds into the coming action phase at which this side's first blow lands, or negative
        /// if it never does. Written when the phase starts and read by the view.
        ///
        /// It can be known because the phase is deterministic: both plans are in before anyone
        /// moves, and nothing during the phase depends on anything outside it. That is what lets the
        /// swing animation start early enough for the weapon to arrive on the frame the blow does,
        /// instead of the blow landing and the swing beginning afterwards.
        /// </summary>
        public float StrikeEta = -1f;
    }

    public sealed class MatchState
    {
        public MatchPhase Phase = MatchPhase.Start;
        public int Round = 0;
        public int Cycle = 0;
        public float PhaseTimer = 0f;

        public PlayerState P1 = new PlayerState { Side = PlayerSide.P1 };
        public PlayerState Bot = new PlayerState { Side = PlayerSide.Bot };
        public ItemSystem Items;
        public TrapSystem Traps;

        /// <summary>
        /// What stands on the sand. Set here rather than only when a match starts, so the views can
        /// build the columns and crates before there is a match to read them from - the layout is
        /// the arena's, not the match's.
        /// </summary>
        public ObstacleField Obstacles = ObstacleField.Standard();

        public PlayerSide? WinnerSide;

        /// <summary>
        /// True for a player's very first fight, which is laid out to teach rather than to be fair:
        /// a sword within one dash of whoever they picked, no traps on the way to it, and labels on
        /// everything. Every later match runs on the ordinary rules.
        /// </summary>
        public bool Tutorial;

        /// <summary>
        /// Where the tutorial is telling the player to tap - just past the sword, so the run picks
        /// it up on the way through. Zero when no tutorial is running.
        /// </summary>
        public Vector2 TutorialTapPoint;

        // action-phase collision/pass-by bookkeeping for the current cycle
        public bool Collided;
        public float? CollisionEndTimer;

        public PlayerState Get(PlayerSide side) => side == PlayerSide.P1 ? P1 : Bot;
        public PlayerState Other(PlayerSide side) => side == PlayerSide.P1 ? Bot : P1;
    }
}
