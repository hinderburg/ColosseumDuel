using System.Collections.Generic;
using ColosseumDuel.Core;
using UnityEngine;
using UnityEngine.UI;

namespace ColosseumDuel.Gameplay.Hud
{
    /// <summary>
    /// The whole HUD: squad bars for both sides, the player's roster cards and action buttons, and
    /// the overlays for picking, reveal, round end and match end.
    ///
    /// Built in code like the rest of the presentation layer, and driven entirely from MatchState
    /// in LateUpdate - it never decides anything, it only reports. LateUpdate specifically, so it
    /// reads the state GameController.Update has already advanced this frame.
    /// </summary>
    [RequireComponent(typeof(Canvas))]
    public sealed class MatchHud : MonoBehaviour
    {
        public GameController Controller;
        public PlayerInputController Input;

        private readonly List<RosterEntryView> _playerIcons = new List<RosterEntryView>();
        private readonly List<RosterEntryView> _botIcons = new List<RosterEntryView>();
        private readonly List<RosterEntryView> _playerCards = new List<RosterEntryView>();
        private readonly List<Button> _pickButtons = new List<Button>();
        private readonly List<Text> _pickButtonLabels = new List<Text>();

        private Text _phaseLabel;
        private Text _hint;
        private Button _defendButton;
        private Button _abilityButton;
        private Text _abilityLabel;

        private GameObject _overlay;
        private Text _overlayTitle;
        private Text _overlaySubtitle;
        private RectTransform _pickRow;
        private Button _restartButton;

        private bool _built;

        private void Start()
        {
            if (Controller == null) Controller = FindFirstObjectByType<GameController>();
            if (Input == null) Input = FindFirstObjectByType<PlayerInputController>();

            var palette = Controller != null && Controller.Arena != null ? Controller.Arena.Palette : null;
            if (palette != null && palette.HudFont != null)
                HudFactory.ActiveFont = palette.HudFont;
            else
                Debug.LogWarning("[Colosseum] No HUD font in the palette - falling back to the built-in " +
                                 "font, which cannot draw Cyrillic in a build. Run the bootstrap.");

            Build();
        }

        // ------------------------------------------------------------------
        // construction
        // ------------------------------------------------------------------

        private void Build()
        {
            if (_built) return;
            _built = true;

            var root = (RectTransform)transform;

            BuildTopBar(root);
            BuildBottomBar(root);
            BuildOverlay(root);
        }

        private void BuildTopBar(RectTransform root)
        {
            var bar = HudFactory.CreatePanel("TopBar", root, new Color(0.05f, 0.05f, 0.07f, 0.55f)).rectTransform;
            HudFactory.AnchorToEdge(bar, RectTransform.Edge.Top, 78f);

            var playerRow = HudFactory.CreateRect("PlayerSquad", bar);
            HudFactory.Stretch(playerRow);
            HudFactory.AddRow(playerRow, 8f, TextAnchor.MiddleLeft);

            var botRow = HudFactory.CreateRect("BotSquad", bar);
            HudFactory.Stretch(botRow);
            HudFactory.AddRow(botRow, 8f, TextAnchor.MiddleRight);

            for (int i = 0; i < GameConstants.SquadSize; i++)
            {
                _playerIcons.Add(RosterEntryView.Create($"P1_{i}", playerRow, HudFactory.PlayerColor, detailed: false));
                _botIcons.Add(RosterEntryView.Create($"Bot_{i}", botRow, HudFactory.BotColor, detailed: false));
            }

            _phaseLabel = HudFactory.CreateLabel("PhaseLabel", bar, "", 26);
            _phaseLabel.rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
            _phaseLabel.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            _phaseLabel.rectTransform.sizeDelta = new Vector2(420f, 40f);
            _phaseLabel.rectTransform.anchoredPosition = Vector2.zero;
        }

        private void BuildBottomBar(RectTransform root)
        {
            var bar = HudFactory.CreatePanel("BottomBar", root, new Color(0.05f, 0.05f, 0.07f, 0.55f)).rectTransform;
            HudFactory.AnchorToEdge(bar, RectTransform.Edge.Bottom, 148f);

            var cards = HudFactory.CreateRect("Cards", bar);
            HudFactory.Stretch(cards);
            HudFactory.AddRow(cards, 10f, TextAnchor.MiddleLeft, new RectOffset(16, 16, 10, 26));

            for (int i = 0; i < GameConstants.SquadSize; i++)
                _playerCards.Add(RosterEntryView.Create($"Card_{i}", cards, HudFactory.PlayerColor, detailed: true));

            var actions = HudFactory.CreateRect("Actions", bar);
            HudFactory.Stretch(actions);
            HudFactory.AddRow(actions, 12f, TextAnchor.MiddleRight, new RectOffset(16, 16, 10, 26));

            _defendButton = HudFactory.CreateButton("Defend", actions, "Защита");
            _defendButton.GetComponent<RectTransform>().sizeDelta = new Vector2(170f, 60f);
            _defendButton.onClick.AddListener(() => Input?.SubmitDefend());

            _abilityButton = HudFactory.CreateButton("Ability", actions, "Способность");
            _abilityButton.GetComponent<RectTransform>().sizeDelta = new Vector2(210f, 60f);
            _abilityLabel = _abilityButton.GetComponentInChildren<Text>();
            _abilityButton.onClick.AddListener(() => Input?.ToggleAbility());

            _hint = HudFactory.CreateLabel("Hint", bar,
                "Потяни от гладиатора и отпусти — рывок   ·   Space — защита   ·   Q — способность",
                16, TextAnchor.LowerCenter, HudFactory.MutedTextColor);
            _hint.rectTransform.anchorMin = new Vector2(0f, 0f);
            _hint.rectTransform.anchorMax = new Vector2(1f, 0f);
            _hint.rectTransform.pivot = new Vector2(0.5f, 0f);
            _hint.rectTransform.sizeDelta = new Vector2(0f, 22f);
            _hint.rectTransform.anchoredPosition = new Vector2(0f, 6f);
        }

        private void BuildOverlay(RectTransform root)
        {
            var panel = HudFactory.CreatePanel("Overlay", root, HudFactory.OverlayColor);
            HudFactory.Stretch(panel.rectTransform);
            _overlay = panel.gameObject;

            _overlayTitle = HudFactory.CreateLabel("Title", panel.transform, "", 56);
            _overlayTitle.rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
            _overlayTitle.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            _overlayTitle.rectTransform.sizeDelta = new Vector2(900f, 70f);
            _overlayTitle.rectTransform.anchoredPosition = new Vector2(0f, 110f);

            _overlaySubtitle = HudFactory.CreateLabel("Subtitle", panel.transform, "", 24,
                TextAnchor.MiddleCenter, HudFactory.MutedTextColor);
            _overlaySubtitle.rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
            _overlaySubtitle.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            _overlaySubtitle.rectTransform.sizeDelta = new Vector2(900f, 40f);
            _overlaySubtitle.rectTransform.anchoredPosition = new Vector2(0f, 50f);

            _pickRow = HudFactory.CreateRect("PickRow", panel.transform);
            _pickRow.anchorMin = new Vector2(0.5f, 0.5f);
            _pickRow.anchorMax = new Vector2(0.5f, 0.5f);
            _pickRow.sizeDelta = new Vector2(900f, 90f);
            _pickRow.anchoredPosition = new Vector2(0f, -40f);
            HudFactory.AddRow(_pickRow, 16f, TextAnchor.MiddleCenter);

            foreach (var def in GladiatorDef.All)
            {
                var id = def.Id;
                var button = HudFactory.CreateButton($"Pick_{def.Name}", _pickRow, def.Name, 22);
                button.GetComponent<RectTransform>().sizeDelta = new Vector2(260f, 76f);
                button.onClick.AddListener(() => Controller?.SubmitPlayerPick(id));
                _pickButtons.Add(button);
                _pickButtonLabels.Add(button.GetComponentInChildren<Text>());
            }

            _restartButton = HudFactory.CreateButton("Restart", panel.transform, "Ещё раз", 26);
            _restartButton.GetComponent<RectTransform>().anchorMin = new Vector2(0.5f, 0.5f);
            _restartButton.GetComponent<RectTransform>().anchorMax = new Vector2(0.5f, 0.5f);
            _restartButton.GetComponent<RectTransform>().sizeDelta = new Vector2(240f, 70f);
            _restartButton.GetComponent<RectTransform>().anchoredPosition = new Vector2(0f, -40f);
            _restartButton.onClick.AddListener(() => Controller?.RestartMatch());

            _overlay.SetActive(false);
        }

        // ------------------------------------------------------------------
        // per-frame refresh
        // ------------------------------------------------------------------

        private void LateUpdate()
        {
            var manager = Controller != null ? Controller.Manager : null;
            if (manager == null) return;

            var state = manager.State;
            SyncRoster(state);
            SyncActions(state);
            SyncPhaseLabel(state);
            SyncOverlay(state);
        }

        private void SyncRoster(MatchState state)
        {
            SyncSide(_playerIcons, state.P1);
            SyncSide(_botIcons, state.Bot);
            SyncSide(_playerCards, state.P1);
        }

        private static void SyncSide(List<RosterEntryView> views, PlayerState player)
        {
            for (int i = 0; i < views.Count; i++)
            {
                bool present = i < player.Roster.Count;
                if (views[i].gameObject.activeSelf != present) views[i].gameObject.SetActive(present);
                if (present) views[i].Sync(player.Roster[i], ReferenceEquals(player.Roster[i], player.Active));
            }
        }

        private void SyncActions(MatchState state)
        {
            var g = state.P1.Active;
            bool canAct = state.Phase == MatchPhase.Planning && g != null && g.Alive;

            _defendButton.interactable = canAct;
            _abilityButton.interactable = canAct && g.CanActivateAbility;

            bool armed = Input != null && Input.AbilityArmed;
            _abilityLabel.text = armed ? "Способность ✓" : "Способность";
            _abilityLabel.color = armed ? HudFactory.RageColor : HudFactory.TextColor;

            _hint.enabled = canAct;
        }

        private void SyncPhaseLabel(MatchState state)
        {
            switch (state.Phase)
            {
                case MatchPhase.Planning:
                    float left = Mathf.Max(0f, GameConstants.PlanningTime - state.PhaseTimer);
                    _phaseLabel.text = $"Раунд {state.Round} · цикл {state.Cycle} · планирование {left:0.0}";
                    break;
                case MatchPhase.Action:
                    _phaseLabel.text = $"Раунд {state.Round} · цикл {state.Cycle} · действие";
                    break;
                case MatchPhase.Pick:
                    _phaseLabel.text = "Выбор гладиатора";
                    break;
                default:
                    _phaseLabel.text = state.Round > 0 ? $"Раунд {state.Round}" : "";
                    break;
            }
        }

        private void SyncOverlay(MatchState state)
        {
            // Keyed off NeedsPick rather than Phase: from round two only the loser picks, so when the
            // player is the survivor the Pick phase is entered and left within the same frame and
            // would never be catchable by a phase check.
            bool picking = state.P1.NeedsPick;
            bool matchOver = state.Phase == MatchPhase.MatchEnd;
            bool banner = state.Phase == MatchPhase.Reveal || state.Phase == MatchPhase.RoundEnd;

            bool visible = picking || matchOver || banner;
            if (_overlay.activeSelf != visible) _overlay.SetActive(visible);
            if (!visible) return;

            SetActive(_pickRow.gameObject, picking);
            SetActive(_restartButton.gameObject, matchOver);

            if (picking)
            {
                _overlayTitle.text = state.Round == 0 ? "Colosseum Duel" : "Выберите гладиатора";
                _overlaySubtitle.text = state.Round == 0
                    ? "Кем начнёте бой?"
                    : "Ваш боец пал — кто выйдет на арену?";

                for (int i = 0; i < _pickButtons.Count; i++)
                {
                    var def = GladiatorDef.All[i];
                    var instance = state.P1.Roster.Find(x => x.Def.Id == def.Id);
                    bool alive = instance != null && instance.Alive;
                    _pickButtons[i].interactable = alive;
                    _pickButtonLabels[i].text = alive
                        ? $"{def.Name}\n{Mathf.CeilToInt(instance.Hp)} HP · {def.AbilityName}"
                        : $"{def.Name}\nвыбыл";
                }
                return;
            }

            if (matchOver)
            {
                bool won = state.WinnerSide == PlayerSide.P1;
                _overlayTitle.text = won ? "Победа" : "Поражение";
                _overlayTitle.color = won ? HudFactory.HpColor : HudFactory.BotColor;
                _overlaySubtitle.text = won
                    ? "У противника не осталось гладиаторов."
                    : "Ваши гладиаторы пали.";
                return;
            }

            if (state.Phase == MatchPhase.Reveal)
            {
                _overlayTitle.text = $"Раунд {state.Round}";
                _overlayTitle.color = HudFactory.TextColor;
                _overlaySubtitle.text = state.P1.Active != null && state.Bot.Active != null
                    ? $"{state.P1.Active.Def.Name}   против   {state.Bot.Active.Def.Name}"
                    : "";
                return;
            }

            // RoundEnd
            bool playerLost = state.P1.Active == null || !state.P1.Active.Alive;
            _overlayTitle.text = playerLost ? "Раунд проигран" : "Раунд выигран";
            _overlayTitle.color = playerLost ? HudFactory.BotColor : HudFactory.HpColor;
            _overlaySubtitle.text = "";
        }

        private static void SetActive(GameObject go, bool active)
        {
            if (go.activeSelf != active) go.SetActive(active);
        }
    }
}
