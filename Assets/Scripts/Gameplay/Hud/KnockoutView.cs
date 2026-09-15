using ColosseumDuel.Core;
using ColosseumDuel.Gameplay.View;
using UnityEngine;
using UnityEngine.UI;

namespace ColosseumDuel.Gameplay.Hud
{
    /// <summary>
    /// The moment a man drops: KO goes up over him in his side's colour - large, snapping in from
    /// larger and holding before it fades - and the frame flashes white. The slow motion and the
    /// camera coming in are elsewhere (see GameController and DeathCameraView); this is the hit on
    /// the frame itself.
    ///
    /// Drawn on the HUD canvas and projected from the arena, like the numbers and the callouts.
    /// </summary>
    public sealed class KnockoutView : MonoBehaviour
    {
        public const string Caption = "KO";

        /// <summary>How long the caption stays, in real seconds, and how long it takes to snap in and to fade.</summary>
        public const float HoldSeconds = 1.4f, SnapSeconds = 0.25f, FadeSeconds = 0.35f;

        /// <summary>How much larger it starts than it settles at.</summary>
        private const float SnapScale = 1.4f;

        /// <summary>The white the frame flashes to, and how long it takes to go.</summary>
        public const float FlashAlpha = 0.45f, FlashSeconds = 0.3f;

        private const int CaptionSize = 96;
        private const float SpawnHeight = 3.2f;

        private RectTransform _root;
        private ArenaView _arena;
        private Image _flash;
        private Text _caption;
        private float _captionAge = float.MaxValue;
        private float _flashLeft;

        /// <summary>Whether KO is up right now. For tests.</summary>
        public bool IsShowing => _caption != null && _caption.gameObject.activeSelf;

        public static KnockoutView Create(Transform canvas, ArenaView arena)
        {
            var root = HudFactory.CreateRect("Knockout", canvas);
            HudFactory.Stretch(root);
            var view = root.gameObject.AddComponent<KnockoutView>();
            view._root = root;
            view._arena = arena;

            view._flash = HudFactory.CreatePanel("KnockoutFlash", root, Color.clear);
            view._flash.raycastTarget = false;
            HudFactory.Stretch(view._flash.rectTransform);
            view._flash.enabled = false;

            view._caption = HudFactory.CreateLabel("KnockoutCaption", root, Caption, CaptionSize);
            view._caption.fontStyle = FontStyle.Bold;
            view._caption.raycastTarget = false;
            view._caption.horizontalOverflow = HorizontalWrapMode.Overflow;
            view._caption.verticalOverflow = VerticalWrapMode.Overflow;
            view._caption.rectTransform.anchorMin = view._caption.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            view._caption.rectTransform.sizeDelta = new Vector2(300f, 120f);
            var outline = view._caption.gameObject.AddComponent<Outline>();
            outline.effectColor = new Color(0f, 0f, 0f, 0.9f);
            outline.effectDistance = new Vector2(3f, -3f);
            view._caption.gameObject.SetActive(false);

            return view;
        }

        /// <summary>He is down: KO over the point he fell at, in his side's colour, and the frame flashes.</summary>
        public void Show(Vector2 virtualPos, PlayerSide side)
        {
            // Lit on this frame, not the next: the flash is the impact, and an impact a frame late is a flicker.
            _flashLeft = FlashSeconds;
            _flash.color = new Color(1f, 1f, 1f, FlashAlpha);
            _flash.enabled = true;

            if (_arena == null || _arena.ArenaCamera == null) return;
            var screen = _arena.ArenaCamera.WorldToScreenPoint(_arena.ToWorld(virtualPos, SpawnHeight));
            if (screen.z <= 0f) return;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(_root, screen, UiCamera(), out var local)) return;

            _caption.rectTransform.anchoredPosition = local;
            _caption.color = side == PlayerSide.P1 ? HudFactory.PlayerColor : HudFactory.BotColor;
            _caption.rectTransform.localScale = Vector3.one * SnapScale;
            _captionAge = 0f;
            _caption.gameObject.SetActive(true);
        }

        private Camera UiCamera()
        {
            var canvas = _root != null ? _root.GetComponentInParent<Canvas>() : null;
            if (canvas == null || canvas.renderMode == RenderMode.ScreenSpaceOverlay) return null;
            return canvas.worldCamera;
        }

        /// <summary>Unscaled: the whole point of the moment is that the world has slowed down.</summary>
        private void Update()
        {
            float dt = Time.unscaledDeltaTime;

            if (_flash.enabled)
            {
                _flashLeft = Mathf.Max(0f, _flashLeft - dt);
                float a = FlashAlpha * (_flashLeft / FlashSeconds);
                _flash.color = new Color(1f, 1f, 1f, a);
                if (_flashLeft <= 0f) _flash.enabled = false;
            }

            if (!_caption.gameObject.activeSelf) return;
            _captionAge += dt;

            float snap = Mathf.Clamp01(_captionAge / SnapSeconds);
            float eased = 1f - (1f - snap) * (1f - snap);
            _caption.rectTransform.localScale = Vector3.one * Mathf.Lerp(SnapScale, 1f, eased);

            float total = HoldSeconds + FadeSeconds;
            var color = _caption.color;
            color.a = _captionAge < HoldSeconds ? 1f : Mathf.Clamp01((total - _captionAge) / FadeSeconds);
            _caption.color = color;
            if (_captionAge >= total) _caption.gameObject.SetActive(false);
        }
    }
}
