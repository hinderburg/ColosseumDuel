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
    /// arena without depth-sorting against the props on it.
    ///
    /// Each label sits on its own plate. Light text with a dark outline is what a HUD does when it
    /// has to survive any background; these labels have a background of exactly one kind - bright
    /// sand, sometimes with a trap or a patch of blood crossing it - and against that the
    /// outline was doing the reading work that a plate does better. Dark text on a solid plate also
    /// separates instruction from the game's own light-on-dark chrome.
    /// </summary>
    public sealed class TutorialView : MonoBehaviour
    {
        /// <summary>How many cycles of the first fight carry the labels.</summary>
        public const int TutorialCycles = 2;

        /// <summary>How high above a thing on the floor its label sits, in reference pixels.</summary>
        private const float LabelLift = 34f;

        private const int LabelSize = 13;
        private const float LabelHeight = 24f;

        /// <summary>Breathing room between the text and the edge of its plate, each side.</summary>
        private const float PlatePadding = 9f;

        /// <summary>Clear air between two plates that would otherwise sit on each other.</summary>
        private const float LabelGap = 3f;

        /// <summary>
        /// How much of the frame, top and bottom, the squad rosters occupy. Labels stay out of it.
        /// </summary>
        private const float RosterBandFraction = 0.13f;

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

        /// <summary>Which control is in play, so the instruction names the gesture it wants.</summary>
        private PlayerInputController _input;

        private readonly List<Hint> _itemHints = new List<Hint>();
        private readonly List<Hint> _trapHints = new List<Hint>();

        /// <summary>Scratch list for the de-overlap pass, reused so it allocates nothing per frame.</summary>
        private readonly List<Hint> _placed = new List<Hint>();
        private Hint _instruction;

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
            /// "Trap" to "Two-handed: hits in passing", and one width that suits both is either a
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

        public static TutorialView Create(Transform canvas, ViewPalette palette, ArenaView arena,
            PlayerInputController input = null)
        {
            var root = HudFactory.CreateRect("Tutorial", canvas);
            HudFactory.Stretch(root);

            var view = root.gameObject.AddComponent<TutorialView>();
            view._arena = arena;
            view._input = input;
            view._canvas = canvas.GetComponentInParent<Canvas>();

            for (int i = 0; i < GameConstants.ItemCountOnArena; i++)
                view._itemHints.Add(MakeHint(root, $"ItemHint_{i}", PlateColor, LabelSize));

            for (int i = 0; i < GameConstants.TrapCount; i++)
                view._trapHints.Add(MakeHint(root, $"TrapHint_{i}", TrapPlateColor, LabelSize));

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

        /// <summary>Which weapon and which trap carry the labels this cycle. See ChooseWhatToLabel.</summary>
        private int _labelledItem = -1;
        private int _labelledTrap = -1;
        private int _chosenForCycle = -1;

        /// <summary>
        /// Picks which trap and which weapon carry the labels, once per cycle.
        ///
        /// Once per cycle rather than once, because the sand changes under it - a weapon picked up
        /// respawns elsewhere and a sprung trap stops being worth warning about - and once per frame
        /// would mean a label that walks from one object to another while the player reads it.
        /// </summary>
        private void ChooseWhatToLabel(MatchState state,
            IReadOnlyList<ArenaItem> items, IReadOnlyList<ArenaTrap> traps)
        {
            bool stale = _chosenForCycle != state.Cycle
                         || _labelledItem < 0
                         || items == null || _labelledItem >= items.Count
                         || _labelledTrap < 0
                         || traps == null || _labelledTrap >= traps.Count
                         || !traps[_labelledTrap].Armed;
            if (!stale) return;

            var player = state.P1.Active;
            var here = player != null ? player.Pos : Vector2.zero;

            _labelledItem = NearestIndex(items, here, item => item.Pos, _ => true);
            _labelledTrap = NearestIndex(traps, here, trap => trap.Pos, trap => trap.Armed);
            _chosenForCycle = state.Cycle;
        }

        /// <summary>
        /// Which of these is closest to a point, or -1 if none of them counts.
        ///
        /// Takes the position and the "counts at all" test as functions rather than being written
        /// twice, once for weapons and once for traps: a trap has to be armed to be worth warning
        /// about and a weapon has no such condition, and that is the only difference between them.
        /// </summary>
        private static int NearestIndex<T>(IReadOnlyList<T> candidates, Vector2 to,
            System.Func<T, Vector2> positionOf, System.Func<T, bool> counts)
        {
            if (candidates == null) return -1;

            int nearest = -1;
            float best = float.MaxValue;

            for (int i = 0; i < candidates.Count; i++)
            {
                if (candidates[i] == null || !counts(candidates[i])) continue;

                float d = Vector2.SqrMagnitude(positionOf(candidates[i]) - to);
                if (d >= best) continue;

                best = d;
                nearest = i;
            }

            return nearest;
        }

        /// <summary>What a pickup is worth, in as few words as will fit above it.</summary>
        private static string DescribeItem(ArenaItem item)
        {
            switch (item.Kind)
            {
                case WeaponKind.SwordAndShield: return "Sword and shield: half the damage taken";
                case WeaponKind.TwoHandedMace: return "Mace: one heavy blow, knocks back";
                case WeaponKind.DualSwords: return "Twin swords: two blows, and bleeding";
                default: return "A weapon";
            }
        }

        public void Sync(MatchState state)
        {
            // The first two cycles of a first fight and nothing else. Two is enough to run at the
            // sword and swing once with it; past that the player has done the thing the labels were
            // there to explain, and a caption on every trap and pickup is in the way rather than in
            // aid - it covers exactly the sand they are now trying to read.
            bool teaching = state.Tutorial && state.Round == 1 && state.Cycle <= TutorialCycles
                            && state.Phase != MatchPhase.Pick && state.Phase != MatchPhase.MatchEnd;

            if (gameObject.activeSelf != teaching) gameObject.SetActive(teaching);
            if (!teaching || _arena == null || _arena.ArenaCamera == null) return;

            // One of each kind, and the nearest one.
            //
            // A caption on every trap and every weapon taught the same two things six times over and
            // filled the arena doing it - and the arena is what the labels are pointing at. One
            // example says as much as six, and the nearest is the one the player can act on: a label
            // on a trap across the arena is a fact, a label on the trap in front of them is a
            // warning about the run they are deciding on.
            // Chosen once a cycle and then left alone. Nearest is measured from a gladiator who is
            // running, so recomputing it every frame hands the label from one trap to the next
            // partway through a charge - and a caption that moves while you are reading it is worse
            // than a caption on the wrong trap.
            var items = state.Items?.Items;
            var traps = state.Traps?.Traps;
            ChooseWhatToLabel(state, items, traps);

            bool showItem = _labelledItem >= 0 && items != null && _labelledItem < items.Count;
            for (int i = 0; i < _itemHints.Count; i++)
                Place(_itemHints[i], i == 0 && showItem ? (Vector2?)items[_labelledItem].Pos : null,
                      i == 0 && showItem ? DescribeItem(items[_labelledItem]) : null);

            bool showTrap = _labelledTrap >= 0 && traps != null && _labelledTrap < traps.Count
                            && traps[_labelledTrap].Armed;
            for (int i = 0; i < _trapHints.Count; i++)
                Place(_trapHints[i], i == 0 && showTrap ? (Vector2?)traps[_labelledTrap].Pos : null,
                      i == 0 && showTrap ? "Trap" : null);

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
        /// Away from the middle of the frame, and only as far as clearing takes: the label stays
        /// visibly attached to the thing it names, which matters more than perfect placement.
        ///
        /// Which way "away" is depends on which half the label is in. Everything used to stack
        /// upwards, so a knot of traps at the far end of the arena piled up into the opponent's
        /// roster cards - opaque, and drawn over everything. In the top half a stack therefore grows
        /// downwards, into the empty middle of the arena, and only in the bottom half does it grow
        /// up. Both directions run away from the squad strips rather than into them.
        /// </summary>
        private void SeparateOverlaps()
        {
            // The instruction is an obstacle rather than a participant: it sits where it sits, and
            // an item that happens to lie near the bottom of the arena had its label swallowed whole
            // by it - the plate is opaque, so the caption did not even read as hidden, it read as
            // absent.
            var fixedCentre = Centre(_instruction);
            var fixedSize = _instruction.Rect.sizeDelta;

            SeparateHalf(-1f, fixedCentre, fixedSize);
            SeparateHalf(1f, fixedCentre, fixedSize);
        }

        /// <summary>
        /// Settles the labels in one half of the frame, pushing them in the given direction.
        ///
        /// The two halves are settled independently. They cannot reach each other: nothing is ever
        /// moved across the middle, so a label in the bottom half is no obstacle to one in the top.
        /// </summary>
        private void SeparateHalf(float direction, Vector2 fixedCentre, Vector2 fixedSize)
        {
            _placed.Clear();
            foreach (var hint in _itemHints)
                if (hint.Active && Mathf.Sign(Centre(hint).y) == direction) _placed.Add(hint);
            foreach (var hint in _trapHints)
                if (hint.Active && Mathf.Sign(Centre(hint).y) == direction) _placed.Add(hint);

            // Nearest the middle first, so a stack grows outwards and each label only ever has to
            // clear ones already settled.
            _placed.Sort((a, b) => (direction * Centre(a).y).CompareTo(direction * Centre(b).y));

            for (int i = 0; i < _placed.Count; i++)
            {
                var size = _placed[i].Rect.sizeDelta;

                // At most one shove per obstacle nearer the middle: every pass moves this label
                // strictly outwards, past the outermost thing it currently touches, so it cannot
                // cycle.
                for (int pass = 0; pass <= i; pass++)
                {
                    float clearEdge = float.NegativeInfinity;

                    if (Overlaps(Centre(_placed[i]), size, fixedCentre, fixedSize))
                        clearEdge = direction * fixedCentre.y + fixedSize.y * 0.5f;

                    for (int j = 0; j < i; j++)
                    {
                        var other = Centre(_placed[j]);
                        if (Overlaps(Centre(_placed[i]), size, other, _placed[j].Rect.sizeDelta))
                            clearEdge = Mathf.Max(clearEdge,
                                direction * other.y + _placed[j].Rect.sizeDelta.y * 0.5f);
                    }

                    if (float.IsNegativeInfinity(clearEdge)) break;

                    var p = _placed[i].Rect.anchoredPosition;
                    p.y += direction * (clearEdge + LabelGap + size.y * 0.5f)
                           - Centre(_placed[i]).y;
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
            bool armed = player != null && player.WeaponIsGilded;

            // Says the gesture the game is actually listening for.
            //
            // It was a fixed string naming whichever control happened to be default when it was
            // written, and it has now been wrong in both directions - "tap" after the swipe took
            // over, then "swipe" after the tap came back. The ring that used to point at the spot is
            // gone, so this line is the only guidance there is; it reads the scheme.
            var scheme = _input != null ? _input.Scheme : ControlScheme.Tap;
            _instruction.SetText(armed ? ChargeOrder(scheme) : FetchOrder(scheme));
        }

        /// <summary>Go and get the weapon, in the gesture this player is using.</summary>
        private static string FetchOrder(ControlScheme scheme)
        {
            switch (scheme)
            {
                case ControlScheme.Drag: return "Pull back from your gladiator, away from the gold weapon";
                case ControlScheme.Swipe: return "Swipe away from the gold weapon - you take it on the way";
                default: return "Tap just past the gold weapon - you take it on the way";
            }
        }

        /// <summary>And now go and use it.</summary>
        private static string ChargeOrder(ControlScheme scheme)
        {
            switch (scheme)
            {
                case ControlScheme.Drag: return "Gilded and stronger - pull back away from the enemy";
                case ControlScheme.Swipe: return "Gilded and stronger - swipe away from the enemy and charge";
                default: return "Gilded and stronger - tap toward the enemy and swing";
            }
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

                    // Above things in the near half of the arena and below things in the far half,
                    // so a label always hangs towards the middle of the frame and away from the
                    // squad strip at that end.
                    float lift = local.y > 0f ? -LabelLift : LabelLift;
                    hint.Rect.anchoredPosition = ClampToCanvas(
                        local + new Vector2(0f, lift), hint.Rect.sizeDelta.x);
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

            float sideLimit = Mathf.Max(canvasRect.width * 0.5f - labelWidth * 0.5f - 6f, 0f);
            local.x = Mathf.Clamp(local.x, -sideLimit, sideLimit);

            // And out of the squad strips along the top and bottom. A trap at the far end of the
            // arena projects up behind the opponent's roster cards, which are opaque and sit over
            // everything - so its label was landing on top of a gladiator portrait, reading as part
            // of the card rather than as something on the sand.
            float endLimit = Mathf.Max(
                canvasRect.height * (0.5f - RosterBandFraction) - LabelHeight * 0.5f, 0f);
            local.y = Mathf.Clamp(local.y, -endLimit, endLimit);
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
