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
        private const float BarWidth = 1.1f;
        private const float BarHeight = 0.14f;
        private const float BarGap = 0.05f;

        private ArenaView _arena;
        private Transform _model;
        private Transform _hpFill;
        private Transform _rageFill;
        private Transform _weaponMarker;
        private Transform _shieldMarker;
        private Transform _abilityMarker;

        public static GladiatorView Create(string name, Transform parent, ArenaView arena, Material helmetMaterial)
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

            // The model is a separate child so it can spin to face a direction while the bars above
            // the head stay axis-aligned to the top-down camera.
            var model = new GameObject("Model");
            model.transform.SetParent(root.transform, false);
            view._model = model.transform;

            var body = ViewPrimitives.Create(palette.MeshFor(PrimitiveType.Capsule), "Body", model.transform, palette.Body);
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

            // --- bars, laid flat on the ground plane above the head (screen-up is world +Z under
            // the top-down camera) ---
            var bars = new GameObject("Bars");
            bars.transform.SetParent(root.transform, false);
            bars.transform.localPosition = new Vector3(0f, 0.05f, radius * 2.6f);

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
            Material fill, float zOffset)
        {
            var root = new GameObject(name + "Bar");
            root.transform.SetParent(parent, false);
            root.transform.localPosition = new Vector3(0f, 0f, zOffset);

            var bg = ViewPrimitives.CreateGroundQuad(palette.MeshFor(PrimitiveType.Quad), "Background", root.transform, background);
            bg.transform.localScale = new Vector3(BarWidth, BarHeight, 1f);

            // The fill is parented to a pivot sitting on the bar's left edge, so scaling the pivot
            // grows the bar rightwards instead of from the middle out.
            var pivot = new GameObject("FillPivot");
            pivot.transform.SetParent(root.transform, false);
            pivot.transform.localPosition = new Vector3(-BarWidth * 0.5f, 0.001f, 0f);

            var fillQuad = ViewPrimitives.CreateGroundQuad(palette.MeshFor(PrimitiveType.Quad), "Fill", pivot.transform, fill);
            fillQuad.transform.localScale = new Vector3(BarWidth, BarHeight * 0.8f, 1f);
            fillQuad.transform.localPosition = new Vector3(BarWidth * 0.5f, 0f, 0f);

            return pivot.transform;
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

            SetFill(_hpFill, g.Def.MaxHp > 0f ? g.Hp / g.Def.MaxHp : 0f);
            SetFill(_rageFill, g.Rage / GameConstants.RageMax);

            SetActive(_weaponMarker, g.Weapon != WeaponType.None);
            SetActive(_shieldMarker, g.HasShield);
            SetActive(_abilityMarker, g.Buff.IsActive);
        }

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
