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

            // Wall blocks stand 1.2 tall centred at 0.5, so their top edge is at 1.1.
            const float wallTop = 1.15f;

            for (int i = 0; i < TorchCount; i++)
            {
                float t = i / (float)TorchCount * Mathf.PI * 2f;
                var position = new Vector3(Mathf.Cos(t) * WorldRadiusX, wallTop, Mathf.Sin(t) * WorldRadiusZ);

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
