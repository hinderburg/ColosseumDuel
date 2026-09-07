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
    ///
    /// Each label sits on its own plate. Light text with a dark outline is what a HUD does when it
    /// has to survive any background; these labels have a background of exactly one kind - bright
    /// sand, sometimes with a red hazard ring or a black spike crossing it - and against that the
    /// outline was doing the reading work that a plate does better. Dark text on a solid plate also
    /// separates instruction from the game's own light-on-dark chrome.
    /// </summary>
    public sealed class TutorialView : MonoBehaviour
    {
        /// <summary>How high above a thing on the floor its label sits, in reference pixels.</summary>
        private const float LabelLift = 34f;

        private const int LabelSize = 13;
        private const float LabelHeight = 24f;

        /// <summary>Breathing room between the text and the edge of its plate, each side.</summary>
        private const float PlatePadding = 9f;

        /// <summary>Clear air between two plates that would otherwise sit on each other.</summary>
        private const float LabelGap = 3f;

        /// <summary>Ink. Near-black rather than black, which reads less like a system dialog.</summary>
        private static readonly Color InkColor = new Color(0.07f, 0.06f, 0.05f);

        /// <summary>Parchment, matching the sand it lies on rather than fighting it.</summary>
        private static readonly Color PlateColor = new Color(0.96f, 0.93f, 0.85f, 0.94f);

        /// <summary>The same plate, gone the colour of the thing it warns about.</summary>
        private static readonly Color TrapPlateColor = new Color(0.97f, 0.76f, 0.70f, 0.95f);

        /// <summary>The order of the round, so it carries the eye before the labels do.</summary>
        private static readonly Color InstructionPlateColor = new Color(1f, 0.87f, 0.52f, 0.96f);

        private ArenaView _arena;
        private Canvas _canvas;

        private readonly List<Hint> _itemHints = new List<Hint>();
        private readonly List<Hint> _trapHints = new List<Hint>();

        /// <summary>Scratch list for the de-overlap pass, reused so it allocates nothing per frame.</summary>
        private readonly List<Hint> _placed = new List<Hint>();
        private Hint _instruction;
        private Image _tapMark;

        /// <summary>A plate and the text on it, resized together whenever the text changes.</summary>
        private sealed class Hint
        {
            public Image Plate;
            public Text Label;

            /// <summary>Plate height. The instruction sets its own - it is a size larger.</summary>
            public float Height = LabelHeight;

            public RectTransform Rect => Plate.rectTransform;

            /// <summary>
            /// Sets the caption and fits the plate to it.
            ///
            /// Sized from the text rather than left at a fixed width: these captions run from
            /// "Капкан" to "Двуручный: бьёт на пролёте", and one width that suits both is either a
            /// slab of empty parchment around the short one or a plate the long one hangs out of.
            /// </summary>
            public void SetText(string text)
            {
                if (Label.text != text) Label.text = text;
                Rect.sizeDelta = new Vector2(Label.preferredWidth + PlatePadding * 2f, Height);
            }

            public bool Active
            {
                get => Plate.gameObject.activeSelf;
                set { if (Plate.gameObject.activeSelf != value) Plate.gameObject.SetActive(value); }
            }
        }

        public static TutorialView Create(Transform canvas, ViewPalette palette, ArenaView arena)
        {
            var root = HudFactory.CreateRect("Tutorial", canvas);
            HudFactory.Stretch(root);

            var view = root.gameObject.AddComponent<TutorialView>();
            view._arena = arena;
            view._canvas = canvas.GetComponentInParent<Canvas>();

            for (int i = 0; i < GameConstants.ItemCountOnArena; i++)
                view._itemHints.Add(MakeHint(root, $"ItemHint_{i}", PlateColor, LabelSize));

            for (int i = 0; i < GameConstants.TrapCount; i++)
                view._trapHints.Add(MakeHint(root, $"TrapHint_{i}", TrapPlateColor, LabelSize));

            // The ring the player is being told to tap. Drawn in the HUD rather than on the arena
            // because it has to sit on top of the spikes and the sand alike, and because it is
            // instruction rather than scenery.
            view._tapMark = HudFactory.CreatePanel("TutorialTapMark", root, new Color(1f, 0.9f, 0.4f, 0.9f));
            view._tapMark.sprite = palette != null ? palette.Ring : null;
            view._tapMark.raycastTarget = false;
            var markRect = view._tapMark.rectTransform;
            markRect.anchorMin = markRect.anchorMax = new Vector2(0.5f, 0.5f);
            markRect.sizeDelta = new Vector2(64f, 64f);

            view._instruction = MakeHint(root, "TutorialInstruction", InstructionPlateColor, 17);
            view._instruction.Height = 36f;
            var rect = view._instruction.Rect;
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0f);
            rect.pivot = new Vector2(0.5f, 0f);
            rect.anchoredPosition = new Vector2(0f, 228f);
            view._instruction.Active = true;

            root.gameObject.SetActive(false);
            return view;
        }

        private static Hint MakeHint(Transform parent, string name, Color plateColor, int fontSize)
        {
            var plate = HudFactory.CreatePanel(name, parent, plateColor);
            plate.raycastTarget = false; // nothing up here may swallow a tap meant for the sand

            var rect = plate.rectTransform;
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(120f, LabelHeight);

            var label = HudFactory.CreateLabel("Text", plate.transform, "", fontSize,
                TextAnchor.MiddleCenter, InkColor);
            HudFactory.Stretch(label.rectTransform);

            var hint = new Hint { Plate = plate, Label = label };
            hint.Active = false;
            return hint;
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
            for (int i = 0; i < _itemHints.Count; i++)
            {
                bool show = items != null && i < items.Count;
                Place(_itemHints[i], show ? (Vector2?)items[i].Pos : null,
                      show ? DescribeItem(items[i]) : null);
            }

            var traps = state.Traps?.Traps;
            for (int i = 0; i < _trapHints.Count; i++)
            {
                bool show = traps != null && i < traps.Count && traps[i].Armed;
                Place(_trapHints[i], show ? (Vector2?)traps[i].Pos : null,
                      show ? "Капкан" : null);
            }

            // The instruction first: it is an obstacle for the rest, and it has to be at its final
            // width before anything can be lifted clear of it.
            SyncInstruction(state);
            SeparateOverlaps();
        }

        /// <summary>
        /// Lifts labels off each other.
        ///
        /// Six traps and three pickups on one oval put plates on top of plates - a trap sitting just
        /// behind a sword hides half its caption, and the top one wins by draw order rather than by
        /// being the one worth reading. It never showed while the labels were bare text, because two
        /// transparent captions overlapping merely look untidy; a plate is opaque and simply deletes
        /// what is under it.
        ///
        /// Upwards, and only as far as clearing takes: the label stays visibly attached to the thing
        /// it names, which matters more than perfect placement.
        /// </summary>
        private void SeparateOverlaps()
        {
            _placed.Clear();
            foreach (var hint in _itemHints) if (hint.Active) _placed.Add(hint);
            foreach (var hint in _trapHints) if (hint.Active) _placed.Add(hint);

            // Lowest first, so a stack grows away from the sand rather than down into it, and each
            // label only ever has to clear ones already settled.
            _placed.Sort((a, b) => Centre(a).y.CompareTo(Centre(b).y));

            // The instruction is an obstacle rather than a participant: it sits where it sits, and
            // an item that happens to lie near the bottom of the arena had its label swallowed whole
            // by it - the plate is opaque, so the caption did not even read as hidden, it read as
            // absent.
            var fixedCentre = Centre(_instruction);
            var fixedSize = _instruction.Rect.sizeDelta;

            for (int i = 0; i < _placed.Count; i++)
            {
                // At most one lift per obstacle below it: every pass moves this label strictly
                // upwards, past the highest thing it currently touches, so it cannot cycle.
                for (int pass = 0; pass <= i; pass++)
                {
                    float clearTop = float.NegativeInfinity;

                    if (Overlaps(Centre(_placed[i]), _placed[i].Rect.sizeDelta, fixedCentre, fixedSize))
                        clearTop = fixedCentre.y + fixedSize.y * 0.5f;

                    for (int j = 0; j < i; j++)
                    {
                        var other = Centre(_placed[j]);
                        if (Overlaps(Centre(_placed[i]), _placed[i].Rect.sizeDelta,
                                     other, _placed[j].Rect.sizeDelta))
                            clearTop = Mathf.Max(clearTop, other.y + _placed[j].Rect.sizeDelta.y * 0.5f);
                    }

                    if (float.IsNegativeInfinity(clearTop)) break;

                    var p = _placed[i].Rect.anchoredPosition;
                    p.y += clearTop + LabelGap + _placed[i].Rect.sizeDelta.y * 0.5f - Centre(_placed[i]).y;
                    _placed[i].Rect.anchoredPosition = p;
                }
            }
        }

        /// <summary>
        /// A hint's centre in one shared frame, whatever it is anchored to.
        ///
        /// The floating labels are anchored to the middle of the canvas and the instruction to the
        /// bottom of it, so their anchoredPositions are not comparable - measured against each other
        /// raw, the instruction reads as sitting a screen-height below the arena and never collides
        /// with anything.
        /// </summary>
        private Vector2 Centre(Hint hint)
        {
            var rect = hint.Rect;
            var canvasRect = ((RectTransform)_canvas.transform).rect;
            var centre = rect.anchoredPosition;

            // Only the y anchor differs between the two kinds, and only by the half-height the
            // bottom pivot introduces.
            centre.y += (rect.anchorMin.y - 0.5f) * canvasRect.height
                        + (0.5f - rect.pivot.y) * rect.sizeDelta.y;
            return centre;
        }

        private static bool Overlaps(Vector2 aCentre, Vector2 aSize, Vector2 bCentre, Vector2 bSize)
            => Mathf.Abs(aCentre.y - bCentre.y) < (aSize.y + bSize.y) * 0.5f + LabelGap
               && Mathf.Abs(aCentre.x - bCentre.x) < (aSize.x + bSize.x) * 0.5f + LabelGap;

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

            _instruction.SetText(armed
                ? "Меч у тебя — тапни к противнику и бей"
                : "Тапни чуть дальше меча — подберёшь на бегу");

            // The ring marks the spot only while it is still the thing to do.
            bool marking = !armed && state.Phase == MatchPhase.Planning
                           && state.TutorialTapPoint != Vector2.zero;
            if (_tapMark.enabled != marking) _tapMark.enabled = marking;
            if (marking && TryCanvasPoint(state.TutorialTapPoint, 0f, out var markLocal))
                _tapMark.rectTransform.anchoredPosition = markLocal;
        }

        private void Place(Hint hint, Vector2? virtualPos, string text)
        {
            bool show = virtualPos.HasValue;
            if (show)
            {
                if (TryCanvasPoint(virtualPos.Value, 0f, out var local))
                {
                    // Text first: the plate has to be its final width before it can be kept inside
                    // the frame, or a long caption is clamped by the width of the previous one.
                    hint.SetText(text);
                    hint.Rect.anchoredPosition = ClampToCanvas(
                        local + new Vector2(0f, LabelLift), hint.Rect.sizeDelta.x);
                }
                else show = false;
            }

            hint.Active = show;
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
