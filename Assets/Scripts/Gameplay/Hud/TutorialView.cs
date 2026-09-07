using System.Collections.Generic;
using ColosseumDuel.Core;
using ColosseumDuel.Gameplay.View;
using UnityEngine;
using UnityEngine.UI;

namespace ColosseumDuel.Gameplay.Hud
{
    /// <summary>
    /// The first fight's teaching layer: a short label floating over everything on the sand, and one
    /// line telling the player what to do with it.
    ///
    /// Labels rather than a wall of text at the start: what a shield does is worth knowing at the
    /// moment you are looking at a shield, and nowhere else. They are placed over the things
    /// themselves, so reading one is the same glance as deciding whether to run at it.
    ///
    /// Screen-space labels following world positions, the same way the action buttons work: they
    /// stay a constant size and stay crisp whatever the camera is doing, and they layer over the
    /// arena without depth-sorting against a spike.
    /// </summary>
    public sealed class TutorialView : MonoBehaviour
    {
        /// <summary>How high above a thing on the floor its label sits, in reference pixels.</summary>
        private const float LabelLift = 34f;

        private const int LabelSize = 13;

        private ArenaView _arena;
        private Canvas _canvas;

        private readonly List<Text> _itemLabels = new List<Text>();
        private readonly List<Text> _trapLabels = new List<Text>();
        private Text _instruction;
        private Image _tapMark;

        public static TutorialView Create(Transform canvas, ViewPalette palette, ArenaView arena)
        {
            var root = HudFactory.CreateRect("Tutorial", canvas);
            HudFactory.Stretch(root);

            var view = root.gameObject.AddComponent<TutorialView>();
            view._arena = arena;
            view._canvas = canvas.GetComponentInParent<Canvas>();

            for (int i = 0; i < GameConstants.ItemCountOnArena; i++)
                view._itemLabels.Add(view.MakeLabel(root, $"ItemHint_{i}", HudFactory.TextColor));

            for (int i = 0; i < GameConstants.TrapCount; i++)
                view._trapLabels.Add(view.MakeLabel(root, $"TrapHint_{i}", new Color(1f, 0.55f, 0.45f)));

            // The ring the player is being told to tap. Drawn in the HUD rather than on the arena
            // because it has to sit on top of the spikes and the sand alike, and because it is
            // instruction rather than scenery.
            view._tapMark = HudFactory.CreatePanel("TutorialTapMark", root, new Color(1f, 0.9f, 0.4f, 0.9f));
            view._tapMark.sprite = palette != null ? palette.Ring : null;
            view._tapMark.raycastTarget = false;
            var markRect = view._tapMark.rectTransform;
            markRect.anchorMin = markRect.anchorMax = new Vector2(0.5f, 0.5f);
            markRect.sizeDelta = new Vector2(64f, 64f);

            view._instruction = HudFactory.CreateLabel("TutorialInstruction", root, "", 17,
                TextAnchor.MiddleCenter);
            var rect = view._instruction.rectTransform;
            rect.anchorMin = new Vector2(0f, 0f);
            rect.anchorMax = new Vector2(1f, 0f);
            rect.pivot = new Vector2(0.5f, 0f);
            rect.offsetMin = new Vector2(20f, 0f);
            rect.offsetMax = new Vector2(-20f, 0f);
            rect.sizeDelta = new Vector2(rect.sizeDelta.x, 46f);
            rect.anchoredPosition = new Vector2(0f, 228f);

            var outline = view._instruction.gameObject.AddComponent<Outline>();
            outline.effectColor = new Color(0f, 0f, 0f, 0.85f);
            outline.effectDistance = new Vector2(2f, -2f);

            root.gameObject.SetActive(false);
            return view;
        }

        private Text MakeLabel(Transform parent, string name, Color color)
        {
            var label = HudFactory.CreateLabel(name, parent, "", LabelSize, TextAnchor.MiddleCenter, color);
            var rect = label.rectTransform;
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(190f, 20f);

            // Over bright sand and dark spikes both, so the text needs its own edge either way.
            var outline = label.gameObject.AddComponent<Outline>();
            outline.effectColor = new Color(0f, 0f, 0f, 0.85f);
            outline.effectDistance = new Vector2(1.5f, -1.5f);

            label.gameObject.SetActive(false);
            return label;
        }

        /// <summary>What a pickup is worth, in as few words as will fit above it.</summary>
        private static string DescribeItem(ArenaItem item)
        {
            if (item.Kind == ItemKind.Shield) return "Щит: вдвое меньше урона";
            return item.WeaponType == WeaponType.TwoHanded
                ? "Двуручный: бьёт на пролёте"
                : "Меч: урон в полтора раза";
        }

        public void Sync(MatchState state)
        {
            // The opening round of a first fight and nothing else. From round two the player has
            // fought, and labels on everything would be in the way rather than in aid.
            bool teaching = state.Tutorial && state.Round == 1
                            && state.Phase != MatchPhase.Pick && state.Phase != MatchPhase.MatchEnd;

            if (gameObject.activeSelf != teaching) gameObject.SetActive(teaching);
            if (!teaching || _arena == null || _arena.ArenaCamera == null) return;

            var items = state.Items?.Items;
            for (int i = 0; i < _itemLabels.Count; i++)
            {
                bool show = items != null && i < items.Count;
                Place(_itemLabels[i], show ? (Vector2?)items[i].Pos : null,
                      show ? DescribeItem(items[i]) : null);
            }

            var traps = state.Traps?.Traps;
            for (int i = 0; i < _trapLabels.Count; i++)
            {
                bool show = traps != null && i < traps.Count && traps[i].Armed;
                Place(_trapLabels[i], show ? (Vector2?)traps[i].Pos : null,
                      show ? "Капкан" : null);
            }

            SyncInstruction(state);
        }

        /// <summary>
        /// One line, changing as the lesson lands: run through the sword, then fight.
        ///
        /// Keyed off what the player is actually carrying rather than off a step counter, so it
        /// cannot get out of step with the match - a player who picks the sword up by accident on
        /// the way somewhere else has still learned the thing and is told so.
        /// </summary>
        private void SyncInstruction(MatchState state)
        {
            var player = state.P1.Active;
            bool armed = player != null && player.Weapon != WeaponType.None;

            _instruction.text = armed
                ? "Меч у тебя — тапни к противнику и бей"
                : "Тапни чуть дальше меча — подберёшь на бегу";

            // The ring marks the spot only while it is still the thing to do.
            bool marking = !armed && state.Phase == MatchPhase.Planning
                           && state.TutorialTapPoint != Vector2.zero;
            if (_tapMark.enabled != marking) _tapMark.enabled = marking;
            if (marking && TryCanvasPoint(state.TutorialTapPoint, 0f, out var markLocal))
                _tapMark.rectTransform.anchoredPosition = markLocal;
        }

        private void Place(Text label, Vector2? virtualPos, string text)
        {
            bool show = virtualPos.HasValue;
            if (show)
            {
                if (TryCanvasPoint(virtualPos.Value, 0f, out var local))
                    label.rectTransform.anchoredPosition = ClampToCanvas(
                        local + new Vector2(0f, LabelLift), label.rectTransform.sizeDelta.x);
                else
                    show = false;
            }

            if (label.gameObject.activeSelf != show) label.gameObject.SetActive(show);
            if (show) label.text = text;
        }

        /// <summary>
        /// Keeps a label inside the frame.
        ///
        /// Things near the wall project close to the edge of the screen, and a label centred on one
        /// of them hangs half off it - which is how the first version read: several hints trailing
        /// off both sides mid-word. Nudged in rather than hidden, because a label the player cannot
        /// finish reading is still better than a hazard nobody labelled.
        /// </summary>
        private Vector2 ClampToCanvas(Vector2 local, float labelWidth)
        {
            var canvasRect = ((RectTransform)_canvas.transform).rect;
            float limit = Mathf.Max(canvasRect.width * 0.5f - labelWidth * 0.5f - 6f, 0f);
            local.x = Mathf.Clamp(local.x, -limit, limit);
            return local;
        }

        /// <summary>
        /// A point on the arena floor, in canvas coordinates.
        ///
        /// Through ScreenPointToLocalPointInRectangle rather than by assigning screen coordinates to
        /// a RectTransform: that shortcut only holds for a Screen Space - Overlay canvas, and under
        /// Screen Space - Camera the same property is world space and everything silently flies off
        /// behind the arena.
        /// </summary>
        private bool TryCanvasPoint(Vector2 virtualPos, float height, out Vector2 local)
        {
            var screen = _arena.ArenaCamera.WorldToScreenPoint(_arena.ToWorld(virtualPos, height));
            var canvasRect = (RectTransform)_canvas.transform;
            var canvasCamera = _canvas.renderMode == RenderMode.ScreenSpaceOverlay
                ? null
                : _canvas.worldCamera;
            return RectTransformUtility.ScreenPointToLocalPointInRectangle(
                canvasRect, screen, canvasCamera, out local);
        }
    }
}
