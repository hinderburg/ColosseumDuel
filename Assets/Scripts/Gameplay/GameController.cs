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

        [Header("Planning slow motion")]
        [Tooltip("How fast the world runs while the player is planning. The phase still lasts its " +
                 "full real-time duration - only the visuals slow down.")]
        [Range(0.05f, 1f)] public float PlanningTimeScale = 0.3f;

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
            Arena.BuildBloodPool();
            Arena.BuildBloodStains();

            var viewRoot = new GameObject("Views").transform;
            viewRoot.SetParent(transform, false);

            _playerView = GladiatorView.Create("Player", viewRoot, Arena, Arena.Palette.PlayerBody, Arena.Palette.PlayerHelmet);
            _botView = GladiatorView.Create("Bot", viewRoot, Arena, Arena.Palette.BotBody, Arena.Palette.BotHelmet);

            for (int i = 0; i < GameConstants.ItemCountOnArena; i++)
                _itemViews.Add(ItemView.Create($"Item_{i}", viewRoot, Arena));
        }


        private void Update()
        {
            if (Manager == null) return;

            // The simulation runs on unscaled time on purpose. Planning slows the world down so the
            // wall torches visibly drag - but the three seconds the player gets to decide are three
            // real seconds, not ten. Scaling the tick as well would stretch the phase itself.
            Manager.Tick(Time.unscaledDeltaTime);

            ApplyTimeScale();
            SyncViews();
        }

        private void ApplyTimeScale()
        {
            float target = 1f;
            if (Manager.State.Phase == MatchPhase.Planning) target = PlanningTimeScale;
            else if (Manager.State.Phase == MatchPhase.RoundEnd) target = DeathTimeScale;

            if (!Mathf.Approximately(Time.timeScale, target)) Time.timeScale = target;
        }

        private void OnDisable()
        {
            // Time.timeScale is global. Leaving it at a third would follow the scene out and slow
            // down whatever loads next - including the rest of a test run.
            Time.timeScale = 1f;
        }

        private void SyncViews()
        {
            if (Manager == null) return;
            var state = Manager.State;

            _playerView.Sync(state.P1.Active);
            _botView.Sync(state.Bot.Active);
            Arena.Sync(state);

            // ItemSystem replaces entries in place and never changes the list length, so index i of
            // the pool always maps to index i of the list.
            var items = state.Items?.Items;
            for (int i = 0; i < _itemViews.Count; i++)
                _itemViews[i].Sync(items != null && i < items.Count ? items[i] : null);
        }

        private void OnPhaseChanged(MatchState state)
        {
            PhaseChanged?.Invoke(state);
        }

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
            var striker = Manager.State.Get(otherSide).Active;
            if (striker != null && striker.WeaponDef.Knockback > 0f) ViewFor(side).PlayKnockback();
            else ViewFor(side).PlayHit();

            ViewFor(otherSide).PlaySwing();

            // Blood is spawned at the arena rather than parented to the gladiator: a burst that
            // follows a body still sprinting away reads as a trail, not as a blow landing.
            var victim = Manager.State.Get(side).Active;
            if (victim != null) Arena.PlayBlood(victim.Pos);
            ShowDamage(side, amount, DamageNumbersView.Source.Blow);

            // And a knock on the camera, so a blow is felt and not only seen. Only for blows: a
            // trap and a bleed go through their own events and leave the frame alone.
            if (_deathCamera != null) _deathCamera.Shake();
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
