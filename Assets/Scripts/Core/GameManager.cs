using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace ColosseumDuel.Core
{
    /// <summary>
    /// Deterministic, presentation-agnostic simulation of one match. Equivalent to the JS
    /// prototype's state machine (state.phase / tick() / submitAction() / stepAction()).
    /// A thin MonoBehaviour (see Gameplay/GameController.cs) owns an instance of this, calls
    /// Tick(Time.deltaTime) every frame, and reflects MatchState back onto the actual scene
    /// (3D models, UI, camera). Nothing in here touches a GameObject, so it can be unit tested
    /// and iterated on outside Play Mode.
    /// </summary>
    public sealed class GameManager
    {
        public MatchState State { get; } = new MatchState();
        public event Action<MatchState> PhaseChanged;

        private readonly System.Random _rng;

        public GameManager(System.Random rng = null)
        {
            _rng = rng ?? new System.Random();
        }

        // ------------------------------------------------------------------
        // match lifecycle
        // ------------------------------------------------------------------

        public void StartMatch(IEnumerable<GladiatorDef> p1Squad, IEnumerable<GladiatorDef> botSquad)
        {
            State.P1.Roster = p1Squad.Select(d => new GladiatorInstance(d)).ToList();
            State.Bot.Roster = botSquad.Select(d => new GladiatorInstance(d)).ToList();
            State.Items = new ItemSystem(_rng);
            State.Items.SpawnInitial();
            State.Round = 0;
            State.Cycle = 0;
            State.WinnerSide = null;

            BeginRoundPick();
        }

        private void SetPhase(MatchPhase phase)
        {
            State.Phase = phase;
            State.PhaseTimer = 0f;
            PhaseChanged?.Invoke(State);
        }

        // ------------------------------------------------------------------
        // pick phase
        // ------------------------------------------------------------------

        private void BeginRoundPick()
        {
            SetPhase(MatchPhase.Pick);
            if (State.Bot.NeedsPick) BotAutoPick();
        }

        public bool SubmitPick(PlayerSide side, GladiatorId id)
        {
            if (State.Phase != MatchPhase.Pick) return false;
            var player = State.Get(side);
            if (!player.NeedsPick) return false;

            var chosen = player.Roster.FirstOrDefault(g => g.Def.Id == id && g.Alive);
            if (chosen == null) return false;

            chosen.ResetForNewRound();
            player.Active = chosen;

            if (!State.P1.NeedsPick && !State.Bot.NeedsPick)
                ConfirmPicksAndReveal();

            return true;
        }

        private void BotAutoPick()
        {
            var alive = State.Bot.Roster.Where(g => g.Alive).ToList();
            if (alive.Count == 0) return;
            // simple heuristic: whichever gladiator currently has the highest HP fraction
            var pick = alive.OrderByDescending(g => g.Hp / g.Def.MaxHp).First();
            SubmitPick(PlayerSide.Bot, pick.Def.Id);
        }

        private void ConfirmPicksAndReveal()
        {
            State.Round++;
            State.Cycle = 0;
            PlaceFightersForRound();
            // Reveal is a real phase with a duration (see Tick) so the UI can show both picks
            // before planning opens - it used to be skipped in the same frame it was entered.
            SetPhase(MatchPhase.Reveal);
        }

        /// <summary>
        /// Puts both actives on opposite sides of the arena, facing each other. Runs at the start of
        /// every round, for a freshly picked gladiator and a surviving one alike: the round winner
        /// keeps HP and carried items (per the design doc) but not last round's leftover position.
        /// </summary>
        private void PlaceFightersForRound()
        {
            float d = GameConstants.ArenaRadius * GameConstants.SpawnDistanceFraction;
            Place(State.P1.Active, new Vector2(-d, 0f), Vector2.right);
            Place(State.Bot.Active, new Vector2(d, 0f), Vector2.left);
        }

        private static void Place(GladiatorInstance g, Vector2 pos, Vector2 facing)
        {
            if (g == null) return;
            g.Pos = pos;
            g.Vel = Vector2.zero;
            g.Facing = facing;
        }

        // ------------------------------------------------------------------
        // planning / action cycle
        // ------------------------------------------------------------------

        private void StartCycle()
        {
            State.Cycle++;
            State.P1.Active?.BeginCycle();
            State.Bot.Active?.BeginCycle();
            SetPhase(MatchPhase.Planning);
        }

        public bool SubmitPlanningAction(PlayerSide side, ActionType action, Vector2 aimDirection, float power, bool useAbility)
        {
            if (State.Phase != MatchPhase.Planning) return false;
            var g = State.Get(side).Active;
            if (g == null || !g.Alive) return false;

            g.PlannedAction = action;
            g.PlannedAimDirection = aimDirection.sqrMagnitude > 0.0001f ? aimDirection.normalized : Vector2.zero;
            g.PlannedPower = Mathf.Clamp01(power);
            g.AbilityArmed = useAbility && g.CanActivateAbility;
            return true;
        }

        /// <summary>Simulates the full bounced trajectory for a prospective Move, for UI preview
        /// while the player is still dragging. Mirrors computeTrajectoryPreview() in the JS build.</summary>
        public static List<Vector2> ComputeTrajectoryPreview(GladiatorInstance g, Vector2 aimDirection, float power, float stepSeconds = 0.05f)
        {
            var points = new List<Vector2>();
            Vector2 pos = g.Pos;
            Vector2 vel = aimDirection.normalized * (g.EffectiveSpeed() * GameConstants.SpeedScale * Mathf.Clamp01(power));
            float t = 0f;
            points.Add(pos);
            float maxCenterDist = GameConstants.ArenaRadius - GameConstants.GladiatorRadius;
            while (t < GameConstants.ActionTime)
            {
                pos += vel * stepSeconds;
                if (pos.magnitude > maxCenterDist)
                {
                    Vector2 normal = pos.normalized;
                    pos = normal * maxCenterDist;
                    vel -= 2f * Vector2.Dot(vel, normal) * normal;
                }
                points.Add(pos);
                t += stepSeconds;
            }
            return points;
        }

        /// <summary>Advance the simulation. Call every frame with Time.deltaTime; the manager
        /// internally handles phase timers and (during Action) substepped physics.</summary>
        public void Tick(float dt)
        {
            switch (State.Phase)
            {
                case MatchPhase.Reveal:
                    State.PhaseTimer += dt;
                    if (State.PhaseTimer >= GameConstants.RevealTime)
                        StartCycle();
                    break;

                case MatchPhase.Planning:
                    State.PhaseTimer += dt;
                    if (State.PhaseTimer >= GameConstants.PlanningTime)
                    {
                        AutoFillMissingPlans();
                        BeginActionPhase();
                    }
                    break;

                case MatchPhase.Action:
                    State.PhaseTimer += dt;
                    float subDt = dt / GameConstants.ActionSubsteps;
                    for (int i = 0; i < GameConstants.ActionSubsteps; i++)
                        StepActionSub(subDt);

                    if (State.CollisionEndTimer.HasValue)
                    {
                        State.CollisionEndTimer -= dt;
                        if (State.CollisionEndTimer.Value <= 0f)
                        {
                            EndActionPhase();
                            break;
                        }
                    }
                    if (State.PhaseTimer >= GameConstants.ActionTime)
                        EndActionPhase();
                    break;

                case MatchPhase.RoundEnd:
                    State.PhaseTimer += dt;
                    if (State.PhaseTimer >= GameConstants.RoundEndTime)
                        AfterRoundEndDelay();
                    break;

                default:
                    break; // Start / Pick / MatchEnd are driven by explicit calls, not Tick
            }
        }

        private void AutoFillMissingPlans()
        {
            var bot = State.Bot.Active;
            if (bot != null && bot.Alive && bot.PlannedAction == ActionType.None)
            {
                var decision = BotAI.Decide(bot, State.P1.Active, State.Items, _rng);
                SubmitPlanningAction(PlayerSide.Bot, decision.Action, decision.AimDirection, decision.Power, decision.UseAbility);
            }
            // if the human player didn't submit anything in time, default to Defend
            var p1 = State.P1.Active;
            if (p1 != null && p1.Alive && p1.PlannedAction == ActionType.None)
                SubmitPlanningAction(PlayerSide.P1, ActionType.Defend, Vector2.zero, 0f, false);
        }

        private void BeginActionPhase()
        {
            State.Collided = false;
            State.WasNear = false;
            State.CollisionEndTimer = null;

            ApplyPlannedAction(State.P1.Active, State.Bot.Active);
            ApplyPlannedAction(State.Bot.Active, State.P1.Active);

            SetPhase(MatchPhase.Action);
        }

        private void ApplyPlannedAction(GladiatorInstance g, GladiatorInstance opponent)
        {
            if (g == null || !g.Alive) return;

            // Ability is a supplementary effect - it does NOT consume the whole turn.
            if (g.AbilityArmed && g.CanActivateAbility)
                g.ActivateAbility();

            if (g.PlannedAction == ActionType.Move)
            {
                g.Vel = g.PlannedAimDirection * (g.EffectiveSpeed() * GameConstants.SpeedScale * g.PlannedPower);
                if (g.PlannedAimDirection.sqrMagnitude > 0.0001f) g.Facing = g.PlannedAimDirection;
            }
            else
            {
                g.Vel = Vector2.zero; // Defend (or no plan) - stand still
                // Design doc: a defender turns to face the opponent.
                if (opponent != null)
                {
                    Vector2 toOpponent = opponent.Pos - g.Pos;
                    if (toOpponent.sqrMagnitude > 0.0001f) g.Facing = toOpponent.normalized;
                }
            }
        }

        private void StepActionSub(float dt)
        {
            var a = State.P1.Active;
            var b = State.Bot.Active;

            StepGladiator(a, dt);
            StepGladiator(b, dt);

            if (a == null || b == null || !a.Alive || !b.Alive) return;

            float dist = Vector2.Distance(a.Pos, b.Pos);

            if (State.Collided) return;

            if (dist <= GameConstants.CollideDistance)
            {
                ResolveCollision(a, b);
                return;
            }

            if (dist <= GameConstants.PassByDistance)
            {
                State.WasNear = true;
            }
            else if (State.WasNear)
            {
                // They came close without touching and have now separated again.
                State.WasNear = false;
                ResolvePassBy(a, b);
            }
        }

        private void StepGladiator(GladiatorInstance g, float dt)
        {
            if (g == null || !g.Alive) return;

            float maxCenterDist = GameConstants.ArenaRadius - GameConstants.GladiatorRadius;
            g.Pos += g.Vel * dt;

            // wall bounce
            if (g.Pos.magnitude > maxCenterDist)
            {
                Vector2 normal = g.Pos.normalized;
                g.Pos = normal * maxCenterDist;
                g.Vel -= 2f * Vector2.Dot(g.Vel, normal) * normal;
            }
            if (g.Vel.sqrMagnitude > 0.0001f) g.Facing = g.Vel.normalized;

            // hazard damage - continuous DOT while standing in an active danger ring
            if (HazardSystem.IsInActiveHazard(g.Pos, State.Cycle))
            {
                float dps = GameConstants.HazardDamageFraction * g.Def.MaxHp / GameConstants.ActionTime;
                g.TakeDamage(dps * dt);
            }

            // item pickup
            var item = State.Items.TryPickup(g);
            if (item != null) State.Items.ApplyPickup(g, item);
        }

        private void ResolveCollision(GladiatorInstance a, GladiatorInstance b)
        {
            State.Collided = true;
            State.WasNear = false;
            a.Vel = Vector2.zero;
            b.Vel = Vector2.zero;

            // Both land every attack they still have this cycle - Mongoose (Hilius) gets two.
            // Weapons are single-use, so a second swing is always unarmed.
            ExchangeBlows(a, b, isCollision: true);

            // knock the two apart along the line between them so they can disengage next cycle
            Vector2 mid = (a.Pos + b.Pos) * 0.5f;
            Vector2 dir = (a.Pos - b.Pos);
            if (dir.sqrMagnitude < 0.0001f) dir = Vector2.up;
            dir = dir.normalized;
            a.Pos = mid + dir * (GameConstants.KnockbackDistance * 0.5f);
            b.Pos = mid - dir * (GameConstants.KnockbackDistance * 0.5f);

            State.CollisionEndTimer = GameConstants.CollisionEarlyEndDelay;
        }

        private void ResolvePassBy(GladiatorInstance a, GladiatorInstance b)
        {
            ExchangeBlows(a, b, isCollision: false);
        }

        /// <summary>
        /// One exchange. Each side spends one of the attacks it has left this cycle, so a normal
        /// gladiator lands a single blow while Mongoose (Hilius) gets a second one. The first
        /// exchange is simultaneous - a lethal hit does not rob the dying fighter of their return
        /// blow - but a fighter who died there does not get to throw any follow-up attacks.
        /// </summary>
        private static void ExchangeBlows(GladiatorInstance a, GladiatorInstance b, bool isCollision)
        {
            for (int exchange = 0; a.AttacksRemainingThisCycle > 0 || b.AttacksRemainingThisCycle > 0; exchange++)
            {
                bool aSwings = a.AttacksRemainingThisCycle > 0 && (exchange == 0 || a.Alive);
                bool bSwings = b.AttacksRemainingThisCycle > 0 && (exchange == 0 || b.Alive);

                if (a.AttacksRemainingThisCycle > 0) a.AttacksRemainingThisCycle--;
                if (b.AttacksRemainingThisCycle > 0) b.AttacksRemainingThisCycle--;

                if (aSwings) CombatResolver.DealDamage(a, b, isCollision);
                if (bSwings) CombatResolver.DealDamage(b, a, isCollision);
            }
        }

        private void EndActionPhase()
        {
            // The phase ran out while the two were still inside the pass-by band without colliding.
            // Without this, ending a cycle mid-near-miss would deal no damage to anyone.
            if (!State.Collided && State.WasNear)
            {
                var pa = State.P1.Active;
                var pb = State.Bot.Active;
                State.WasNear = false;
                if (pa != null && pb != null && pa.Alive && pb.Alive)
                    ResolvePassBy(pa, pb);
            }

            State.P1.Active?.ResolveCycleRage();
            State.Bot.Active?.ResolveCycleRage();

            bool p1Died = State.P1.Active != null && !State.P1.Active.Alive;
            bool botDied = State.Bot.Active != null && !State.Bot.Active.Alive;

            if (p1Died || botDied)
            {
                SetPhase(MatchPhase.RoundEnd);
                return;
            }

            StartCycle();
        }

        private void AfterRoundEndDelay()
        {
            bool p1Died = State.P1.Active != null && !State.P1.Active.Alive;
            bool botDied = State.Bot.Active != null && !State.Bot.Active.Alive;

            if (p1Died) State.P1.Active = null;
            if (botDied) State.Bot.Active = null;

            if (!State.P1.HasAnyAlive) { FinishMatch(PlayerSide.Bot); return; }
            if (!State.Bot.HasAnyAlive) { FinishMatch(PlayerSide.P1); return; }

            BeginRoundPick();
        }

        private void FinishMatch(PlayerSide winner)
        {
            State.WinnerSide = winner;
            SetPhase(MatchPhase.MatchEnd);
        }
    }
}
