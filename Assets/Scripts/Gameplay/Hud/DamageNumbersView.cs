using System.Collections.Generic;
using ColosseumDuel.Gameplay.View;
using UnityEngine;
using UnityEngine.UI;

namespace ColosseumDuel.Gameplay.Hud
{
    /// <summary>
    /// The number that flies off a gladiator when something costs him health.
    ///
    /// Every source, because from the player's seat they are the same event - he is down forty and
    /// wants to know why. A blow, a trap, a wound still bleeding and the spikes closing in all get
    /// a number; the colour says which, so the reason is readable without a legend.
    ///
    /// Drawn on the HUD canvas and projected from the arena rather than as world-space text. The
    /// HUD's font is already proven in a WebGL build, the camera never moves so a projection is a
    /// one-off, and a screen-space label stays the same size whether it comes off a fighter at the
    /// near rail or the far one.
    /// </summary>
    public sealed class DamageNumbersView : MonoBehaviour
    {
        /// <summary>Where a number came from. Colour and size are read off this.</summary>
        public enum Source
        {
            /// <summary>A weapon landing.</summary>
            Blow,

            /// <summary>A trap closing.</summary>
            Trap,

            /// <summary>A wound opened earlier, still costing him.</summary>
            Bleed,

            /// <summary>A phase spent in the spikes.</summary>
            Spikes,
        }

        /// <summary>How many can be in the air at once. Past this the oldest is taken back.</summary>
        private const int Capacity = 8;

        private const float RiseSeconds = 0.95f;

        /// <summary>How far one climbs before it goes out, in canvas units.</summary>
        private const float RiseDistance = 58f;

        /// <summary>Where it starts, above the gladiator's feet - roughly his own height.</summary>
        private const float SpawnHeight = 1.9f;

        /// <summary>
        /// How far off centre a number can start, so two arriving together are not stacked exactly.
        ///
        /// A blow and the bleed it opens land on the same gladiator on the same frame, and drawn at
        /// the same point they read as one number with bad kerning.
        /// </summary>
        private const float Scatter = 26f;

        private static readonly Color BlowColor = new Color(1.00f, 0.95f, 0.86f);
        private static readonly Color TrapColor = new Color(0.96f, 0.66f, 0.22f);
        private static readonly Color BleedColor = new Color(0.86f, 0.24f, 0.28f);
        private static readonly Color SpikesColor = new Color(0.98f, 0.45f, 0.20f);

        private sealed class Number
        {
            public Text Label;
            public Vector2 From;
            public float Left;
            public float Life;
        }

        private readonly List<Number> _numbers = new List<Number>();
        private RectTransform _root;
        private ArenaView _arena;
        private int _next;

        public static DamageNumbersView Create(Transform canvas, ArenaView arena)
        {
            var root = HudFactory.CreateRect("DamageNumbers", canvas);
            HudFactory.Stretch(root);

            var view = root.gameObject.AddComponent<DamageNumbersView>();
            view._root = root;
            view._arena = arena;

            for (int i = 0; i < Capacity; i++)
            {
                var label = HudFactory.CreateLabel($"Damage_{i}", root, "", 26);
                label.raycastTarget = false;
                label.rectTransform.sizeDelta = new Vector2(120f, 34f);
                label.gameObject.SetActive(false);
                view._numbers.Add(new Number { Label = label });
            }

            return view;
        }

        /// <summary>
        /// Sends one number up from a point on the arena.
        ///
        /// Takes the position rather than the gladiator: a number that follows a body still
        /// sprinting away reads as a label attached to him, and what it is actually reporting is
        /// something that happened at a place, a moment ago.
        /// </summary>
        public void Show(Vector2 virtualPos, float amount, Source source)
        {
            if (_arena == null || amount <= 0.5f) return;

            var camera = _arena.ArenaCamera;
            if (camera == null) return;

            var screen = camera.WorldToScreenPoint(_arena.ToWorld(virtualPos, SpawnHeight));

            // Behind the camera projects to a point in front of it, mirrored. Nothing on the arena
            // is ever back there, but a stray call would put a number in the middle of the screen.
            if (screen.z <= 0f) return;

            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    _root, screen, UiCamera(), out var local))
                return;

            local.x += Random.Range(-Scatter, Scatter);

            var slot = Take();
            slot.From = local;
            slot.Life = RiseSeconds;
            slot.Left = RiseSeconds;
            slot.Label.text = Mathf.Max(1, Mathf.RoundToInt(amount)).ToString();
            slot.Label.color = ColorOf(source);
            slot.Label.fontSize = source == Source.Blow ? 30 : 22;
            slot.Label.rectTransform.anchoredPosition = local;
            slot.Label.gameObject.SetActive(true);
        }

        /// <summary>
        /// Which camera a screen point has to be read through to land on this canvas.
        ///
        /// Null for a Screen Space - Overlay canvas, which the game ships as, and the canvas's own
        /// camera for anything else. Not a detail worth skipping: hard-coding the null was right in
        /// the game and silently wrong the moment anything switched the canvas to camera mode - the
        /// numbers were converted against the wrong space and landed off the edge of the screen.
        /// </summary>
        private Camera UiCamera()
        {
            var canvas = _root != null ? _root.GetComponentInParent<Canvas>() : null;
            if (canvas == null || canvas.renderMode == RenderMode.ScreenSpaceOverlay) return null;
            return canvas.worldCamera;
        }

        /// <summary>
        /// A free slot, or the one that has been in the air longest.
        ///
        /// Round-robin rather than a search: with eight of them and a blow every second, the oldest
        /// is the next one along, and taking a live number back is a better failure than dropping
        /// the one that just landed.
        /// </summary>
        private Number Take()
        {
            foreach (var number in _numbers)
                if (!number.Label.gameObject.activeSelf) return number;

            var reused = _numbers[_next];
            _next = (_next + 1) % _numbers.Count;
            return reused;
        }

        private static Color ColorOf(Source source)
        {
            switch (source)
            {
                case Source.Trap: return TrapColor;
                case Source.Bleed: return BleedColor;
                case Source.Spikes: return SpikesColor;
                default: return BlowColor;
            }
        }

        /// <summary>
        /// Unscaled, because the world is not. Planning runs the arena at a third speed and a death
        /// slows it further, and a number that crawled through those would still be climbing when
        /// the next cycle's blows arrived.
        /// </summary>
        private void Update()
        {
            float dt = Time.unscaledDeltaTime;

            foreach (var number in _numbers)
            {
                if (!number.Label.gameObject.activeSelf) continue;

                number.Left -= dt;
                if (number.Left <= 0f)
                {
                    number.Label.gameObject.SetActive(false);
                    continue;
                }

                float travelled = 1f - number.Left / number.Life;
                number.Label.rectTransform.anchoredPosition =
                    number.From + new Vector2(0f, RiseDistance * Rise(travelled));

                // Held solid for the first half and faded over the second: a number that starts
                // fading the instant it appears is hardest to read exactly when it matters.
                var color = number.Label.color;
                color.a = Mathf.Clamp01((1f - travelled) * 2f);
                number.Label.color = color;
            }
        }

        /// <summary>Quick off the mark and slowing, the way something thrown up behaves.</summary>
        private static float Rise(float t) => 1f - (1f - t) * (1f - t);
    }
}
