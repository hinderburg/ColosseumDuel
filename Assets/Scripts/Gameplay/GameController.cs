using System;
using System.Collections.Generic;
using ColosseumDuel.Core;
using ColosseumDuel.Gameplay.View;
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

        [Tooltip("Leave at 0 for a different match every run; set a value to replay a deterministic one.")]
        public int RandomSeed = 0;

        public GameManager Manager { get; private set; }

        /// <summary>Kept for input code: the virtual->world scale lives on ArenaView.</summary>
        public float VirtualToWorld => Arena != null ? Arena.VirtualToWorld : 1f;

        /// <summary>Raised whenever the match changes phase. The HUD hooks in here.</summary>
        public event Action<MatchState> PhaseChanged;

        private GladiatorView _playerView;
        private GladiatorView _botView;
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

            Manager = new GameManager(RandomSeed != 0 ? new System.Random(RandomSeed) : null);
            Manager.PhaseChanged += OnPhaseChanged;
            Manager.Damaged += OnDamaged;
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
            Manager.StartMatch(
                new[] { GladiatorDef.Brutius, GladiatorDef.Barbarius, GladiatorDef.Hilius },
                new[] { GladiatorDef.Brutius, GladiatorDef.Barbarius, GladiatorDef.Hilius });
        }

        private void BuildViews()
        {
            Arena.BuildHazardRings();
            Arena.BuildTorches();
            Arena.BuildHazardFire();

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
            float target = Manager.State.Phase == MatchPhase.Planning ? PlanningTimeScale : 1f;
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
            ViewFor(side).PlayHit();
        }


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
        public void SubmitPlayerMove(Vector2 aimDirection, float power, bool useAbility)
        {
            Manager?.SubmitPlanningAction(PlayerSide.P1, ActionType.Move, aimDirection, power, useAbility);
        }

        public void SubmitPlayerDefend(bool useAbility)
        {
            Manager?.SubmitPlanningAction(PlayerSide.P1, ActionType.Defend, Vector2.zero, 0f, useAbility);
        }

        public void SubmitPlayerPick(GladiatorId id)
        {
            Manager?.SubmitPick(PlayerSide.P1, id);
        }
    }
}
