using System;
using System.Collections.Generic;
using System.Linq;
using ColosseumDuel.Core;
using ColosseumDuel.Gameplay.View;
using ColosseumDuel.Gameplay.Hud;
using UnityEngine;

namespace ColosseumDuel.Gameplay
{
    /// <summary>
    /// Thin MonoBehaviour glue: owns the presentation-agnostic GameManager, ticks it every frame,
    /// and pushes the resulting MatchState onto the views. All gameplay rules live in Core; nothing
    /// in this file may decide anything about the match.
    /// </summary>
    public class GameController : MonoBehaviour
    {
        [Header("Scene references")]
        public ArenaView Arena;

        [Header("Squad setup (defaults to the 3 starting gladiators for both sides)")]
        public bool AutoStartOnPlay = true;

        [Header("Knockout slow motion")]
        [Tooltip("How fast the world runs while a knockout plays out. Not as slow as planning: the " +
                 "round-end pause is a fixed length in real seconds, and at a quarter speed the " +
                 "death animation would only be a quarter played when the next round started.")]
        [Range(0.05f, 1f)] public float DeathTimeScale = 0.35f;

        [Tooltip("Leave at 0 for a different match every run; set a value to replay a deterministic one.")]
        public int RandomSeed = 0;

        public GameManager Manager { get; private set; }

        /// <summary>Kept for input code: the virtual->world scale lives on ArenaView.</summary>
        public float VirtualToWorld => Arena != null ? Arena.VirtualToWorld : 1f;

        /// <summary>Raised whenever the match changes phase. The HUD hooks in here.</summary>
        public event Action<MatchState> PhaseChanged;

        /// <summary>The red strike wedge drawn in front of the player while he is given his orders.</summary>
        public ControlZoneView ControlZone { get; private set; }

        private GladiatorView _playerView;
        private GladiatorView _botView;

        /// <summary>The one thing that moves the camera; it takes the shake as well.</summary>
        private DeathCameraView _deathCamera;
        private readonly List<ItemView> _itemViews = new List<ItemView>();

        private void Start()
        {
            if (Arena == null) Arena = FindFirstObjectByType<ArenaView>();
            if (Arena == null)
            {
                Debug.LogError("[Colosseum] GameController has no ArenaView - run Tools > Colosseum > Rebuild arena scene.");
                enabled = false;
                return;
            }
            if (Arena.Palette == null)
            {
                Debug.LogError("[Colosseum] ArenaView has no ViewPalette - run Tools > Colosseum > Bootstrap project.");
                enabled = false;
                return;
            }

            BuildViews();
            _deathCamera = FindFirstObjectByType<DeathCameraView>();

            Manager = new GameManager(RandomSeed != 0 ? new System.Random(RandomSeed) : null);
            Manager.PhaseChanged += OnPhaseChanged;
            Manager.Damaged += OnDamaged;
            Manager.Bitten += OnBitten;
            Manager.Bled += OnBled;
            Manager.Scorched += OnScorched;
            Manager.AbilityFired += OnAbilityFired;

            if (AutoStartOnPlay) RestartMatch();

            SyncViews();
        }

        /// <summary>
        /// Starts a fresh match on the existing manager. StartMatch rebuilds the rosters, items and
        /// phase from scratch, and the views bind to MatchState every frame rather than holding onto
        /// gladiator references, so nothing needs tearing down first.
        /// </summary>
        public void RestartMatch()
        {
            if (Manager == null) return;

            // Only the first fight of a session is taught. Counting here rather than storing it
            // anywhere: a player who reloads the page is starting over in every sense, and one who
            // presses "again" has just played a match and does not need the labels back.
            bool tutorial = _matchesStarted == 0;
            _matchesStarted++;

            // A fresh match gets fresh sand. Rounds do not: a round is over when somebody falls, but
            // the sand he fell on is the same sand, and by the third round it should look like it.
            if (Arena != null) Arena.ClearBloodStains();

            Manager.StartMatch(
                Squad.Select(GladiatorDef.Get),
                new[] { GladiatorDef.Brutius, GladiatorDef.Barbarius, GladiatorDef.Hilius },
                tutorial);
        }

        private int _matchesStarted;

        /// <summary>
        /// The three the player fights with, in the order they appear on the pick screen.
        ///
        /// Archetypes rather than instances, and duplicates are allowed: the roster screen offers
        /// two of each, so a squad of two Brutius and one Hilius is a legitimate composition. That
        /// is also why picks are submitted by slot - see GameManager.SubmitPick(side, index).
        ///
        /// Lives here rather than in Core because it outlives a match: it is what the player chose
        /// in the menu, and every restart is fought with it until they change it.
        /// </summary>
        public readonly List<GladiatorId> Squad = new List<GladiatorId>
        {
            GladiatorId.Brutius, GladiatorId.Barbarius, GladiatorId.Hilius
        };

        /// <summary>
        /// Replaces the squad and starts a fresh match with it. Refuses a squad of the wrong size,
        /// which would otherwise leave the pick screen with slots that answer to nobody.
        /// </summary>
        public bool SetSquad(IReadOnlyList<GladiatorId> squad)
        {
            if (squad == null || squad.Count != GameConstants.SquadSize) return false;

            Squad.Clear();
            Squad.AddRange(squad);
            RestartMatch();
            return true;
        }

        private void BuildViews()
        {
            Arena.BuildHazardRings();
            Arena.BuildSpikes();
            Arena.BuildTraps();
            Arena.BuildPalisade();

            // From the simulation's own list, so the stone drawn on the sand is the stone the paths
            // go round. The layout belongs to the arena rather than the match, which is why the
            // state carries it from the moment it exists.
            Arena.BuildObstacles(Manager != null ? Manager.State.Obstacles : ObstacleField.Standard());
            Arena.BuildBloodPool();
            Arena.BuildBloodStains();

            var viewRoot = new GameObject("Views").transform;
            viewRoot.SetParent(transform, false);

            _playerView = GladiatorView.Create("Player", viewRoot, Arena, Arena.Palette.PlayerBody, Arena.Palette.PlayerHelmet);
            _botView = GladiatorView.Create("Bot", viewRoot, Arena, Arena.Palette.BotBody, Arena.Palette.BotHelmet);

            // On the controller rather than on the arena: what it draws is one gladiator's options,
            // and which gladiator that is only this object knows.
            ControlZone = gameObject.AddComponent<ControlZoneView>();
            ControlZone.Bind(Arena);

            for (int i = 0; i < GameConstants.ItemCountOnArena; i++)
                _itemViews.Add(ItemView.Create($"Item_{i}", viewRoot, Arena));
        }


        private void Update()
        {
            if (Manager == null) return;

            // The simulation runs on unscaled time on purpose. Planning slows the world down so the
            // wall torches visibly drag - but the four seconds the player gets to decide are four
            // real seconds, not twenty. Scaling the tick as well would stretch the phase itself.
            Manager.Tick(Time.unscaledDeltaTime);

            ApplyTimeScale();
            SyncViews();
        }

        /// <summary>
        /// The world's speed for the phase it is in: a fifth while the player plans, slowed for a
        /// knockout, full speed otherwise.
        ///
        /// Planning ran at a third once, then at full speed; it is back at a fifth on request. The
        /// phase still lasts its full real length - the simulation ticks on unscaled time - so only
        /// what is on screen slows down.
        /// </summary>
        private void ApplyTimeScale()
        {
            float target = 1f;
            if (Manager.State.Phase == MatchPhase.Planning) target = GameConstants.PlanningTimeScale;
            else if (Manager.State.Phase == MatchPhase.RoundEnd) target = DeathTimeScale;
            if (!Mathf.Approximately(Time.timeScale, target)) Time.timeScale = target;
        }

        private void OnDisable()
        {
            // Time.timeScale is global. Leaving it low would follow the scene out and slow
            // down whatever loads next - including the rest of a test run.
            Time.timeScale = 1f;
        }

        private void SyncViews()
        {
            if (Manager == null) return;
            var state = Manager.State;

            _playerView.Sync(state.P1.Active);
            _botView.Sync(state.Bot.Active);

            // Both of them stand ready while the player thinks. Driven from the phase here rather
            // than from inside the view's own Sync, because it is a fact about the match and not
            // about either gladiator - and both sides are doing it for the same reason.
            bool planning = state.Phase == MatchPhase.Planning;
            _playerView.SetReadyStance(planning);
            _botView.SetReadyStance(planning);

            // The zones belong to the decision being made, so they go up and down with the phase
            // that makes it. The bot gets none: they are the player's options, and drawing the
            // opponent's would hand over the half of the guess the blind planning phase is for.
            if (ControlZone != null) ControlZone.Sync(state.P1.Active, planning);

            Arena.Sync(state);

            // ItemSystem replaces entries in place and never changes the list length, so index i of
            // the pool always maps to index i of the list.
            var items = state.Items?.Items;
            for (int i = 0; i < _itemViews.Count; i++)
                _itemViews[i].Sync(items != null && i < items.Count ? items[i] : null);
        }

        private void OnPhaseChanged(MatchState state)
        {
            if (state.Phase == MatchPhase.Action) ScheduleSwings(state);
            else
            {
                _playerView?.CancelScheduledSwing();
                _botView?.CancelScheduledSwing();
            }

            PhaseChanged?.Invoke(state);
        }

        /// <summary>
        /// Starts each side's swing early enough that the weapon arrives when the blow does.
        ///
        /// The simulation has already worked out when in this phase each of them lands one - it can,
        /// because the phase is deterministic once both plans are in. All this does is take that off
        /// the clock by however long that weapon's swing takes to travel.
        ///
        /// A side with no blow coming is not scheduled at all: the swing that follows a blow landing
        /// is still there for anything the prediction could not see, and one thrown at nothing is
        /// worse than one thrown late.
        /// </summary>
        private void ScheduleSwings(MatchState state)
        {
            ScheduleSwing(_playerView, state.P1);
            ScheduleSwing(_botView, state.Bot);
        }

        private void ScheduleSwing(GladiatorView view, PlayerState side)
        {
            _effectDelay[(int)side.Side] = 0f;
            if (view == null) return;

            view.CancelScheduledSwing();

            var g = side.Active;
            if (g == null || !g.Alive || side.StrikeEta < 0f) return;

            float lead = GladiatorView.SwingLead(g.Weapon);
            view.ScheduleSwing(side.StrikeEta - lead);

            // What the swing could not be started early enough to cover.
            //
            // An exchange between two fighters already standing in reach of each other lands on the
            // first substep of the phase, so there is no room in front of it to wind up: the swing
            // starts now and the weapon arrives a third of a second later, while the blood and the
            // stagger were going out immediately. The shortfall is held back off the effects so they
            // meet the weapon rather than beating it there.
            _effectDelay[(int)side.Side] = Mathf.Max(0f, lead - side.StrikeEta);
        }

        /// <summary>
        /// Per side, how long a blow's effects wait so they land with the weapon rather than ahead
        /// of it. Indexed by PlayerSide, and it belongs to the striker rather than to the victim.
        /// </summary>
        private readonly float[] _effectDelay = new float[2];

        // ------------------------------------------------------------------
        // one-shot effects, driven by Core's events
        // ------------------------------------------------------------------

        /// <summary>Colour of an ability burst. Warm, matching the rage meter it spends.</summary>
        private static readonly Color AbilityBurstColor = new Color(1f, 0.75f, 0.25f);

        private void OnDamaged(PlayerSide side, float amount)
        {
            if (amount <= 0f) return;

            // The event names the victim, so the swing belongs to the other one. Both sides can be
            // dealt damage in the same exchange, and then both swing - which is exactly right.
            var otherSide = side == PlayerSide.P1 ? PlayerSide.Bot : PlayerSide.P1;

            // Whether the blow throws him is a property of the weapon that landed it, and that is
            // readable from the striker rather than needing an event of its own. A flinch played
            // over a body already sliding backwards reads as the ground moving, not the man.
            // The swing goes out now whatever happens - the guard inside it ignores this call if one
            // is already running from the prediction, and answers it if there was none.
            ViewFor(otherSide).PlaySwing();

            float delay = _effectDelay[(int)otherSide];
            if (delay <= 0f) PlayBlowEffects(side, otherSide, amount);
            else StartCoroutine(PlayBlowEffectsAfter(delay, side, otherSide, amount));
        }

        /// <summary>
        /// Everything a landed blow throws off: the recoil on the man hit, the blood, the number and
        /// the knock on the camera.
        ///
        /// Separated from the event so it can be held back. The blow is announced when the
        /// simulation resolves it, and the weapon arrives when the animation gets there - and those
        /// are the same moment only when there was room to start the swing early.
        /// </summary>
        private void PlayBlowEffects(PlayerSide side, PlayerSide strikerSide, float amount)
        {
            // Whether the blow throws him is a property of the weapon that landed it, and that is
            // readable from the striker rather than needing an event of its own. A flinch played
            // over a body already sliding backwards reads as the ground moving, not the man.
            var striker = Manager.State.Get(strikerSide).Active;
            if (striker != null && striker.WeaponDef.Knockback > 0f) ViewFor(side).PlayKnockback();
            else ViewFor(side).PlayHit();

            // Blood is spawned at the arena rather than parented to the gladiator: a burst that
            // follows a body still sprinting away reads as a trail, not as a blow landing.
            var victim = Manager.State.Get(side).Active;
            if (victim != null) Arena.PlayBlood(victim.Pos);
            ShowDamage(side, amount, DamageNumbersView.Source.Blow);

            // And a knock on the camera, so a blow is felt and not only seen. Only for blows: a
            // trap and a bleed go through their own events and leave the frame alone.
            if (_deathCamera != null) _deathCamera.Shake();
        }

        private System.Collections.IEnumerator PlayBlowEffectsAfter(
            float delay, PlayerSide side, PlayerSide strikerSide, float amount)
        {
            // Unscaled, because the delay was measured against the phase clock and the simulation is
            // ticked on unscaled time. They agree during the action phase, and this keeps them
            // agreeing if the phase is ever slowed the way planning is.
            float waited = 0f;
            while (waited < delay)
            {
                yield return null;
                waited += Time.unscaledDeltaTime;
            }

            // The round can end while this is in flight. The blood and the number belong to a blow
            // that did land, so they still go out; nothing here reads a gladiator that may be gone.
            PlayBlowEffects(side, strikerSide, amount);
        }


        /// <summary>
        /// A trap closed on somebody.
        ///
        /// The recoil and the blood, but no swing from the other side - a trap used to be reported
        /// through Damaged, and since the view reads a hit as "the other one swung", a gladiator
        /// stepping into one made his opponent throw an attack from wherever he happened to be
        /// standing, with nothing anywhere near him.
        /// </summary>
        private void OnBitten(PlayerSide side, float amount)
        {
            if (amount <= 0f) return;
            ViewFor(side).PlayHit();

            var victim = Manager.State.Get(side).Active;
            if (victim != null) Arena.PlayBlood(victim.Pos);
            ShowDamage(side, amount, DamageNumbersView.Source.Trap);
        }

        /// <summary>
        /// A wound opened up at the top of a cycle.
        ///
        /// Deliberately quieter than a blow: no recoil animation and no swing on the other side,
        /// because nobody swung. A red flicker and a small spot of blood is the whole of it.
        /// </summary>
        private void OnBled(PlayerSide side, float amount)
        {
            if (amount <= 0f) return;
            ViewFor(side).PlayBleed();

            var victim = Manager.State.Get(side).Active;
            if (victim != null) Arena.PlayBlood(victim.Pos);
            ShowDamage(side, amount, DamageNumbersView.Source.Bleed);
        }

        /// <summary>A phase spent in the closing arena, totalled up.</summary>
        private void OnScorched(PlayerSide side, float amount)
            => ShowDamage(side, amount, DamageNumbersView.Source.Spikes);

        /// <summary>
        /// Sends the number up off whoever paid it.
        ///
        /// The view is found on demand rather than at startup: it is built by the HUD in its own
        /// Start, and which of the two components starts first is not something either of them
        /// gets to decide.
        /// </summary>
        private void ShowDamage(PlayerSide side, float amount, DamageNumbersView.Source source)
        {
            if (_damageNumbers == null) _damageNumbers = FindFirstObjectByType<DamageNumbersView>();
            if (_damageNumbers == null) return;

            var victim = Manager.State.Get(side).Active;
            if (victim != null) _damageNumbers.Show(victim.Pos, amount, source);
        }

        private DamageNumbersView _damageNumbers;

        private void OnAbilityFired(PlayerSide side)
        {
            ViewFor(side).PlayAbility(AbilityBurstColor);
        }

        private GladiatorView ViewFor(PlayerSide side)
            => side == PlayerSide.P1 ? _playerView : _botView;

        // ------------------------------------------------------------------
        // called by the input layer
        // ------------------------------------------------------------------

        /// <summary>Call from the drag-input handler once the player releases the pull-back.</summary>
        // Movement and the ability are filed separately: the ability supplements whatever the turn
        // turns out to be, so ordering a move must not disturb it and vice versa.

        public void SubmitPlayerMove(Vector2 aimDirection, float power)
        {
            Manager?.SubmitPlanningAction(PlayerSide.P1, ActionType.Move, aimDirection, power);
        }

        /// <summary>Sends the player's gladiator to a point, round whatever is in the way.</summary>
        public void SubmitPlayerMoveTo(Vector2 target)
        {
            Manager?.SubmitPlanningMoveTo(PlayerSide.P1, target);
        }

        /// <summary>Files a run the player drew, corner to corner from the gladiator.</summary>
        public void SubmitPlayerPath(IReadOnlyList<Vector2> path)
        {
            Manager?.SubmitPlanningPath(PlayerSide.P1, path);
        }

        public void SubmitPlayerDefend()
        {
            Manager?.SubmitPlanningAction(PlayerSide.P1, ActionType.Defend, Vector2.zero, 0f);
        }

        /// <summary>Arms or disarms the ability. Returns false if it could not fire anyway.</summary>
        public bool SubmitPlayerAbility(bool armed)
            => Manager != null && Manager.SubmitAbility(PlayerSide.P1, armed);

        /// <summary>
        /// Takes back whatever the player had chosen this phase, leaving them undecided again.
        ///
        /// Undecided is not the same as defending, even though a phase that runs out while nobody
        /// has decided falls back to a guard - the difference is what the buttons show.
        /// </summary>
        public void ClearPlayerPlan()
        {
            Manager?.SubmitPlanningAction(PlayerSide.P1, ActionType.None, Vector2.zero, 0f, false);
        }

        public void SubmitPlayerPick(GladiatorId id)
        {
            Manager?.SubmitPick(PlayerSide.P1, id);
        }

        /// <summary>Sends in the fighter in that squad slot. What the pick cards call.</summary>
        public void SubmitPlayerPick(int rosterIndex)
        {
            Manager?.SubmitPick(PlayerSide.P1, rosterIndex);
        }
    }
}
