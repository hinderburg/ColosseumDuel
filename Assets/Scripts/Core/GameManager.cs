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

        // One-shot events for the presentation layer. The views poll MatchState every frame, which
        // works for anything continuous (positions, HP, buffs) but cannot see a moment: a hit that
        // lands and resolves inside one frame leaves no trace in the state to poll. These report
        // those moments without Core knowing that anything is watching.

        /// <summary>
        /// A gladiator was struck by the other gladiator. Carries the side hit and the amount.
        ///
        /// Blows only. The view reads "somebody was hit" as "the other one swung", so anything the
        /// arena itself does to a fighter has to arrive by another route or a gladiator alone at his
        /// end of the sand plays an attack at nobody.
        /// </summary>
        public event Action<PlayerSide, float> Damaged;

        /// <summary>A trap closed on a gladiator. Nobody swung, so nothing should swing.</summary>
        public event Action<PlayerSide, float> Bitten;

        /// <summary>A head-on collision resolved, at this point in virtual space.</summary>
        public event Action<Vector2> Impact;

        /// <summary>A side's ability just fired.</summary>
        public event Action<PlayerSide> AbilityFired;

        /// <summary>
        /// An open wound cost a side health at the top of a cycle.
        ///
        /// Its own event rather than Damaged, because it should not read as a blow: nobody swung,
        /// so there is no recoil to play and nobody to swing back. What it wants is a red flicker.
        /// </summary>
        public event Action<PlayerSide, float> Bled;

        /// <summary>
        /// What the closing arena has cost somebody since the last time it said so.
        ///
        /// The spikes deal their damage continuously, substep by substep, against the position the
        /// gladiator actually occupied - fractions of a point, far too small to put on screen. This
        /// is the running total, announced four times a second so that standing in the fire reads as
        /// burning rather than as one hit from something invisible.
        /// </summary>
        public event Action<PlayerSide, float> Scorched;

        private readonly float[] _scorched = new float[2];

        private readonly System.Random _rng;

        public GameManager(System.Random rng = null)
        {
            _rng = rng ?? new System.Random();
        }

        // ------------------------------------------------------------------
        // match lifecycle
        // ------------------------------------------------------------------

        public void StartMatch(IEnumerable<GladiatorDef> p1Squad, IEnumerable<GladiatorDef> botSquad,
                               bool tutorial = false)
        {
            State.Tutorial = tutorial;
            State.TutorialTapPoint = Vector2.zero;

            State.P1.Roster = p1Squad.Select(d => new GladiatorInstance(d)).ToList();
            State.Bot.Roster = botSquad.Select(d => new GladiatorInstance(d)).ToList();

            // Both sides put back to "nobody chosen yet". Without this a restart kept the previous
            // match's fighter as the active one: NeedsPick reads Active == null, so the pick screen
            // never came up, and that stale instance carried its rage, its running buff and its
            // ability lock into a match that was supposed to start clean.
            State.P1.Active = null;
            State.Bot.Active = null;
            State.Items = new ItemSystem(_rng);
            State.Items.SpawnInitial();
            State.Traps = new TrapSystem(_rng);
            State.Traps.SpawnForRound();
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
            var player = State.Get(side);
            return SubmitPick(side, player.Roster.FirstOrDefault(g => g.Def.Id == id && g.Alive));
        }

        /// <summary>
        /// Picks by squad slot rather than by archetype.
        ///
        /// A squad can now hold two of the same archetype, and once it does, "send in Brutius" is
        /// an ambiguous order: the two are separate fighters with separate health, and the player
        /// choosing between them is choosing which of those two is fresh enough to send.
        /// </summary>
        public bool SubmitPick(PlayerSide side, int rosterIndex)
        {
            var roster = State.Get(side).Roster;
            if (rosterIndex < 0 || rosterIndex >= roster.Count) return false;
            return SubmitPick(side, roster[rosterIndex]);
        }

        private bool SubmitPick(PlayerSide side, GladiatorInstance chosen)
        {
            if (State.Phase != MatchPhase.Pick) return false;
            var player = State.Get(side);
            if (!player.NeedsPick) return false;
            if (chosen == null || !chosen.Alive || !player.Roster.Contains(chosen)) return false;

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
            // By instance, not by archetype: with two of the same in a squad, picking by id would
            // send in whichever came first in the list rather than the one it actually chose.
            var pick = alive.OrderByDescending(g => g.Hp / g.Def.MaxHp).First();
            SubmitPick(PlayerSide.Bot, pick);
        }

        private void ConfirmPicksAndReveal()
        {
            State.Round++;
            State.Cycle = 0;
            PlaceFightersForRound();

            // Fresh traps every round. Left from the last one they would all be sprung by the time
            // the third round started, and the arena would quietly stop having a hazard in it.
            State.Traps?.SpawnForRound();

            if (State.Tutorial && State.Round == 1) ArrangeTutorialRound();
            // Reveal is a real phase with a duration (see Tick) so the UI can show both picks
            // before planning opens - it used to be skipped in the same frame it was entered.
            SetPhase(MatchPhase.Reveal);
        }

        /// <summary>
        /// Puts both actives at opposite ends of the arena, facing each other. Runs at the start of
        /// every round, for a freshly picked gladiator and a surviving one alike: the round winner
        /// keeps HP and carried items (per the design doc) but not last round's leftover position.
        ///
        /// Down the long axis, and always the same way round: the player's fighter at the near end
        /// of the arena and the opponent's at the far end, matching where each side's roster sits on
        /// screen.
        ///
        /// The distance is a fraction of the long semi-axis - the one they are spread along. What
        /// spreading them costs is measured in cycles spent closing rather than in units; see
        /// SpawnDistanceFraction.
        /// </summary>
        private void PlaceFightersForRound()
        {
            float d = ArenaShape.RadiusY * GameConstants.SpawnDistanceFraction;
            Place(State.P1.Active, new Vector2(0f, -d), Vector2.up);
            Place(State.Bot.Active, new Vector2(0f, d), Vector2.down);
        }

        /// <summary>
        /// Rearranges the opening round of a first match into something that can be taught from.
        ///
        /// The lesson is one sentence - tap past a thing and you run through it - so the round has
        /// to be able to deliver that sentence whoever the player picked. Everything here exists to
        /// remove the ways it could fail to.
        /// </summary>
        private void ArrangeTutorialRound()
        {
            var player = State.P1.Active;
            if (player == null || State.Items == null) return;

            // Measured against this gladiator's own dash, not a fixed distance: Hilius covers twice
            // the ground Brutius does, so any single number would be a comfortable stroll for one of
            // them and out of reach for another. At 0.55 of his reach the sword is inside one move
            // for all three, with room to tap past it.
            float reach = player.DashReach();
            var forward = player.Facing.sqrMagnitude > 0.0001f ? player.Facing.normalized : Vector2.up;

            // The gilded copy of his own weapon, so the first thing the player is ever told to do
            // hands them a straight upgrade rather than a different way of fighting they have not
            // been taught yet - and so the lesson is the same lesson whoever they picked.
            var sword = State.Items.Items.FirstOrDefault(i => i.Kind == player.Def.SkilledWith)
                        ?? State.Items.Items.FirstOrDefault();
            if (sword != null) sword.Pos = player.Pos + forward * (reach * 0.55f);

            State.TutorialTapPoint = player.Pos + forward * (reach * 0.85f);

            // Nothing on the path the player is being told to run. Being stopped and bitten by
            // scenery on the one move a tutorial asked for teaches the wrong lesson entirely.
            ClearTrapsBetween(player.Pos, State.TutorialTapPoint);
        }

        private void ClearTrapsBetween(Vector2 from, Vector2 to)
        {
            var traps = State.Traps?.Traps;
            if (traps == null) return;

            float clearance = TrapSystem.TriggerDistance + GameConstants.TrapRadius;
            traps.RemoveAll(t => DistanceToSegment(t.Pos, from, to) < clearance);
        }

        private static float DistanceToSegment(Vector2 point, Vector2 a, Vector2 b)
        {
            var ab = b - a;
            float lengthSq = ab.sqrMagnitude;
            if (lengthSq < 0.0001f) return Vector2.Distance(point, a);

            float t = Mathf.Clamp01(Vector2.Dot(point - a, ab) / lengthSq);
            return Vector2.Distance(point, a + ab * t);
        }

        /// <summary>Which side a gladiator is fighting for, for events that carry the victim.</summary>
        private PlayerSide SideOf(GladiatorInstance g)
            => ReferenceEquals(g, State.Bot.Active) ? PlayerSide.Bot : PlayerSide.P1;

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

        /// <summary>
        /// Files what the gladiator will do this cycle, leaving any armed ability alone.
        ///
        /// The two are separate on purpose. They used to arrive together, which quietly made the
        /// order of the player's button presses matter: arm then move worked, move then arm lost the
        /// ability, and arming without moving lost it too - the auto-filled guard came in with the
        /// flag cleared. The ability is a supplement to a turn, so it is filed as its own decision
        /// and survives however many times the move is changed before the phase ends.
        /// </summary>
        public bool SubmitPlanningAction(PlayerSide side, ActionType action, Vector2 aimDirection, float power)
        {
            if (State.Phase != MatchPhase.Planning) return false;
            var g = State.Get(side).Active;
            if (g == null || !g.Alive) return false;

            g.PlannedAction = action;
            g.PlannedAimDirection = aimDirection.sqrMagnitude > 0.0001f ? aimDirection.normalized : Vector2.zero;
            g.PlannedPower = Mathf.Clamp01(power);
            return true;
        }

        /// <summary>Files both at once, for callers that decide them together - the bot, and tests.</summary>
        public bool SubmitPlanningAction(PlayerSide side, ActionType action, Vector2 aimDirection, float power, bool useAbility)
        {
            if (!SubmitPlanningAction(side, action, aimDirection, power)) return false;
            SubmitAbility(side, useAbility);
            return true;
        }

        /// <summary>
        /// Arms or disarms the ability for this cycle, on its own.
        ///
        /// Refuses to arm one that could not fire, so a lit button always means something will
        /// happen - arming a half-charged meter would just do nothing when the phase resolved.
        ///
        /// Returns the state it ended up in, not whether the request was honoured. A caller
        /// mirroring this into a button needs to know what is armed; told only "yes, done" it would
        /// light the button back up on the very press meant to turn it off.
        /// </summary>
        public bool SubmitAbility(PlayerSide side, bool armed)
        {
            if (State.Phase != MatchPhase.Planning) return false;
            var g = State.Get(side).Active;
            if (g == null || !g.Alive) return false;

            g.AbilityArmed = armed && g.CanActivateAbility;
            return g.AbilityArmed;
        }

        /// <summary>Simulates the full bounced trajectory for a prospective Move, for UI preview
        /// while the player is still dragging. Mirrors computeTrajectoryPreview() in the JS build.</summary>
        public static List<Vector2> ComputeTrajectoryPreview(GladiatorInstance g, Vector2 aimDirection, float power, float stepSeconds = 0.05f)
        {
            var points = new List<Vector2>();
            Vector2 pos = g.Pos;

            // Clamped through the same envelope the action phase will clamp the order through, so
            // the preview cannot promise a turn he is not going to make. Doing it here rather than
            // in the caller means every drawing of a run gets it, including the one left on screen
            // after the order was given.
            var heading = MoveEnvelope.For(g).ClampAim(aimDirection.normalized);

            // PlannedSpeed, not EffectiveSpeed: an armed ability has not fired yet, and this is a
            // drawing of what is about to happen rather than of what is happening.
            Vector2 vel = heading * (g.PlannedSpeed() * GameConstants.SpeedScale * Mathf.Clamp01(power));
            float t = 0f;
            points.Add(pos);
            while (t < GameConstants.ActionTime)
            {
                pos += vel * stepSeconds;
                // Same bounce the action phase will run, so the preview stays a promise.
                ArenaShape.Bounce(ref pos, ref vel, GameConstants.GladiatorRadius);
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

                    AdvanceScorchTicks();

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
            // If the human player didn't submit a move in time, default to Defend - but only the
            // move. Passing the ability flag here too was what threw away an ability armed by a
            // player who then chose not to move at all.
            var p1 = State.P1.Active;
            if (p1 != null && p1.Alive && p1.PlannedAction == ActionType.None)
                SubmitPlanningAction(PlayerSide.P1, ActionType.Defend, Vector2.zero, 0f);
        }

        private void BeginActionPhase()
        {
            State.Collided = false;
            State.CollisionEndTimer = null;

            // Wounds settle up before anyone moves, which is the whole shape of a bleed: it is the
            // cost of having started the cycle already cut, and a fighter it kills does not get to
            // spend that cycle as though he were whole.
            Bleed(PlayerSide.P1, State.P1.Active);
            Bleed(PlayerSide.Bot, State.Bot.Active);

            ApplyPlannedAction(PlayerSide.P1, State.P1.Active, State.Bot.Active);
            ApplyPlannedAction(PlayerSide.Bot, State.Bot.Active, State.P1.Active);
            FaceTravel();

            // Zeroed here rather than only when it is announced, so a phase abandoned partway - a
            // round restarted, a match thrown away - cannot carry its tally into the next one.
            _scorched[0] = 0f;
            _scorched[1] = 0f;

            // And the beat starts with the phase, so the first tick of a burn is a quarter second
            // into it rather than at whatever point in the beat the last phase happened to end on.
            _scorchTicksSent = 0;

            PredictStrikes();
            SetPhase(MatchPhase.Action);
        }

        private void Bleed(PlayerSide side, GladiatorInstance g)
        {
            if (g == null || !g.Alive) return;

            float amount = g.TickBleed();
            if (amount > 0f) Bled?.Invoke(side, amount);
        }

        private void ApplyPlannedAction(PlayerSide side, GladiatorInstance g, GladiatorInstance opponent)
        {
            if (g == null || !g.Alive) return;

            // Ability is a supplementary effect - it does NOT consume the whole turn.
            if (g.AbilityArmed && g.CanActivateAbility)
            {
                g.ActivateAbility();
                AbilityFired?.Invoke(side);
            }

            // Facing is not set here - see FaceTravel, which runs once both sides have been given
            // their velocity. It still holds last cycle's heading at this point, which is exactly
            // what the envelope has to be measured from: the arc drawn during planning was struck
            // about the way he was already looking.
            //
            // The turn is clamped here rather than only where the order is given, so it is a rule of
            // the world and not a manner of the interface. The bot files aims through the same
            // field, and an order that reached this line by any other route - a test, a replay -
            // would otherwise be allowed a turn no player could ask for.
            if (g.PlannedAction == ActionType.Move)
            {
                var aim = MoveEnvelope.For(g).ClampAim(g.PlannedAimDirection);
                g.Vel = aim * (g.EffectiveSpeed() * GameConstants.SpeedScale * g.PlannedPower);
            }
            else
            {
                g.Vel = Vector2.zero; // Defend, or no plan at all - stand still
            }
        }

        /// <summary>
        /// Turns each fighter to look the way he is going.
        ///
        /// They used to look at each other whatever they were doing, which read well and made the
        /// direction a man faces mean nothing. It means something now: it decides which of him a
        /// blow lands on. Turning your back on the other one is a thing you can do, and it costs.
        ///
        /// Standing still keeps the last heading rather than falling back to anything, so a
        /// gladiator who stops is still facing where he was going - which is what decides the sector
        /// for the rest of the cycle.
        /// </summary>
        private void FaceTravel()
        {
            FaceTravel(State.P1.Active);
            FaceTravel(State.Bot.Active);
        }

        private static void FaceTravel(GladiatorInstance g)
        {
            if (g == null || !g.Alive) return;
            if (g.Vel.sqrMagnitude < 0.0001f) return;

            g.Facing = g.Vel.normalized;
        }

        /// <summary>
        /// Works out when in the coming phase each side's first blow lands, before anyone has moved.
        ///
        /// The phase is deterministic once both plans are in: everything that decides where the two
        /// end up - speed, aim, power, the wall - is known now, and nothing during the phase depends
        /// on anything outside it. So the answer can be had by running the movement forward.
        ///
        /// Movement only. Damage, rage, traps and the hazard are left out on purpose: they change
        /// what the phase costs, not where anybody is, and a prediction that also dealt damage would
        /// be a second simulation quietly disagreeing with the first. Collisions are the exception,
        /// because running into somebody is an exchange and there is nothing after it worth
        /// predicting.
        ///
        /// A weapon picked up mid-run counts from the moment it is picked up, which is the whole
        /// reason a fighter runs at one: the reach he strikes with is the reach he has when he
        /// arrives, not the one he set off with.
        /// </summary>
        private void PredictStrikes()
        {
            State.P1.StrikeEta = -1f;
            State.Bot.StrikeEta = -1f;

            var a = State.P1.Active;
            var b = State.Bot.Active;
            if (a == null || b == null || !a.Alive || !b.Alive) return;

            Vector2 posA = a.Pos, velA = a.Vel, posB = b.Pos, velB = b.Vel;
            Vector2 lookA = a.Facing, lookB = b.Facing;
            WeaponDef armedA = a.WeaponDef, armedB = b.WeaponDef;

            int steps = Mathf.CeilToInt(GameConstants.ActionTime / PredictionStep);
            float t = 0f;

            for (int step = 0; step < steps; step++)
            {
                var wasA = posA;
                var wasB = posB;

                posA += velA * PredictionStep;
                ArenaShape.Bounce(ref posA, ref velA, GameConstants.GladiatorRadius);
                posB += velB * PredictionStep;
                ArenaShape.Bounce(ref posB, ref velB, GameConstants.GladiatorRadius);

                t += PredictionStep;

                // What he will be holding by the time he gets there, and the way he will be
                // looking once he is moving - both of them the future, not the present.
                armedA = LongerOf(armedA, WeaponAt(posA));
                armedB = LongerOf(armedB, WeaponAt(posB));
                if (velA.sqrMagnitude > 0.000001f) lookA = velA.normalized;
                if (velB.sqrMagnitude > 0.000001f) lookB = velB.normalized;

                float distance = ClosestApproach(wasA, posA, wasB, posB, out float when);
                var metA = Vector2.Lerp(wasA, posA, when);
                var metB = Vector2.Lerp(wasB, posB, when);

                if (State.P1.StrikeEta < 0f && GladiatorInstance.WithinSwing(metA, lookA, armedA, metB))
                    State.P1.StrikeEta = t;
                if (State.Bot.StrikeEta < 0f && GladiatorInstance.WithinSwing(metB, lookB, armedB, metA))
                    State.Bot.StrikeEta = t;

                // A collision is an exchange, and both sides swing in it whatever their reach.
                if (distance <= GameConstants.CollideDistance)
                {
                    if (State.P1.StrikeEta < 0f) State.P1.StrikeEta = t;
                    if (State.Bot.StrikeEta < 0f) State.Bot.StrikeEta = t;
                    return;
                }

                if (State.P1.StrikeEta >= 0f && State.Bot.StrikeEta >= 0f) return;
            }
        }

        /// <summary>
        /// The weapon lying close enough to be swept up at this point, or null.
        ///
        /// Null rather than a miss, so callers can take the longer of this and what they carry: a
        /// fighter never picks up something shorter than the weapon in his hands and loses reach.
        /// </summary>
        private WeaponDef WeaponAt(Vector2 pos)
        {
            var items = State.Items?.Items;
            if (items == null) return null;

            WeaponDef best = null;
            foreach (var item in items)
            {
                if (item == null) continue;
                if (Vector2.Distance(pos, item.Pos) > GameConstants.PickupDistance) continue;
                best = LongerOf(best, WeaponDef.Get(item.Kind));
            }
            return best;
        }

        /// <summary>Whichever of the two strikes further. Either may be null.</summary>
        private static WeaponDef LongerOf(WeaponDef a, WeaponDef b)
        {
            if (a == null) return b;
            if (b == null) return a;
            return b.Reach > a.Reach ? b : a;
        }

        /// <summary>
        /// How finely the prediction walks the phase. Finer than the substeps the phase itself uses,
        /// because this is arithmetic on two positions rather than a full step of the world, and the
        /// answer feeds an animation whose lead time is measured in tenths of a second.
        /// </summary>
        private const float PredictionStep = GameConstants.ActionTime / 60f;

        private void StepActionSub(float dt)
        {
            var a = State.P1.Active;
            var b = State.Bot.Active;

            var wasA = a?.Pos ?? Vector2.zero;
            var wasB = b?.Pos ?? Vector2.zero;

            StepGladiator(a, dt);
            StepGladiator(b, dt);
            FaceTravel();

            if (a == null || b == null || !a.Alive || !b.Alive) return;

            if (State.Collided) return;

            if (Vector2.Distance(a.Pos, b.Pos) <= GameConstants.CollideDistance)
            {
                ResolveCollision(a, b);
                return;
            }

            float nearest = ClosestApproach(wasA, a.Pos, wasB, b.Pos, out float when);
            ResolveReachAttacks(a, b, Vector2.Lerp(wasA, a.Pos, when), Vector2.Lerp(wasB, b.Pos, when), nearest);
        }

        /// <summary>
        /// How near the two came at any point during this substep, not merely where they ended it.
        ///
        /// A gladiator at full speed covers most of a body length per substep, so two running past
        /// each other can start the substep out of reach and end it out of reach on the other side,
        /// having crossed. Sampling the endpoints alone misses exactly the case this is for - a
        /// blow struck in passing - and misses it more often the faster the fighter, which is the
        /// wrong way round.
        ///
        /// Both move at a constant velocity across a substep, so the distance between them is a
        /// quadratic in time and its minimum is one division.
        /// </summary>
        private static float ClosestApproach(Vector2 fromA, Vector2 toA, Vector2 fromB, Vector2 toB,
            out float at)
        {
            var separation = fromA - fromB;
            var closing = (toA - fromA) - (toB - fromB);

            float speedSq = closing.sqrMagnitude;
            if (speedSq < 0.000001f)
            {
                at = 0f;
                return separation.magnitude;
            }

            at = Mathf.Clamp01(-Vector2.Dot(separation, closing) / speedSq);
            return (separation + closing * at).magnitude;
        }

        private void StepGladiator(GladiatorInstance g, float dt)
        {
            if (g == null || !g.Alive) return;

            g.Pos += g.Vel * dt;
            ArenaShape.Bounce(ref g.Pos, ref g.Vel, GameConstants.GladiatorRadius);

            // hazard damage - continuous DOT while standing in an active danger ring
            if (HazardSystem.IsInActiveHazard(g.Pos, State.Cycle))
            {
                float dps = GameConstants.HazardDamagePerPhase / GameConstants.ActionTime;
                float bite = dps * dt;
                g.TakeDamage(bite);

                // Totalled rather than announced. This runs once per substep of every frame, so a
                // listener told each time would be told a hundred times a phase about a fraction of
                // a point - which is not something anybody can be shown. The total goes out once,
                // when the phase ends.
                _scorched[(int)SideOf(g)] += bite;
            }

            // item pickup
            var item = State.Items.TryPickup(g);
            if (item != null) State.Items.ApplyPickup(g, item);

            // traps - checked after the move, so a gladiator who ran into one this substep is
            // stopped at the jaws rather than a substep past them.
            var trap = State.Traps?.TryTrigger(g);
            if (trap != null)
            {
                Bitten?.Invoke(SideOf(g), TrapSystem.Damage);
                Impact?.Invoke(trap.Pos);
            }
        }

        private void ResolveCollision(GladiatorInstance a, GladiatorInstance b)
        {
            State.Collided = true;

            Impact?.Invoke((a.Pos + b.Pos) * 0.5f);

            // Both land every attack they still have this cycle - a weapon that swings twice lands
            // twice, and Mongoose doubles whatever that was.
            ExchangeBlows(a, b);

            // A step back rather than a shove apart.
            //
            // They used to be teleported to opposite ends of a fixed distance the instant they
            // touched. On screen that is not a collision, it is a cut: two fighters meet and are
            // suddenly somewhere else, and it landed on exactly the frame their swings were meant
            // to be playing. Now they are set moving backwards and the rest of the phase carries
            // them, so the whole thing reads as one continuous beat - the blow, then the recoil.
            Vector2 apart = a.Pos - b.Pos;
            if (apart.sqrMagnitude < 0.0001f) apart = Vector2.up;
            apart = apart.normalized;

            // Just enough to stop them standing inside one another, since bodies that overlap read
            // as one shape whatever they do next. Everything past that is the bounce.
            Vector2 mid = (a.Pos + b.Pos) * 0.5f;
            a.Pos = mid + apart * (GameConstants.CollideDistance * 0.5f);
            b.Pos = mid - apart * (GameConstants.CollideDistance * 0.5f);

            a.Vel = apart * GameConstants.BounceSpeed;
            b.Vel = -apart * GameConstants.BounceSpeed;

            // Put them back inside the wall here rather than leaving it to the next step. A
            // collision against the wall throws one of them through it, and because the collision
            // also ends the action phase, nothing steps him again until the next one - so he would
            // stand outside the arena for the whole of planning, which is seconds, not a frame.
            ArenaShape.Bounce(ref a.Pos, ref a.Vel, GameConstants.GladiatorRadius);
            ArenaShape.Bounce(ref b.Pos, ref b.Vel, GameConstants.GladiatorRadius);

            State.CollisionEndTimer = GameConstants.CollisionEarlyEndDelay;
        }

        /// <summary>
        /// A blow the moment the other one comes inside your weapon's reach - running past him, or
        /// standing your ground while he runs past you, or simply ending the cycle next to him.
        ///
        /// Checked every substep rather than only when the phase ends. Ending in range and passing
        /// through range are the same event to a fighter with a weapon in his hand, and resolving
        /// only at the end meant a charge straight through somebody cost nothing at all - the one
        /// approach in the game that most obviously should.
        ///
        /// Nothing needs a "have they attacked yet" flag: an attack is spent out of
        /// AttacksRemainingThisCycle, so a gladiator who swings on the substep he comes into range
        /// has nothing left to swing again with while he is still there. The budget is the cooldown.
        ///
        /// Each side is checked against its own weapon, so a mace user really can land a blow from a
        /// distance a pair of short blades cannot answer from - and from an angle they cannot either.
        /// That asymmetry is the reason reach and swing arc are stats rather than constants.
        ///
        /// The pair of positions is where the two came nearest during the substep, not where they
        /// ended it. A blow struck in passing is struck somewhere in the middle of a step, and the
        /// wedge has to be tested at that moment or a charge that swept through somebody would be
        /// judged on the angle it had after it had gone past.
        /// </summary>
        private void ResolveReachAttacks(GladiatorInstance a, GladiatorInstance b,
            Vector2 atA, Vector2 atB, float distance)
        {
            if (a == null || b == null || !a.Alive || !b.Alive) return;
            if (distance > Mathf.Max(a.WeaponDef.Reach, b.WeaponDef.Reach)) return;

            bool aReaches = a.AttacksRemainingThisCycle > 0 && a.CanStrikeFrom(atA, atB);
            bool bReaches = b.AttacksRemainingThisCycle > 0 && b.CanStrikeFrom(atB, atA);
            if (!aReaches && !bReaches) return;

            ExchangeBlows(a, b, aReaches, bReaches);
        }

        /// <summary>
        /// One exchange. Each side spends the attacks it has left this cycle, so a weapon that
        /// swings twice lands twice and Mongoose doubles whatever that was. The first exchange is
        /// simultaneous - a lethal hit does not rob the dying fighter of their return blow - but a
        /// fighter who died there does not get to throw any follow-up attacks.
        ///
        /// A side out of reach neither swings nor spends anything: reach decides who is in the
        /// exchange at all, and the two sides can disagree about the answer.
        /// </summary>
        /// <param name="a">Always the player's active gladiator.</param>
        /// <param name="b">Always the bot's.</param>
        private void ExchangeBlows(GladiatorInstance a, GladiatorInstance b,
            bool aMaySwing = true, bool bMaySwing = true)
        {
            for (int exchange = 0;
                 (aMaySwing && a.AttacksRemainingThisCycle > 0) || (bMaySwing && b.AttacksRemainingThisCycle > 0);
                 exchange++)
            {
                bool aSwings = aMaySwing && a.AttacksRemainingThisCycle > 0 && (exchange == 0 || a.Alive);
                bool bSwings = bMaySwing && b.AttacksRemainingThisCycle > 0 && (exchange == 0 || b.Alive);

                if (aMaySwing && a.AttacksRemainingThisCycle > 0) a.AttacksRemainingThisCycle--;
                if (bMaySwing && b.AttacksRemainingThisCycle > 0) b.AttacksRemainingThisCycle--;

                // Deal first, announce second. Folding the call into Damaged?.Invoke(...) would put
                // it inside a null-conditional, and with no subscriber the argument is never
                // evaluated - so nobody watching would mean nobody taking damage.
                if (aSwings)
                {
                    float dealt = CombatResolver.DealDamage(a, b);
                    Damaged?.Invoke(PlayerSide.Bot, dealt);
                }
                if (bSwings)
                {
                    float dealt = CombatResolver.DealDamage(b, a);
                    Damaged?.Invoke(PlayerSide.P1, dealt);
                }

                // After the exchange, not between the two halves of it: shoving the defender out of
                // reach mid-exchange would rob him of the return blow he is owed for standing there.
                if (aSwings) Shove(a, b);
                if (bSwings) Shove(b, a);
            }
        }

        /// <summary>
        /// Throws the target back along the line between the pair, if the weapon does that.
        ///
        /// The mace's whole character. Bounced off the wall here rather than left to the next step,
        /// for the same reason a collision is: a shove that lands as the phase ends would otherwise
        /// leave a gladiator standing outside the arena for the length of a planning phase.
        /// </summary>
        private static void Shove(GladiatorInstance attacker, GladiatorInstance target)
        {
            float distance = attacker.WeaponDef.Knockback;
            if (distance <= 0f || !target.Alive) return;

            var away = target.Pos - attacker.Pos;
            if (away.sqrMagnitude < 0.0001f) away = Vector2.up;

            target.Pos += away.normalized * distance;
            var still = Vector2.zero;
            ArenaShape.Bounce(ref target.Pos, ref still, GameConstants.GladiatorRadius);
        }

        private void EndActionPhase()
        {
            // Nothing to resolve here any more. Blows land during the phase, on the substep the two
            // come within reach of each other, so a cycle that ends with them standing together has
            // already been paid for - on the substep they arrived.

            FlushScorched();

            // Everybody stops. The phase is over, and nothing between here and the next action
            // phase moves anyone - but the velocity that carried them here was being left standing,
            // so for the whole four seconds of planning the view was told they were still travelling
            // at a full charge and ran them on the spot.
            if (State.P1.Active != null) State.P1.Active.Vel = Vector2.zero;
            if (State.Bot.Active != null) State.Bot.Active.Vel = Vector2.zero;

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

        /// <summary>
        /// How often the fire says what it has taken, in seconds of the action phase.
        ///
        /// The damage itself is continuous - it is charged per substep, against the position the
        /// gladiator actually occupied - and this is only how often that running total is announced.
        /// A quarter of a second gives four ticks for a second spent in the fire, which reads as
        /// burning; the whole phase in one number read as a single hit from something invisible, and
        /// per substep would be a hundred numbers a second, each of a fraction of a point.
        /// </summary>
        private const float ScorchTickInterval = 0.25f;

        private int _scorchTicksSent;

        /// <summary>
        /// Announces the running tally on the beat, without waiting for the phase to end.
        ///
        /// Counted off the phase's own clock rather than a separate accumulator, and that is not
        /// tidiness. A phase is exactly as long as four intervals, so an accumulator lands its
        /// fourth tick on the same frame the phase ends - and whether it lands just before or just
        /// after is decided by the last bit of a float. Just after, and the phase's closing flush
        /// finds a frame's worth of damage left and puts a fifth number on screen reading "1".
        ///
        /// Counting instead means the closing flush is the last tick rather than a sliver after it,
        /// however the arithmetic rounds.
        /// </summary>
        private void AdvanceScorchTicks()
        {
            int due = Mathf.FloorToInt(State.PhaseTimer / ScorchTickInterval);
            while (_scorchTicksSent < due)
            {
                _scorchTicksSent++;
                FlushScorched();
            }
        }

        /// <summary>
        /// Announces whatever spike damage has been tallied since the last time, and clears it.
        ///
        /// Cleared whether or not anybody was listening, so time spent in the fire cannot be carried
        /// forward and reported twice.
        /// </summary>
        private void FlushScorched()
        {
            for (int side = 0; side < _scorched.Length; side++)
            {
                float total = _scorched[side];
                _scorched[side] = 0f;
                if (total > 0f) Scorched?.Invoke((PlayerSide)side, total);
            }
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
