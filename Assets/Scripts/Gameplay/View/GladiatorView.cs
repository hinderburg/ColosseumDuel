using ColosseumDuel.Core;
using UnityEngine;

namespace ColosseumDuel.Gameplay.View
{
    /// <summary>
    /// The visual for one gladiator on the arena floor: a body, a coloured helmet (blue = player,
    /// red = opponent, per the design doc), a facing marker, and HP/rage bars.
    ///
    /// Built from primitives in code rather than from a prefab so the whole scene stays
    /// regenerable by ProjectBootstrap - there is no binary asset to hand-wire or to lose.
    /// </summary>
    public sealed class GladiatorView : MonoBehaviour
    {
        private const float BarWidth = 0.95f;
        private const float BarHeight = 0.11f;
        private const float BarGap = 0.05f;

        /// <summary>Seconds a hit reaction lasts.</summary>
        private const float HitPunchTime = 0.22f;

        private ArenaView _arena;
        private Transform _model;
        private Transform _bars;
        private Transform _burst;
        private MeshRenderer _burstRenderer;
        private MaterialPropertyBlock _burstProperties;
        private float _hitPunchLeft;
        private float _burstLeft;
        private float _burstDuration;
        private float _burstMaxRadius;
        private Color _burstColor;
        private Transform _hpFill;
        private Transform _rageFill;
        private Transform _weaponMarker;
        private Transform _shieldMarker;
        private Transform _abilityMarker;

        public static GladiatorView Create(string name, Transform parent, ArenaView arena,
            Material bodyMaterial, Material helmetMaterial)
        {
            var palette = arena.Palette;

            var root = new GameObject(name);
            root.transform.SetParent(parent, false);
            var view = root.AddComponent<GladiatorView>();
            view._arena = arena;

            // World size derived from the simulation so the visual can never drift from the radius
            // collisions are actually resolved against.
            float radius = arena.ScaleLength(GameConstants.GladiatorRadius);
            float bodyHeight = radius * 2.2f;

            // The model is a separate child so it can spin to face a direction without dragging the
            // bars above the head around with it - those answer to the camera, not to the fight.
            var model = new GameObject("Model");
            model.transform.SetParent(root.transform, false);
            view._model = model.transform;

            var body = ViewPrimitives.Create(palette.MeshFor(PrimitiveType.Capsule), "Body", model.transform, bodyMaterial);
            body.transform.localScale = new Vector3(radius * 2f, bodyHeight * 0.5f, radius * 2f);
            body.transform.localPosition = new Vector3(0f, bodyHeight * 0.5f, 0f);

            var helmet = ViewPrimitives.Create(palette.MeshFor(PrimitiveType.Sphere), "Helmet", model.transform, helmetMaterial);
            helmet.transform.localScale = Vector3.one * (radius * 1.5f);
            helmet.transform.localPosition = new Vector3(0f, bodyHeight * 0.92f, 0f);

            // A small wedge on the front - without it a capsule gives no clue which way it faces,
            // which matters because defending turns the gladiator towards the opponent.
            var nose = ViewPrimitives.Create(palette.MeshFor(PrimitiveType.Cube), "FacingMarker", model.transform, helmetMaterial);
            nose.transform.localScale = new Vector3(radius * 0.5f, radius * 0.35f, radius * 0.9f);
            nose.transform.localPosition = new Vector3(0f, bodyHeight * 0.92f, radius * 1.0f);

            // Carried items, shown as small tags beside the head.
            view._weaponMarker = MakeMarker(palette, "WeaponMarker", model.transform, palette.Weapon,
                new Vector3(radius * 1.3f, bodyHeight * 0.75f, 0f), radius * 0.55f).transform;
            view._shieldMarker = MakeMarker(palette, "ShieldMarker", model.transform, palette.Shield,
                new Vector3(-radius * 1.3f, bodyHeight * 0.75f, 0f), radius * 0.55f).transform;

            // A ring at the feet while an ability buff is running.
            var ability = new GameObject("AbilityRing");
            ability.transform.SetParent(model.transform, false);
            ability.transform.localPosition = new Vector3(0f, 0.04f, 0f);
            ability.AddComponent<MeshFilter>().sharedMesh =
                ViewPrimitives.CreateAnnulus(radius * 1.25f, radius * 1.7f, 48);
            var abilityRenderer = ability.AddComponent<MeshRenderer>();
            abilityRenderer.sharedMaterial = palette.BarRage;
            abilityRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            view._abilityMarker = ability.transform;

            // --- bars, standing upright above the head and turned to face the camera ---
            // They used to lie flat on the ground, which only reads under a straight top-down view;
            // with the camera tilted they would be seen edge-on and squashed.
            var bars = new GameObject("Bars");
            bars.transform.SetParent(root.transform, false);
            // Generous, because a tilted camera foreshortens vertical offsets by roughly half.
            bars.transform.localPosition = new Vector3(0f, bodyHeight * 3.4f, 0f);
            view._bars = bars.transform;

            // An expanding ring for one-shot moments (a hit landing, an ability firing). Kept as a
            // single reusable object rather than spawned per event - at two gladiators there is
            // never more than one in flight, and nothing has to be allocated mid-match.
            var burst = new GameObject("Burst");
            burst.transform.SetParent(root.transform, false);
            burst.transform.localPosition = new Vector3(0f, 0.08f, 0f);
            burst.AddComponent<MeshFilter>().sharedMesh = ViewPrimitives.CreateAnnulus(0.62f, 1f, 48); // thick enough to read at a glance
            view._burstRenderer = burst.AddComponent<MeshRenderer>();
            view._burstRenderer.sharedMaterial = palette.Burst;
            view._burstRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            view._burstRenderer.receiveShadows = false;
            view._burstRenderer.enabled = false;
            view._burst = burst.transform;
            view._burstProperties = new MaterialPropertyBlock();

            view._hpFill = MakeBar(palette, "Hp", bars.transform, palette.BarBackground, palette.BarHp, 0f);
            view._rageFill = MakeBar(palette, "Rage", bars.transform, palette.BarBackground, palette.BarRage,
                -(BarHeight + BarGap));

            root.SetActive(false);
            return view;
        }

        private static GameObject MakeMarker(ViewPalette palette, string name, Transform parent, Material material,
            Vector3 localPos, float size)
        {
            var go = ViewPrimitives.Create(palette.MeshFor(PrimitiveType.Cube), name, parent, material);
            go.transform.localPosition = localPos;
            go.transform.localScale = Vector3.one * size;
            go.SetActive(false);
            return go;
        }

        /// <summary>Returns the fill transform; its local X scale is driven 0..1 by Sync.</summary>
        private static Transform MakeBar(ViewPalette palette, string name, Transform parent, Material background,
            Material fill, float verticalOffset)
        {
            var root = new GameObject(name + "Bar");
            root.transform.SetParent(parent, false);
            root.transform.localPosition = new Vector3(0f, verticalOffset, 0f);

            // Plain upright quads in the billboard's local XY plane; the parent turns them to camera.
            var bg = ViewPrimitives.Create(palette.MeshFor(PrimitiveType.Quad), "Background", root.transform, background);
            bg.transform.localScale = new Vector3(BarWidth, BarHeight, 1f);

            // The fill is parented to a pivot sitting on the bar's left edge, so scaling the pivot
            // grows the bar rightwards instead of from the middle out.
            var pivot = new GameObject("FillPivot");
            pivot.transform.SetParent(root.transform, false);
            pivot.transform.localPosition = new Vector3(-BarWidth * 0.5f, 0f, -0.01f);

            var fillQuad = ViewPrimitives.Create(palette.MeshFor(PrimitiveType.Quad), "Fill", pivot.transform, fill);
            fillQuad.transform.localScale = new Vector3(BarWidth, BarHeight * 0.75f, 1f);
            fillQuad.transform.localPosition = new Vector3(BarWidth * 0.5f, 0f, 0f);

            return pivot.transform;
        }

        /// <summary>A blow just landed on this gladiator: squash the model and ring the impact.</summary>
        public void PlayHit()
        {
            _hitPunchLeft = HitPunchTime;
            StartBurst(_arena.ScaleLength(GameConstants.GladiatorRadius) * 2.6f, 0.30f, Color.white);
        }

        /// <summary>This gladiator's ability just fired.</summary>
        public void PlayAbility(Color color)
        {
            StartBurst(_arena.ScaleLength(GameConstants.GladiatorRadius) * 6.5f, 0.55f, color);
        }

        private void StartBurst(float maxRadius, float duration, Color color)
        {
            _burstMaxRadius = maxRadius;
            _burstDuration = duration;
            _burstLeft = duration;
            _burstColor = color;
        }

        /// <summary>Pushes one frame of simulation state onto the visuals. Safe to call with null.</summary>
        public void Sync(GladiatorInstance g)
        {
            bool visible = g != null && g.Alive;
            if (gameObject.activeSelf != visible) gameObject.SetActive(visible);
            if (!visible) return;

            transform.localPosition = _arena.ToWorld(g.Pos);

            var forward = new Vector3(g.Facing.x, 0f, g.Facing.y);
            if (forward.sqrMagnitude > 0.0001f)
                _model.localRotation = Quaternion.LookRotation(forward, Vector3.up);

            // Billboard the bars. The camera never moves, so this is the same rotation every frame -
            // but reading it from the camera keeps the two from drifting apart if the framing is
            // ever retuned, which it will be.
            if (_arena.ArenaCamera != null)
                _bars.rotation = _arena.ArenaCamera.transform.rotation;

            AdvanceEffects(Time.deltaTime);

            SetFill(_hpFill, g.Def.MaxHp > 0f ? g.Hp / g.Def.MaxHp : 0f);
            SetFill(_rageFill, g.Rage / GameConstants.RageMax);

            SetActive(_weaponMarker, g.Weapon != WeaponType.None);
            SetActive(_shieldMarker, g.HasShield);
            SetActive(_abilityMarker, g.Buff.IsActive);
        }

        private void AdvanceEffects(float dt)
        {
            // Hit reaction: a quick squash-and-recover on the model only, so the bars above the head
            // stay put and readable while it plays.
            if (_hitPunchLeft > 0f)
            {
                _hitPunchLeft = Mathf.Max(0f, _hitPunchLeft - dt);
                float t = _hitPunchLeft / HitPunchTime;          // 1 at impact, 0 when recovered
                float punch = Mathf.Sin(t * Mathf.PI) * 0.28f;   // in and back out
                _model.localScale = new Vector3(1f + punch, 1f - punch * 0.6f, 1f + punch);
            }
            else if (_model.localScale != Vector3.one)
            {
                _model.localScale = Vector3.one;
            }

            if (_burstLeft <= 0f)
            {
                if (_burstRenderer.enabled) _burstRenderer.enabled = false;
                return;
            }

            _burstLeft = Mathf.Max(0f, _burstLeft - dt);
            float progress = 1f - _burstLeft / _burstDuration; // 0 -> 1 over the burst

            _burstRenderer.enabled = true;
            float radius = Mathf.Lerp(_burstMaxRadius * 0.25f, _burstMaxRadius, progress);
            _burst.localScale = new Vector3(radius, 1f, radius);

            // Per-instance alpha through a property block: the burst material is a shared asset, and
            // tinting it directly would fade both gladiators' rings at once.
            var color = _burstColor;
            // Hold the alpha up early and drop it late: a linear fade over sand spends most of its
            // life too faint to notice, which made the effect read as if it were not firing at all.
            color.a = Mathf.Pow(1f - progress, 0.55f);
            _burstProperties.SetColor(BaseColorId, color);
            _burstRenderer.SetPropertyBlock(_burstProperties);
        }

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        private static void SetFill(Transform pivot, float t)
        {
            t = Mathf.Clamp01(t);
            var scale = pivot.localScale;
            scale.x = t;
            pivot.localScale = scale;
            // A zero-width quad still renders a hairline; hide it outright instead.
            pivot.gameObject.SetActive(t > 0.001f);
        }

        private static void SetActive(Transform t, bool active)
        {
            if (t != null && t.gameObject.activeSelf != active) t.gameObject.SetActive(active);
        }
    }
}
