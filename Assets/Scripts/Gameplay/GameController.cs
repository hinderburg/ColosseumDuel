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
                 "clash-end pause is a fixed length in real seconds, and at a quarter speed the " +
                 "death animation would only be a quarter played when the next clash started.")]
        [Range(0.05f, 1f)] public float DeathTimeScale = 0.35f;

        [Tooltip("Leave at 0 for a different match every run; set a value to replay a deterministic one.")]
        public int RandomSeed = 0;

        public GameManager Manager { get; private set; }

        /// <summary>
        /// The bot plays the player's side too - the Auto switch beside his squad. Kept on the
        /// manager, which lives for the session, so it stays on across restarts.
        /// </summary>
        public bool AutoPlay
        {
            get => Manager != null && Manager.P1Auto;
            set { if (Manager != null) Manager.P1Auto = value; }
        }

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
        /// <summary>The blessing, the apple and the horn lying on the sand - one view each.</summary>
        private readonly List<PickupView> _pickupViews = new List<PickupView>();

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
            Manager.BlowLanded += OnBlowLanded;
            Manager.PickedUp += OnPickedUp;
            Manager.Bled += OnBled;
            Manager.Scorched += OnScorched;
            Manager.AbilityFired += OnAbilityFired;
            Manager.BoonTaken += OnBoonTaken;

            if (AutoStartOnPlay) RestartMatch();

            SyncViews();
        }

        /// <summary>
        /// Starts a fresh match on the existing manager. StartMatch rebuilds the rosters, the blessing and
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

            // A boon chosen in a match thrown away mid-pick has no clash to be announced at.
            foreach (var pending in _boonsToAnnounce) pending.Clear();

            // A fresh match gets fresh sand. Clashes do not: a clash is over when somebody falls, but
            // the sand he fell on is the same sand, and by the third clash it should look like it.
            if (Arena != null) Arena.ClearBloodStains();

            // The bot takes one of each of its men's three abilities at random, so the three it
            // fields are not the same three every match. Unity's random rather than the manager's,
            // which is seeded and which every other roll of the match comes off.
            var bot = BotSquadAgainst(Squad);
            var botAbilities = bot.ToDictionary(d => d.Id,
                d => d.Abilities[UnityEngine.Random.Range(0, d.Abilities.Count)]);

            // And five boons at random, for the same reason. Neither side gets any while boons are off.
            List<BoonKey> botBoons = null;
            if (OfferBoons)
                botBoons = BoonDef.All.Select(d => d.Key).OrderBy(_ => UnityEngine.Random.value)
                    .Take(BoonDef.LoadoutSize).ToList();

            Manager.StartMatch(Squad.Select(GladiatorDef.Get), bot, tutorial, AbilityChoices, botAbilities,
                OfferBoons ? BoonLoadout : null, botBoons);
        }

        /// <summary>
        /// Whether matches offer boons. On in the game. Off by default in a batch run - which every test
        /// run is - because the choice is a pause of up to ten seconds between the pick and the clash,
        /// and the tests written before boons walk straight from one to the other; the boon tests turn
        /// it on for themselves. Set the override to force it either way.
        /// </summary>
        public static bool? OfferBoonsOverride;

        public static bool OfferBoons => OfferBoonsOverride ?? !Application.isBatchMode;

        /// <summary>The five boons the player brings into every match, chosen on the menu's boon screen.</summary>
        public readonly List<BoonKey> BoonLoadout = BoonDef.DefaultLoadout();

        /// <summary>
        /// Replaces the five and starts the match behind the menu again, so the next one draws from
        /// them. Refuses anything but five different boons.
        /// </summary>
        public bool SetBoonLoadout(IReadOnlyList<BoonKey> loadout)
        {
            if (loadout == null || loadout.Count != BoonDef.LoadoutSize || loadout.Distinct().Count() != loadout.Count)
                return false;

            BoonLoadout.Clear();
            BoonLoadout.AddRange(loadout);
            RestartMatch();
            return true;
        }

        /// <summary>Takes one of the three boons on offer to the player.</summary>
        public bool SubmitPlayerBoon(BoonKey key) => Manager != null && Manager.SubmitBoon(PlayerSide.P1, key);

        /// <summary>
        /// The ability the player has chosen for each archetype - on the roster screen, one of his
        /// three. Kept per archetype rather than per squad slot, so taking a man out of the squad and
        /// back in again does not forget what he was going to fight with.
        /// </summary>
        public readonly Dictionary<GladiatorId, AbilityKey> AbilityChoices =
            GladiatorDef.All.ToDictionary(d => d.Id, d => d.Abilities[0]);

        public AbilityKey AbilityFor(GladiatorId id)
            => AbilityChoices.TryGetValue(id, out var key) ? key : GladiatorDef.Get(id).Abilities[0];

        /// <summary>
        /// Chooses the ability an archetype takes into the fight. Refuses one that is not his, and
        /// starts the match behind the menu again so the men waiting in it carry the choice.
        /// </summary>
        public bool SetAbility(GladiatorId id, AbilityKey key)
        {
            if (!GladiatorDef.Get(id).Abilities.Contains(key)) return false;
            AbilityChoices[id] = key;
            RestartMatch();
            return true;
        }

        /// <summary>
        /// The bot fields the three the player did not take. Six archetypes and two squads of three:
        /// every match is six different men, and nobody fights his own mirror.
        /// </summary>
        public static List<GladiatorDef> BotSquadAgainst(IEnumerable<GladiatorId> playerSquad)
        {
            var taken = new HashSet<GladiatorId>(playerSquad);
            return GladiatorDef.All.Where(d => !taken.Contains(d.Id)).Take(GameConstants.SquadSize).ToList();
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

            foreach (PickupKind kind in Enum.GetValues(typeof(PickupKind)))
                _pickupViews.Add(PickupView.Create(kind, viewRoot, Arena));
        }


        private void Update()
        {
            if (Manager == null) return;

            // The simulation runs on unscaled time on purpose. Planning slows the world down so the
            // wall torches visibly drag - but the four seconds the player gets to decide are four
            // real seconds, not twenty. Scaling the tick as well would stretch the phase itself.
            //
            // Except in a hit-stop, when the simulation waits with the picture: a blow that landed a
            // frame ago is being felt, and nothing moves until it has been.
            if (!IsHitStopped) Manager.Tick(Time.unscaledDeltaTime);

            ApplyTimeScale();
            SyncViews();
        }

        // ------------------------------------------------------------------
        // hit-stop
        // ------------------------------------------------------------------

        /// <summary>
        /// How long the world freezes when a blow lands, in real seconds: long enough to be felt as
        /// weight, short enough not to be noticed as a pause. Longer for a blow from the side or
        /// behind, for a heavy weapon and for the one that drops him; shorter for one a guard met.
        /// </summary>
        public const float HitStopSeconds = 0.07f;
        public const float HitStopFlankMult = 1.5f, HitStopLethalMult = 2f, HitStopHeavyMult = 1.3f, HitStopBlockedMult = 0.6f;

        private float _hitStopUntil;

        /// <summary>Whether the world is frozen on a blow right now.</summary>
        public bool IsHitStopped => Time.unscaledTime < _hitStopUntil;

        /// <summary>Freezes everything - the simulation, the animation, the particles, the camera - for this long.</summary>
        public void HitStop(float seconds)
            => _hitStopUntil = Mathf.Max(_hitStopUntil, Time.unscaledTime + Mathf.Max(0f, seconds));

        /// <summary>How long a hit-stop this blow is worth.</summary>
        public static float HitStopFor(Blow blow)
        {
            float seconds = HitStopSeconds;
            if (blow.Flanking) seconds *= HitStopFlankMult;
            if (WeaponDef.Get(blow.Weapon).DamageMultiplier >= 1.2f) seconds *= HitStopHeavyMult;
            if (blow.Blocked) seconds *= HitStopBlockedMult;
            if (blow.Lethal) seconds *= HitStopLethalMult;
            return seconds;
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
            else if (Manager.State.Phase == MatchPhase.ClashEnd) target = DeathTimeScale;
            if (IsHitStopped) target = 0f;
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
            // The circle of his reach stays up while the orders are carried out as well: where his
            // blow lands is worth reading while he runs, not only while he is deciding where to.
            if (ControlZone != null)
                ControlZone.Sync(state.P1.Active, planning, planning || state.Phase == MatchPhase.Action);

            Arena.Sync(state);

            foreach (var view in _pickupViews) view.Sync(state.Pickup(view.Kind)?.Position);

            // The shelves follow the match: a boon whose callout was never seen still goes up, and
            // a restart takes them all down.
            // Less the ones still waiting to be announced: the bot takes its boon while the men are
            // still being picked, and its picture has to fly in over him, not be there before he is.
            var callouts = Callouts;
            if (callouts != null)
            {
                callouts.SyncBoons(PlayerSide.P1, state.P1.Boons.Except(_boonsToAnnounce[0]), BoonPicture);
                callouts.SyncBoons(PlayerSide.Bot, state.Bot.Boons.Except(_boonsToAnnounce[1]), BoonPicture);
            }
        }

        private void OnPhaseChanged(MatchState state)
        {
            // A clash is being announced: both men walk out to their marks, with whatever the last
            // clash left playing on them thrown away.
            if (state.Phase == MatchPhase.Reveal)
            {
                EnterArena(_playerView, state.P1.Active);
                EnterArena(_botView, state.Bot.Active);

                // And the boon each side took for this clash goes up over the man it sent out to
                // earn it - now, and not when it was chosen, because until now he was nowhere.
                AnnounceBoons(PlayerSide.P1, state.P1.Active);
                AnnounceBoons(PlayerSide.Bot, state.Bot.Active);
            }

            if (state.Phase == MatchPhase.Action) ScheduleSwings(state);
            else
            {
                _playerView?.CancelScheduledSwing();
                _botView?.CancelScheduledSwing();
            }

            PhaseChanged?.Invoke(state);
        }

        /// <summary>
        /// Sends a man walking out to his mark from the wall behind it. Behind him along the way he
        /// is facing - the marks are down the long axis, each man facing the other end - out to just
        /// inside the wall.
        /// </summary>
        private void EnterArena(GladiatorView view, GladiatorInstance g)
        {
            if (view == null || g == null) return;

            var back = g.Facing.sqrMagnitude > 0.0001f
                ? -g.Facing.normalized
                : (g.Pos.y < 0f ? Vector2.down : Vector2.up);
            float walk = Mathf.Max(0f, ArenaShape.RadiusY * GladiatorView.EntranceFromFraction - g.Pos.magnitude);
            view.EnterArena(Arena.ToWorld(g.Pos + back * walk), GladiatorView.EntranceSeconds);
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
            if (view == null) return;

            view.CancelScheduledSwing();

            var g = side.Active;
            if (g == null || !g.Alive || side.StrikeEta < 0f) return;

            float lead = GladiatorView.SwingLead(g.Weapon);
            view.ScheduleSwing(side.StrikeEta - lead);
        }

        // ------------------------------------------------------------------
        // one-shot effects, driven by Core's events
        // ------------------------------------------------------------------

        /// <summary>Colour of an ability burst. Warm, matching the rage meter it spends.</summary>
        private static readonly Color AbilityBurstColor = new Color(1f, 0.75f, 0.25f);

        private void OnBlowLanded(Blow blow)
        {
            if (blow.Damage <= 0f && !blow.Blocked) return;

            // The blow names the victim and the striker. Both sides can be struck in the same
            // exchange, and then both swing - which is exactly right.
            var side = blow.Victim;
            var otherSide = blow.Striker;

            // How long until the striker's weapon gets there - asked before any swing is started, so
            // a swing already on its way can be told from one about to begin. Then the swing goes
            // out: the guard inside it ignores the call if one is already running from the
            // prediction, and answers it if there was none.
            var strikerView = ViewFor(otherSide);
            var striker = Manager.State.Get(otherSide).Active;
            float delay = striker != null ? strikerView.SecondsUntilImpact(striker.Weapon) : 0f;
            strikerView.PlaySwing();

            // And the man struck stays on his feet until then. He is dead in the simulation the
            // moment the blow resolves, and falling on that frame put his death in front of the
            // weapon that caused it.
            if (delay > 0f) ViewFor(side).HoldDeathFor(delay);

            if (delay <= 0f) PlayBlowEffects(blow);
            else StartCoroutine(PlayBlowEffectsAfter(delay, blow));
        }

        /// <summary>
        /// Everything a landed blow throws off: the recoil on the man hit, the blood, the number and
        /// the knock on the camera.
        ///
        /// Separated from the event so it can be held back. The blow is announced when the
        /// simulation resolves it, and the weapon arrives when the animation gets there - and those
        /// are the same moment only when there was room to start the swing early.
        /// </summary>
        private void PlayBlowEffects(Blow blow)
        {
            var side = blow.Victim;

            // Whether the blow throws him is a property of the weapon that landed it. A flinch
            // played over a body already sliding backwards reads as the ground moving, not the man.
            // A guard that held against the throw takes the plain hit.
            bool thrown = WeaponDef.Get(blow.Weapon).Knockback > 0f && !blow.Blocked;
            if (thrown) ViewFor(side).PlayKnockback();
            else ViewFor(side).PlayHit();

            // The world stops on the blow for a moment, so it is felt before anything moves on - and
            // the man it landed on flashes, white or steel for a guard, for exactly that moment.
            HitStop(HitStopFor(blow));
            ViewFor(side).PlayHitFlash(blow.Blocked);

            // Blood is spawned at the arena rather than parented to the gladiator: a burst that
            // follows a body still sprinting away reads as a trail, not as a blow landing.
            var victim = Manager.State.Get(side).Active;
            if (victim != null) Arena.PlayBlood(victim.Pos);
            ShowDamage(side, blow.Damage, DamageNumbersView.Source.Blow);

            // And a knock on the camera, so a blow is felt and not only seen. Only for blows: a
            // trap and a bleed go through their own events and leave the frame alone.
            if (_deathCamera != null) _deathCamera.Shake();
        }

        private System.Collections.IEnumerator PlayBlowEffectsAfter(float delay, Blow blow)
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

            // The clash can end while this is in flight. The blood and the number belong to a blow
            // that did land, so they still go out; nothing here reads a gladiator that may be gone.
            PlayBlowEffects(blow);
        }


        /// <summary>Colour of the ring that goes out from a gladiator taking up the blessing.</summary>
        private static readonly Color BlessingBurstColor = new Color(1f, 0.24f, 0.16f);

        /// <summary>Something was taken up off the sand. Each is announced on the man in its own colour.</summary>
        private void OnPickedUp(PlayerSide side, PickupKind kind, float amount)
        {
            switch (kind)
            {
                case PickupKind.Apple: OnAppleEaten(side, amount); break;
                case PickupKind.Horn: OnHornBlown(side, amount); break;
                default: OnWeaponBlessed(side); break;
            }
        }

        /// <summary>
        /// The apple: a green ring and a green burst on him, the health it gave back going up off him
        /// in green, and its name over him - spent the moment it is eaten, so it fades where it is.
        /// </summary>
        private void OnAppleEaten(PlayerSide side, float healed)
        {
            var view = ViewFor(side);
            view.PlayAbility(GearSizes.AppleTint);
            view.PlayPickup(PickupKind.Apple);
            ShowDamage(side, healed, DamageNumbersView.Source.Heal);

            var g = Manager.State.Get(side).Active;
            if (g != null && Callouts != null)
                Callouts.ShowPickup(g.Pos, side, "Apple", $"+{Mathf.RoundToInt(healed)} health");
        }

        /// <summary>
        /// The horn: an orange ring and burst on him, its name over him, and - for the player's own
        /// man - his ability button up for a moment, out of turn, with the rage it gave filling its
        /// ring. The opponent's meter is the bar over his head, which shows it anyway.
        /// </summary>
        private void OnHornBlown(PlayerSide side, float gained)
        {
            var view = ViewFor(side);
            view.PlayAbility(GearSizes.HornTint);
            view.PlayPickup(PickupKind.Horn);

            var g = Manager.State.Get(side).Active;
            if (g == null) return;
            if (Callouts != null)
                Callouts.ShowPickup(g.Pos, side, "Horn",
                    $"+{Mathf.RoundToInt(gained / GameConstants.RageMax * 100f)}% rage");
            if (side == PlayerSide.P1 && ActionButtons != null)
                ActionButtons.ShowRageGain(g.Rage - gained, g.Rage);
        }

        /// <summary>Found on demand, like the callouts - and inactive outside planning, so looked for with the inactive.</summary>
        private ActionButtonsView ActionButtons
            => _actionButtons != null
                ? _actionButtons
                : (_actionButtons = FindFirstObjectByType<ActionButtonsView>(FindObjectsInactive.Include));

        private ActionButtonsView _actionButtons;

        /// <summary>
        /// A gladiator took up the blessing off the sand: a red ring goes out from him and its effect
        /// plays on him, and its name goes up over him the way an ability's does - then down into
        /// the left of his side's strip, counting its rounds, for as long as it lasts.
        /// </summary>
        private void OnWeaponBlessed(PlayerSide side)
        {
            var view = ViewFor(side);
            view.PlayAbility(BlessingBurstColor);
            view.PlayBlessing();

            var g = Manager.State.Get(side).Active;
            if (g == null || Callouts == null) return;

            // At most the three rounds the rule promises: the rest of the round it is taken up in
            // comes free, and a count that opened on four would contradict the caption beside it.
            Callouts.ShowBlessing(g.Pos, side, () => StillFighting(side, g) && g.WeaponBuffed
                ? Mathf.Min(g.WeaponBuffRoundsLeft, GameConstants.WeaponBuffRounds)
                : 0);
        }

        /// <summary>
        /// Whether this is still the man fighting for that side, and on his feet. What a strip's count
        /// is read through: once he falls or the match is thrown away, what he had is over.
        /// </summary>
        private bool StillFighting(PlayerSide side, GladiatorInstance g)
            => Manager != null && ReferenceEquals(Manager.State.Get(side).Active, g) && g.Alive;

        /// <summary>
        /// A wound opened up at the top of a round.
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

        /// <summary>
        /// An ability went off: a ring in its colour from the man who used it, and its name going up
        /// over him - large, with what it does underneath - then down into the right of his side's
        /// strip, counting its rounds, for as long as it lasts.
        /// </summary>
        private void OnAbilityFired(PlayerSide side)
        {
            var g = Manager.State.Get(side).Active;
            ViewFor(side).PlayAbility(g != null ? AbilityVisuals.ColorFor(g.Ability) : AbilityBurstColor);
            if (g == null || Callouts == null) return;

            Callouts.Show(g.Pos, g.Ability, side,
                () => StillFighting(side, g) && g.Buff.IsActive ? g.Buff.RoundsLeft : 0);
        }

        /// <summary>
        /// The boons taken since the last clash was announced, a list per side. A boon is chosen while
        /// the men are still being picked, before either is on the sand, so its callout waits for the
        /// clash to be announced and the man to be standing somewhere it can go up over.
        /// </summary>
        private readonly List<BoonKey>[] _boonsToAnnounce = { new List<BoonKey>(), new List<BoonKey>() };

        private void OnBoonTaken(PlayerSide side, BoonKey key) => _boonsToAnnounce[side == PlayerSide.P1 ? 0 : 1].Add(key);

        private void AnnounceBoons(PlayerSide side, GladiatorInstance g)
        {
            var pending = _boonsToAnnounce[side == PlayerSide.P1 ? 0 : 1];
            if (g != null && Callouts != null)
                foreach (var key in pending)
                    Callouts.ShowBoon(g.Pos, key, side, BoonPicture(key));
            pending.Clear();
        }

        private Sprite BoonPicture(BoonKey key)
            => Arena != null && Arena.Palette != null ? Arena.Palette.BoonIconFor(key) : null;

        /// <summary>Found on demand, like the damage numbers: the HUD builds it in its own Start.</summary>
        private AbilityCalloutView Callouts
            => _callouts != null ? _callouts : (_callouts = FindFirstObjectByType<AbilityCalloutView>());

        private AbilityCalloutView _callouts;

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
