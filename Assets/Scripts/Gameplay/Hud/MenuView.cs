using System.Collections.Generic;
using System.Linq;
using ColosseumDuel.Core;
using ColosseumDuel.Gameplay.View;
using UnityEngine;
using UnityEngine.UI;

namespace ColosseumDuel.Gameplay.Hud
{
    /// <summary>
    /// Everything that happens before a match: the squad you are taking in, and the screen for
    /// changing it.
    ///
    /// It sits over the match rather than in front of it. The match underneath is already built and
    /// waiting on its pick screen, so "start" is a curtain going up rather than a load - which also
    /// means every fight, including the first, is set up by exactly the same code path.
    /// </summary>
    public sealed class MenuView : MonoBehaviour
    {
        /// <summary>How many of each archetype the player may take. Two, so a pair is possible.</summary>
        public const int CopiesPerArchetype = 2;

        public enum Screen
        {
            /// <summary>The squad, and the two ways forward from it.</summary>
            Main,

            /// <summary>Six fighters, three slots.</summary>
            Roster,

            /// <summary>Out of the way; the match has it.</summary>
            Closed,
        }

        public Screen Current { get; private set; } = Screen.Main;
        public bool IsOpen => Current != Screen.Closed;

        private static readonly Color SkillGreen = new Color(0.35f, 0.85f, 0.42f);

        private GameController _controller;
        private ViewPalette _palette;

        private GameObject _mainPanel;
        private GameObject _rosterPanel;
        private readonly List<SquadTile> _squadTiles = new List<SquadTile>();
        private readonly List<RosterCard> _rosterCards = new List<RosterCard>();
        private Text _rosterSubtitle;
        private Button _confirm;

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
            public Text Name;
        }

        private sealed class RosterCard
        {
            public Button Button;
            public Image Frame;
            public Text Order;
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

                var name = HudFactory.CreateLabel($"SquadName_{slot}", tile.transform, "", 17,
                    TextAnchor.MiddleLeft);
                name.rectTransform.anchorMin = Vector2.zero;
                name.rectTransform.anchorMax = Vector2.one;
                name.rectTransform.offsetMin = new Vector2(76f, 0f);
                name.rectTransform.offsetMax = new Vector2(-64f, 0f);

                // The green badge: which weapon this one is trained in. On the right, where the eye
                // lands last, because it answers a question about a fighter already recognised.
                var skill = HudFactory.CreatePanel($"SquadSkill_{slot}", tile.transform, SkillGreen);
                skill.preserveAspect = true;
                skill.raycastTarget = false;
                Anchor(skill.rectTransform, new Vector2(1f, 0.5f), new Vector2(38f, 38f), new Vector2(-14f, 0f));

                _squadTiles.Add(new SquadTile { Icon = icon, Skill = skill, Name = name });
            }

            var start = HudFactory.CreateButton("StartMatch", panel.transform, "Start match", 24);
            Centre((RectTransform)start.transform, new Vector2(300f, 66f), -140f);
            start.onClick.AddListener(StartMatch);

            var choose = HudFactory.CreateButton("ChooseGladiators", panel.transform,
                "Choose gladiators", 22);
            Centre((RectTransform)choose.transform, new Vector2(300f, 58f), -216f);
            choose.onClick.AddListener(OpenRosterScreen);
        }

        private void BuildRoster(RectTransform root)
        {
            var panel = HudFactory.CreatePanel("MenuRoster", root, HudFactory.OverlayColor);
            HudFactory.Stretch(panel.rectTransform);
            _rosterPanel = panel.gameObject;

            var title = HudFactory.CreateLabel("Title", panel.transform, "Choose three", 32);
            Centre(title.rectTransform, new Vector2(520f, 42f), 424f);

            _rosterSubtitle = HudFactory.CreateLabel("Subtitle", panel.transform, "", 17,
                TextAnchor.MiddleCenter, HudFactory.MutedTextColor);
            Centre(_rosterSubtitle.rectTransform, new Vector2(520f, 26f), 392f);

            // Six cards in one column. Two of each archetype are on offer, and they are listed as
            // six separate fighters rather than three with a counter, because that is what they
            // are: a squad of two Brutius fields two men, not one man twice.
            for (int i = 0; i < Offers.Count; i++)
            {
                int index = i;
                var offer = Offers[i];
                var def = offer.Def;

                var button = HudFactory.CreateButton($"Offer_{i}", panel.transform, "", 18);
                Centre((RectTransform)button.transform, new Vector2(400f, 96f), 300f - i * 104f);
                button.onClick.AddListener(() => ToggleOffer(index));

                var icon = HudFactory.CreatePanel($"OfferIcon_{i}", button.transform,
                    _palette != null ? _palette.ArchetypeColor(def.Id) : Color.white);
                HudFactory.UseSprite(icon, _palette != null ? _palette.IconFor(def.Id) : null);
                icon.preserveAspect = true;
                icon.raycastTarget = false;
                icon.enabled = icon.sprite != null;
                Anchor(icon.rectTransform, new Vector2(0f, 0.5f), new Vector2(52f, 52f), new Vector2(12f, 0f));

                var label = button.GetComponentInChildren<Text>();
                label.alignment = TextAnchor.UpperLeft;
                label.text = $"{def.Name}\n{Mathf.RoundToInt(def.MaxHp)} HP · " +
                             $"{Mathf.RoundToInt(def.Damage)} dmg · {Mathf.RoundToInt(def.Speed)} spd";
                label.rectTransform.offsetMin = new Vector2(76f, 0f);
                label.rectTransform.offsetMax = new Vector2(-62f, -8f);

                var ability = HudFactory.CreateLabel($"OfferAbility_{i}", button.transform,
                    $"{def.AbilityName}: {def.AbilityDescription}", 13, TextAnchor.LowerLeft,
                    HudFactory.RageColor);
                ability.rectTransform.anchorMin = Vector2.zero;
                ability.rectTransform.anchorMax = Vector2.one;
                ability.rectTransform.offsetMin = new Vector2(76f, 26f);
                ability.rectTransform.offsetMax = new Vector2(-62f, 0f);

                var weapon = WeaponDef.Get(def.SkilledWith);
                var skillText = HudFactory.CreateLabel($"OfferSkill_{i}", button.transform,
                    weapon.Name, 12, TextAnchor.LowerLeft, SkillGreen);
                skillText.rectTransform.anchorMin = Vector2.zero;
                skillText.rectTransform.anchorMax = Vector2.one;
                skillText.rectTransform.offsetMin = new Vector2(76f, 6f);
                skillText.rectTransform.offsetMax = new Vector2(-62f, 0f);

                var skill = HudFactory.CreatePanel($"OfferSkillIcon_{i}", button.transform, SkillGreen);
                HudFactory.UseSprite(skill, _palette != null ? _palette.IconFor(def.SkilledWith) : null);
                skill.preserveAspect = true;
                skill.raycastTarget = false;
                skill.enabled = skill.sprite != null;
                Anchor(skill.rectTransform, new Vector2(1f, 0.5f), new Vector2(40f, 40f), new Vector2(-14f, 0f));

                // The frame and the number are the whole of "this one is in your squad, third".
                var frame = HudFactory.CreatePanel($"OfferFrame_{i}", button.transform,
                    HudFactory.ActiveOutline);
                HudFactory.UseSprite(frame, _palette != null ? _palette.Ring : null);
                frame.raycastTarget = false;
                Anchor(frame.rectTransform, new Vector2(1f, 1f), new Vector2(30f, 30f), new Vector2(-8f, -8f));

                var order = HudFactory.CreateLabel($"OfferOrder_{i}", frame.transform, "", 15);
                HudFactory.Stretch(order.rectTransform);

                _rosterCards.Add(new RosterCard { Button = button, Frame = frame, Order = order });
            }

            _confirm = HudFactory.CreateButton("ConfirmSquad", panel.transform, "Take these three", 22);
            Centre((RectTransform)_confirm.transform, new Vector2(340f, 60f), -400f);
            _confirm.onClick.AddListener(ConfirmSquad);
        }

        // ------------------------------------------------------------------
        // behaviour
        // ------------------------------------------------------------------

        /// <summary>Drops the curtain: the match underneath has been waiting on its pick screen.</summary>
        public void StartMatch() => Current = Screen.Closed;

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

            Current = Screen.Roster;
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

        private void ConfirmSquad()
        {
            if (_draft.Count != GameConstants.SquadSize) return;

            _controller.SetSquad(_draft.Select(i => Offers[i].Def.Id).ToList());
            Current = Screen.Main;
        }

        private void LateUpdate()
        {
            bool main = Current == Screen.Main;
            bool roster = Current == Screen.Roster;

            if (_mainPanel.activeSelf != main) _mainPanel.SetActive(main);
            if (_rosterPanel.activeSelf != roster) _rosterPanel.SetActive(roster);

            if (main) SyncMain();
            if (roster) SyncRoster();
        }

        private void SyncMain()
        {
            for (int slot = 0; slot < _squadTiles.Count; slot++)
            {
                var tile = _squadTiles[slot];
                bool present = slot < _controller.Squad.Count;
                tile.Name.enabled = present;
                tile.Icon.enabled = present;
                tile.Skill.enabled = present;
                if (!present) continue;

                var def = GladiatorDef.Get(_controller.Squad[slot]);
                tile.Name.text = $"{def.Name}   ·   {WeaponDef.Get(def.SkilledWith).Name}";
                HudFactory.UseSprite(tile.Icon, _palette != null ? _palette.IconFor(def.Id) : null);
                tile.Icon.color = _palette != null ? _palette.ArchetypeColor(def.Id) : Color.white;
                tile.Icon.enabled = tile.Icon.sprite != null;
                HudFactory.UseSprite(tile.Skill, _palette != null ? _palette.IconFor(def.SkilledWith) : null);
                tile.Skill.enabled = tile.Skill.sprite != null;
            }
        }

        private void SyncRoster()
        {
            for (int i = 0; i < _rosterCards.Count; i++)
            {
                int order = _draft.IndexOf(i);
                bool chosen = order >= 0;

                _rosterCards[i].Frame.enabled = chosen;
                _rosterCards[i].Order.text = chosen ? (order + 1).ToString() : "";

                // A card that cannot be added is dimmed rather than hidden: the fighter is still on
                // offer, just not while three are already chosen, and hiding him would read as him
                // having been taken away.
                _rosterCards[i].Button.interactable =
                    chosen || _draft.Count < GameConstants.SquadSize;
            }

            int left = GameConstants.SquadSize - _draft.Count;
            _rosterSubtitle.text = left > 0
                ? $"Two of each are available. {left} more to choose."
                : "Tap one again to swap it out.";
            _confirm.interactable = left == 0;
        }

        // ------------------------------------------------------------------

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
