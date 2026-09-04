using UnityEngine;
using UnityEngine.UI;

namespace ColosseumDuel.Gameplay.Hud
{
    /// <summary>
    /// Small builders for the runtime-constructed HUD, so MatchHud reads as layout rather than as
    /// a wall of RectTransform bookkeeping.
    ///
    /// Deliberately built on legacy uGUI Text rather than TextMeshPro: TMP needs its "Essential
    /// Resources" imported into the project before a single label will render, which is a manual
    /// Editor step that would break the "regenerate everything from a script" property the rest of
    /// this project has. Swapping to TMP is a polish-phase job once those resources are committed.
    /// </summary>
    public static class HudFactory
    {
        public static readonly Color PanelColor = new Color(0.08f, 0.08f, 0.10f, 0.85f);
        public static readonly Color OverlayColor = new Color(0.03f, 0.03f, 0.05f, 0.80f);
        public static readonly Color TextColor = new Color(0.92f, 0.90f, 0.86f);
        public static readonly Color MutedTextColor = new Color(0.55f, 0.53f, 0.50f);
        public static readonly Color PlayerColor = new Color(0.35f, 0.60f, 1.00f);
        public static readonly Color BotColor = new Color(1.00f, 0.38f, 0.34f);
        public static readonly Color HpColor = new Color(0.30f, 0.85f, 0.35f);
        public static readonly Color RageColor = new Color(0.95f, 0.65f, 0.15f);
        public static readonly Color DeadColor = new Color(0.30f, 0.12f, 0.12f, 0.85f);
        public static readonly Color ActiveOutline = new Color(1f, 0.92f, 0.55f);

        private static Font _builtinFont;

        /// <summary>
        /// The font every label is built with. MatchHud sets this from ViewPalette before building
        /// the HUD; the built-in font is only a last-resort fallback, and a poor one - it has no
        /// Cyrillic glyphs, so with it the Russian captions silently render as blank space in a
        /// build (in the Editor the OS fonts paper over it, which is how this hid until the first
        /// WebGL build).
        /// </summary>
        public static Font ActiveFont { get; set; }

        public static Font DefaultFont
        {
            get
            {
                if (ActiveFont != null) return ActiveFont;
                if (_builtinFont == null)
                    _builtinFont = TryBuiltinFont("LegacyRuntime.ttf") ?? TryBuiltinFont("Arial.ttf");
                return _builtinFont;
            }
        }

        private static Font TryBuiltinFont(string name)
        {
            try { return Resources.GetBuiltinResource<Font>(name); }
            catch { return null; }
        }

        public static RectTransform CreateRect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        public static Image CreatePanel(string name, Transform parent, Color color)
        {
            var rect = CreateRect(name, parent);
            var image = rect.gameObject.AddComponent<Image>();
            image.color = color;
            return image;
        }

        public static Text CreateLabel(string name, Transform parent, string text, int fontSize,
            TextAnchor alignment = TextAnchor.MiddleCenter, Color? color = null)
        {
            var rect = CreateRect(name, parent);
            var label = rect.gameObject.AddComponent<Text>();
            label.font = DefaultFont;
            label.text = text;
            label.fontSize = fontSize;
            label.alignment = alignment;
            label.color = color ?? TextColor;
            label.horizontalOverflow = HorizontalWrapMode.Overflow;
            label.verticalOverflow = VerticalWrapMode.Overflow;
            label.raycastTarget = false;
            return label;
        }

        public static Button CreateButton(string name, Transform parent, string caption, int fontSize = 24)
        {
            var image = CreatePanel(name, parent, new Color(0.16f, 0.16f, 0.20f, 0.95f));
            var button = image.gameObject.AddComponent<Button>();
            button.targetGraphic = image;

            var colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1.3f, 1.3f, 1.3f);
            colors.pressedColor = new Color(0.7f, 0.7f, 0.7f);
            colors.disabledColor = new Color(0.55f, 0.55f, 0.55f, 0.9f); // still legible on a dark ground
            button.colors = colors;

            var label = CreateLabel("Label", image.transform, caption, fontSize);
            Stretch(label.rectTransform);
            return button;
        }

        /// <summary>
        /// A background plus a left-anchored fill. Returns the fill Image; drive it with
        /// <see cref="SetFill"/>.
        /// </summary>
        public static Image CreateBar(string name, Transform parent, Color fillColor)
        {
            var background = CreatePanel(name, parent, new Color(0.04f, 0.04f, 0.06f, 0.9f));

            var fill = CreatePanel("Fill", background.transform, fillColor);
            var rect = fill.rectTransform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            return fill;
        }

        /// <summary>
        /// Resizes the fill by its right anchor rather than by Image.fillAmount. fillAmount only
        /// works on an Image that has a sprite, and these bars are plain coloured quads - it would
        /// silently do nothing and every bar would read as permanently full.
        /// </summary>
        public static void SetFill(Image fill, float t)
        {
            t = Mathf.Clamp01(t);
            var rect = fill.rectTransform;
            rect.anchorMax = new Vector2(t, 1f);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            fill.enabled = t > 0.0005f;
        }

        public static void Stretch(RectTransform rect, float padding = 0f)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(padding, padding);
            rect.offsetMax = new Vector2(-padding, -padding);
        }

        /// <summary>Anchors a rect to one edge of its parent with a fixed thickness.</summary>
        public static void AnchorToEdge(RectTransform rect, RectTransform.Edge edge, float thickness)
        {
            switch (edge)
            {
                case RectTransform.Edge.Top:
                    rect.anchorMin = new Vector2(0f, 1f);
                    rect.anchorMax = new Vector2(1f, 1f);
                    rect.pivot = new Vector2(0.5f, 1f);
                    break;
                case RectTransform.Edge.Bottom:
                    rect.anchorMin = new Vector2(0f, 0f);
                    rect.anchorMax = new Vector2(1f, 0f);
                    rect.pivot = new Vector2(0.5f, 0f);
                    break;
            }
            rect.offsetMin = new Vector2(0f, rect.offsetMin.y);
            rect.offsetMax = new Vector2(0f, rect.offsetMax.y);
            rect.sizeDelta = new Vector2(0f, thickness);
            rect.anchoredPosition = Vector2.zero;
        }

        public static HorizontalLayoutGroup AddRow(RectTransform rect, float spacing, TextAnchor alignment,
            RectOffset padding = null)
        {
            var layout = rect.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = spacing;
            layout.childAlignment = alignment;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;
            layout.childControlWidth = false;
            layout.childControlHeight = false;
            layout.padding = padding ?? new RectOffset(12, 12, 8, 8);
            return layout;
        }
    }
}
