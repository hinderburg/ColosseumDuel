using ColosseumDuel.Core;
using UnityEngine;

namespace ColosseumDuel.Gameplay.View
{
    /// <summary>
    /// One of the things lying on the sand to be run over, turning slowly over a glowing ring in its
    /// own colour: the blessing a red blade, the apple green, the horn orange. Each edged in black -
    /// a bright thing on bright sand does not read from across the arena without an edge.
    ///
    /// Built once and moved: there is only ever one of each on the sand, and a view that appears and
    /// disappears costs nothing, where one built on the round it is laid would be a hitch at the
    /// moment the player is looking at it.
    ///
    /// The apple and the horn are built from shapes here rather than models - none of the packs has
    /// either - which also means they are there in a clean clone without the packs.
    /// </summary>
    public sealed class PickupView : MonoBehaviour
    {
        /// <summary>How fast it turns, in degrees a second of world time.</summary>
        private const float SpinDegreesPerSecond = 60f;

        /// <summary>How long one breath of the glow takes, in seconds of world time.</summary>
        private const float PulsePeriod = 1.4f;

        /// <summary>How far above the sand it lies, so the ring under it shows.</summary>
        private const float LieHeight = 0.18f;

        /// <summary>How much bigger than the apple and the horn their black edge is drawn.</summary>
        private const float OutlineScale = 1.14f;

        /// <summary>The colour each is, on the thing itself and on the ring under it.</summary>
        public static Color ColorOf(PickupKind kind)
        {
            switch (kind)
            {
                case PickupKind.Apple: return GearSizes.AppleTint;
                case PickupKind.Horn: return GearSizes.HornTint;
                default: return GearSizes.BlessedTint;
            }
        }

        /// <summary>The name of its view in the scene, by what it is.</summary>
        public static string NameOf(PickupKind kind)
        {
            switch (kind)
            {
                case PickupKind.Apple: return "Apple";
                case PickupKind.Horn: return "Horn";
                default: return "WeaponBlessing";
            }
        }

        public PickupKind Kind { get; private set; }

        private ArenaView _arena;
        private Transform _spin;
        private readonly System.Collections.Generic.List<Renderer> _glow =
            new System.Collections.Generic.List<Renderer>();
        private MaterialPropertyBlock _glowProperties;
        private Color _glowColor;
        private float _age;

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        public static PickupView Create(PickupKind kind, Transform parent, ArenaView arena)
        {
            var palette = arena.Palette;
            var root = new GameObject(NameOf(kind));
            root.transform.SetParent(parent, false);

            var view = root.AddComponent<PickupView>();
            view.Kind = kind;
            view._arena = arena;
            view._glowProperties = new MaterialPropertyBlock();

            var spin = new GameObject("Spin").transform;
            spin.SetParent(root.transform, false);
            spin.localPosition = new Vector3(0f, LieHeight, 0f);
            view._spin = spin;

            float size = GearSizes.SwordLength * 0.85f;
            switch (kind)
            {
                case PickupKind.Apple: view.BuildApple(palette, spin, size); break;
                case PickupKind.Horn: view.BuildHorn(palette, spin, size); break;
                default: view.BuildBlade(palette, spin, size); break;
            }

            // The ring under it, in its own colour, with a dark rim on both edges for the same reason
            // as the black edge round the thing itself.
            if (palette != null && palette.WeaponGlow != null)
            {
                view._glowColor = ColorOf(kind);
                view._glowColor.a = palette.WeaponGlow.color.a;

                float radius = arena.ScaleLength(GameConstants.BuffRadius) * 2.2f;
                var ring = ViewPrimitives.Create(ViewPrimitives.CreateAnnulus(radius * 0.72f, radius, 40),
                    "Ring", root.transform, palette.WeaponGlow);
                ring.transform.localPosition = new Vector3(0f, 0.03f, 0f);
                view._glow.Add(ring.GetComponent<Renderer>());

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

        /// <summary>The blessing: a red blade lying down, edged in black, in a red glow.</summary>
        private void BuildBlade(ViewPalette palette, Transform spin, float length)
        {
            if (palette != null && palette.SwordModel != null)
            {
                var blade = Instantiate(palette.SwordModel, spin).transform;
                blade.name = "Blade";
                blade.localRotation = GearSizes.LyingDown;
                blade.localScale = Vector3.one * length;
                GearSizes.Tint(blade.gameObject, GearSizes.BlessedTint);

                // Drawn as an inside-out copy, the same way the untrained-weapon outline is, in that
                // shell's material washed black.
                if (palette.GearUntrained != null)
                    Ink(GearSizes.MakeShell(palette.SwordModel, blade, GearSizes.OutlineShellName,
                        GearSizes.OutlineShell, palette.GearUntrained));

                if (palette.WeaponGlow != null)
                {
                    var halo = GearSizes.MakeShell(palette.SwordModel, blade, GearSizes.GlowShellName,
                        GearSizes.GlowShell, palette.WeaponGlow);
                    halo.SetActive(true);
                    _glow.AddRange(halo.GetComponentsInChildren<Renderer>(true));
                }
            }
            else if (palette != null)
            {
                // No model pack: a bead, which at least reads as the one bright thing there.
                var bead = ViewPrimitives.Create(palette.MeshFor(PrimitiveType.Sphere), "Blade", spin, palette.Weapon);
                bead.transform.localScale = Vector3.one * length * 0.35f;
                GearSizes.Tint(bead, GearSizes.BlessedTint);
            }
        }

        /// <summary>
        /// The apple: a round green fruit a little flattened top and bottom, a brown stalk and one
        /// leaf, stood up so it reads as an apple from the camera's height rather than as a ball.
        /// </summary>
        private void BuildApple(ViewPalette palette, Transform spin, float size)
        {
            if (palette == null) return;

            var apple = new GameObject("AppleModel").transform;
            apple.SetParent(spin, false);
            // About the size of the blade's glow ring across, and inside its own: at twice this it
            // hid the ring it sits on and read as a ball rather than as fruit.
            float body = size * 0.26f;
            apple.localPosition = new Vector3(0f, body * 0.45f, 0f);

            var fruit = ViewPrimitives.Create(palette.MeshFor(PrimitiveType.Sphere), "Fruit", apple, palette.Weapon);
            fruit.transform.localScale = new Vector3(body, body * 0.88f, body);
            GearSizes.Tint(fruit, GearSizes.AppleTint);

            var stalk = ViewPrimitives.Create(palette.MeshFor(PrimitiveType.Cylinder), "Stalk", apple, palette.Weapon);
            stalk.transform.localScale = new Vector3(body * 0.08f, body * 0.16f, body * 0.08f);
            stalk.transform.localPosition = new Vector3(0f, body * 0.52f, 0f);
            stalk.transform.localRotation = Quaternion.Euler(0f, 0f, -12f);
            GearSizes.Tint(stalk, new Color(0.35f, 0.22f, 0.10f));

            var leaf = ViewPrimitives.Create(palette.MeshFor(PrimitiveType.Sphere), "Leaf", apple, palette.Weapon);
            leaf.transform.localScale = new Vector3(body * 0.34f, body * 0.06f, body * 0.16f);
            leaf.transform.localPosition = new Vector3(body * 0.17f, body * 0.56f, 0f);
            leaf.transform.localRotation = Quaternion.Euler(0f, 0f, 24f);
            GearSizes.Tint(leaf, new Color(0.16f, 0.55f, 0.14f));

            Outline(palette, apple);
        }

        /// <summary>
        /// The horn: a hunting horn, a tapering tube curled through a quarter turn with a flared
        /// mouth, and a dark band where a hand would hold it. Lying on its side, mouth up and to one
        /// side, so the curl shows from above.
        /// </summary>
        private void BuildHorn(ViewPalette palette, Transform spin, float size)
        {
            if (palette == null) return;

            var horn = new GameObject("HornModel").transform;
            horn.SetParent(spin, false);
            horn.localPosition = new Vector3(0f, size * 0.12f, 0f);

            var tube = new GameObject("Tube");
            tube.transform.SetParent(horn, false);
            tube.AddComponent<MeshFilter>().sharedMesh = CreateHornMesh(size * 0.55f, size * 0.14f, size * 0.025f, 0f);
            tube.AddComponent<MeshRenderer>().sharedMaterial = palette.Weapon;
            GearSizes.Tint(tube, GearSizes.HornTint);

            var band = new GameObject("Band");
            band.transform.SetParent(horn, false);
            band.AddComponent<MeshFilter>().sharedMesh = CreateHornMesh(size * 0.55f, size * 0.14f, size * 0.025f, 0.012f * size, 0.55f, 0.66f);
            band.AddComponent<MeshRenderer>().sharedMaterial = palette.Weapon;
            GearSizes.Tint(band, new Color(0.30f, 0.16f, 0.07f));

            Outline(palette, horn);
        }

        /// <summary>
        /// A tube swept round a quarter circle of the given radius in the XZ plane, from a flared
        /// mouth to a narrow tip, grown outward by <paramref name="grow"/> - for the band that sits on
        /// it - and only between the two fractions of its length asked for.
        /// </summary>
        private static Mesh CreateHornMesh(float curl, float mouth, float tip, float grow, float from = 0f, float to = 1f)
        {
            const int along = 18;
            const int round = 14;
            var vertices = new Vector3[(along + 1) * (round + 1)];
            var normals = new Vector3[vertices.Length];
            var triangles = new int[along * round * 6];

            for (int i = 0; i <= along; i++)
            {
                float t = Mathf.Lerp(from, to, i / (float)along);
                float angle = t * Mathf.PI * 0.5f;

                // The centre line: a quarter circle, the mouth at its start.
                var centre = new Vector3(Mathf.Cos(angle) * curl - curl * 0.5f, 0f, Mathf.Sin(angle) * curl - curl * 0.5f);
                var ahead = new Vector3(-Mathf.Sin(angle), 0f, Mathf.Cos(angle));
                var outward = Vector3.Cross(ahead, Vector3.up).normalized;

                // Flared at the mouth, narrowing fast and then slowly to the tip.
                float radius = Mathf.Lerp(mouth, tip, Mathf.Sqrt(t)) + grow;

                for (int j = 0; j <= round; j++)
                {
                    float around = j / (float)round * Mathf.PI * 2f;
                    var normal = outward * Mathf.Cos(around) + Vector3.up * Mathf.Sin(around);
                    vertices[i * (round + 1) + j] = centre + normal * radius;
                    normals[i * (round + 1) + j] = normal;
                }
            }

            int k = 0;
            for (int i = 0; i < along; i++)
            for (int j = 0; j < round; j++)
            {
                int a = i * (round + 1) + j;
                int b = a + round + 1;
                // Wound so each face's normal points out of the tube: along the tube, then round it.
                triangles[k++] = a; triangles[k++] = b; triangles[k++] = a + 1;
                triangles[k++] = b; triangles[k++] = b + 1; triangles[k++] = a + 1;
            }

            var mesh = new Mesh { name = "Horn", vertices = vertices, normals = normals, triangles = triangles };
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>
        /// The black edge round a thing built here: a copy of it a size bigger, in the inside-out
        /// shell material the blade's edge is drawn in, washed black.
        /// </summary>
        private static void Outline(ViewPalette palette, Transform model)
        {
            if (palette.GearUntrained == null) return;
            Ink(GearSizes.MakeShell(model.gameObject, model, GearSizes.OutlineShellName,
                Vector3.one * OutlineScale, palette.GearUntrained));
        }

        private static void Ink(GameObject shell)
        {
            var ink = new MaterialPropertyBlock();
            ink.SetColor(BaseColorId, Color.black);
            foreach (var renderer in shell.GetComponentsInChildren<Renderer>(true))
                renderer.SetPropertyBlock(ink);
            shell.SetActive(true);
        }

        /// <summary>Shows it where it lies, or hides the view when there is none.</summary>
        public void Sync(Vector2? position)
        {
            bool visible = position.HasValue;
            if (gameObject.activeSelf != visible) gameObject.SetActive(visible);
            if (!visible) return;

            transform.localPosition = _arena.ToWorld(position.Value);

            // World time: it is part of the arena, and planning slowing the arena slows it with it.
            _age += Time.deltaTime;
            _spin.localRotation = Quaternion.Euler(0f, _age * SpinDegreesPerSecond, 0f);

            if (_glow.Count == 0) return;

            float breath = 0.65f + 0.35f * Mathf.Sin(_age * Mathf.PI * 2f / PulsePeriod);
            var color = _glowColor;
            color.a *= breath;
            _glowProperties.SetColor(BaseColorId, color);
            foreach (var renderer in _glow)
                if (renderer != null) renderer.SetPropertyBlock(_glowProperties);
        }
    }
}
