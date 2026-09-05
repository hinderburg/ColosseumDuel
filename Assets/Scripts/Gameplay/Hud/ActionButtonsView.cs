using ColosseumDuel.Core;
using ColosseumDuel.Gameplay.View;
using UnityEngine;
using UnityEngine.UI;

namespace ColosseumDuel.Gameplay.Hud
{
    /// <summary>
    /// The two action buttons, pinned to the player's gladiator: defend up and to the left, ability
    /// up and to the right.
    ///
    /// They only exist during Planning, which is the only phase in which either choice means
    /// anything - during Action the decision is already made and the buttons were dead weight in
    /// the corner. Putting them on the gladiator also keeps the hand near the thing being aimed.
    ///
    /// Screen-space buttons following a world position, rather than a world-space canvas: they stay
    /// a constant size and stay crisp, and they layer above the arena without depth-sorting against
    /// it. The ready flame is the one piece that does live in the world, at the same anchor, because
    /// a particle system has nothing sensible to do inside an overlay canvas.
    /// </summary>
    public sealed class ActionButtonsView : MonoBehaviour
    {
        /// <summary>Screen-space offsets from the gladiator, in reference-resolution pixels.</summary>
        private static readonly Vector2 DefendOffset = new Vector2(-62f, 54f);
        private static readonly Vector2 AbilityOffset = new Vector2(62f, 54f);

        private const float ButtonSize = 74f;

        /// <summary>How visible the ability button is while the rage meter is still filling.</summary>
        private const float NotReadyAlpha = 0.42f;

        public Button Defend { get; private set; }
        public Button Ability { get; private set; }

        private RectTransform _defendRect;
        private RectTransform _abilityRect;
        private CanvasGroup _defendGroup;
        private CanvasGroup _abilityGroup;
        private Image _rageGauge;
        private Image _abilityGlow;
        private GameObject _readyFire;
        private ArenaView _arena;
        private Canvas _canvas;
        private Text _abilityLabel;
        private Image _abilityBackground;

        public static ActionButtonsView Create(Transform canvas, ViewPalette palette, ArenaView arena)
        {
            var root = HudFactory.CreateRect("ActionButtons", canvas);
            HudFactory.Stretch(root);
            var view = root.gameObject.AddComponent<ActionButtonsView>();
            view._arena = arena;
            view._canvas = canvas.GetComponentInParent<Canvas>();

            view.Defend = view.BuildRound(root, "Defend", palette, "Щит", HudFactory.PlayerColor,
                out view._defendRect, out view._defendGroup);

            view.Ability = view.BuildRound(root, "Ability", palette, "", HudFactory.RageColor,
                out view._abilityRect, out view._abilityGroup);
            view._abilityLabel = view._abilityRect.GetComponentInChildren<Text>();
            view._abilityBackground = (Image)view.Ability.targetGraphic;

            // The rage gauge rides on the ability button as a ring, so charge is read in the same
            // glance as the button itself rather than from a bar elsewhere on screen.
            view._rageGauge = HudFactory.CreatePanel("RageGauge", view._abilityRect, HudFactory.RageColor);
            view._rageGauge.sprite = palette != null ? palette.Ring : null;
            view._rageGauge.type = Image.Type.Filled;
            view._rageGauge.fillMethod = Image.FillMethod.Radial360;
            view._rageGauge.fillOrigin = (int)Image.Origin360.Top;
            view._rageGauge.fillClockwise = true;
            view._rageGauge.raycastTarget = false;
            HudFactory.Stretch(view._rageGauge.rectTransform, -6f);

            // A soft halo under the button, pulsed only when the ability is actually available.
            view._abilityGlow = HudFactory.CreatePanel("ReadyGlow", view._abilityRect, HudFactory.RageColor);
            view._abilityGlow.sprite = palette != null ? palette.Disc : null;
            view._abilityGlow.raycastTarget = false;
            view._abilityGlow.transform.SetAsFirstSibling();
            HudFactory.Stretch(view._abilityGlow.rectTransform, -14f);

            if (palette != null && palette.AbilityReadyFire != null)
            {
                view._readyFire = Instantiate(palette.AbilityReadyFire);
                view._readyFire.name = "AbilityReadyFire";
                view._readyFire.SetActive(false);
            }

            root.gameObject.SetActive(false);
            return view;
        }

        private Button BuildRound(Transform parent, string name, ViewPalette palette, string caption,
            Color accent, out RectTransform rect, out CanvasGroup group)
        {
            var image = HudFactory.CreatePanel(name, parent, new Color(0.10f, 0.10f, 0.13f, 0.92f));
            image.sprite = palette != null ? palette.Disc : null;

            rect = image.rectTransform;
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(ButtonSize, ButtonSize);

            var button = image.gameObject.AddComponent<Button>();
            button.targetGraphic = image;

            var edge = HudFactory.CreatePanel("Edge", rect, accent);
            edge.sprite = palette != null ? palette.Ring : null;
            edge.raycastTarget = false;
            HudFactory.Stretch(edge.rectTransform);

            var label = HudFactory.CreateLabel("Label", rect, caption, 15);
            HudFactory.Stretch(label.rectTransform);

            group = image.gameObject.AddComponent<CanvasGroup>();
            return button;
        }

        /// <summary>
        /// Places and updates both buttons. <paramref name="gladiator"/> is null whenever the player
        /// has nobody on the arena, which hides the pair.
        /// </summary>
        public void Sync(GladiatorInstance gladiator, MatchPhase phase, Camera camera, bool abilityArmed)
        {
            bool visible = phase == MatchPhase.Planning && gladiator != null && gladiator.Alive
                           && camera != null && _arena != null;

            if (gameObject.activeSelf != visible) gameObject.SetActive(visible);
            if (_readyFire != null && !visible && _readyFire.activeSelf) _readyFire.SetActive(false);
            if (!visible) return;

            // Anchor above the gladiator's head rather than at his feet, so the buttons do not sit
            // on top of the model.
            float headHeight = _arena.ScaleLength(GameConstants.GladiatorRadius) * 2.4f;
            Vector3 anchorWorld = _arena.ToWorld(gladiator.Pos, headHeight);
            Vector2 anchorScreen = camera.WorldToScreenPoint(anchorWorld);

            // Screen point to a position inside the canvas, rather than assigning screen coordinates
            // to RectTransform.position. That shortcut only holds for a Screen Space - Overlay
            // canvas; under Screen Space - Camera the same property is world space, and the buttons
            // silently fly off somewhere behind the arena.
            var canvasRect = (RectTransform)_canvas.transform;
            var canvasCamera = _canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : _canvas.worldCamera;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    canvasRect, anchorScreen, canvasCamera, out var anchorLocal))
                return;

            _defendRect.anchoredPosition = anchorLocal + DefendOffset;
            _abilityRect.anchoredPosition = anchorLocal + AbilityOffset;

            // The ability differs per gladiator, so name it rather than saying "special" - the
            // names are short enough to fit and tell the player what the button will actually do.
            _abilityLabel.text = gladiator.Def.AbilityName;

            float rage = Mathf.Clamp01(gladiator.Rage / GameConstants.RageMax);
            _rageGauge.fillAmount = rage;

            bool ready = gladiator.CanActivateAbility;
            Ability.interactable = ready;
            _abilityGroup.alpha = ready ? 1f : NotReadyAlpha;
            _defendGroup.alpha = 1f;

            // A slow pulse while ready, so a charged ability catches the eye during planning.
            // Armed is a different state from ready, and the player has to be able to tell which
            // they are looking at: ready means "you may", armed means "you already chose to".
            _abilityBackground.color = ready && abilityArmed
                ? HudFactory.RageColor
                : new Color(0.10f, 0.10f, 0.13f, 0.92f);

            float pulse = ready ? 0.55f + 0.45f * Mathf.Sin(Time.time * 4f) : 0f;
            var glow = HudFactory.RageColor;
            glow.a = pulse * (abilityArmed ? 0.9f : 0.55f);
            _abilityGlow.color = glow;
            _abilityGlow.enabled = ready;

            if (_readyFire != null)
            {
                if (_readyFire.activeSelf != ready) _readyFire.SetActive(ready);
                if (ready)
                {
                    // The flame lives in the world at the same anchor the button is derived from,
                    // so the two line up on screen without the particle needing a canvas.
                    _readyFire.transform.position = _arena.ToWorld(gladiator.Pos, headHeight * 1.15f)
                                                    + new Vector3(_arena.ScaleLength(GameConstants.GladiatorRadius) * 1.6f, 0f, 0f);
                }
            }
        }

    }
}
