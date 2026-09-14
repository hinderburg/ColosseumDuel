using System.Collections.Generic;
using ColosseumDuel.Core;
using ColosseumDuel.Gameplay.View;
using UnityEngine;
using UnityEngine.UI;

namespace ColosseumDuel.Gameplay.Hud
{
    /// <summary>
    /// The first fight's teaching layer: a short label over the weapon blessing on the sand, and one
    /// line telling the player what to do with it.
    ///
    /// A label rather than a wall of text at the start: what the blessing does is worth knowing at
    /// the moment you are looking at it, and nowhere else. It is placed over the thing itself, so
    /// reading it is the same glance as deciding whether to run at it.
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
        /// <summary>How many rounds of the first fight carry the labels.</summary>
        public const int TutorialRounds = 2;

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

        /// <summary>The order of the clash, so it carries the eye before the labels do.</summary>
        private static readonly Color InstructionPlateColor = new Color(1f, 0.87f, 0.52f, 0.96f);

        /// <summary>What the blessing does, in as few words as will fit above it.</summary>
        public static readonly string BlessingCaption =
            $"Blessing: +{Mathf.RoundToInt((GameConstants.WeaponBuffDamageMult - 1f) * 100f)}% damage " +
            $"for {GameConstants.WeaponBuffRounds} rounds";

        private ArenaView _arena;
        private Canvas _canvas;

        /// <summary>Which control is in play, so the instruction names the gesture it wants.</summary>
        private PlayerInputController _input;

        private Hint _blessingHint;

        /// <summary>Every label that floats over the sand - the ones the de-overlap pass settles.</summary>
        private readonly List<Hint> _floating = new List<Hint>();

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
            /// Sets the caption and fits the plate to it, so a short caption does not sit in a slab
            /// of empty parchment and a long one does not hang out of its plate.
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

            view._blessingHint = MakeHint(root, "BlessingHint", PlateColor, LabelSize);
            view._floating.Add(view._blessingHint);

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

        public void Sync(MatchState state)
        {
            // The first two rounds of a first fight and nothing else. Two is enough to run at the
            // blessing and swing once with it; past that the player has done the thing the labels
            // were there to explain, and a caption on the sand is in the way rather than in aid.
            bool teaching = state.Tutorial && state.Clash == 1 && state.Round <= TutorialRounds
                            && state.Phase != MatchPhase.Pick && state.Phase != MatchPhase.MatchEnd;

            if (gameObject.activeSelf != teaching) gameObject.SetActive(teaching);
            if (!teaching || _arena == null || _arena.ArenaCamera == null) return;

            var blessing = state.Buffs?.Position;
            Place(_blessingHint, blessing, BlessingCaption);

            // The instruction first: it is an obstacle for the rest, and it has to be at its final
            // width before anything can be lifted clear of it.
            SyncInstruction(state);
            SeparateOverlaps();
        }

        /// <summary>
        /// Lifts labels off each other and off the instruction.
        ///
        /// A plate is opaque and simply deletes what is under it, so two that overlap read as one
        /// with a caption missing. Away from the middle of the frame, and only as far as clearing
        /// takes: the label stays visibly attached to the thing it names, which matters more than
        /// perfect placement. In the top half a stack grows downwards and in the bottom half up, so
        /// it runs away from the squad strips rather than into them.
        /// </summary>
        private void SeparateOverlaps()
        {
            // The instruction is an obstacle rather than a participant: it sits where it sits, and a
            // label that happens to lie near the bottom of the arena was swallowed whole by it.
            var fixedCentre = Centre(_instruction);
            var fixedSize = _instruction.Rect.sizeDelta;

            SeparateHalf(-1f, fixedCentre, fixedSize);
            SeparateHalf(1f, fixedCentre, fixedSize);
        }

        /// <summary>
        /// Settles the labels in one half of the frame, pushing them in the given direction. The two
        /// halves cannot reach each other: nothing is ever moved across the middle.
        /// </summary>
        private void SeparateHalf(float direction, Vector2 fixedCentre, Vector2 fixedSize)
        {
            _placed.Clear();
            foreach (var hint in _floating)
                if (hint.Active && Mathf.Sign(Centre(hint).y) == direction) _placed.Add(hint);

            // Nearest the middle first, so a stack grows outwards and each label only ever has to
            // clear ones already settled.
            _placed.Sort((a, b) => (direction * Centre(a).y).CompareTo(direction * Centre(b).y));

            for (int i = 0; i < _placed.Count; i++)
            {
                var size = _placed[i].Rect.sizeDelta;

                // At most one shove per obstacle nearer the middle: every pass moves this label
                // strictly outwards, past the outermost thing it currently touches, so it cannot
                // round.
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
        /// One line, changing as the lesson lands: run through the blessing, then fight.
        ///
        /// Keyed off whether his weapon is actually blessed rather than off a step counter, so it
        /// cannot get out of step with the match - a player who takes the blessing by accident on
        /// the way somewhere else has still learned the thing and is told so.
        /// </summary>
        private void SyncInstruction(MatchState state)
        {
            var player = state.P1.Active;
            bool blessed = player != null && player.WeaponBuffed;

            // Says the gesture the game is actually listening for. It was a fixed string naming
            // whichever control happened to be default when it was written, and was wrong in both
            // directions before it read the scheme.
            var scheme = _input != null ? _input.Scheme : ControlScheme.Draw;
            _instruction.SetText(blessed ? ChargeOrder(scheme) : FetchOrder(scheme));
        }

        /// <summary>Go and get the blessing, in the gesture this player is using.</summary>
        private static string FetchOrder(ControlScheme scheme)
        {
            switch (scheme)
            {
                case ControlScheme.Drag: return "Pull back from your gladiator, away from the red blessing";
                case ControlScheme.Swipe: return "Swipe away from the red blessing - you take it on the way";
                default: return "Tap just past the red blessing - you take it on the way";
            }
        }

        /// <summary>And now go and use it.</summary>
        private static string ChargeOrder(ControlScheme scheme)
        {
            switch (scheme)
            {
                case ControlScheme.Drag: return "Your weapon glows - pull back away from the enemy";
                case ControlScheme.Swipe: return "Your weapon glows - swipe away from the enemy and charge";
                default: return "Your weapon glows - tap toward the enemy and strike";
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
        /// Keeps a label inside the frame, and out of the squad strips along the top and bottom.
        /// Nudged in rather than hidden, because a label the player cannot finish reading is still
        /// better than one they never see.
        /// </summary>
        private Vector2 ClampToCanvas(Vector2 local, float labelWidth)
        {
            var canvasRect = ((RectTransform)_canvas.transform).rect;

            float sideLimit = Mathf.Max(canvasRect.width * 0.5f - labelWidth * 0.5f - 6f, 0f);
            local.x = Mathf.Clamp(local.x, -sideLimit, sideLimit);

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
