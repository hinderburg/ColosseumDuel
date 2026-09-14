using System.Collections.Generic;
using ColosseumDuel.Core;
using ColosseumDuel.Gameplay.View;
using UnityEngine;
using UnityEngine.UI;

namespace ColosseumDuel.Gameplay.Hud
{
    /// <summary>
    /// The name of an ability, going up over the man who used it: the name large, what it does in
    /// smaller type underneath.
    ///
    /// Both sides, because the moment the bot's Bulwark goes up is the moment the player has to stop
    /// charging it head on - and a ring on the sand in a colour he has not learned says nothing.
    ///
    /// Drawn on the HUD canvas and projected from the arena, like the damage numbers and for the
    /// same reasons: the font is proven in a build, and a screen-space label stays the same size
    /// whichever end of the arena it comes off.
    /// </summary>
    public sealed class AbilityCalloutView : MonoBehaviour
    {
        /// <summary>Both sides can fire on the same phase; two more for a restart landing on top.</summary>
        private const int Capacity = 4;

        /// <summary>How long one stays up, in real seconds.</summary>
        public const float Seconds = 1.8f;

        /// <summary>How far one climbs while it is up, in canvas units.</summary>
        private const float RiseDistance = 44f;

        /// <summary>Where it starts: above the head and the bars over it.</summary>
        private const float SpawnHeight = 3.4f;

        public const int NameSize = 34;
        public const int DescriptionSize = 17;

        private sealed class Callout
        {
            public RectTransform Root;
            public Text Name;
            public Text Description;
            public Vector2 From;
            public float Left;
        }

        private readonly List<Callout> _callouts = new List<Callout>();
        private RectTransform _root;
        private ArenaView _arena;
        private int _next;

        public static AbilityCalloutView Create(Transform canvas, ArenaView arena)
        {
            var root = HudFactory.CreateRect("AbilityCallouts", canvas);
            HudFactory.Stretch(root);

            var view = root.gameObject.AddComponent<AbilityCalloutView>();
            view._root = root;
            view._arena = arena;

            for (int i = 0; i < Capacity; i++)
            {
                var callout = HudFactory.CreateRect($"Callout_{i}", root);
                callout.sizeDelta = new Vector2(360f, 70f);

                var name = HudFactory.CreateLabel("Name", callout, "", NameSize);
                name.fontStyle = FontStyle.Bold;
                name.rectTransform.anchorMin = new Vector2(0f, 0.5f);
                name.rectTransform.anchorMax = new Vector2(1f, 1f);
                name.rectTransform.offsetMin = Vector2.zero;
                name.rectTransform.offsetMax = Vector2.zero;
                Outline(name, 2f);

                var description = HudFactory.CreateLabel("Description", callout, "", DescriptionSize);
                description.rectTransform.anchorMin = new Vector2(0f, 0f);
                description.rectTransform.anchorMax = new Vector2(1f, 0.5f);
                description.rectTransform.offsetMin = Vector2.zero;
                description.rectTransform.offsetMax = Vector2.zero;
                Outline(description, 1.5f);

                callout.gameObject.SetActive(false);
                view._callouts.Add(new Callout { Root = callout, Name = name, Description = description });
            }

            return view;
        }

        /// <summary>
        /// A dark edge round the letters: they go up over sand, blood and gold, and plain text on
        /// all three is unreadable on at least one of them.
        /// </summary>
        private static void Outline(Text label, float width)
        {
            var outline = label.gameObject.AddComponent<Outline>();
            outline.effectColor = new Color(0f, 0f, 0f, 0.85f);
            outline.effectDistance = new Vector2(width, -width);
        }

        /// <summary>
        /// Puts one ability's name up over the point he is standing on, in his side's colour - blue
        /// for the player's men, red for the opponent's - so whose ability it was reads before the name.
        /// </summary>
        public void Show(Vector2 virtualPos, GladiatorDef def, PlayerSide side)
        {
            if (_arena == null || def == null) return;
            var camera = _arena.ArenaCamera;
            if (camera == null) return;

            var screen = camera.WorldToScreenPoint(_arena.ToWorld(virtualPos, SpawnHeight));
            if (screen.z <= 0f) return;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(_root, screen, UiCamera(), out var local))
                return;

            var slot = Take();
            slot.From = local;
            slot.Left = Seconds;
            slot.Name.text = def.AbilityName;
            slot.Name.color = side == PlayerSide.P1 ? HudFactory.PlayerColor : HudFactory.BotColor;
            slot.Description.text = Capitalised(def.AbilityDescription);
            slot.Description.color = HudFactory.TextColor;
            slot.Root.anchoredPosition = local;
            slot.Root.SetAsLastSibling();
            slot.Root.gameObject.SetActive(true);
        }

        private static string Capitalised(string text)
            => string.IsNullOrEmpty(text) ? "" : char.ToUpperInvariant(text[0]) + text.Substring(1);

        /// <summary>Null for the Overlay canvas the game ships with; the canvas camera otherwise.</summary>
        private Camera UiCamera()
        {
            var canvas = _root != null ? _root.GetComponentInParent<Canvas>() : null;
            if (canvas == null || canvas.renderMode == RenderMode.ScreenSpaceOverlay) return null;
            return canvas.worldCamera;
        }

        private Callout Take()
        {
            foreach (var callout in _callouts)
                if (!callout.Root.gameObject.activeSelf) return callout;

            var reused = _callouts[_next];
            _next = (_next + 1) % _callouts.Count;
            return reused;
        }

        /// <summary>
        /// Unscaled: an ability fires at the top of the action phase and the next planning phase
        /// slows the world to a fifth - on scaled time the name would hang over the next decision.
        /// </summary>
        private void Update()
        {
            float dt = Time.unscaledDeltaTime;
            foreach (var callout in _callouts)
            {
                if (!callout.Root.gameObject.activeSelf) continue;

                callout.Left -= dt;
                if (callout.Left <= 0f)
                {
                    callout.Root.gameObject.SetActive(false);
                    continue;
                }

                float t = 1f - callout.Left / Seconds;
                callout.Root.anchoredPosition = callout.From + new Vector2(0f, RiseDistance * (1f - (1f - t) * (1f - t)));

                // Solid for most of its time and gone over the last third: a name that starts
                // fading as it appears is hardest to read exactly when it is being read.
                float alpha = Mathf.Clamp01((1f - t) * 3f);
                SetAlpha(callout.Name, alpha);
                SetAlpha(callout.Description, alpha);
            }
        }

        private static void SetAlpha(Text label, float alpha)
        {
            var color = label.color;
            color.a = alpha;
            label.color = color;
        }
    }
}
