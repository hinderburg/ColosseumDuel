using ColosseumDuel.Core;
using UnityEngine;
using UnityEngine.UI;

namespace ColosseumDuel.Gameplay.Hud
{
    /// <summary>
    /// One gladiator's slot in a squad panel: a coloured tile with HP and rage, a frame around
    /// whoever is currently on the arena, a skull over anyone who is out, and the name and level
    /// underneath.
    ///
    /// Both squads use the same class - they differ only in the accent colour - so the player's
    /// corner and the opponent's corner cannot drift apart in layout or in what they report.
    /// </summary>
    public sealed class RosterEntryView : MonoBehaviour
    {
        public const float Width = 104f;
        public const float Height = 96f;

        private Image _tile;
        private Image _frame;
        private Image _skull;
        private Image _hpFill;
        private Image _rageFill;
        private Text _name;
        private Text _level;
        private Color _sideColor;

        public static RosterEntryView Create(string name, Transform parent, Color sideColor, Sprite skullSprite)
        {
            var tile = HudFactory.CreatePanel(name, parent, HudFactory.PanelColor);
            var rect = tile.rectTransform;
            rect.sizeDelta = new Vector2(Width, Height);

            var view = tile.gameObject.AddComponent<RosterEntryView>();
            view._tile = tile;
            view._sideColor = sideColor;

            // Portrait area: a flat block in the side's colour, standing in for the character art
            // that will arrive with the humanoid models.
            var portrait = HudFactory.CreatePanel("Portrait", rect, sideColor);
            var portraitRect = portrait.rectTransform;
            portraitRect.anchorMin = new Vector2(0f, 1f);
            portraitRect.anchorMax = new Vector2(1f, 1f);
            portraitRect.pivot = new Vector2(0.5f, 1f);
            portraitRect.offsetMin = new Vector2(6f, 0f);
            portraitRect.offsetMax = new Vector2(-6f, 0f);
            portraitRect.sizeDelta = new Vector2(portraitRect.sizeDelta.x, 44f);
            portraitRect.anchoredPosition = new Vector2(0f, -6f);

            // Frame marking the gladiator currently fighting. A separate outline object rather than
            // a colour change on the tile, so "on the arena" and "still alive" stay independent.
            var frame = HudFactory.CreatePanel("ActiveFrame", rect, Color.clear);
            HudFactory.Stretch(frame.rectTransform);
            frame.sprite = null;
            var outline = frame.gameObject.AddComponent<Outline>();
            outline.effectColor = HudFactory.ActiveOutline;
            outline.effectDistance = new Vector2(3f, 3f);
            frame.raycastTarget = false;
            view._frame = frame;

            view._skull = HudFactory.CreatePanel("Skull", rect, Color.white);
            view._skull.sprite = skullSprite;
            view._skull.preserveAspect = true;
            var skullRect = view._skull.rectTransform;
            skullRect.anchorMin = new Vector2(0.5f, 1f);
            skullRect.anchorMax = new Vector2(0.5f, 1f);
            skullRect.pivot = new Vector2(0.5f, 1f);
            skullRect.sizeDelta = new Vector2(36f, 36f);
            skullRect.anchoredPosition = new Vector2(0f, -10f);

            view._hpFill = HudFactory.CreateBar("Hp", rect, HudFactory.HpColor);
            PlaceRow(view._hpFill.transform.parent.GetComponent<RectTransform>(), -52f, 8f);

            view._rageFill = HudFactory.CreateBar("Rage", rect, HudFactory.RageColor);
            PlaceRow(view._rageFill.transform.parent.GetComponent<RectTransform>(), -62f, 6f);

            view._name = HudFactory.CreateLabel("Name", rect, "", 14, TextAnchor.MiddleCenter);
            PlaceRow(view._name.rectTransform, -70f, 18f);

            view._level = HudFactory.CreateLabel("Level", rect, "", 12, TextAnchor.MiddleCenter,
                HudFactory.MutedTextColor);
            PlaceRow(view._level.rectTransform, -86f, 14f);

            return view;
        }

        private static void PlaceRow(RectTransform rect, float top, float height)
        {
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.offsetMin = new Vector2(6f, 0f);
            rect.offsetMax = new Vector2(-6f, 0f);
            rect.sizeDelta = new Vector2(rect.sizeDelta.x, height);
            rect.anchoredPosition = new Vector2(0f, top);
        }

        public void Sync(GladiatorInstance g, bool isActive)
        {
            bool alive = g.Alive;

            _name.text = g.Def.Name;
            _name.color = alive ? HudFactory.TextColor : HudFactory.MutedTextColor;
            _level.text = $"ур. {g.Def.Level}";

            _tile.color = alive ? HudFactory.PanelColor : HudFactory.DeadColor;
            _frame.gameObject.SetActive(isActive && alive);
            _skull.gameObject.SetActive(!alive);

            HudFactory.SetFill(_hpFill, g.Def.MaxHp > 0f ? g.Hp / g.Def.MaxHp : 0f);
            HudFactory.SetFill(_rageFill, g.Rage / GameConstants.RageMax);
            _hpFill.color = alive ? HudFactory.HpColor : HudFactory.MutedTextColor;
        }
    }
}
