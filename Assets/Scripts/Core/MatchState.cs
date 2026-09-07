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
        /// <summary>True while the two actives are inside PassByDistance without having collided.
        /// A pass-by resolves when they leave that band again, or when the action phase ends while
        /// they are still inside it (otherwise ending a phase in a near-miss would deal no damage).</summary>
        public bool WasNear;
        public float? CollisionEndTimer;

        public PlayerState Get(PlayerSide side) => side == PlayerSide.P1 ? P1 : Bot;
        public PlayerState Other(PlayerSide side) => side == PlayerSide.P1 ? Bot : P1;
    }
}
