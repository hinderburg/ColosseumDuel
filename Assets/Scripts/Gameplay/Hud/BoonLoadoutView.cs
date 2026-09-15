using System.Collections.Generic;
using ColosseumDuel.Core;
using ColosseumDuel.Gameplay.View;
using UnityEngine;
using UnityEngine.UI;

namespace ColosseumDuel.Gameplay.Hud
{
    /// <summary>
    /// The menu's boon screen: every boon there is, and the five the player takes into a match. Each
    /// time a new man of his steps out he is offered three of those five - so the five are the shape
    /// of every match he plays, and this is where he sets it.
    ///
    /// Nothing is changed until Done, and Done only with exactly five: choosing restarts the match
    /// behind the menu, and doing that on every tap would throw it away while he was still deciding.
    /// </summary>
    public sealed class BoonLoadoutView : MonoBehaviour
    {
        private static readonly Color CardColor = new Color(0.12f, 0.12f, 0.16f, 0.97f);
        private static readonly Color WindowColor = new Color(0.07f, 0.07f, 0.09f, 1f);
        private static readonly Color Gold = new Color(1.00f, 0.78f, 0.26f);

        private GameController _controller;
        private GameObject _panel;
        private readonly List<Image> _outlines = new List<Image>();
        private readonly List<Text> _marks = new List<Text>();
        private Text _counter;
        private Button _done;
        private readonly List<BoonKey> _draft = new List<BoonKey>();

        public bool IsOpen => _panel != null && _panel.activeSelf;

        /// <summary>What the screen is holding, which is not the loadout until Done. For tests.</summary>
        public IReadOnlyList<BoonKey> Draft => _draft;

        public static BoonLoadoutView Create(Transform parent, GameController controller, ViewPalette palette = null)
        {
            var root = HudFactory.CreateRect("BoonLoadoutRoot", parent);
            HudFactory.Stretch(root);
            var view = root.gameObject.AddComponent<BoonLoadoutView>();
            view._controller = controller;

            var panel = HudFactory.CreatePanel("BoonLoadout", root, WindowColor);
            HudFactory.Stretch(panel.rectTransform);
            view._panel = panel.gameObject;

            var back = HudFactory.CreateButton("BoonLoadoutBack", panel.transform, "<", 26);
            var backRect = (RectTransform)back.transform;
            backRect.anchorMin = backRect.anchorMax = new Vector2(0f, 1f);
            backRect.pivot = new Vector2(0f, 1f);
            backRect.sizeDelta = new Vector2(50f, 46f);
            backRect.anchoredPosition = new Vector2(14f, -14f);
            back.onClick.AddListener(view.Close);

            var title = HudFactory.CreateLabel("BoonLoadoutTitle", panel.transform, "CHOOSE YOUR BOONS", 30,
                TextAnchor.MiddleCenter, Gold);
            title.fontStyle = FontStyle.Bold;
            Place(title.rectTransform, new Vector2(460f, 44f), new Vector2(0f, 440f));

            var subtitle = HudFactory.CreateLabel("BoonLoadoutSubtitle", panel.transform,
                "Take five into every match. Each time a new man of yours steps out, you choose one of three of them - for the whole squad, for the rest of the match.",
                16, TextAnchor.MiddleCenter, HudFactory.MutedTextColor);
            subtitle.horizontalOverflow = HorizontalWrapMode.Wrap;
            Place(subtitle.rectTransform, new Vector2(500f, 64f), new Vector2(0f, 382f));

            // Two columns of four. Each card: the painting top left, the name beside it, the text
            // along the bottom - the card sheet's own layout, turned on its side to fit two across.
            const float cardWidth = 256f, cardHeight = 136f, gap = 12f;
            const float artWidth = 100f, artHeight = artWidth / 1.52f;
            for (int i = 0; i < BoonDef.All.Count; i++)
            {
                var def = BoonDef.All[i];
                var key = def.Key;
                int column = i % 2, row = i / 2;
                var position = new Vector2((column == 0 ? -1f : 1f) * (cardWidth + gap) * 0.5f,
                    270f - row * (cardHeight + gap));

                var outline = HudFactory.CreatePanel($"BoonChoiceOutline_{i}", panel.transform, Gold);
                outline.raycastTarget = false;
                Place(outline.rectTransform, new Vector2(cardWidth + 6f, cardHeight + 6f), position);

                var card = HudFactory.CreateButton($"BoonChoice_{key}", panel.transform, "", 14);
                ((Image)card.targetGraphic).color = CardColor;
                Place((RectTransform)card.transform, new Vector2(cardWidth, cardHeight), position);
                card.onClick.AddListener(() => view.Toggle(key));

                var picture = palette != null ? palette.BoonIconFor(key) : null;
                var art = HudFactory.CreatePanel("Art", card.transform, Color.white);
                art.raycastTarget = false;
                art.preserveAspect = true;
                art.rectTransform.anchorMin = art.rectTransform.anchorMax = new Vector2(0f, 1f);
                art.rectTransform.pivot = new Vector2(0f, 1f);
                art.rectTransform.sizeDelta = new Vector2(artWidth, artHeight);
                art.rectTransform.anchoredPosition = new Vector2(10f, -10f);
                HudFactory.UseSprite(art, picture);
                art.enabled = picture != null;

                var name = HudFactory.CreateLabel("Name", card.transform, def.Name, 17, TextAnchor.MiddleLeft, Gold);
                name.fontStyle = FontStyle.Bold;
                name.horizontalOverflow = HorizontalWrapMode.Wrap;
                name.rectTransform.anchorMin = new Vector2(0f, 1f);
                name.rectTransform.anchorMax = new Vector2(1f, 1f);
                name.rectTransform.pivot = new Vector2(0.5f, 1f);
                name.rectTransform.offsetMin = new Vector2(10f + artWidth + 10f, -10f - artHeight);
                name.rectTransform.offsetMax = new Vector2(-12f, -10f);

                var text = HudFactory.CreateLabel("Text", card.transform,
                    char.ToUpperInvariant(def.Description[0]) + def.Description.Substring(1), 14, TextAnchor.UpperLeft);
                text.horizontalOverflow = HorizontalWrapMode.Wrap;
                text.rectTransform.anchorMin = Vector2.zero;
                text.rectTransform.anchorMax = Vector2.one;
                text.rectTransform.offsetMin = new Vector2(12f, 6f);
                text.rectTransform.offsetMax = new Vector2(-12f, -10f - artHeight - 8f);

                // A word rather than a tick: the WebGL build has only the built-in font, and no
                // system fonts behind it to find a glyph it lacks. In the bottom corner, out of the
                // name's way - every description ends on a short last line.
                var mark = HudFactory.CreateLabel("Mark", card.transform, "", 12, TextAnchor.LowerRight, Gold);
                mark.fontStyle = FontStyle.Bold;
                mark.rectTransform.anchorMin = Vector2.zero;
                mark.rectTransform.anchorMax = Vector2.one;
                mark.rectTransform.offsetMin = new Vector2(0f, 6f);
                mark.rectTransform.offsetMax = new Vector2(-10f, 0f);

                view._outlines.Add(outline);
                view._marks.Add(mark);
            }

            view._counter = HudFactory.CreateLabel("BoonCounter", panel.transform, "", 20);
            Place(view._counter.rectTransform, new Vector2(300f, 34f), new Vector2(0f, -318f));

            view._done = HudFactory.CreateButton("BoonsDone", panel.transform, "Done", 24);
            Place((RectTransform)view._done.transform, new Vector2(260f, 64f), new Vector2(0f, -380f));
            view._done.onClick.AddListener(view.Done);

            view._panel.SetActive(false);
            return view;
        }

        private static void Place(RectTransform rect, Vector2 size, Vector2 position)
        {
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = size;
            rect.anchoredPosition = position;
        }

        /// <summary>Opens on the five he brings now.</summary>
        public void Open()
        {
            _draft.Clear();
            if (_controller != null) _draft.AddRange(_controller.BoonLoadout);
            _panel.SetActive(true);
            _panel.transform.parent.SetAsLastSibling();
            Refresh();
        }

        public void Close() => _panel.SetActive(false);

        /// <summary>Takes a boon into the five, or out of it. A sixth is not taken until one is let go.</summary>
        public void Toggle(BoonKey key)
        {
            if (_draft.Contains(key)) _draft.Remove(key);
            else if (_draft.Count < BoonDef.LoadoutSize) _draft.Add(key);
            Refresh();
        }

        private void Done()
        {
            if (_draft.Count != BoonDef.LoadoutSize) return;
            _controller?.SetBoonLoadout(_draft);
            Close();
        }

        private void Refresh()
        {
            for (int i = 0; i < BoonDef.All.Count; i++)
            {
                bool taken = _draft.Contains(BoonDef.All[i].Key);
                _outlines[i].enabled = taken;
                _marks[i].text = taken ? "TAKEN" : "";
            }
            _counter.text = $"{_draft.Count} / {BoonDef.LoadoutSize}";
            _done.interactable = _draft.Count == BoonDef.LoadoutSize;
        }
    }
}
