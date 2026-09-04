using ColosseumDuel.Core;
using UnityEngine;
using UnityEngine.UI;

namespace ColosseumDuel.Gameplay.Hud
{
    /// <summary>
    /// One gladiator's slot in the HUD. The same class serves the compact squad icons in the top
    /// bar and the detailed cards in the bottom bar - they show the same three states (fighting /
    /// available / out) and differ only in how much they spell out.
    /// </summary>
    public sealed class RosterEntryView : MonoBehaviour
    {
        private Image _background;
        private Image _accent;
        private Text _name;
        private Text _detail;
        private Image _hpFill;
        private Image _rageFill;
        private bool _detailed;

        public static RosterEntryView Create(string name, Transform parent, Color sideColor, bool detailed)
        {
            var background = HudFactory.CreatePanel(name, parent, HudFactory.PanelColor);
            var rect = background.rectTransform;
            rect.sizeDelta = detailed ? new Vector2(180f, 104f) : new Vector2(132f, 62f);

            var view = background.gameObject.AddComponent<RosterEntryView>();
            view._background = background;
            view._detailed = detailed;

            // A thin stripe in the side's colour, which also doubles as the "currently fighting"
            // highlight - it turns bright instead of a separate outline object.
            var accent = HudFactory.CreatePanel("Accent", rect, sideColor);
            accent.rectTransform.anchorMin = new Vector2(0f, 0f);
            accent.rectTransform.anchorMax = new Vector2(0f, 1f);
            accent.rectTransform.pivot = new Vector2(0f, 0.5f);
            accent.rectTransform.sizeDelta = new Vector2(6f, 0f);
            accent.rectTransform.anchoredPosition = Vector2.zero;
            view._accent = accent;

            float top = detailed ? -8f : -6f;

            view._name = HudFactory.CreateLabel("Name", rect, "", detailed ? 20 : 16, TextAnchor.UpperLeft);
            PlaceRow(view._name.rectTransform, top, detailed ? 24f : 20f);

            if (detailed)
            {
                view._detail = HudFactory.CreateLabel("Detail", rect, "", 15, TextAnchor.UpperLeft,
                    HudFactory.MutedTextColor);
                PlaceRow(view._detail.rectTransform, top - 26f, 20f);
            }

            view._hpFill = HudFactory.CreateBar("Hp", rect, HudFactory.HpColor);
            PlaceRow(view._hpFill.transform.parent.GetComponent<RectTransform>(),
                detailed ? top - 52f : top - 26f, 12f);

            view._rageFill = HudFactory.CreateBar("Rage", rect, HudFactory.RageColor);
            PlaceRow(view._rageFill.transform.parent.GetComponent<RectTransform>(),
                detailed ? top - 68f : top - 42f, 8f);

            return view;
        }

        private static void PlaceRow(RectTransform rect, float top, float height)
        {
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.offsetMin = new Vector2(14f, 0f);
            rect.offsetMax = new Vector2(-10f, 0f);
            rect.sizeDelta = new Vector2(rect.sizeDelta.x, height);
            rect.anchoredPosition = new Vector2(2f, top);
        }

        public void Sync(GladiatorInstance g, bool isActive)
        {
            _name.text = g.Def.Name;
            _name.color = g.Alive ? HudFactory.TextColor : HudFactory.MutedTextColor;

            _background.color = !g.Alive ? HudFactory.DeadColor : HudFactory.PanelColor;
            // Bright stripe = this is the gladiator currently on the arena.
            _accent.color = isActive
                ? HudFactory.ActiveOutline
                : new Color(_accent.color.r, _accent.color.g, _accent.color.b, g.Alive ? 0.55f : 0.25f);

            HudFactory.SetFill(_hpFill, g.Def.MaxHp > 0f ? g.Hp / g.Def.MaxHp : 0f);
            HudFactory.SetFill(_rageFill, g.Rage / GameConstants.RageMax);
            _hpFill.color = g.Alive ? HudFactory.HpColor : HudFactory.MutedTextColor;

            if (!_detailed) return;

            if (!g.Alive)
            {
                _detail.text = "выбыл";
                return;
            }

            string carried = "";
            if (g.Weapon == WeaponType.OneHanded) carried = "  топор";
            else if (g.Weapon == WeaponType.TwoHanded) carried = "  трезубец";
            if (g.HasShield) carried += "  щит";

            string status = isActive ? "на арене" : "в запасе";
            _detail.text = $"{Mathf.CeilToInt(g.Hp)}/{Mathf.RoundToInt(g.Def.MaxHp)}  {status}{carried}";
        }
    }
}
