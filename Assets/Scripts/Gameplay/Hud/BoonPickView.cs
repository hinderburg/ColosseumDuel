using System.Collections.Generic;
using System.Linq;
using ColosseumDuel.Core;
using ColosseumDuel.Gameplay.View;
using UnityEngine;
using UnityEngine.UI;

namespace ColosseumDuel.Gameplay.Hud
{
    /// <summary>
    /// The choice of a boon: three cards over the dimmed arena whenever a new man of the player's
    /// steps out, and ten seconds to take one before one is taken for him. With the boons he already
    /// has written underneath, so the choice is made knowing them.
    ///
    /// Only while the match is waiting on the choice - the BoonPick phase, with an offer to the player.
    /// The bot's own choice is made in the same instant its offer is dealt, and never shows.
    /// </summary>
    public sealed class BoonPickView : MonoBehaviour
    {
        private static readonly Color CardColor = new Color(0.12f, 0.12f, 0.16f, 0.97f);
        private static readonly Color Gold = new Color(1.00f, 0.78f, 0.26f);

        /// <summary>The painting's width on a card, and the shape it was cut to (see BoonIconSheet).</summary>
        private const float ArtWidth = 132f, ArtAspect = 1.52f;

        /// <summary>Where the words start: past the painting and a gap.</summary>
        private const float TextLeft = 12f + ArtWidth + 14f;

        private GameController _controller;
        private ViewPalette _palette;
        private GameObject _panel;
        private readonly List<Button> _cards = new List<Button>();
        private readonly List<Image> _arts = new List<Image>();
        private readonly List<Text> _names = new List<Text>();
        private readonly List<Text> _texts = new List<Text>();
        private Text _timer;
        private Image _timerFill;
        private Text _owned;

        /// <summary>The offer the cards are showing, read when one is tapped.</summary>
        private List<BoonKey> _offer;

        public bool IsShowing => _panel != null && _panel.activeSelf;

        public static BoonPickView Create(Transform canvas, GameController controller, ViewPalette palette = null)
        {
            var root = HudFactory.CreateRect("BoonPickRoot", canvas);
            HudFactory.Stretch(root);
            var view = root.gameObject.AddComponent<BoonPickView>();
            view._controller = controller;
            view._palette = palette;

            var panel = HudFactory.CreatePanel("BoonPick", root, HudFactory.OverlayColor);
            HudFactory.Stretch(panel.rectTransform);
            view._panel = panel.gameObject;

            var title = HudFactory.CreateLabel("BoonTitle", panel.transform, "Choose a boon", 38, TextAnchor.MiddleCenter, Gold);
            title.fontStyle = FontStyle.Bold;
            Place(title.rectTransform, new Vector2(520f, 52f), 262f);

            var subtitle = HudFactory.CreateLabel("BoonSubtitle", panel.transform,
                "It stays with your whole squad for the rest of the match.", 17,
                TextAnchor.MiddleCenter, HudFactory.MutedTextColor);
            subtitle.horizontalOverflow = HorizontalWrapMode.Wrap;
            Place(subtitle.rectTransform, new Vector2(480f, 44f), 214f);

            for (int i = 0; i < BoonDef.OfferSize; i++)
            {
                int index = i;
                var card = HudFactory.CreateButton($"BoonCard_{i}", panel.transform, "", 14);
                ((Image)card.targetGraphic).color = CardColor;
                Place((RectTransform)card.transform, new Vector2(430f, 118f), 118f - i * 126f);
                card.onClick.AddListener(() => view.Take(index));

                // The painting, down the left of the card; the words to the right of it.
                var art = HudFactory.CreatePanel($"BoonArt_{i}", card.transform, Color.white);
                art.raycastTarget = false;
                art.preserveAspect = true;
                art.rectTransform.anchorMin = art.rectTransform.anchorMax = new Vector2(0f, 0.5f);
                art.rectTransform.pivot = new Vector2(0f, 0.5f);
                art.rectTransform.sizeDelta = new Vector2(ArtWidth, ArtWidth / ArtAspect);
                art.rectTransform.anchoredPosition = new Vector2(12f, 0f);

                var name = HudFactory.CreateLabel($"BoonName_{i}", card.transform, "", 24, TextAnchor.UpperLeft, Gold);
                name.fontStyle = FontStyle.Bold;
                name.rectTransform.anchorMin = Vector2.zero;
                name.rectTransform.anchorMax = Vector2.one;
                name.rectTransform.offsetMin = new Vector2(TextLeft, 0f);
                name.rectTransform.offsetMax = new Vector2(-16f, -14f);

                var text = HudFactory.CreateLabel($"BoonText_{i}", card.transform, "", 17, TextAnchor.LowerLeft);
                text.horizontalOverflow = HorizontalWrapMode.Wrap;
                text.rectTransform.anchorMin = Vector2.zero;
                text.rectTransform.anchorMax = Vector2.one;
                text.rectTransform.offsetMin = new Vector2(TextLeft, 14f);
                text.rectTransform.offsetMax = new Vector2(-16f, -48f);

                view._cards.Add(card);
                view._arts.Add(art);
                view._names.Add(name);
                view._texts.Add(text);
            }

            view._timer = HudFactory.CreateLabel("BoonTimer", panel.transform, "", 30);
            view._timer.fontStyle = FontStyle.Bold;
            Place(view._timer.rectTransform, new Vector2(120f, 40f), -262f);

            view._timerFill = HudFactory.CreateBar("BoonTimerBar", panel.transform, Gold);
            Place((RectTransform)view._timerFill.transform.parent, new Vector2(320f, 10f), -294f);

            view._owned = HudFactory.CreateLabel("BoonOwned", panel.transform, "", 16,
                TextAnchor.UpperCenter, HudFactory.MutedTextColor);
            view._owned.horizontalOverflow = HorizontalWrapMode.Wrap;
            Place(view._owned.rectTransform, new Vector2(480f, 60f), -338f);

            view._panel.SetActive(false);
            return view;
        }

        private static void Place(RectTransform rect, Vector2 size, float y)
        {
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = size;
            rect.anchoredPosition = new Vector2(0f, y);
        }

        private void Take(int index)
        {
            if (_offer == null || index >= _offer.Count || _controller == null) return;
            _controller.SubmitPlayerBoon(_offer[index]);
        }

        /// <summary>Shows the offer and the time left on it, or nothing. A null state means the menu has the screen.</summary>
        public void Sync(MatchState state)
        {
            bool show = state != null && state.Phase == MatchPhase.BoonPick && state.P1.NeedsBoonPick;
            if (_panel.activeSelf != show) _panel.SetActive(show);
            if (!show)
            {
                _offer = null;
                return;
            }

            _offer = state.P1.BoonOffer;
            for (int i = 0; i < _cards.Count; i++)
            {
                bool present = i < _offer.Count;
                if (_cards[i].gameObject.activeSelf != present) _cards[i].gameObject.SetActive(present);
                if (!present) continue;
                var def = BoonDef.Get(_offer[i]);
                _names[i].text = def.Name;
                _texts[i].text = Capitalised(def.Description);
                var picture = _palette != null ? _palette.BoonIconFor(def.Key) : null;
                HudFactory.UseSprite(_arts[i], picture);
                _arts[i].enabled = picture != null;
            }

            float left = Mathf.Max(0f, GameConstants.BoonPickTime - state.PhaseTimer);
            _timer.text = Mathf.CeilToInt(left).ToString();
            HudFactory.SetFill(_timerFill, left / GameConstants.BoonPickTime);

            _owned.text = state.P1.Boons.Count > 0
                ? "Your boons: " + string.Join(" · ", state.P1.Boons.Select(k => BoonDef.Get(k).Name))
                : "";
        }

        private static string Capitalised(string text)
            => string.IsNullOrEmpty(text) ? "" : char.ToUpperInvariant(text[0]) + text.Substring(1);
    }
}
