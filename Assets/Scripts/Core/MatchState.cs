using System.Collections.Generic;
using System.Linq;

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

        public PlayerSide? WinnerSide;

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
