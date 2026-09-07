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

        /// <summary>A gladiator took combat damage. Carries the side that was hit and the amount.</summary>
        public event Action<PlayerSide, float> Damaged;

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
            Vector2 vel = aimDirection.normalized * (g.EffectiveSpeed() * GameConstants.SpeedScale * Mathf.Clamp01(power));
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
            FaceOpponents();

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

            // Facing is not set here any more - see FaceOpponents. A gladiator never turns his back
            // on the other one, whichever way he is running.
            g.Vel = g.PlannedAction == ActionType.Move
                ? g.PlannedAimDirection * (g.EffectiveSpeed() * GameConstants.SpeedScale * g.PlannedPower)
                : Vector2.zero; // Defend, or no plan at all - stand still
        }

        /// <summary>
        /// Turns both fighters to look at each other, whatever either of them is doing.
        ///
        /// Facing used to follow the run, which meant a gladiator ordered to back off or circle
        /// spent the cycle showing the enemy his shoulders - and with the camera fixed overhead, the
        /// direction a figure looks is most of what says the two are in a fight rather than two
        /// people crossing the same field. Movement is still whatever the player ordered; only where
        /// he is looking is decided here.
        ///
        /// The cost is that the run clip now plays while strafing or retreating. On a figure this
        /// size it reads as footwork; a directional blend tree would do it properly, and is not
        /// worth a rig change yet.
        /// </summary>
        private void FaceOpponents()
        {
            var a = State.P1.Active;
            var b = State.Bot.Active;
            if (a == null || b == null) return;

            var between = b.Pos - a.Pos;
            if (between.sqrMagnitude < 0.0001f) return;

            var towardsBot = between.normalized;
            if (a.Alive) a.Facing = towardsBot;
            if (b.Alive) b.Facing = -towardsBot;
        }

        private void StepActionSub(float dt)
        {
            var a = State.P1.Active;
            var b = State.Bot.Active;

            StepGladiator(a, dt);
            StepGladiator(b, dt);
            FaceOpponents();

            if (a == null || b == null || !a.Alive || !b.Alive) return;

            if (State.Collided) return;

            if (Vector2.Distance(a.Pos, b.Pos) <= GameConstants.CollideDistance)
                ResolveCollision(a, b);
        }

        private void StepGladiator(GladiatorInstance g, float dt)
        {
            if (g == null || !g.Alive) return;

            g.Pos += g.Vel * dt;
            ArenaShape.Bounce(ref g.Pos, ref g.Vel, GameConstants.GladiatorRadius);

            // hazard damage - continuous DOT while standing in an active danger ring
            if (HazardSystem.IsInActiveHazard(g.Pos, State.Cycle))
            {
                float dps = GameConstants.HazardDamageFraction * g.Def.MaxHp / GameConstants.ActionTime;
                g.TakeDamage(dps * dt);
            }

            // item pickup
            var item = State.Items.TryPickup(g);
            if (item != null) State.Items.ApplyPickup(g, item);

            // traps - checked after the move, so a gladiator who ran into one this substep is
            // stopped at the jaws rather than a substep past them.
            var trap = State.Traps?.TryTrigger(g);
            if (trap != null)
            {
                Damaged?.Invoke(SideOf(g), TrapSystem.Damage);
                Impact?.Invoke(trap.Pos);
            }
        }

        private void ResolveCollision(GladiatorInstance a, GladiatorInstance b)
        {
            State.Collided = true;
            a.Vel = Vector2.zero;
            b.Vel = Vector2.zero;

            Impact?.Invoke((a.Pos + b.Pos) * 0.5f);

            // Both land every attack they still have this cycle - Mongoose (Hilius) gets two.
            // Weapons are single-use, so a second swing is always unarmed.
            ExchangeBlows(a, b);

            // knock the two apart along the line between them so they can disengage next cycle
            Vector2 mid = (a.Pos + b.Pos) * 0.5f;
            Vector2 dir = (a.Pos - b.Pos);
            if (dir.sqrMagnitude < 0.0001f) dir = Vector2.up;
            dir = dir.normalized;
            a.Pos = mid + dir * (GameConstants.KnockbackDistance * 0.5f);
            b.Pos = mid - dir * (GameConstants.KnockbackDistance * 0.5f);

            // Put them back inside the wall here rather than leaving it to the next step. A
            // collision against the wall throws one of them through it, and because the collision
            // also ends the action phase, nothing steps him again until the next one - so he would
            // stand outside the arena for the whole of planning, which is seconds, not a frame.
            var still = Vector2.zero;
            ArenaShape.Bounce(ref a.Pos, ref still, GameConstants.GladiatorRadius);
            ArenaShape.Bounce(ref b.Pos, ref still, GameConstants.GladiatorRadius);

            State.CollisionEndTimer = GameConstants.CollisionEarlyEndDelay;
        }

        /// <summary>
        /// The blow at the end of a run: whoever finishes the cycle with the other inside his own
        /// weapon's reach swings at him.
        ///
        /// This replaces the old pass-by, which fired mid-flight the moment the two crossed a single
        /// fixed band and separated again. Two things were wrong with it. It ignored the weapon -
        /// everybody's reach was the same 66 units, so the mace's whole point had nowhere to live.
        /// And it fired on approach rather than on arrival, which meant a move that ended next to
        /// somebody and a move that merely brushed past them at speed were the same event.
        ///
        /// Ordinary movement can now finish in an attack, which is the point: closing to exactly the
        /// edge of your reach and no further is a real decision, and the answer differs per weapon.
        ///
        /// Each side is checked against its own reach, so a mace user really can land a blow from a
        /// distance a pair of short blades cannot answer from. That asymmetry is the reason reach is
        /// a stat rather than a constant.
        /// </summary>
        private void ResolveReachAttacks()
        {
            var a = State.P1.Active;
            var b = State.Bot.Active;
            if (a == null || b == null || !a.Alive || !b.Alive) return;

            float distance = Vector2.Distance(a.Pos, b.Pos);
            bool aReaches = distance <= a.WeaponDef.Reach;
            bool bReaches = distance <= b.WeaponDef.Reach;
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
            // A collision already resolved its own exchange and threw the two apart, so it does not
            // also get the end-of-run blow: one approach, one exchange.
            if (!State.Collided) ResolveReachAttacks();

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
