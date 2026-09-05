using System.Collections.Generic;
using ColosseumDuel.Core;
using UnityEngine;

namespace ColosseumDuel.Gameplay.View
{
    /// <summary>
    /// The single place where the simulation's virtual 2D space is converted to world space, plus
    /// the visuals that belong to the arena itself (the shrinking danger rings).
    ///
    /// The simulation is a top-down plane, so virtual (x, y) maps to world (x, 0, y) - the same
    /// convention the original web build used. Nothing else in the project should do this
    /// conversion by hand.
    /// </summary>
    public sealed class ArenaView : MonoBehaviour
    {
        [Tooltip("World radius of the arena floor. GameConstants.ArenaRadius virtual units map onto this.")]
        public float WorldArenaRadius = 8f;

        public ViewPalette Palette;

        [Tooltip("The fixed arena camera. World-space labels billboard towards it.")]
        public Camera ArenaCamera;

        [Tooltip("Height above the floor at which the danger rings are drawn, to avoid z-fighting.")]
        public float HazardRingHeight = 0.03f;

        [Tooltip("Top of the wall. The torches burn here, on the posts the decor puts under them.")]
        public float WallHeight = 1.2f;

        public float VirtualToWorld => WorldArenaRadius / GameConstants.ArenaRadius;

        /// <summary>World semi-axis across the screen.</summary>
        public float WorldRadiusX => WorldArenaRadius;

        /// <summary>World semi-axis up the screen - the long one. The elongation already lives in
        /// the simulation's coordinates, so the virtual-to-world scale stays uniform.</summary>
        public float WorldRadiusZ => WorldArenaRadius * GameConstants.ArenaElongation;

        public Vector3 ToWorld(Vector2 virtualPos, float height = 0f)
            => new Vector3(virtualPos.x * VirtualToWorld, height, virtualPos.y * VirtualToWorld);

        public Vector2 ToVirtual(Vector3 worldPos)
            => new Vector2(worldPos.x, worldPos.z) / VirtualToWorld;

        /// <summary>Converts a virtual length (a radius, a distance) to world units.</summary>
        public float ScaleLength(float virtualLength) => virtualLength * VirtualToWorld;

        private readonly List<Renderer> _hazardRings = new List<Renderer>();
        private MaterialPropertyBlock _ringProperties;
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        public void BuildHazardRings()
        {
            foreach (var ring in _hazardRings)
                if (ring != null) Destroy(ring.gameObject);
            _hazardRings.Clear();
            _ringProperties = new MaterialPropertyBlock();

            var root = new GameObject("HazardRings");
            root.transform.SetParent(transform, false);
            root.transform.localPosition = new Vector3(0f, HazardRingHeight, 0f);

            foreach (var stage in HazardSystem.Schedule)
            {
                var go = new GameObject($"Ring_{stage.InnerFraction:0.00}-{stage.OuterFraction:0.00}");
                go.transform.SetParent(root.transform, false);

                // Built as a unit ring and stretched onto the arena's ellipse, so a stage at 0.75 is
                // the same fraction of the way to the wall in every direction - matching how
                // HazardSystem actually measures it.
                float inner = Mathf.Max(stage.InnerFraction, 0.001f);
                float outer = stage.OuterFraction;

                go.AddComponent<MeshFilter>().sharedMesh = ViewPrimitives.CreateAnnulus(inner, outer);
                go.transform.localScale = new Vector3(WorldRadiusX, 1f, WorldRadiusZ);
                var renderer = go.AddComponent<MeshRenderer>();
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                renderer.enabled = false;
                _hazardRings.Add(renderer);
            }
        }

        [Tooltip("Depth of the stone tier behind the wall.")]
        public float GalleryDepth = 4.5f;

        /// <summary>
        /// How many world units one metre of the modular stone kit should measure.
        ///
        /// The arena is not modelled at human scale - a gladiator stands three units tall because a
        /// steeply tilted camera flattens vertical extent, not because he is three metres. Laying
        /// the kit out at 1:1 would put chest-high blocks beside a figure four times their height
        /// and make the wall read as a garden border. This restores the proportion the pieces were
        /// modelled at.
        /// </summary>
        private const float MetreToWorld = 1.67f;

        /// <summary>
        /// Builds the arena wall, and behind it the posts and gallery, out of the modular stone kit.
        ///
        /// Built here at runtime rather than baked into the scene by the bootstrap for the same
        /// reason the torches are: the kit is a paid pack that is not in the repository, and a scene
        /// full of prefab instances pointing at it would open in a clean clone as several hundred
        /// missing objects. Coming through the palette, an absent pack is one null to check.
        /// </summary>
        public void BuildWall()
        {
            var root = new GameObject("ArenaWall");
            root.transform.SetParent(transform, false);

            float perimeter = ArenaShape.Perimeter(WorldRadiusX, WorldRadiusZ);

            if (Palette == null || Palette.WallBlock == null)
            {
                BuildPrimitiveWall(root.transform, perimeter);
                return;
            }

            // The block mesh is a metre square and a quarter deep, and scale multiplies that depth
            // rather than setting it - the first version asked for 0.42 and got 0.10, a wall thin
            // enough to read as a drawn outline. Built thicker than the kit's own proportion on
            // purpose: at this camera angle the top of the wall is most of what is seen of it.
            const float thickness = 0.6f;
            float depthScale = thickness / 0.25f;

            // The blocks sit with their inner face on the arena's ellipse, so their centres run
            // round a slightly larger one - and it is that larger curve they have to cover. Laying
            // them out on the inner ellipse and then pushing each one outwards stretches the ring
            // it needs to fill without adding the blocks to fill it, which opens hairline gaps at
            // the ends of the oval where the curvature is sharpest.
            float centreX = WorldRadiusX + thickness * 0.5f;
            float centreZ = WorldRadiusZ + thickness * 0.5f;
            float centrePerimeter = ArenaShape.Perimeter(centreX, centreZ);

            // One block per kit-metre of wall, so the brickwork keeps the proportions it was
            // modelled with instead of being stretched to whatever count came out round.
            int count = Mathf.Max(Mathf.RoundToInt(centrePerimeter / MetreToWorld), 8);
            float step = centrePerimeter / count;
            var angles = ArenaShape.EvenlySpacedAngles(count, centreX, centreZ);

            for (int i = 0; i < count; i++)
            {
                float t = angles[i];
                var outward = Outward(t, centreX, centreZ);

                var piece = Place(Palette.WallBlock, root.transform, $"Segment_{i:00}",
                                  OnEllipse(t, centreX, centreZ, 0f), outward);

                // A hair of overlap: neighbours meeting exactly leave hairline gaps wherever the
                // chord cuts inside the curve, which on the ends of an oval is everywhere.
                piece.transform.localScale = new Vector3(step * 1.04f, WallHeight, depthScale);
            }

            BuildPosts(root.transform);
            BuildGallery(root.transform);
        }

        /// <summary>
        /// Painted blocks, for a clone without the kit. Kept rather than leaving the arena open: the
        /// wall is what the eye reads the playable edge from, and without it the bounces look like
        /// the fighters are turning round at nothing.
        /// </summary>
        private void BuildPrimitiveWall(Transform root, float perimeter)
        {
            const int segments = 72;
            float step = perimeter / segments;
            var angles = ArenaShape.EvenlySpacedAngles(segments, WorldRadiusX, WorldRadiusZ);
            var mesh = Palette != null ? Palette.Cube : null;

            for (int i = 0; i < segments; i++)
            {
                float t = angles[i];
                var outward = Outward(t, WorldRadiusX, WorldRadiusZ);

                var go = new GameObject($"Segment_{i:00}");
                go.transform.SetParent(root, false);
                go.transform.localPosition = OnEllipse(t, WorldRadiusX, WorldRadiusZ, WallHeight * 0.5f);
                go.transform.localRotation = Quaternion.LookRotation(outward, Vector3.up);
                go.transform.localScale = new Vector3(step * 1.06f, WallHeight, 0.4f);

                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                go.AddComponent<MeshRenderer>().sharedMaterial = Palette != null ? Palette.WallStone : null;
            }
        }

        /// <summary>Posts at the torch positions, so each flame stands on something.</summary>
        private void BuildPosts(Transform root)
        {
            if (Palette.WallPost == null || TorchCount <= 0) return;

            var angles = ArenaShape.EvenlySpacedAngles(TorchCount, WorldRadiusX, WorldRadiusZ);
            for (int i = 0; i < TorchCount; i++)
            {
                float t = angles[i];
                var outward = Outward(t, WorldRadiusX, WorldRadiusZ);
                var post = Place(Palette.WallPost, root, $"Post_{i:00}",
                                 OnEllipse(t, WorldRadiusX, WorldRadiusZ, 0f) + outward * 0.1f, outward);

                // The mesh is two metres tall, scaled so its top lands where the torches burn. Kept
                // narrow: at kit scale the posts were wider than the wall was thick and read as
                // crates set down beside it rather than as part of it.
                post.transform.localScale = new Vector3(1.1f, WallHeight / 2f, 1.1f);
            }
        }

        /// <summary>
        /// A tier of stone behind the wall, with a rail along its outer edge.
        ///
        /// Pure scenery, and it earns its place for one reason: beyond the wall the frame was empty
        /// background, and an arena that stops at a waist-high parapet reads as a model on a table
        /// rather than a building.
        /// </summary>
        private void BuildGallery(Transform root)
        {
            if (Palette.GallerySlab == null) return;

            // Spaced on the outer edge: pieces stepped by the inner arc would pile into each other
            // by the far side of a ring this deep.
            float outerX = WorldRadiusX + GalleryDepth;
            float outerZ = WorldRadiusZ + GalleryDepth;
            float perimeter = ArenaShape.Perimeter(outerX, outerZ);
            int count = Mathf.Max(Mathf.RoundToInt(perimeter / MetreToWorld), 8);
            float step = perimeter / count;
            var angles = ArenaShape.EvenlySpacedAngles(count, outerX, outerZ);

            for (int i = 0; i < count; i++)
            {
                float t = angles[i];
                var outward = Outward(t, outerX, outerZ);
                var onOuter = OnEllipse(t, outerX, outerZ, WallHeight);

                var slab = Place(Palette.GallerySlab, root, $"GalleryFloor_{i:00}",
                                 onOuter - outward * (GalleryDepth * 0.5f), outward);
                slab.transform.localScale = new Vector3(step * 1.06f, 1f, GalleryDepth);

                if (Palette.GalleryRail == null) continue;

                var rail = Place(Palette.GalleryRail, root, $"GalleryRail_{i:00}", onOuter, outward);
                rail.transform.localScale = new Vector3(step * 1.06f, MetreToWorld, MetreToWorld);
            }
        }

        private static Vector3 OnEllipse(float t, float radiusX, float radiusZ, float height)
            => new Vector3(Mathf.Cos(t) * radiusX, height, Mathf.Sin(t) * radiusZ);

        private static Vector3 Outward(float t, float radiusX, float radiusZ)
        {
            var normal = ArenaShape.OutwardNormal(t, radiusX, radiusZ);
            return new Vector3(normal.x, 0f, normal.y);
        }

        /// <summary>
        /// Warm sandstone, per the layout sketch. The kit's own stone is a cool grey that sits
        /// oddly against orange sand.
        /// </summary>
        private static readonly Color StoneTint = new Color(0.86f, 0.66f, 0.45f);

        private readonly Dictionary<Material, Material> _tintedStone = new Dictionary<Material, Material>();

        /// <summary>
        /// One tinted copy per kit material, shared by every piece that uses it.
        ///
        /// A copy rather than a property block: a per-renderer override breaks SRP batching, and
        /// there are a few hundred of these. A runtime copy rather than an authored asset: an asset
        /// would carry a reference to the pack's textures into the repository, where the pack is not.
        /// </summary>
        private Material Tinted(Material source)
        {
            if (source == null) return null;
            if (_tintedStone.TryGetValue(source, out var tinted)) return tinted;

            tinted = new Material(source);
            if (tinted.HasProperty(ColorId)) tinted.SetColor(ColorId, StoneTint);
            else if (tinted.HasProperty(BaseColorId)) tinted.SetColor(BaseColorId, StoneTint);
            _tintedStone[source] = tinted;
            return tinted;
        }

        private static readonly int ColorId = Shader.PropertyToID("_Color");

        private GameObject Place(GameObject prefab, Transform parent, string name,
                                 Vector3 localPosition, Vector3 outward)
        {
            var instance = Instantiate(prefab, parent);
            instance.name = name;
            instance.transform.localPosition = localPosition;
            instance.transform.localRotation = Quaternion.LookRotation(outward, Vector3.up);

            foreach (var renderer in instance.GetComponentsInChildren<Renderer>(true))
                renderer.sharedMaterial = Tinted(renderer.sharedMaterial);

            // The kit ships a collider on every piece. Nothing here is ever raycast - input projects
            // onto a mathematical plane, and the wall is a formula - so several hundred of them
            // would be pure weight.
            foreach (var collider in instance.GetComponentsInChildren<Collider>(true))
                Destroy(collider);

            return instance;
        }

        /// <summary>Torches set along the top of the wall.</summary>
        [Tooltip("How many torches to space around the wall.")]
        public int TorchCount = 14;

        /// <summary>
        /// Rings the wall with flames. They are the visible clock of the match: during Planning the
        /// world runs slow, and a burning torch is the only thing on a still arena that shows it.
        /// </summary>
        public void BuildTorches()
        {
            if (Palette == null || Palette.Torch == null) return;

            var root = new GameObject("Torches");
            root.transform.SetParent(transform, false);

            // Equal arcs, not equal angles - the same walk the wall blocks and the posts use, so
            // every flame lands on a post instead of most of them standing on bare parapet.
            var angles = ArenaShape.EvenlySpacedAngles(TorchCount, WorldRadiusX, WorldRadiusZ);

            for (int i = 0; i < TorchCount; i++)
            {
                float t = angles[i];
                var position = new Vector3(Mathf.Cos(t) * WorldRadiusX, WallHeight, Mathf.Sin(t) * WorldRadiusZ);

                var torch = Instantiate(Palette.Torch, root.transform);
                torch.name = $"Torch_{i:00}";
                torch.transform.localPosition = position;

                // Face the flame inwards, towards the fight.
                var inward = new Vector3(-position.x, 0f, -position.z);
                if (inward.sqrMagnitude > 0.0001f)
                    torch.transform.localRotation = Quaternion.LookRotation(inward.normalized, Vector3.up);

                // A particle system set to unscaled time would keep burning at full speed through
                // the planning slowdown - and these flames are the only thing that shows it.
                foreach (var particles in torch.GetComponentsInChildren<ParticleSystem>(true))
                {
                    var main = particles.main;
                    main.useUnscaledTime = false;
                }
            }
        }

        [Tooltip("How many flames make up the ring marking the edge of the danger zone.")]
        public int HazardFireCount = 22;

        private readonly List<Transform> _hazardFlames = new List<Transform>();
        private float _flameRingFraction = -1f;

        /// <summary>
        /// Builds the ring of flames that marks where safe ground ends.
        ///
        /// A boundary rather than a filled area: covering the whole danger zone in particles is a
        /// lot of overdraw for something the player reads as one line - the line they must not
        /// cross. The flat red ring already shows the area; this shows its edge.
        /// </summary>
        public void BuildHazardFire()
        {
            if (Palette == null || Palette.HazardFire == null) return;

            var root = new GameObject("HazardFire");
            root.transform.SetParent(transform, false);

            for (int i = 0; i < HazardFireCount; i++)
            {
                var flame = Instantiate(Palette.HazardFire, root.transform);
                flame.name = $"Flame_{i:00}";
                flame.SetActive(false);

                foreach (var particles in flame.GetComponentsInChildren<ParticleSystem>(true))
                {
                    var main = particles.main;
                    main.useUnscaledTime = false; // must drag with the planning slowdown like the torches
                }

                _hazardFlames.Add(flame.transform);
            }
        }

        /// <summary>
        /// Moves the flame ring onto the current edge of safety, or hides it while the arena is safe.
        /// </summary>
        private void SyncHazardFire(MatchState state)
        {
            if (_hazardFlames.Count == 0) return;

            // The innermost active stage's inner edge is the boundary; as stages light up, the ring
            // closes in on the middle.
            float boundary = float.MaxValue;
            foreach (var stage in HazardSystem.Schedule)
                if (state.Cycle >= stage.ActivateCycle)
                    boundary = Mathf.Min(boundary, stage.InnerFraction);

            bool burning = boundary < float.MaxValue;
            if (!burning)
            {
                if (_flameRingFraction >= 0f)
                {
                    foreach (var flame in _hazardFlames) flame.gameObject.SetActive(false);
                    _flameRingFraction = -1f;
                }
                return;
            }

            // Only reposition when the boundary actually moves - otherwise this rewrites two dozen
            // transforms every frame for no visible change.
            if (Mathf.Approximately(boundary, _flameRingFraction)) return;
            _flameRingFraction = boundary;

            // A collapsed boundary means the whole floor burns; keep the flames visible by leaving
            // them at a small radius rather than stacking them all on the centre point.
            float fraction = Mathf.Max(boundary, 0.12f);

            for (int i = 0; i < _hazardFlames.Count; i++)
            {
                float t = i / (float)_hazardFlames.Count * Mathf.PI * 2f;
                _hazardFlames[i].localPosition = new Vector3(
                    Mathf.Cos(t) * WorldRadiusX * fraction,
                    HazardRingHeight,
                    Mathf.Sin(t) * WorldRadiusZ * fraction);
                _hazardFlames[i].gameObject.SetActive(true);
            }
        }

        [Tooltip("How many blood bursts can overlap before the oldest is reused.")]
        public int BloodPoolSize = 6;

        private readonly List<GameObject> _bloodPool = new List<GameObject>();
        private int _nextBlood;

        /// <summary>
        /// Pre-instantiates the blood bursts.
        ///
        /// A pool rather than instantiate-and-destroy per hit: with Mongoose landing twice a cycle
        /// and both sides trading blows simultaneously, spawning would allocate several particle
        /// hierarchies a second during a fight - exactly when the frame budget matters most.
        /// </summary>
        public void BuildBloodPool()
        {
            if (Palette == null || Palette.BloodHit == null) return;

            var root = new GameObject("BloodBursts");
            root.transform.SetParent(transform, false);

            for (int i = 0; i < BloodPoolSize; i++)
            {
                var burst = Instantiate(Palette.BloodHit, root.transform);
                burst.name = $"Blood_{i:00}";
                burst.SetActive(false);

                foreach (var particles in burst.GetComponentsInChildren<ParticleSystem>(true))
                {
                    var main = particles.main;
                    main.useUnscaledTime = false;
                    main.playOnAwake = false;
                }

                _bloodPool.Add(burst);
            }
        }

        /// <summary>Plays a blood burst where a blow landed.</summary>
        public void PlayBlood(Vector2 virtualPosition)
        {
            if (_bloodPool.Count == 0) return;

            var burst = _bloodPool[_nextBlood];
            _nextBlood = (_nextBlood + 1) % _bloodPool.Count;

            // Chest height, not the floor - a burst at the feet reads as dust, not as a hit.
            burst.transform.localPosition = ToWorld(virtualPosition, ScaleLength(GameConstants.GladiatorRadius) * 1.3f);

            // Restart rather than merely enable: a pooled system that already ran is sitting at the
            // end of its lifetime and would show nothing at all on reuse.
            burst.SetActive(true);
            foreach (var particles in burst.GetComponentsInChildren<ParticleSystem>(true))
            {
                particles.Clear(true);
                particles.Play(true);
            }
        }

        /// <summary>
        /// Shows the rings that are dealing damage right now, and - during Planning only - the one
        /// that will light up next cycle. The design calls for that stage to be telegraphed a cycle
        /// ahead so the player can plan a move out of it.
        /// </summary>
        public void Sync(MatchState state)
        {
            SyncHazardFire(state);

            if (_hazardRings.Count == 0 || Palette == null) return;

            var upcoming = HazardSystem.UpcomingStage(state.Cycle);
            bool telegraphing = state.Phase == MatchPhase.Planning && upcoming.HasValue;

            for (int i = 0; i < _hazardRings.Count && i < HazardSystem.Schedule.Count; i++)
            {
                var stage = HazardSystem.Schedule[i];
                var renderer = _hazardRings[i];

                bool active = state.Cycle >= stage.ActivateCycle;
                bool warned = telegraphing && stage.ActivateCycle == upcoming.Value.ActivateCycle;

                renderer.enabled = active || warned;
                if (!renderer.enabled) continue;

                var material = active ? Palette.HazardActive : Palette.HazardTelegraph;
                renderer.sharedMaterial = material;

                // Flat paint reads as decoration; a slow flicker reads as fire, which is what the
                // design asks for and what makes the ring feel dangerous rather than decorative.
                // Each ring gets its own phase so they do not breathe in lockstep, and the warning
                // ring pulses faster and harder to say "this one is not burning yet, but will be".
                float speed = active ? 2.4f : 5.5f;
                float depth = active ? 0.14f : 0.30f;
                float pulse = 1f + Mathf.Sin(Time.time * speed + i * 1.7f) * depth;

                var color = material.color * pulse;
                color.a = material.color.a;
                _ringProperties.SetColor(BaseColorId, color);
                renderer.SetPropertyBlock(_ringProperties);
            }
        }
    }
}
