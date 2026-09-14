using System.Collections.Generic;
using System.Linq;
using ColosseumDuel.Core;
using ColosseumDuel.Gameplay.View;
using UnityEngine;
using UnityEngine.UI;

namespace ColosseumDuel.Gameplay.Hud
{
    /// <summary>
    /// Everything that happens before a match: the squad you are taking in, the screen for
    /// changing it, and the window for choosing what each man fights with.
    ///
    /// It sits over the match rather than in front of it. The match underneath is already built and
    /// waiting on its pick screen, so "start" is a curtain going up rather than a load - which also
    /// means every fight, including the first, is set up by exactly the same code path.
    /// </summary>
    public sealed class MenuView : MonoBehaviour
    {
        /// <summary>
        /// How many of each archetype the player may take. One: with six archetypes on offer a
        /// squad is three different men, and the bot fields the other three.
        /// </summary>
        public const int CopiesPerArchetype = 1;

        public enum Screen
        {
            /// <summary>The squad, and the two ways forward from it.</summary>
            Main,

            /// <summary>Six fighters, three places in the team - and, over it, a man's abilities.</summary>
            Roster,

            /// <summary>Out of the way; the match has it.</summary>
            Closed,
        }

        public Screen Current { get; private set; } = Screen.Main;
        public bool IsOpen => Current != Screen.Closed;

        /// <summary>Whether the ability window is up over the roster, and for which offer.</summary>
        public bool AbilityWindowOpen => _abilityFor >= 0 && Current == Screen.Roster;

        private static readonly Color SkillGreen = new Color(0.35f, 0.85f, 0.42f);
        private static readonly Color Gold = new Color(1.00f, 0.78f, 0.26f);
        private static readonly Color CardColor = new Color(0.12f, 0.12f, 0.16f, 0.97f);
        private static readonly Color WindowColor = new Color(0.07f, 0.07f, 0.09f, 0.97f);
        private static readonly Color FightRed = new Color(0.80f, 0.22f, 0.18f, 1f);
        private static readonly Color IdleButton = new Color(0.30f, 0.31f, 0.36f, 1f);
        private static readonly Color DarkText = new Color(0.10f, 0.08f, 0.04f);

        private GameController _controller;
        private ViewPalette _palette;

        private GameObject _mainPanel;
        private GameObject _rosterPanel;
        private GameObject _abilityWindow;
        private readonly List<SquadTile> _squadTiles = new List<SquadTile>();
        private readonly List<RosterCard> _rosterCards = new List<RosterCard>();
        private readonly List<TeamSlot> _teamSlots = new List<TeamSlot>();
        private readonly List<AbilityCard> _abilityCards = new List<AbilityCard>();
        private Text _rosterSubtitle;
        private Button _confirm;

        private Image _windowPortrait;
        private Text _windowName;
        private Text _windowBlurb;
        private Text _windowStats;
        private Text _windowWeapon;
        private Image _detailIcon;
        private Text _detailName;
        private Text _detailText;

        /// <summary>The offer whose abilities the window is showing, or -1 when it is closed.</summary>
        private int _abilityFor = -1;

        /// <summary>
        /// What the roster screen is holding, which is not the squad until the player confirms it.
        ///
        /// Kept apart on purpose: choosing a squad restarts the match, and doing that on every tap
        /// would throw away the arena three times while somebody was still deciding.
        /// </summary>
        private readonly List<int> _draft = new List<int>();

        // ------------------------------------------------------------------
        // construction
        // ------------------------------------------------------------------

        /// <summary>One of the six fighters on offer: an archetype and which copy of it this is.</summary>
        private sealed class Offer
        {
            public GladiatorDef Def;
            public int Copy;
        }

        private static IReadOnlyList<Offer> BuildOffers()
        {
            var offers = new List<Offer>();
            foreach (var def in GladiatorDef.All)
                for (int copy = 0; copy < CopiesPerArchetype; copy++)
                    offers.Add(new Offer { Def = def, Copy = copy });
            return offers;
        }

        private static readonly IReadOnlyList<Offer> Offers = BuildOffers();

        private sealed class SquadTile
        {
            public Image Icon;
            public Image Skill;
            public Image Ability;
            public Text Name;
        }

        private sealed class RosterCard
        {
            public Button Button;
            public Text ButtonLabel;
            public Image Outline;
            public Image Frame;
            public Text Order;
            public Image AbilityIcon;
            public Text AbilityLabel;
        }

        private sealed class TeamSlot
        {
            public Image Icon;
            public Image Ability;
            public Text Name;
            public Text Empty;
            public Button Remove;
        }

        private sealed class AbilityCard
        {
            public Image Outline;
            public Image Icon;
            public Text Name;
            public Text Text;
            public Text Duration;
            public Button Select;
            public Text SelectLabel;
        }

        public static MenuView Create(Transform canvas, ViewPalette palette, GameController controller)
        {
            var root = HudFactory.CreateRect("Menu", canvas);
            HudFactory.Stretch(root);

            var view = root.gameObject.AddComponent<MenuView>();
            view._controller = controller;
            view._palette = palette;

            view.BuildMain(root);
            view.BuildRoster(root);
            return view;
        }

        private void BuildMain(RectTransform root)
        {
            var panel = HudFactory.CreatePanel("MenuMain", root, HudFactory.OverlayColor);
            HudFactory.Stretch(panel.rectTransform);
            _mainPanel = panel.gameObject;

            var title = HudFactory.CreateLabel("Title", panel.transform, "Colosseum Duel", 42);
            Centre(title.rectTransform, new Vector2(520f, 56f), 300f);

            var subtitle = HudFactory.CreateLabel("Subtitle", panel.transform,
                "Your three, and the order they stand in", 18, TextAnchor.MiddleCenter,
                HudFactory.MutedTextColor);
            Centre(subtitle.rectTransform, new Vector2(520f, 30f), 255f);

            // The squad, laid out the way the pick screen will list it, so the order chosen here is
            // the order they are offered in.
            for (int slot = 0; slot < GameConstants.SquadSize; slot++)
            {
                var tile = HudFactory.CreatePanel($"SquadTile_{slot}", panel.transform,
                    new Color(0.14f, 0.14f, 0.18f, 0.95f));
                Centre(tile.rectTransform, new Vector2(380f, 74f), 160f - slot * 84f);

                var icon = HudFactory.CreatePanel($"SquadIcon_{slot}", tile.transform, Color.white);
                icon.preserveAspect = true;
                icon.raycastTarget = false;
                Anchor(icon.rectTransform, new Vector2(0f, 0.5f), new Vector2(52f, 52f), new Vector2(12f, 0f));

                // Name over weapon, on two lines: on one, with the ability badge beside the weapon's,
                // the longer weapon names ran under the badges.
                var name = HudFactory.CreateLabel($"SquadName_{slot}", tile.transform, "", 15,
                    TextAnchor.MiddleLeft);
                name.rectTransform.anchorMin = Vector2.zero;
                name.rectTransform.anchorMax = Vector2.one;
                name.rectTransform.offsetMin = new Vector2(76f, 0f);
                name.rectTransform.offsetMax = new Vector2(-104f, 0f);

                // The ability he is taking in, beside the weapon he fights with.
                var ability = HudFactory.CreatePanel($"SquadAbility_{slot}", tile.transform, Color.white);
                ability.preserveAspect = true;
                ability.raycastTarget = false;
                Anchor(ability.rectTransform, new Vector2(1f, 0.5f), new Vector2(38f, 38f), new Vector2(-58f, 0f));

                // The green badge: which weapon this one is trained in. On the right, where the eye
                // lands last, because it answers a question about a fighter already recognised.
                var skill = HudFactory.CreatePanel($"SquadSkill_{slot}", tile.transform, SkillGreen);
                skill.preserveAspect = true;
                skill.raycastTarget = false;
                Anchor(skill.rectTransform, new Vector2(1f, 0.5f), new Vector2(38f, 38f), new Vector2(-14f, 0f));

                _squadTiles.Add(new SquadTile { Icon = icon, Skill = skill, Ability = ability, Name = name });
            }

            var start = HudFactory.CreateButton("StartMatch", panel.transform, "Start match", 24);
            Centre((RectTransform)start.transform, new Vector2(300f, 66f), -140f);
            start.onClick.AddListener(StartMatch);

            var choose = HudFactory.CreateButton("ChooseGladiators", panel.transform,
                "Choose gladiators", 22);
            Centre((RectTransform)choose.transform, new Vector2(300f, 58f), -216f);
            choose.onClick.AddListener(OpenRosterScreen);

            // Which build this is. The link stays the same from one build to the next and a browser
            // will happily serve the last one from its cache, so the menu says which one is open.
            // The build stamps the version with its branch, commit and time (ProjectBootstrap).
            var version = HudFactory.CreateLabel("BuildVersion", panel.transform,
                $"build {Application.version}", 13, TextAnchor.LowerRight, HudFactory.MutedTextColor);
            Anchor(version.rectTransform, new Vector2(1f, 0f), new Vector2(420f, 20f), new Vector2(-14f, 10f));
        }

        // --- the roster: six cards, and the team along the bottom ----------------------------

        private const float CardWidth = 266f;
        private const float CardHeight = 238f;
        private const float CardGap = 8f;
        private const float GridTop = 84f;

        private void BuildRoster(RectTransform root)
        {
            var panel = HudFactory.CreatePanel("MenuRoster", root, HudFactory.OverlayColor);
            HudFactory.Stretch(panel.rectTransform);
            _rosterPanel = panel.gameObject;

            // Back to the squad as it was: the draft is only a draft until the fight is started.
            var back = HudFactory.CreateButton("RosterBack", panel.transform, "<", 26);
            Anchor((RectTransform)back.transform, new Vector2(0f, 1f), new Vector2(50f, 46f), new Vector2(14f, -14f));
            back.onClick.AddListener(() =>
            {
                _abilityFor = -1;
                Current = Screen.Main;
            });

            var title = HudFactory.CreateLabel("Title", panel.transform, "SELECT YOUR GLADIATORS", 26,
                TextAnchor.MiddleCenter, Gold);
            title.fontStyle = FontStyle.Bold;
            Anchor(title.rectTransform, new Vector2(0.5f, 1f), new Vector2(440f, 34f), new Vector2(0f, -14f));

            _rosterSubtitle = HudFactory.CreateLabel("Subtitle", panel.transform, "", 15,
                TextAnchor.MiddleCenter, HudFactory.MutedTextColor);
            Anchor(_rosterSubtitle.rectTransform, new Vector2(0.5f, 1f), new Vector2(440f, 22f), new Vector2(0f, -50f));

            // Two columns of three: a portrait frame is taller than it is wide, and three cards across
            // this width leave no room for the numbers that the choice is made on.
            for (int i = 0; i < Offers.Count; i++)
                _rosterCards.Add(BuildCard(panel.transform, i));

            BuildTeamStrip(panel.transform);
            BuildAbilityWindow(panel.transform);
        }

        private RosterCard BuildCard(Transform parent, int index)
        {
            var def = Offers[index].Def;
            int column = index % 2, row = index / 2;

            // A holder, so the gold outline can sit behind the card rather than over it.
            var holder = HudFactory.CreateRect($"OfferSlot_{index}", parent);
            Anchor(holder, new Vector2(0.5f, 1f), new Vector2(CardWidth, CardHeight),
                new Vector2((column == 0 ? -1f : 1f) * (CardWidth + CardGap) * 0.5f,
                            -GridTop - row * (CardHeight + CardGap)));

            var outline = HudFactory.CreatePanel($"OfferOutline_{index}", holder, Gold);
            HudFactory.Stretch(outline.rectTransform, -3f);
            outline.raycastTarget = false;

            // The card itself opens the man's abilities; the button along its foot takes him.
            var card = HudFactory.CreateButton($"OfferCard_{index}", holder, "", 14);
            ((Image)card.targetGraphic).color = CardColor;
            HudFactory.Stretch((RectTransform)card.transform);
            card.onClick.AddListener(() => OpenAbilityWindow(index));

            var portrait = HudFactory.CreatePanel($"OfferIcon_{index}", card.transform, Color.white);
            HudFactory.UseSprite(portrait, _palette != null ? _palette.IconFor(def.Id) : null);
            portrait.color = _palette != null ? _palette.IconTint(def.Id) : Color.white;
            portrait.preserveAspect = true;
            portrait.raycastTarget = false;
            portrait.enabled = portrait.sprite != null;
            TopBand(portrait.rectTransform, 6f, 104f);

            // In the team, and where: the ring and the number are the whole of "this one goes third".
            var frame = HudFactory.CreatePanel($"OfferFrame_{index}", card.transform, Gold);
            HudFactory.UseSprite(frame, _palette != null ? _palette.Ring : null);
            frame.raycastTarget = false;
            Anchor(frame.rectTransform, new Vector2(1f, 1f), new Vector2(30f, 30f), new Vector2(-8f, -8f));
            var order = HudFactory.CreateLabel($"OfferOrder_{index}", frame.transform, "", 15, TextAnchor.MiddleCenter, Gold);
            HudFactory.Stretch(order.rectTransform);

            var name = HudFactory.CreateLabel($"OfferName_{index}", card.transform, def.Name, 18, TextAnchor.MiddleLeft);
            name.fontStyle = FontStyle.Bold;
            Anchor(name.rectTransform, new Vector2(0f, 1f), new Vector2(CardWidth - 24f, 22f), new Vector2(12f, -112f));

            var stats = HudFactory.CreateLabel($"OfferStats_{index}", card.transform,
                $"HP {Mathf.RoundToInt(def.MaxHp)}    DMG {Mathf.RoundToInt(def.Damage)}    SPD {Mathf.RoundToInt(def.Speed)}",
                13, TextAnchor.MiddleLeft);
            Anchor(stats.rectTransform, new Vector2(0f, 1f), new Vector2(CardWidth - 24f, 18f), new Vector2(12f, -136f));

            var weapon = WeaponDef.Get(def.SkilledWith);
            var skillIcon = HudFactory.CreatePanel($"OfferSkillIcon_{index}", card.transform, SkillGreen);
            HudFactory.UseSprite(skillIcon, _palette != null ? _palette.IconFor(def.SkilledWith) : null);
            skillIcon.preserveAspect = true;
            skillIcon.raycastTarget = false;
            skillIcon.enabled = skillIcon.sprite != null;
            Anchor(skillIcon.rectTransform, new Vector2(0f, 1f), new Vector2(18f, 18f), new Vector2(12f, -157f));

            var skill = HudFactory.CreateLabel($"OfferSkill_{index}", card.transform, weapon.Name, 12,
                TextAnchor.MiddleLeft, SkillGreen);
            Anchor(skill.rectTransform, new Vector2(0f, 1f), new Vector2(170f, 18f), new Vector2(34f, -157f));

            // The ability he takes in, as it stands: its name, and its icon in the corner.
            var abilityLabel = HudFactory.CreateLabel($"OfferAbility_{index}", card.transform, "", 12,
                TextAnchor.MiddleLeft, HudFactory.RageColor);
            Anchor(abilityLabel.rectTransform, new Vector2(0f, 1f), new Vector2(170f, 18f), new Vector2(12f, -178f));

            var abilityIcon = HudFactory.CreatePanel($"OfferAbilityIcon_{index}", card.transform, Color.white);
            abilityIcon.preserveAspect = true;
            abilityIcon.raycastTarget = false;
            Anchor(abilityIcon.rectTransform, new Vector2(1f, 0f), new Vector2(48f, 48f), new Vector2(-10f, 44f));

            var select = HudFactory.CreateButton($"Offer_{index}", card.transform, "Select", 15);
            var selectRect = (RectTransform)select.transform;
            selectRect.anchorMin = new Vector2(0f, 0f);
            selectRect.anchorMax = new Vector2(1f, 0f);
            selectRect.pivot = new Vector2(0.5f, 0f);
            selectRect.sizeDelta = new Vector2(-20f, 30f);
            selectRect.anchoredPosition = new Vector2(0f, 8f);
            select.onClick.AddListener(() => ToggleOffer(index));

            return new RosterCard
            {
                Button = select,
                ButtonLabel = select.GetComponentInChildren<Text>(),
                Outline = outline,
                Frame = frame,
                Order = order,
                AbilityIcon = abilityIcon,
                AbilityLabel = abilityLabel,
            };
        }

        private void BuildTeamStrip(Transform parent)
        {
            var strip = HudFactory.CreatePanel("TeamStrip", parent, WindowColor);
            var rect = strip.rectTransform;
            rect.anchorMin = new Vector2(0f, 0f);
            rect.anchorMax = new Vector2(1f, 0f);
            rect.pivot = new Vector2(0.5f, 0f);
            rect.sizeDelta = new Vector2(-20f, 184f);
            rect.anchoredPosition = new Vector2(0f, 10f);

            var title = HudFactory.CreateLabel("TeamTitle", strip.transform, "YOUR TEAM", 18, TextAnchor.MiddleCenter, Gold);
            title.fontStyle = FontStyle.Bold;
            Anchor(title.rectTransform, new Vector2(0.5f, 1f), new Vector2(300f, 24f), new Vector2(-90f, -8f));

            for (int slot = 0; slot < GameConstants.SquadSize; slot++)
            {
                int index = slot;
                var tile = HudFactory.CreatePanel($"TeamSlot_{slot}", strip.transform, CardColor);
                Anchor(tile.rectTransform, new Vector2(0f, 0f), new Vector2(104f, 136f), new Vector2(12f + slot * 114f, 12f));

                var icon = HudFactory.CreatePanel($"TeamIcon_{slot}", tile.transform, Color.white);
                icon.preserveAspect = true;
                icon.raycastTarget = false;
                TopBand(icon.rectTransform, 4f, 96f);

                var ability = HudFactory.CreatePanel($"TeamAbility_{slot}", tile.transform, Color.white);
                ability.preserveAspect = true;
                ability.raycastTarget = false;
                Anchor(ability.rectTransform, new Vector2(1f, 0f), new Vector2(34f, 34f), new Vector2(-4f, 24f));

                var name = HudFactory.CreateLabel($"TeamName_{slot}", tile.transform, "", 13);
                Anchor(name.rectTransform, new Vector2(0.5f, 0f), new Vector2(100f, 20f), new Vector2(0f, 3f));

                var empty = HudFactory.CreateLabel($"TeamEmpty_{slot}", tile.transform, "empty", 14,
                    TextAnchor.MiddleCenter, HudFactory.MutedTextColor);
                HudFactory.Stretch(empty.rectTransform);

                var remove = HudFactory.CreateButton($"TeamRemove_{slot}", tile.transform, "X", 14);
                Anchor((RectTransform)remove.transform, new Vector2(1f, 1f), new Vector2(26f, 26f), new Vector2(-3f, -3f));
                remove.onClick.AddListener(() => RemoveFromTeam(index));

                _teamSlots.Add(new TeamSlot { Icon = icon, Ability = ability, Name = name, Empty = empty, Remove = remove });
            }

            // The fight starts from here: the team as it stands goes in, and the curtain goes up.
            _confirm = HudFactory.CreateButton("ConfirmSquad", strip.transform, "Start Fight", 22);
            ((Image)_confirm.targetGraphic).color = FightRed;
            _confirm.GetComponentInChildren<Text>().fontStyle = FontStyle.Bold;
            Anchor((RectTransform)_confirm.transform, new Vector2(1f, 0f), new Vector2(190f, 136f), new Vector2(-12f, 12f));
            _confirm.onClick.AddListener(ConfirmSquad);
        }

        // --- the ability window: over the roster, for one man ---------------------------------

        private void BuildAbilityWindow(Transform parent)
        {
            var shade = HudFactory.CreatePanel("AbilityWindow", parent, new Color(0f, 0f, 0f, 0.6f));
            HudFactory.Stretch(shade.rectTransform);
            shade.raycastTarget = true; // taps behind it go nowhere
            _abilityWindow = shade.gameObject;

            var window = HudFactory.CreatePanel("AbilityPanel", shade.transform, WindowColor);
            Centre(window.rectTransform, new Vector2(556f, 716f), 0f);

            var title = HudFactory.CreateLabel("AbilityTitle", window.transform, "SELECT ABILITY", 26,
                TextAnchor.MiddleCenter, Gold);
            title.fontStyle = FontStyle.Bold;
            Anchor(title.rectTransform, new Vector2(0.5f, 1f), new Vector2(360f, 34f), new Vector2(0f, -16f));

            var close = HudFactory.CreateButton("AbilityClose", window.transform, "X", 22);
            Anchor((RectTransform)close.transform, new Vector2(1f, 1f), new Vector2(46f, 42f), new Vector2(-12f, -12f));
            close.onClick.AddListener(CloseAbilityWindow);

            // The man: his portrait, and what he brings before an ability is added.
            _windowPortrait = HudFactory.CreatePanel("AbilityPortrait", window.transform, Color.white);
            _windowPortrait.preserveAspect = true;
            _windowPortrait.raycastTarget = false;
            Anchor(_windowPortrait.rectTransform, new Vector2(0f, 1f), new Vector2(150f, 150f), new Vector2(16f, -64f));

            _windowName = HudFactory.CreateLabel("AbilityGladiatorName", window.transform, "", 24, TextAnchor.MiddleLeft);
            _windowName.fontStyle = FontStyle.Bold;
            Anchor(_windowName.rectTransform, new Vector2(0f, 1f), new Vector2(360f, 30f), new Vector2(182f, -66f));

            _windowBlurb = Wrapped(HudFactory.CreateLabel("AbilityGladiatorBlurb", window.transform, "", 14,
                TextAnchor.UpperLeft, HudFactory.MutedTextColor));
            Anchor(_windowBlurb.rectTransform, new Vector2(0f, 1f), new Vector2(356f, 40f), new Vector2(182f, -98f));

            _windowStats = HudFactory.CreateLabel("AbilityGladiatorStats", window.transform, "", 15, TextAnchor.UpperLeft);
            Anchor(_windowStats.rectTransform, new Vector2(0f, 1f), new Vector2(150f, 70f), new Vector2(182f, -142f));

            _windowWeapon = Wrapped(HudFactory.CreateLabel("AbilityGladiatorWeapon", window.transform, "", 14,
                TextAnchor.UpperLeft, SkillGreen));
            Anchor(_windowWeapon.rectTransform, new Vector2(0f, 1f), new Vector2(200f, 70f), new Vector2(340f, -142f));

            var choose = HudFactory.CreateLabel("AbilityChoose", window.transform, "CHOOSE AN ABILITY", 18,
                TextAnchor.MiddleCenter, Gold);
            choose.fontStyle = FontStyle.Bold;
            Anchor(choose.rectTransform, new Vector2(0.5f, 1f), new Vector2(360f, 24f), new Vector2(0f, -230f));

            for (int k = 0; k < 3; k++)
                _abilityCards.Add(BuildAbilityCard(window.transform, k));

            // The chosen one, spelled out once more along the foot of the window.
            var detail = HudFactory.CreatePanel("AbilityDetail", window.transform, CardColor);
            var detailRect = detail.rectTransform;
            detailRect.anchorMin = new Vector2(0f, 0f);
            detailRect.anchorMax = new Vector2(1f, 0f);
            detailRect.pivot = new Vector2(0.5f, 0f);
            detailRect.sizeDelta = new Vector2(-24f, 84f);
            detailRect.anchoredPosition = new Vector2(0f, 12f);

            _detailIcon = HudFactory.CreatePanel("AbilityDetailIcon", detail.transform, Color.white);
            _detailIcon.preserveAspect = true;
            _detailIcon.raycastTarget = false;
            Anchor(_detailIcon.rectTransform, new Vector2(0f, 0.5f), new Vector2(64f, 64f), new Vector2(10f, 0f));

            _detailName = HudFactory.CreateLabel("AbilityDetailName", detail.transform, "", 18, TextAnchor.UpperLeft);
            _detailName.fontStyle = FontStyle.Bold;
            Anchor(_detailName.rectTransform, new Vector2(0f, 1f), new Vector2(430f, 24f), new Vector2(86f, -10f));

            _detailText = Wrapped(HudFactory.CreateLabel("AbilityDetailText", detail.transform, "", 14,
                TextAnchor.UpperLeft, HudFactory.TextColor));
            Anchor(_detailText.rectTransform, new Vector2(0f, 1f), new Vector2(430f, 44f), new Vector2(86f, -36f));

            _abilityWindow.SetActive(false);
        }

        private AbilityCard BuildAbilityCard(Transform window, int k)
        {
            const float width = 168f, height = 270f;
            float x = (k - 1) * (width + 12f);

            var holder = HudFactory.CreateRect($"AbilitySlot_{k}", window);
            Anchor(holder, new Vector2(0.5f, 1f), new Vector2(width, height), new Vector2(x, -262f));

            var outline = HudFactory.CreatePanel($"AbilityOutline_{k}", holder, Gold);
            HudFactory.Stretch(outline.rectTransform, -3f);
            outline.raycastTarget = false;

            var card = HudFactory.CreatePanel($"AbilityCard_{k}", holder, CardColor);
            HudFactory.Stretch(card.rectTransform);

            var icon = HudFactory.CreatePanel($"AbilityIcon_{k}", card.transform, Color.white);
            icon.preserveAspect = true;
            icon.raycastTarget = false;
            Anchor(icon.rectTransform, new Vector2(0.5f, 1f), new Vector2(88f, 88f), new Vector2(0f, -12f));

            var name = Wrapped(HudFactory.CreateLabel($"AbilityName_{k}", card.transform, "", 17, TextAnchor.UpperCenter));
            name.fontStyle = FontStyle.Bold;
            Anchor(name.rectTransform, new Vector2(0.5f, 1f), new Vector2(width - 12f, 24f), new Vector2(0f, -106f));

            var text = Wrapped(HudFactory.CreateLabel($"AbilityText_{k}", card.transform, "", 13, TextAnchor.UpperCenter));
            Anchor(text.rectTransform, new Vector2(0.5f, 1f), new Vector2(width - 16f, 100f), new Vector2(0f, -134f));

            var duration = HudFactory.CreateLabel($"AbilityDuration_{k}", card.transform, "", 12,
                TextAnchor.MiddleCenter, HudFactory.MutedTextColor);
            Anchor(duration.rectTransform, new Vector2(0.5f, 0f), new Vector2(width - 12f, 18f), new Vector2(0f, 10f));

            var select = HudFactory.CreateButton($"AbilitySelect_{k}", window, "Select", 16);
            Anchor((RectTransform)select.transform, new Vector2(0.5f, 1f), new Vector2(width - 8f, 40f),
                new Vector2(x, -262f - height - 10f));
            select.onClick.AddListener(() => SelectAbility(k));

            return new AbilityCard
            {
                Outline = outline, Icon = icon, Name = name, Text = text, Duration = duration,
                Select = select, SelectLabel = select.GetComponentInChildren<Text>(),
            };
        }

        // ------------------------------------------------------------------
        // behaviour
        // ------------------------------------------------------------------

        /// <summary>Drops the curtain: the match underneath has been waiting on its pick screen.</summary>
        public void StartMatch() => Current = Screen.Closed;

        /// <summary>
        /// Raises the curtain again, from inside a match.
        ///
        /// The match is thrown away rather than paused. The menu's own button says "start match",
        /// and a player who came back here and pressed it should get a match that starts - not one
        /// resumed at whatever half-dead clash he walked out of, which is what leaving the state
        /// alone would hand him. Nothing is lost that the game keeps between matches anyway.
        /// </summary>
        public void ReturnToMainMenu()
        {
            _controller.RestartMatch();
            _abilityFor = -1;
            Current = Screen.Main;
        }

        /// <summary>
        /// Opens the roster screen with the squad already in it.
        ///
        /// Starting from what they have rather than from nothing: a player who came here to swap one
        /// fighter should not have to re-pick the other two, and an empty screen also gives them no
        /// idea what they are changing.
        /// </summary>
        public void OpenRosterScreen()
        {
            _draft.Clear();
            foreach (var id in _controller.Squad)
            {
                int offer = FirstFreeOfferOf(id);
                if (offer >= 0) _draft.Add(offer);
            }

            _abilityFor = -1;
            Current = Screen.Roster;
        }

        /// <summary>Opens a man's abilities over the roster.</summary>
        public void OpenAbilityWindow(int offer)
        {
            if (offer < 0 || offer >= Offers.Count) return;
            _abilityFor = offer;
        }

        public void CloseAbilityWindow() => _abilityFor = -1;

        /// <summary>
        /// Gives the man in the window the k-th of his three abilities. Kept against his archetype
        /// on the controller, so it goes into this fight and the next ones with him - in the team or not.
        /// </summary>
        private void SelectAbility(int k)
        {
            if (_abilityFor < 0) return;
            var def = Offers[_abilityFor].Def;
            if (k < 0 || k >= def.Abilities.Count) return;
            _controller.SetAbility(def.Id, def.Abilities[k]);
        }

        private int FirstFreeOfferOf(GladiatorId id)
        {
            for (int i = 0; i < Offers.Count; i++)
                if (Offers[i].Def.Id == id && !_draft.Contains(i)) return i;
            return -1;
        }

        /// <summary>
        /// Adds or removes one fighter.
        ///
        /// Order is kept, because it is the order the pick screen will offer them in - so tapping a
        /// fourth is refused rather than silently pushing out the first. Being told "you already
        /// have three" is a smaller surprise than losing one you chose.
        /// </summary>
        private void ToggleOffer(int index)
        {
            if (_draft.Contains(index)) _draft.Remove(index);
            else if (_draft.Count < GameConstants.SquadSize) _draft.Add(index);
        }

        private void RemoveFromTeam(int slot)
        {
            if (slot >= 0 && slot < _draft.Count) _draft.RemoveAt(slot);
        }

        /// <summary>Takes the team in and starts the fight.</summary>
        private void ConfirmSquad()
        {
            if (_draft.Count != GameConstants.SquadSize) return;

            _controller.SetSquad(_draft.Select(i => Offers[i].Def.Id).ToList());
            _abilityFor = -1;
            StartMatch();
        }

        private void LateUpdate()
        {
            bool main = Current == Screen.Main;
            bool roster = Current == Screen.Roster;
            bool window = roster && _abilityFor >= 0;

            if (_mainPanel.activeSelf != main) _mainPanel.SetActive(main);
            if (_rosterPanel.activeSelf != roster) _rosterPanel.SetActive(roster);
            if (_abilityWindow.activeSelf != window) _abilityWindow.SetActive(window);

            if (main) SyncMain();
            if (roster) SyncRoster();
            if (window) SyncAbilityWindow();
        }

        private Sprite AbilityIcon(AbilityKey key) => _palette != null ? _palette.AbilityIconFor(key) : null;

        /// <summary>A painted icon is drawn as painted; a pack glyph is white and takes the rage colour.</summary>
        private Color AbilityIconColor
            => _palette != null && _palette.AbilityIconsArePictures ? Color.white : HudFactory.RageColor;

        private void SyncMain()
        {
            for (int slot = 0; slot < _squadTiles.Count; slot++)
            {
                var tile = _squadTiles[slot];
                bool present = slot < _controller.Squad.Count;
                tile.Name.enabled = present;
                tile.Icon.enabled = present;
                tile.Skill.enabled = present;
                tile.Ability.enabled = present;
                if (!present) continue;

                var def = GladiatorDef.Get(_controller.Squad[slot]);
                tile.Name.text = $"{def.Name}\n{WeaponDef.Get(def.SkilledWith).Name}";
                HudFactory.UseSprite(tile.Icon, _palette != null ? _palette.IconFor(def.Id) : null);
                tile.Icon.color = _palette != null ? _palette.IconTint(def.Id) : Color.white;
                tile.Icon.enabled = tile.Icon.sprite != null;
                HudFactory.UseSprite(tile.Skill, _palette != null ? _palette.IconFor(def.SkilledWith) : null);
                tile.Skill.enabled = tile.Skill.sprite != null;
                HudFactory.UseSprite(tile.Ability, AbilityIcon(_controller.AbilityFor(def.Id)));
                tile.Ability.color = AbilityIconColor;
                tile.Ability.enabled = tile.Ability.sprite != null;
            }
        }

        private void SyncRoster()
        {
            bool full = _draft.Count >= GameConstants.SquadSize;
            for (int i = 0; i < _rosterCards.Count; i++)
            {
                var card = _rosterCards[i];
                int order = _draft.IndexOf(i);
                bool chosen = order >= 0;

                card.Frame.enabled = chosen;
                card.Order.text = chosen ? (order + 1).ToString() : "";
                card.Outline.enabled = chosen;

                // A card that cannot be added is dimmed rather than hidden: the fighter is still on
                // offer, just not while three are already chosen, and hiding him would read as him
                // having been taken away.
                card.Button.interactable = chosen || !full;
                card.ButtonLabel.text = chosen ? "Selected" : "Select";
                card.ButtonLabel.color = chosen ? DarkText : HudFactory.TextColor;
                ((Image)card.Button.targetGraphic).color = chosen ? Gold : IdleButton;

                var ability = AbilityDef.Get(_controller.AbilityFor(Offers[i].Def.Id));
                card.AbilityLabel.text = ability.Name;
                HudFactory.UseSprite(card.AbilityIcon, AbilityIcon(ability.Key));
                card.AbilityIcon.color = AbilityIconColor;
                card.AbilityIcon.enabled = card.AbilityIcon.sprite != null;
            }

            for (int slot = 0; slot < _teamSlots.Count; slot++)
            {
                var team = _teamSlots[slot];
                bool present = slot < _draft.Count;
                team.Icon.enabled = present;
                team.Ability.enabled = present;
                team.Name.enabled = present;
                team.Remove.gameObject.SetActive(present);
                team.Empty.enabled = !present;
                if (!present) continue;

                var def = Offers[_draft[slot]].Def;
                team.Name.text = def.Name;
                HudFactory.UseSprite(team.Icon, _palette != null ? _palette.IconFor(def.Id) : null);
                team.Icon.color = _palette != null ? _palette.IconTint(def.Id) : Color.white;
                team.Icon.enabled = team.Icon.sprite != null;
                HudFactory.UseSprite(team.Ability, AbilityIcon(_controller.AbilityFor(def.Id)));
                team.Ability.color = AbilityIconColor;
                team.Ability.enabled = team.Ability.sprite != null;
            }

            int left = GameConstants.SquadSize - _draft.Count;
            _rosterSubtitle.text = left > 0
                ? $"Choose 3 gladiators for the next fight - {left} more"
                : "Tap a gladiator to choose his ability";
            _confirm.interactable = left == 0;
        }

        private void SyncAbilityWindow()
        {
            var def = Offers[_abilityFor].Def;
            var weapon = WeaponDef.Get(def.SkilledWith);
            var chosen = _controller.AbilityFor(def.Id);

            HudFactory.UseSprite(_windowPortrait, _palette != null ? _palette.IconFor(def.Id) : null);
            _windowPortrait.color = _palette != null ? _palette.IconTint(def.Id) : Color.white;
            _windowPortrait.enabled = _windowPortrait.sprite != null;
            _windowName.text = def.Name;
            _windowBlurb.text = weapon.Description + ".";
            _windowStats.text = $"HP  {Mathf.RoundToInt(def.MaxHp)}\nDamage  {Mathf.RoundToInt(def.Damage)}\n" +
                                $"Speed  {Mathf.RoundToInt(def.Speed)}";
            _windowWeapon.text = weapon.Name;

            for (int k = 0; k < _abilityCards.Count; k++)
            {
                var card = _abilityCards[k];
                bool present = k < def.Abilities.Count;
                card.Select.gameObject.SetActive(present);
                if (!present) continue;

                var ability = AbilityDef.Get(def.Abilities[k]);
                bool isChosen = ability.Key == chosen;

                HudFactory.UseSprite(card.Icon, AbilityIcon(ability.Key));
                card.Icon.color = AbilityIconColor;
                card.Icon.enabled = card.Icon.sprite != null;
                card.Name.text = ability.Name;
                card.Text.text = Capitalised(ability.Description);
                card.Duration.text = $"Duration: {ability.DurationText}";
                card.Outline.enabled = isChosen;
                card.SelectLabel.text = isChosen ? "Selected" : "Select";
                card.SelectLabel.color = isChosen ? DarkText : HudFactory.TextColor;
                ((Image)card.Select.targetGraphic).color = isChosen ? Gold : IdleButton;
            }

            var picked = AbilityDef.Get(chosen);
            HudFactory.UseSprite(_detailIcon, AbilityIcon(picked.Key));
            _detailIcon.color = AbilityIconColor;
            _detailIcon.enabled = _detailIcon.sprite != null;
            _detailName.text = picked.Name;
            _detailText.text = Capitalised(picked.Summary);
        }

        // ------------------------------------------------------------------

        private static string Capitalised(string text)
            => string.IsNullOrEmpty(text) ? "" : char.ToUpperInvariant(text[0]) + text.Substring(1);

        private static Text Wrapped(Text label)
        {
            label.horizontalOverflow = HorizontalWrapMode.Wrap;
            return label;
        }

        /// <summary>A band across the top of the parent, this far in and this tall.</summary>
        private static void TopBand(RectTransform rect, float inset, float height)
        {
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.sizeDelta = new Vector2(-inset * 2f, height);
            rect.anchoredPosition = new Vector2(0f, -inset);
        }

        private static void Centre(RectTransform rect, Vector2 size, float y)
        {
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = size;
            rect.anchoredPosition = new Vector2(0f, y);
        }

        private static void Anchor(RectTransform rect, Vector2 anchor, Vector2 size, Vector2 offset)
        {
            rect.anchorMin = rect.anchorMax = anchor;
            rect.pivot = anchor;
            rect.sizeDelta = size;
            rect.anchoredPosition = offset;
        }
    }
}
