using ColosseumDuel.Core;
using ColosseumDuel.Gameplay.View;
using UnityEngine;
using UnityEngine.UI;

namespace ColosseumDuel.Gameplay.Hud
{
    /// <summary>
    /// The two action buttons, pinned to the player's gladiator - the guard up and to his left, the
    /// ability up and to his right - and the countdown, drawn faintly round the man himself.
    ///
    /// They only exist during Planning, which is the only phase in which either choice means
    /// anything - during Action the decision is already made.
    ///
    /// The buttons spent one iteration in a column down the right-hand edge of the screen, with a
    /// third for the about-face, and came back here by request when the about-face went: beside him
    /// they are read in the same glance as the decision they belong to.
    ///
    /// Screen-space buttons following a world position rather than a world-space canvas: they stay
    /// a constant size and stay crisp, and they layer above the arena without depth-sorting against
    /// it. The ready flame is the one piece that does live in the world, at the same anchor, because
    /// a particle system has nothing sensible to do inside an overlay canvas.
    /// </summary>
    public sealed class ActionButtonsView : MonoBehaviour
    {
        /// <summary>
        /// Screen-space offsets from above his head, in reference-resolution pixels.
        ///
        /// Raised well clear of the figure. At the old height the buttons' lower edges sat on his
        /// body, and a press there went to the button rather than to him.
        /// </summary>
        private static readonly Vector2 DefendOffset = new Vector2(-62f, 100f);
        private static readonly Vector2 AbilityOffset = new Vector2(62f, 100f);

        private const float ButtonSize = 74f;

        /// <summary>
        /// Across the countdown ring, in reference pixels: about a body wide.
        ///
        /// Drawn around the gladiator himself. The clock is the shape of the decision being made,
        /// and the decision is made by looking at him - so putting the two in the same place means
        /// the phase can be timed without the eye leaving the fight. Faint, for the same reason: it
        /// has to be readable at a glance and invisible when it is not being glanced at.
        /// </summary>
        private const float TimerSize = 138f;

        /// <summary>How solid the countdown is over the sand. Enough to read, not enough to obscure.</summary>
        private const float TimerAlpha = 0.55f;

        /// <summary>The part of the ring that has already run out. Dark, and it stays put.</summary>
        private static readonly Color TimerTrackColor = new Color(0f, 0f, 0f, 0.22f);

        /// <summary>Background of a button nobody has pressed.</summary>
        private static readonly Color Idle = new Color(0.10f, 0.10f, 0.13f, 0.92f);

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
        private Image _abilityIcon;
        private ViewPalette _palette;
        private Image _abilityBackground;

        /// <summary>Across the ability glyph, in reference pixels: the upper half of the button.</summary>
        private const float AbilityIconSize = 28f;
        private Image _defendBackground;
        private Image _defendGlow;
        private Image _timer;
        private RectTransform _timerTrack;
        private RectTransform _timerRect;

        public static ActionButtonsView Create(Transform canvas, ViewPalette palette, ArenaView arena)
        {
            var root = HudFactory.CreateRect("ActionButtons", canvas);
            HudFactory.Stretch(root);
            var view = root.gameObject.AddComponent<ActionButtonsView>();
            view._arena = arena;
            view._canvas = canvas.GetComponentInParent<Canvas>();

            view.Defend = view.BuildRound(root, "Defend", palette, "Block", HudFactory.PlayerColor,
                out view._defendRect, out view._defendGroup);
            view._defendBackground = (Image)view.Defend.targetGraphic;

            // The guard gets the same halo the ability has. Pressing it used to file the plan and
            // leave nothing behind, so there was no way to tell a guard from a phase where nothing
            // had been touched at all.
            view._defendGlow = HudFactory.CreatePanel("DefendGlow", view._defendRect, HudFactory.PlayerColor);
            HudFactory.UseSprite(view._defendGlow, palette != null ? palette.Disc : null);
            view._defendGlow.raycastTarget = false;
            view._defendGlow.transform.SetAsFirstSibling();
            HudFactory.Stretch(view._defendGlow.rectTransform, -14f);

            view.Ability = view.BuildRound(root, "Ability", palette, "", HudFactory.RageColor,
                out view._abilityRect, out view._abilityGroup);
            view._abilityLabel = view._abilityRect.GetComponentInChildren<Text>();
            view._abilityBackground = (Image)view.Ability.targetGraphic;
            view._palette = palette;

            // The ability's own glyph above its name: six abilities now, and the button should say
            // which one it is before the name is read.
            view._abilityIcon = HudFactory.CreatePanel("AbilityIcon", view._abilityRect, HudFactory.RageColor);
            view._abilityIcon.raycastTarget = false;
            view._abilityIcon.preserveAspect = true;
            var iconRect = view._abilityIcon.rectTransform;
            iconRect.anchorMin = iconRect.anchorMax = new Vector2(0.5f, 0.5f);
            iconRect.pivot = new Vector2(0.5f, 0.5f);
            iconRect.sizeDelta = new Vector2(AbilityIconSize, AbilityIconSize);
            iconRect.anchoredPosition = new Vector2(0f, 10f);

            // The rage gauge rides on the ability button as a ring, so charge is read in the same
            // glance as the button itself rather than from a bar elsewhere on screen.
            view._rageGauge = HudFactory.CreatePanel("RageGauge", view._abilityRect, HudFactory.RageColor);
            HudFactory.UseSprite(view._rageGauge, palette != null ? palette.Ring : null);
            view._rageGauge.type = Image.Type.Filled;
            view._rageGauge.fillMethod = Image.FillMethod.Radial360;
            view._rageGauge.fillOrigin = (int)Image.Origin360.Top;
            view._rageGauge.fillClockwise = true;
            view._rageGauge.raycastTarget = false;
            HudFactory.Stretch(view._rageGauge.rectTransform, -6f);

            // A soft halo under the button, pulsed only when the ability is actually available.
            view._abilityGlow = HudFactory.CreatePanel("ReadyGlow", view._abilityRect, HudFactory.RageColor);
            HudFactory.UseSprite(view._abilityGlow, palette != null ? palette.Disc : null);
            view._abilityGlow.raycastTarget = false;
            view._abilityGlow.transform.SetAsFirstSibling();
            HudFactory.Stretch(view._abilityGlow.rectTransform, -14f);

            // A ring that empties, not a number.
            //
            // The number was read, and that was the problem: four tenths of a second is a quantity
            // to think about, and the phase is for thinking about the fight. A ring is seen without
            // being read - how much is left is its shape, and the player takes it in from the corner
            // of an eye already on the arena.
            //
            // Two rings: a dim one that stays whole, so there is something for the bright one to be
            // a fraction of, and the bright one on top of it.
            var track = HudFactory.CreatePanel("DecisionTimerTrack", root, TimerTrackColor);
            HudFactory.UseSprite(track, palette != null ? palette.Ring : null);
            track.raycastTarget = false;
            var trackRect = track.rectTransform;
            trackRect.anchorMin = trackRect.anchorMax = new Vector2(0.5f, 0.5f);
            trackRect.pivot = new Vector2(0.5f, 0.5f);
            trackRect.sizeDelta = new Vector2(TimerSize, TimerSize);
            view._timerTrack = trackRect;

            var timer = HudFactory.CreatePanel("DecisionTimer", root, Color.white);
            HudFactory.UseSprite(timer, palette != null ? palette.Ring : null);
            timer.raycastTarget = false;
            timer.type = Image.Type.Filled;
            timer.fillMethod = Image.FillMethod.Radial360;
            timer.fillOrigin = (int)Image.Origin360.Top;

            // Anticlockwise, so what is left drains away from the top rather than growing towards
            // it. Clockwise reads as filling up, which is the opposite of what is happening.
            timer.fillClockwise = false;

            view._timer = timer;
            view._timerRect = timer.rectTransform;
            view._timerRect.anchorMin = view._timerRect.anchorMax = new Vector2(0.5f, 0.5f);
            view._timerRect.pivot = new Vector2(0.5f, 0.5f);
            view._timerRect.sizeDelta = new Vector2(TimerSize, TimerSize);

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
            var image = HudFactory.CreatePanel(name, parent, Idle);
            HudFactory.UseSprite(image, palette != null ? palette.Disc : null);

            rect = image.rectTransform;
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(ButtonSize, ButtonSize);

            var button = image.gameObject.AddComponent<Button>();
            button.targetGraphic = image;

            var edge = HudFactory.CreatePanel("Edge", rect, accent);
            HudFactory.UseSprite(edge, palette != null ? palette.Ring : null);
            edge.raycastTarget = false;
            HudFactory.Stretch(edge.rectTransform);

            var label = HudFactory.CreateLabel("Label", rect, caption, 15);
            HudFactory.Stretch(label.rectTransform);

            group = image.gameObject.AddComponent<CanvasGroup>();
            return button;
        }

        /// <summary>
        /// Takes the buttons off screen regardless of the match, for whatever is standing in front
        /// of it. Applied after Sync, so the ordinary rules still decide when nothing is.
        /// </summary>
        public void SetHidden(bool hidden)
        {
            if (hidden && gameObject.activeSelf) gameObject.SetActive(false);
        }

        /// <summary>
        /// Places and updates both buttons and the countdown. <paramref name="gladiator"/> is null
        /// whenever the player has nobody on the arena, which hides the lot.
        /// </summary>
        public void Sync(GladiatorInstance gladiator, MatchPhase phase, Camera camera,
                         bool abilityArmed, bool defendArmed, float secondsLeft)
        {
            bool present = gladiator != null && gladiator.Alive && camera != null && _arena != null;
            bool planning = phase == MatchPhase.Planning && present;
            bool gain = !planning && present && ShowingRageGain;
            bool visible = planning || gain;

            if (gameObject.activeSelf != visible) gameObject.SetActive(visible);
            if (_readyFire != null && !planning && _readyFire.activeSelf) _readyFire.SetActive(false);
            if (!visible) return;

            if (gain)
            {
                SyncRageGain(gladiator, camera);
                return;
            }
            ShowPlanningControls(true);

            // The buttons hang off a point above his head, so they do not sit on the model; the
            // countdown is round his feet, so it frames him rather than floating over him.
            float headHeight = HeadHeight;
            if (!TryCanvasPoint(_arena.ToWorld(gladiator.Pos, headHeight), camera, out var head)) return;
            if (!TryCanvasPoint(_arena.ToWorld(gladiator.Pos), camera, out var feet)) return;

            _defendRect.anchoredPosition = head + DefendOffset;
            _abilityRect.anchoredPosition = head + AbilityOffset;
            _timerRect.anchoredPosition = feet;
            _timerTrack.anchoredPosition = feet;

            _timer.fillAmount = Mathf.Clamp01(secondsLeft / GameConstants.PlanningTime);

            // Red once there is under a second left. It sits on the man, so it can afford to say
            // something as well as count.
            var timerColor = secondsLeft <= 1f ? new Color(1f, 0.45f, 0.35f) : Color.white;
            timerColor.a = TimerAlpha;
            _timer.color = timerColor;

            SyncAbilityFace(gladiator);

            _rageGauge.fillAmount = Mathf.Clamp01(gladiator.Rage / GameConstants.RageMax);

            bool ready = gladiator.CanActivateAbility;
            Ability.interactable = ready;
            _abilityGroup.alpha = ready ? 1f : NotReadyAlpha;
            _abilityGroup.blocksRaycasts = true;
            _defendGroup.alpha = 1f;

            // A slow pulse while ready, so a charged ability catches the eye during planning.
            // Armed is a different state from ready, and the player has to be able to tell which
            // they are looking at: ready means "you may", armed means "you already chose to".
            _abilityBackground.color = ready && abilityArmed ? HudFactory.RageColor : Idle;

            float pulse = ready ? 0.55f + 0.45f * Mathf.Sin(Time.time * 4f) : 0f;
            var glow = HudFactory.RageColor;
            glow.a = pulse * (abilityArmed ? 0.9f : 0.55f);
            _abilityGlow.color = glow;
            _abilityGlow.enabled = ready;

            // The guard reads the same way: filled background plus a steady halo once chosen. Steady
            // rather than pulsing, because the pulse on the ability means "available, go on" and
            // this one means "chosen, done" - two things that should not share an animation.
            _defendBackground.color = defendArmed ? HudFactory.PlayerColor : Idle;
            var defendGlow = HudFactory.PlayerColor;
            defendGlow.a = 0.75f;
            _defendGlow.color = defendGlow;
            _defendGlow.enabled = defendArmed;

            if (_readyFire != null)
            {
                if (_readyFire.activeSelf != ready) _readyFire.SetActive(ready);
                if (ready)
                {
                    // The flame lives in the world at the same anchor the buttons are derived from,
                    // so the two line up on screen without the particle needing a canvas.
                    _readyFire.transform.position = _arena.ToWorld(gladiator.Pos, headHeight * 1.15f)
                                                    + new Vector3(_arena.ScaleLength(GameConstants.GladiatorRadius) * 1.6f, 0f, 0f);
                }
            }
        }

        /// <summary>Where above his head the buttons hang from, in world units.</summary>
        private float HeadHeight => _arena.ScaleLength(GameConstants.GladiatorRadius) * 2.4f;

        /// <summary>
        /// The ability's name and glyph on its button. The ability differs per gladiator, so it is
        /// named rather than called "special"; with a glyph the name drops below it, and without one -
        /// no icon pack - it keeps the middle.
        /// </summary>
        private void SyncAbilityFace(GladiatorInstance gladiator)
        {
            _abilityLabel.text = gladiator.AbilityInfo.Name;

            HudFactory.UseSprite(_abilityIcon, _palette != null ? _palette.AbilityIconFor(gladiator.Ability) : null);
            _abilityIcon.color = _palette != null && _palette.AbilityIconsArePictures ? Color.white : HudFactory.RageColor;
            bool hasIcon = _abilityIcon.sprite != null;
            if (_abilityIcon.enabled != hasIcon) _abilityIcon.enabled = hasIcon;
            _abilityLabel.alignment = hasIcon ? TextAnchor.LowerCenter : TextAnchor.MiddleCenter;
            _abilityLabel.rectTransform.offsetMin = new Vector2(4f, hasIcon ? 12f : 0f);
            _abilityLabel.rectTransform.offsetMax = new Vector2(-4f, 0f);
            _abilityLabel.fontSize = hasIcon ? 12 : 15;
        }

        // ------------------------------------------------------------------
        // the horn: a look at the rage meter out of turn
        // ------------------------------------------------------------------

        private const float GainFillSeconds = 0.5f;
        private const float GainHoldSeconds = 1.0f;
        private const float GainFadeSeconds = 0.3f;
        private const float GainSeconds = GainFillSeconds + GainHoldSeconds + GainFadeSeconds;

        private float _gainStarted = -1f;
        private float _gainFrom;
        private float _gainTo;

        /// <summary>
        /// The horn has just put rage on his meter: the ability button comes up over him for a moment,
        /// out of turn, with its ring filling from what he had to what he has now - so the gain is
        /// seen to happen rather than found at the next planning phase. Only to be looked at: it
        /// cannot be pressed, and the rest of the controls stay down.
        /// </summary>
        public void ShowRageGain(float from, float to)
        {
            _gainFrom = Mathf.Clamp01(from / GameConstants.RageMax);
            _gainTo = Mathf.Clamp01(to / GameConstants.RageMax);
            _gainStarted = Time.unscaledTime;
        }

        /// <summary>Whether that look at the rage meter is up. For tests, and for Sync.</summary>
        public bool ShowingRageGain => _gainStarted >= 0f && Time.unscaledTime - _gainStarted < GainSeconds;

        /// <summary>How full the meter is drawn right now. For tests.</summary>
        public float RageGaugeFill => _rageGauge != null ? _rageGauge.fillAmount : 0f;

        private void ShowPlanningControls(bool on)
        {
            if (_defendRect.gameObject.activeSelf != on) _defendRect.gameObject.SetActive(on);
            if (_timerRect.gameObject.activeSelf != on) _timerRect.gameObject.SetActive(on);
            if (_timerTrack.gameObject.activeSelf != on) _timerTrack.gameObject.SetActive(on);
        }

        private void SyncRageGain(GladiatorInstance gladiator, Camera camera)
        {
            ShowPlanningControls(false);
            if (!TryCanvasPoint(_arena.ToWorld(gladiator.Pos, HeadHeight), camera, out var head)) return;
            _abilityRect.anchoredPosition = head + AbilityOffset;

            SyncAbilityFace(gladiator);

            float t = Time.unscaledTime - _gainStarted;
            _rageGauge.fillAmount = Mathf.Lerp(_gainFrom, _gainTo, Mathf.SmoothStep(0f, 1f, t / GainFillSeconds));

            float alpha = Mathf.Clamp01(1f - (t - GainFillSeconds - GainHoldSeconds) / GainFadeSeconds);
            Ability.interactable = false;
            _abilityGroup.alpha = alpha;
            _abilityGroup.blocksRaycasts = false;
            _abilityBackground.color = Idle;

            // A glow while it fills, in the horn's own orange-gold, so what changed is the ring.
            var glow = HudFactory.RageColor;
            glow.a = 0.6f * alpha;
            _abilityGlow.color = glow;
            _abilityGlow.enabled = true;
        }

        /// <summary>
        /// A point in the world as a position inside the canvas.
        ///
        /// Through the canvas rather than by assigning screen coordinates to RectTransform.position.
        /// That shortcut only holds for a Screen Space - Overlay canvas; under Screen Space - Camera
        /// the same property is world space, and the buttons silently fly off behind the arena.
        /// </summary>
        private bool TryCanvasPoint(Vector3 world, Camera camera, out Vector2 local)
        {
            Vector2 screen = camera.WorldToScreenPoint(world);
            var canvasRect = (RectTransform)_canvas.transform;
            var canvasCamera = _canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : _canvas.worldCamera;
            return RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, screen, canvasCamera, out local);
        }
    }
}
