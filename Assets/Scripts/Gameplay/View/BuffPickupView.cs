using UnityEngine;

namespace ColosseumDuel.Gameplay.View
{
    /// <summary>
    /// The weapon blessing lying on the sand: a red blade turning slowly over a glowing ring, so it
    /// reads from across the arena as the one thing on the floor worth running for.
    ///
    /// Built once and moved: there is only ever one blessing on the sand, and a view that appears
    /// and disappears costs nothing, where one built on the cycle it is laid would be a hitch at the
    /// moment the player is looking at it.
    /// </summary>
    public sealed class BuffPickupView : MonoBehaviour
    {
        /// <summary>How fast the blade turns, in degrees a second of world time.</summary>
        private const float SpinDegreesPerSecond = 60f;

        /// <summary>How long one breath of the glow takes, in seconds of world time.</summary>
        private const float PulsePeriod = 1.4f;

        /// <summary>How far above the sand the blade lies, so the ring under it shows.</summary>
        private const float LieHeight = 0.18f;

        private ArenaView _arena;
        private Transform _spin;
        private readonly System.Collections.Generic.List<Renderer> _glow =
            new System.Collections.Generic.List<Renderer>();
        private MaterialPropertyBlock _glowProperties;
        private float _age;

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        public static BuffPickupView Create(string name, Transform parent, ArenaView arena)
        {
            var palette = arena.Palette;
            var root = new GameObject(name);
            root.transform.SetParent(parent, false);

            var view = root.AddComponent<BuffPickupView>();
            view._arena = arena;
            view._glowProperties = new MaterialPropertyBlock();

            var spin = new GameObject("Spin").transform;
            spin.SetParent(root.transform, false);
            spin.localPosition = new Vector3(0f, LieHeight, 0f);
            view._spin = spin;

            float length = GearSizes.SwordLength * 0.85f;
            if (palette != null && palette.SwordModel != null)
            {
                var blade = Instantiate(palette.SwordModel, spin).transform;
                blade.name = "Blade";
                blade.localRotation = GearSizes.LyingDown;
                blade.localScale = Vector3.one * length;
                GearSizes.Tint(blade.gameObject, GearSizes.BlessedTint);

                // A black edge round the blade: a bright blade on bright sand needs one. Drawn as
                // an inside-out copy, the same way the untrained-weapon outline is, in that shell's
                // material washed black.
                if (palette.GearUntrained != null)
                {
                    var outline = GearSizes.MakeShell(palette.SwordModel, blade, GearSizes.OutlineShellName,
                        GearSizes.OutlineShell, palette.GearUntrained);
                    var ink = new MaterialPropertyBlock();
                    ink.SetColor(BaseColorId, Color.black);
                    foreach (var renderer in outline.GetComponentsInChildren<Renderer>(true))
                        renderer.SetPropertyBlock(ink);
                    outline.SetActive(true);
                }

                if (palette.WeaponGlow != null)
                {
                    var halo = GearSizes.MakeShell(palette.SwordModel, blade, GearSizes.GlowShellName,
                        GearSizes.GlowShell, palette.WeaponGlow);
                    halo.SetActive(true);
                    view._glow.AddRange(halo.GetComponentsInChildren<Renderer>(true));
                }
            }
            else if (palette != null)
            {
                // No model pack: a gold bead, which at least reads as the one bright thing there.
                var bead = ViewPrimitives.Create(palette.MeshFor(PrimitiveType.Sphere), "Blade", spin, palette.Weapon);
                bead.transform.localScale = Vector3.one * length * 0.35f;
                GearSizes.Tint(bead, GearSizes.BlessedTint);
            }

            if (palette != null && palette.WeaponGlow != null)
            {
                float radius = arena.ScaleLength(Core.GameConstants.BuffRadius) * 2.2f;
                var ring = ViewPrimitives.Create(ViewPrimitives.CreateAnnulus(radius * 0.72f, radius, 40),
                    "Ring", root.transform, palette.WeaponGlow);
                ring.transform.localPosition = new Vector3(0f, 0.03f, 0f);
                view._glow.Add(ring.GetComponent<Renderer>());

                // And a dark rim on both edges of the ring, for the same reason as the blade's outline.
                var rimMaterial = palette.BarBackground;
                if (rimMaterial != null)
                {
                    var rim = ViewPrimitives.Create(ViewPrimitives.CreateAnnulus(radius, radius * 1.12f, 40),
                        "Rim", root.transform, rimMaterial);
                    rim.transform.localPosition = new Vector3(0f, 0.03f, 0f);
                    var inner = ViewPrimitives.Create(ViewPrimitives.CreateAnnulus(radius * 0.63f, radius * 0.72f, 40),
                        "RimInner", root.transform, rimMaterial);
                    inner.transform.localPosition = new Vector3(0f, 0.03f, 0f);
                }
            }

            foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            root.SetActive(false);
            return view;
        }

        /// <summary>Shows the blessing where it lies, or hides the view when there is none.</summary>
        public void Sync(Vector2? position)
        {
            bool visible = position.HasValue;
            if (gameObject.activeSelf != visible) gameObject.SetActive(visible);
            if (!visible) return;

            transform.localPosition = _arena.ToWorld(position.Value);

            // World time: it is part of the arena, and planning slowing the arena slows it with it.
            _age += Time.deltaTime;
            _spin.localRotation = Quaternion.Euler(0f, _age * SpinDegreesPerSecond, 0f);

            var palette = _arena.Palette;
            if (palette == null || palette.WeaponGlow == null) return;

            float breath = 0.65f + 0.35f * Mathf.Sin(_age * Mathf.PI * 2f / PulsePeriod);
            var color = palette.WeaponGlow.color;
            color.a *= breath;
            _glowProperties.SetColor(BaseColorId, color);
            foreach (var renderer in _glow)
                if (renderer != null) renderer.SetPropertyBlock(_glowProperties);
        }
    }
}
